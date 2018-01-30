// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;

namespace Microsoft.Build.Locator
{
    /// <summary>
    ///     Represents an installed instance of Visual Studio.
    /// </summary>
    public class VisualStudioInstance
    {
        internal VisualStudioInstance(string name, string path, Version version, DiscoveryType discoveryType)
        {
            Name = name;
            VisualStudioRootPath = path;
            Version = version;
            DiscoveryType = discoveryType;
            MSBuildPath = Path.Combine(VisualStudioRootPath, "MSBuild", "15.0", "Bin");
        }

        /// <summary>
        ///     Version of the Visual Studio Instance
        /// </summary>
        public Version Version { get; }

        /// <summary>
        ///     Path to the Visual Studio installation
        /// </summary>
        public string VisualStudioRootPath { get; }

        /// <summary>
        ///     Full name of the Visual Studio instance with SKU name
        /// </summary>
        public string Name { get; }

        /// <summary>
        ///     Path to the MSBuild associated with this version of Visual Studio.
        /// </summary>
        public string MSBuildPath { get; }

        /// <summary>
        ///     Indicates how this instance was discovered.
        /// </summary>
        public DiscoveryType DiscoveryType { get; }
    }
}