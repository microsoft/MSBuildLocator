// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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

        /// <summary>
        ///     This variable enables the removal of the existing restriction on querying installed Visual Studio instances 
        ///     with a runtime version lower than or equal to the one in the current environment.
        /// </summary>
        public bool AllowQueryAllRuntimeVersions { get; set; } = Environment.GetEnvironmentVariable("DOTNET_MSBUILD_QUERY_ALL_RUNTIMES") == "1";
    }
}
