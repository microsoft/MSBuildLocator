// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Build.Locator
{
    /// <summary>
    ///     Options to consider when querying for Visual Studio instances.
    /// </summary>
    public class VisualStudioInstanceQueryOptions
    {
        /// <summary>
        ///     Default query options (all instances).
        /// </summary>
        public static VisualStudioInstanceQueryOptions Default => new VisualStudioInstanceQueryOptions
        {
            DiscoveryTypes =
#if FEATURE_VISUALSTUDIOSETUP
                DiscoveryType.DeveloperConsole | DiscoveryType.VisualStudioSetup
#endif
#if NETCOREAPP
                DiscoveryType.DotNetSdk
#endif
        };

        /// <summary>
        ///     Discovery types for instances included in the query.
        /// </summary>
        public DiscoveryType DiscoveryTypes { get; set; }

        /// <summary>
        ///     Working directory to use when querying for instances. Ensure it is the project directory to pick up the right global.json.
        /// </summary>
        public string WorkingDirectory { get; set; } = Environment.CurrentDirectory;
    }
}
