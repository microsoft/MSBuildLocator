// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Build.MSBuildLocator
{
    /// <summary>
    /// Enum to indicate how a <see cref="VisualStudioInstance"/> was discovered.
    /// </summary>
    public enum DiscoveryType
    {
        /// <summary>
        /// Discovered an instance via the current environment. This indicates
        /// the caller originated from a Visual Studio Developer Command Prompt.
        /// </summary>
        DeveloperConsole,

        /// <summary>
        /// Discovered via Visual Studio Setup API.
        /// </summary>
        VisualStudioSetup
    }
}