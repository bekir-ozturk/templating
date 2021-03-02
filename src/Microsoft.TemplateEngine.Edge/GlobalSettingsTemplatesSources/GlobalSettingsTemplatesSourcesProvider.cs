// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.GlobalSettings;
using Microsoft.TemplateEngine.Abstractions.Installer;
using Microsoft.TemplateEngine.Abstractions.TemplatesSources;
using Microsoft.TemplateEngine.Utils;

namespace Microsoft.TemplateEngine.Edge
{
    public partial class GlobalSettingsTemplatesSourcesProviderFactory
    {
        internal class GlobalSettingsTemplatesSourcesProvider : IManagedTemplatesSourcesProvider
        {
            private readonly string PackagesFolder;
            private IEngineEnvironmentSettings _environmentSettings;
            private Dictionary<Guid, IInstaller> _installersByGuid = new Dictionary<Guid, IInstaller>();
            private Dictionary<string, IInstaller> _installersByName = new Dictionary<string, IInstaller>();
            private ConditionalWeakTable<IManagedTemplatesSource, IInstaller> _sourceToInstaller = new ConditionalWeakTable<IManagedTemplatesSource, IInstaller>();
            private List<ITemplatesSource> _notSupportedSources = new List<ITemplatesSource>();
            private Dictionary<IInstaller, Dictionary<string, IManagedTemplatesSource>> _templatesSources = new Dictionary<IInstaller, Dictionary<string, IManagedTemplatesSource>>();

            public GlobalSettingsTemplatesSourcesProvider
                (GlobalSettingsTemplatesSourcesProviderFactory factory, IEngineEnvironmentSettings settings)
            {
                _ = factory ?? throw new ArgumentNullException(nameof(factory));
                _ = settings ?? throw new ArgumentNullException(nameof(settings));

                Factory = factory;
                PackagesFolder = Path.Combine(settings.Paths.TemplateEngineRootDir, "packages");
                if (!settings.Host.FileSystem.DirectoryExists(PackagesFolder))
                {
                    settings.Host.FileSystem.CreateDirectory(PackagesFolder);
                }

                _environmentSettings = settings;
                foreach (var installerFactory in settings.SettingsLoader.Components.OfType<IInstallerFactory>())
                {
                    var installer = installerFactory.CreateInstaller(this, settings, PackagesFolder);
                    _installersByName[installerFactory.Name] = installer;
                    _installersByGuid[installerFactory.Id] = installer;
                }

                ReloadCache();
                settings.SettingsLoader.GlobalSettings.SettingsChanged += ReloadCache;
            }

            public event Action SourcesChanged;

            public ITemplatesSourcesProviderFactory Factory { get; }

            public Task<IReadOnlyList<ITemplatesSource>> GetAllSourcesAsync(CancellationToken cancellationToken)
            {
                List<ITemplatesSource> templatesSources = new List<ITemplatesSource>();
                foreach (Dictionary<string, IManagedTemplatesSource> sourcesByInstaller in _templatesSources.Values)
                {
                    templatesSources.AddRange(sourcesByInstaller.Values);
                }
                templatesSources.AddRange(_notSupportedSources);
                return Task.FromResult((IReadOnlyList<ITemplatesSource>)templatesSources);
            }

            public async Task<IReadOnlyList<CheckUpdateResult>> GetLatestVersionsAsync(IEnumerable<IManagedTemplatesSource> sources, CancellationToken cancellationToken)
            {
                _ = sources ?? throw new ArgumentNullException(nameof(sources));

                var tasks = new List<Task<IReadOnlyList<CheckUpdateResult>>>();
                foreach (var sourcesGroupedByInstaller in sources.GroupBy(s => GetInstaller(s)))
                {
                    tasks.Add(sourcesGroupedByInstaller.Key.GetLatestVersionAsync(sourcesGroupedByInstaller, cancellationToken));
                }
                await Task.WhenAll(tasks).ConfigureAwait(false);

                var result = new List<CheckUpdateResult>();
                foreach (var task in tasks)
                {
                    result.AddRange(task.Result);
                }
                return result;
            }

            public async Task<IReadOnlyList<InstallResult>> InstallAsync(IEnumerable<InstallRequest> installRequests, CancellationToken cancellationToken)
            {
                _ = installRequests ?? throw new ArgumentNullException(nameof(installRequests));
                if (!installRequests.Any())
                {
                    return new List<InstallResult>();
                }

                return await Task.WhenAll(installRequests.Select(async installRequest =>
                {
                    var installersThatCanInstall = new List<IInstaller>();
                    foreach (var install in _installersByName.Values)
                    {
                        if (await install.CanInstallAsync(installRequest, cancellationToken).ConfigureAwait(false))
                        {
                            installersThatCanInstall.Add(install);
                        }
                    }
                    if (installersThatCanInstall.Count == 0)
                    {
                        return InstallResult.CreateFailure(installRequest, InstallerErrorCode.UnsupportedRequest, $"{installRequest.Identifier} cannot be installed");
                    }

                    IInstaller installer = installersThatCanInstall[0];
                    return await InstallAsync(installRequest, installer, cancellationToken).ConfigureAwait(false);
                })).ConfigureAwait(false);
            }

            public async Task<IReadOnlyList<UninstallResult>> UninstallAsync(IEnumerable<IManagedTemplatesSource> sources, CancellationToken cancellationToken)
            {
                _ = sources ?? throw new ArgumentNullException(nameof(sources));
                if (!sources.Any())
                {
                    return new List<UninstallResult>();
                }

                return await Task.WhenAll(sources.Select(async source =>
                {
                    IInstaller installer = GetInstaller(source);
                    UninstallResult result = await installer.UninstallAsync(source, cancellationToken).ConfigureAwait(false);
                    if (result.Success)
                    {
                        _templatesSources[installer].Remove(source.Identifier);
                        _environmentSettings.SettingsLoader.GlobalSettings.Remove(installer.Serialize(source));
                    }
                    return result;
                })).ConfigureAwait(false);
            }

            private IInstaller GetInstaller(IManagedTemplatesSource source) => _sourceToInstaller.TryGetValue(source, out var installer) ? installer : throw new InvalidOperationException();

            public async Task<IReadOnlyList<UpdateResult>> UpdateAsync(IEnumerable<UpdateRequest> updateRequests, CancellationToken cancellationToken)
            {
                _ = updateRequests ?? throw new ArgumentNullException(nameof(updateRequests));
                IEnumerable<UpdateRequest> updatesToApply = updateRequests.Where(request => request.Version != request.Source.Version);
                return await Task.WhenAll(updatesToApply.Select(updateRequest => UpdateAsync(updateRequest, cancellationToken))).ConfigureAwait(false);
            }

            private async Task<UpdateResult> UpdateAsync(UpdateRequest updateRequest, CancellationToken cancellationToken)
            {
                IInstaller installer = GetInstaller(updateRequest.Source);
                (InstallerErrorCode result, string message) = await EnsureInstallPrerequisites(updateRequest.Source.Identifier, updateRequest.Version, installer, cancellationToken, update: true).ConfigureAwait(false);
                if (result != InstallerErrorCode.Success)
                {
                    return UpdateResult.CreateFailure(updateRequest, result, message);
                }

                UpdateResult updateResult = await installer.UpdateAsync(updateRequest, cancellationToken).ConfigureAwait(false);
                if (!updateResult.Success)
                {
                    return updateResult;
                }
                _environmentSettings.SettingsLoader.GlobalSettings.Add(installer.Serialize(updateResult.Source));
                return updateResult;
            }

            private async Task<(InstallerErrorCode, string)> EnsureInstallPrerequisites (string identifier, string version, IInstaller installer, CancellationToken cancellationToken, bool update = false)
            {
                //check if the source with same identifier is already installed
                if (_templatesSources[installer].TryGetValue(identifier, out IManagedTemplatesSource sourceToBeUpdated))
                {
                    //if same version is already installed - return
                    if (sourceToBeUpdated.Version == version)
                    {
                        return (InstallerErrorCode.AlreadyInstalled, $"{sourceToBeUpdated.DisplayName} is already installed.");
                    }
                    if (!update)
                    {
                        _environmentSettings.Host.LogMessage($"{sourceToBeUpdated.Identifier} is already installed, version: {sourceToBeUpdated.Version}, it will be replaced with {(string.IsNullOrWhiteSpace(identifier) ? "latest version" : $"version {version}")}.");
                    }
                    //if different version is installed - uninstall previous version first
                    UninstallResult uninstallResult = await installer.UninstallAsync(sourceToBeUpdated, cancellationToken).ConfigureAwait(false);
                    if (!uninstallResult.Success)
                    {
                        return (InstallerErrorCode.UpdateUninstallFailed, uninstallResult.ErrorMessage);
                    }
                    _environmentSettings.Host.LogMessage($"{sourceToBeUpdated.DisplayName} was successfully uninstalled.");
                    _environmentSettings.SettingsLoader.GlobalSettings.Remove(installer.Serialize(sourceToBeUpdated));
                }
                return (InstallerErrorCode.Success, string.Empty);
            }

            private async Task<InstallResult> InstallAsync(InstallRequest installRequest, IInstaller installer, CancellationToken cancellationToken)
            {
                _ = installRequest ?? throw new ArgumentNullException(nameof(installRequest));
                _ = installer ?? throw new ArgumentNullException(nameof(installer));

                (InstallerErrorCode result, string message) = await EnsureInstallPrerequisites(installRequest.Identifier, installRequest.Version, installer, cancellationToken).ConfigureAwait(false);
                if (result != InstallerErrorCode.Success)
                {
                    return InstallResult.CreateFailure(installRequest, result, message);
                }

                InstallResult installResult = await installer.InstallAsync(installRequest, cancellationToken).ConfigureAwait(false);
                if (!installResult.Success)
                {
                    return installResult;
                }
                _environmentSettings.SettingsLoader.GlobalSettings.Add(installer.Serialize(installResult.Source));
                return installResult;
            }

            private void ReloadCache()
            {
                _templatesSources = new Dictionary<IInstaller, Dictionary<string, IManagedTemplatesSource>>();
                foreach (IInstaller installer in _installersByGuid.Values)
                {
                    _templatesSources[installer] = new Dictionary<string, IManagedTemplatesSource>();
                }

                foreach (TemplatesSourceData entry in _environmentSettings.SettingsLoader.GlobalSettings.UserInstalledTemplatesSources)
                {
                    if (_installersByGuid.TryGetValue(entry.InstallerId, out var installer))
                    {
                        IManagedTemplatesSource managedTemplatesSource = installer.Deserialize(this, entry);
                        if (!_sourceToInstaller.TryGetValue(managedTemplatesSource, out _))
                            _sourceToInstaller.Add(managedTemplatesSource, installer);
                        if (_templatesSources.TryGetValue(installer, out Dictionary<string, IManagedTemplatesSource> installerSources))
                        {
                            installerSources[managedTemplatesSource.Identifier] = managedTemplatesSource;
                        }
                    }
                    else
                    {
                        _notSupportedSources.Add(new TemplatesSource(this, entry.MountPointUri, entry.LastChangeTime));
                    }
                }
                SourcesChanged?.Invoke();
            }
        }
    }
}