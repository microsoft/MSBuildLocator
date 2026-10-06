// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if NET46

using System;

namespace Microsoft.Build.Locator
{
    /// <summary>
    ///     A single <c>bindingRedirect</c> element of a <c>dependentAssembly</c> entry in a .NET Framework
    ///     application configuration file.
    /// </summary>
    internal class AssemblyBindingRedirect
    {
        public AssemblyBindingRedirect(Version oldVersionLow, Version oldVersionHigh, Version newVersion)
        {
            OldVersionLow = oldVersionLow;
            OldVersionHigh = oldVersionHigh;
            NewVersion = newVersion;
        }

        /// <summary>Inclusive low bound of the <c>oldVersion</c> range.</summary>
        public Version OldVersionLow { get; }

        /// <summary>Inclusive high bound of the <c>oldVersion</c> range.</summary>
        public Version OldVersionHigh { get; }

        /// <summary>The version the <c>oldVersion</c> range binds to.</summary>
        public Version NewVersion { get; }

        /// <summary>
        ///     Gets a value indicating whether <paramref name="version"/> falls inside the inclusive
        ///     <c>oldVersion</c> range.
        /// </summary>
        public bool Includes(Version version) =>
            version != null && version >= OldVersionLow && version <= OldVersionHigh;
    }
}

#endif
