﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.TemplateEngine.TemplateLocalizer.Commands
{
    /// <summary>
    /// Represents a <see cref="System.CommandLine.Command"/> together with its handler.
    /// </summary>
    internal abstract class ExecutableCommand : ICommandHandler
    {
        internal ExecutableCommand(ILoggerFactory? loggerFactory = null)
        {
            LoggerFactory = loggerFactory;
            Logger = loggerFactory?.CreateLogger(GetType()) ?? NullLogger.Instance;
        }

        /// <summary>
        /// Gets a <see cref="ILoggerFactory"/> instance to be used for creating <see cref="ILogger"/> instances.
        /// </summary>
        protected ILoggerFactory? LoggerFactory { get; }

        /// <summary>
        /// Gets a <see cref="ILogger"/> instance to be used for logging the events generated by this command.
        /// </summary>
        protected ILogger Logger { get; }

        /// <summary>
        /// Creates a <see cref="System.CommandLine.Command"/> containing the details
        /// of this command such as name, description, arguments and options.
        /// </summary>
        /// <returns>The created command.</returns>
        public abstract Command CreateCommand();

        /// <summary>
        /// Executes the command with the given arguments specified in the invocation context.
        /// </summary>
        /// <returns>A task that tracks the asynchronous operation.
        /// 0 result from the completed task means that the command execution was successful.
        /// where any other value indicates a failure.</returns>
        public abstract Task<int> InvokeAsync(InvocationContext context);
    }
}
