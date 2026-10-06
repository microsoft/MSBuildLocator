// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if NET46

using System;
using System.Collections.Generic;
using System.Reflection;

namespace Microsoft.Build.Locator
{
    /// <summary>
    ///     The assembly binding information declared by a .NET Framework application configuration file such
    ///     as MSBuild.exe.config. .NET Framework only: application configuration files have no equivalent on
    ///     .NET (Core).
    /// </summary>
    internal class AssemblyBindingPolicy
    {
        /// <summary>A policy that declares nothing, used when no usable config file is available.</summary>
        public static readonly AssemblyBindingPolicy Empty = new AssemblyBindingPolicy(
            Array.Empty<DependentAssembly>(),
            new Dictionary<string, AssemblyName>(0, StringComparer.OrdinalIgnoreCase));

        public AssemblyBindingPolicy(
            IReadOnlyList<DependentAssembly> dependentAssemblies,
            IReadOnlyDictionary<string, AssemblyName> qualifiedAssemblies)
        {
            DependentAssemblies = dependentAssemblies;
            QualifiedAssemblies = qualifiedAssemblies;
        }

        /// <summary>
        ///     Every usable <c>dependentAssembly</c> entry, in config order. Entries can share a simple name
        ///     while differing by public key token, culture, or processor architecture, so they are kept as an
        ///     ordered list rather than keyed by name.
        /// </summary>
        public IReadOnlyList<DependentAssembly> DependentAssemblies { get; }

        /// <summary>
        ///     Maps the <c>partialName</c> of every usable <c>qualifyAssembly</c> entry to the full assembly name
        ///     it names. Lookup is case-insensitive, matching assembly name comparison.
        /// </summary>
        public IReadOnlyDictionary<string, AssemblyName> QualifiedAssemblies { get; }

        /// <summary>Gets a value indicating whether the policy has nothing to contribute to assembly resolution.</summary>
        public bool IsEmpty => DependentAssemblies.Count == 0 && QualifiedAssemblies.Count == 0;
    }
}

#endif
