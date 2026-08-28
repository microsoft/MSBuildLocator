// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if NET46

using System;
using System.Collections.Generic;

namespace Microsoft.Build.Locator
{
    /// <summary>
    ///     A single <c>dependentAssembly</c> entry of a .NET Framework application configuration file: an
    ///     assembly identity plus the binding redirects and code bases declared for it. The two kinds of
    ///     child element are independent, so an entry can declare only code bases or only redirects.
    /// </summary>
    internal class DependentAssembly
    {
        public DependentAssembly(
            string name,
            string publicKeyToken,
            string culture,
            string processorArchitecture,
            IReadOnlyList<AssemblyBindingRedirect> bindingRedirects,
            IReadOnlyList<AssemblyCodeBase> codeBases)
        {
            Name = name;
            PublicKeyToken = publicKeyToken;
            Culture = culture;
            ProcessorArchitecture = processorArchitecture;
            BindingRedirects = bindingRedirects;
            CodeBases = codeBases;
        }

        /// <summary>Simple assembly name from <c>assemblyIdentity</c>.</summary>
        public string Name { get; }

        /// <summary>Public key token from <c>assemblyIdentity</c>, or <see langword="null"/> when unspecified.</summary>
        public string PublicKeyToken { get; }

        /// <summary>Culture from <c>assemblyIdentity</c>, or <see langword="null"/> when unspecified.</summary>
        public string Culture { get; }

        /// <summary>Processor architecture from <c>assemblyIdentity</c>, or <see langword="null"/> when unspecified.</summary>
        public string ProcessorArchitecture { get; }

        /// <summary>Every usable <c>bindingRedirect</c> of the entry, in config order.</summary>
        public IReadOnlyList<AssemblyBindingRedirect> BindingRedirects { get; }

        /// <summary>Every usable <c>codeBase</c> of the entry, in config order.</summary>
        public IReadOnlyList<AssemblyCodeBase> CodeBases { get; }

        /// <summary>
        ///     Applies the binding redirects of this entry to <paramref name="requestedVersion"/>, returning the
        ///     version the runtime would bind to. Like the runtime, the first redirect covering the requested
        ///     version wins, and a version no redirect covers is returned unchanged.
        /// </summary>
        public Version GetEffectiveVersion(Version requestedVersion)
        {
            if (requestedVersion == null)
            {
                return null;
            }

            foreach (AssemblyBindingRedirect redirect in BindingRedirects)
            {
                if (redirect.Includes(requestedVersion))
                {
                    return redirect.NewVersion;
                }
            }

            return requestedVersion;
        }
    }
}

#endif
