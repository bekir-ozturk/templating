// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;

namespace Microsoft.TemplateEngine.Abstractions
{
    [Obsolete("Use " + nameof(ICreationEffects2) + " instead")]
    public interface ICreationEffects
    {
        IReadOnlyList<IFileChange> FileChanges { get; }

        ICreationResult CreationResult { get; }
    }

    public interface ICreationEffects2
    {
        IReadOnlyList<IFileChange2> FileChanges { get; }

        ICreationResult CreationResult { get; }
    }
}
