﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.TemplateFiltering;
using Microsoft.TemplateEngine.Cli.CommandParsing;
using Microsoft.TemplateEngine.Edge.Settings;

namespace Microsoft.TemplateEngine.Cli.TemplateResolution
{
    /// <summary>
    /// Resolves the templates to be used for 'dotnet new &lt;template short name&gt; [--dry run]'>.
    /// </summary>
    internal class InstantiateTemplateResolver : BaseTemplateResolver
    {
        private static IReadOnlyList<FilterOption> _supportedFilters = new[]
        {
            SupportedFilterOptions.BaselineFilter,
            SupportedFilterOptions.TypeFilter
        };

        public InstantiateTemplateResolver(TemplatePackageManager templatePackageManager, IHostSpecificDataLoader hostSpecificDataLoader)
            : base(templatePackageManager, hostSpecificDataLoader)
        {
        }

        public InstantiateTemplateResolver(IEnumerable<ITemplateInfo> templateList, IHostSpecificDataLoader hostSpecificDataLoader)
            : base(templateList, hostSpecificDataLoader)
        {
        }

        internal static IReadOnlyList<FilterOption> SupportedFilters => _supportedFilters;

        internal override IReadOnlyList<FilterOption> Filters => SupportedFilters;

        internal override async Task<TemplateResolutionResult> ResolveTemplatesAsync(INewCommandInput commandInput, string? defaultLanguage, CancellationToken cancellationToken)
        {
            IEnumerable<TemplateGroup> templateGroups = await GetTemplateGroupsAsync(cancellationToken).ConfigureAwait(false);

            IEnumerable<Func<TemplateGroup, MatchInfo?>> groupFilters = new[]
            {
                CliFilters.ExactShortNameTemplateGroupFilter(commandInput.TemplateName),
                CliFilters.LanguageGroupFilter(commandInput.Language, defaultLanguage)
            };

            IEnumerable<Func<ITemplateInfo, MatchInfo?>> templateFilters =
                SupportedFilters
                    .OfType<TemplateFilterOption>()
                    .Select(filter => filter.TemplateMatchFilter(commandInput));

            IEnumerable<TemplateGroupMatchInfo> matchInformation =
                templateGroups.Select(
                    group =>
                        TemplateGroupMatchInfo.ApplyFilters(
                            group,
                            groupFilters,
                            templateFilters,
                            CliFilters.TemplateParameterFilter(HostSpecificDataLoader, commandInput)));

            return new TemplateResolutionResult(matchInformation, this);
        }
    }
}
