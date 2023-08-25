// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Build.Locator
{
    /// <summary>
    ///     Enum to indicate type of Visual Studio discovery.
    /// </summary>
    [Flags]
    public enum DiscoveryType
    {
        /// <summary>
        ///     Discovery via the current environment. This indicates the caller originated
        ///     from a Visual Studio Developer Command Prompt.
        /// </summary>
        DeveloperConsole = 1,

        /// <summary>
        ///     Discovery via Visual Studio Setup API.
        /// </summary>
        VisualStudioSetup = 2,

        /// <summary>
        ///     Discovery via dotnet --info.
        /// </summary>
        DotNetSdk = 4
    }
}
