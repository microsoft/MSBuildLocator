// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if NET46

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Microsoft.Build.Locator
{
    /// <summary>
    ///     Selects an MSBuild executable config and applies the parts of its assembly-binding policy that
    ///     identify code base candidates.
    /// </summary>
    internal static class MSBuildExeConfigResolver
    {
        /// <summary>
        ///     Finds the config to use for the first registered path containing MSBuild.exe. An explicitly
        ///     registered amd64 directory uses its adjacent config; other directories prefer an existing amd64
        ///     config, then use the config adjacent to the executable.
        /// </summary>
        public static string FindConfigFilePath(IEnumerable<string> msbuildSearchPaths)
        {
            foreach (string msbuildPath in msbuildSearchPaths)
            {
                string msbuildExePath = Path.Combine(msbuildPath, "MSBuild.exe");
                if (!File.Exists(msbuildExePath))
                {
                    continue;
                }

                msbuildExePath = Path.GetFullPath(msbuildExePath);
                string executableDirectory = Path.GetDirectoryName(msbuildExePath);
                if (string.Equals(Path.GetFileName(executableDirectory), "amd64", StringComparison.OrdinalIgnoreCase))
                {
                    return msbuildExePath + ".config";
                }

                string amd64ConfigPath = Path.Combine(executableDirectory, "amd64", "MSBuild.exe.config");
                return File.Exists(amd64ConfigPath) ? amd64ConfigPath : msbuildExePath + ".config";
            }

            return null;
        }

        /// <summary>
        ///     Gets every local code base candidate applicable to <paramref name="requestedAssembly"/>, in
        ///     config order. File existence and metadata validation are intentionally left to the caller so a
        ///     failed candidate can fall through to the next one.
        /// </summary>
        public static IEnumerable<AssemblyCodeBaseCandidate> GetCodeBaseCandidates(
            AssemblyName requestedAssembly,
            AssemblyBindingPolicy policy,
            string configDirectory)
        {
            if (requestedAssembly == null || string.IsNullOrEmpty(requestedAssembly.Name) || policy == null)
            {
                yield break;
            }

            AssemblyName qualifiedAssembly = QualifyAssembly(requestedAssembly, policy);

            foreach (DependentAssembly dependentAssembly in policy.DependentAssemblies)
            {
                if (!IdentityMatches(dependentAssembly, qualifiedAssembly))
                {
                    continue;
                }

                Version effectiveVersion = dependentAssembly.GetEffectiveVersion(qualifiedAssembly.Version);
                bool isUnsigned = !HasPublicKeyToken(qualifiedAssembly);

                foreach (AssemblyCodeBase codeBase in dependentAssembly.CodeBases)
                {
                    if (!CodeBaseMatches(codeBase, effectiveVersion, isUnsigned) ||
                        !codeBase.TryGetLocalPath(configDirectory, out string path))
                    {
                        continue;
                    }

                    yield return new AssemblyCodeBaseCandidate(
                        path,
                        CloneWithVersion(qualifiedAssembly, effectiveVersion),
                        dependentAssembly);
                }
            }
        }

        private static AssemblyName QualifyAssembly(AssemblyName requestedAssembly, AssemblyBindingPolicy policy)
        {
            if (IsPartialAssemblyName(requestedAssembly) &&
                policy.QualifiedAssemblies.TryGetValue(requestedAssembly.Name, out AssemblyName qualifiedAssembly) &&
                string.Equals(requestedAssembly.Name, qualifiedAssembly.Name, StringComparison.OrdinalIgnoreCase))
            {
                return qualifiedAssembly;
            }

            return requestedAssembly;
        }

        private static bool IsPartialAssemblyName(AssemblyName assemblyName) =>
            assemblyName.Version == null &&
            !HasPublicKeyToken(assemblyName) &&
            IsNeutralCulture(assemblyName.CultureName) &&
            assemblyName.ProcessorArchitecture == ProcessorArchitecture.None;

        private static bool IdentityMatches(DependentAssembly dependentAssembly, AssemblyName requestedAssembly) =>
            string.Equals(dependentAssembly.Name, requestedAssembly.Name, StringComparison.OrdinalIgnoreCase) &&
            PublicKeyTokenMatches(dependentAssembly.PublicKeyToken, requestedAssembly) &&
            CultureMatches(dependentAssembly.Culture, requestedAssembly) &&
            ProcessorArchitectureMatches(dependentAssembly.ProcessorArchitecture, requestedAssembly);

        private static bool CodeBaseMatches(AssemblyCodeBase codeBase, Version effectiveVersion, bool isUnsigned)
        {
            if (codeBase.Version == null)
            {
                return isUnsigned;
            }

            return effectiveVersion != null && codeBase.Version.Equals(effectiveVersion);
        }

        private static bool PublicKeyTokenMatches(string configuredToken, AssemblyName assemblyName)
        {
            byte[] publicKeyToken = assemblyName.GetPublicKeyToken();
            bool isUnsigned = publicKeyToken == null || publicKeyToken.Length == 0;

            if (string.IsNullOrEmpty(configuredToken) ||
                string.Equals(configuredToken, "null", StringComparison.OrdinalIgnoreCase))
            {
                return isUnsigned;
            }

            return !isUnsigned &&
                string.Equals(configuredToken, GetPublicKeyTokenString(publicKeyToken), StringComparison.OrdinalIgnoreCase);
        }

        private static bool CultureMatches(string configuredCulture, AssemblyName assemblyName)
        {
            if (string.IsNullOrEmpty(configuredCulture) ||
                string.Equals(configuredCulture, "neutral", StringComparison.OrdinalIgnoreCase))
            {
                return IsNeutralCulture(assemblyName.CultureName);
            }

            return string.Equals(configuredCulture, assemblyName.CultureName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool ProcessorArchitectureMatches(string configuredArchitecture, AssemblyName assemblyName)
        {
            // AssemblyResolve commonly reports no architecture, even for an MSIL assembly. In that case the
            // configured architecture is still checked against the loaded target's manifest below.
            return string.IsNullOrEmpty(configuredArchitecture) ||
                assemblyName.ProcessorArchitecture == ProcessorArchitecture.None ||
                string.Equals(configuredArchitecture, assemblyName.ProcessorArchitecture.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsNeutralCulture(string culture) =>
            string.IsNullOrEmpty(culture) || string.Equals(culture, "neutral", StringComparison.OrdinalIgnoreCase);

        private static bool HasPublicKeyToken(AssemblyName assemblyName)
        {
            byte[] publicKeyToken = assemblyName.GetPublicKeyToken();
            return publicKeyToken != null && publicKeyToken.Length != 0;
        }

        internal static string GetPublicKeyTokenString(byte[] publicKeyToken)
        {
            var token = new char[publicKeyToken.Length * 2];
            const string HexDigits = "0123456789abcdef";
            for (int i = 0; i < publicKeyToken.Length; i++)
            {
                token[i * 2] = HexDigits[publicKeyToken[i] >> 4];
                token[(i * 2) + 1] = HexDigits[publicKeyToken[i] & 0x0f];
            }

            return new string(token);
        }

        private static AssemblyName CloneWithVersion(AssemblyName assemblyName, Version version)
        {
            var clone = (AssemblyName)assemblyName.Clone();
            clone.Version = version;
            return clone;
        }
    }

    /// <summary>
    ///     A local code base candidate together with the identity its target must have before it is loaded.
    /// </summary>
    internal class AssemblyCodeBaseCandidate
    {
        public AssemblyCodeBaseCandidate(string path, AssemblyName effectiveAssemblyName, DependentAssembly dependentAssembly)
        {
            Path = path;
            EffectiveAssemblyName = effectiveAssemblyName;
            DependentAssembly = dependentAssembly;
        }

        /// <summary>The normalized local path from the config's codeBase href.</summary>
        public string Path { get; }

        /// <summary>The qualified request after applying any binding redirect.</summary>
        public AssemblyName EffectiveAssemblyName { get; }

        /// <summary>The dependentAssembly entry that produced this candidate.</summary>
        public DependentAssembly DependentAssembly { get; }

        /// <summary>
        ///     Validates that a code base target has the identity this candidate declares, preventing a stale or
        ///     malicious config from loading an unrelated assembly.
        /// </summary>
        public bool HasCompatibleIdentity(AssemblyName targetAssembly) =>
            targetAssembly != null &&
            string.Equals(EffectiveAssemblyName.Name, targetAssembly.Name, StringComparison.OrdinalIgnoreCase) &&
            VersionMatches(targetAssembly) &&
            PublicKeyTokensMatch(targetAssembly) &&
            CulturesMatch(targetAssembly) &&
            ProcessorArchitecturesMatch(targetAssembly);

        private bool VersionMatches(AssemblyName targetAssembly) =>
            EffectiveAssemblyName.Version == null || EffectiveAssemblyName.Version.Equals(targetAssembly.Version);

        private bool PublicKeyTokensMatch(AssemblyName targetAssembly)
        {
            byte[] effectiveToken = EffectiveAssemblyName.GetPublicKeyToken();
            byte[] targetToken = targetAssembly.GetPublicKeyToken();
            bool effectiveIsUnsigned = effectiveToken == null || effectiveToken.Length == 0;
            bool targetIsUnsigned = targetToken == null || targetToken.Length == 0;

            return effectiveIsUnsigned == targetIsUnsigned &&
                (effectiveIsUnsigned ||
                string.Equals(
                    MSBuildExeConfigResolver.GetPublicKeyTokenString(effectiveToken),
                    MSBuildExeConfigResolver.GetPublicKeyTokenString(targetToken),
                    StringComparison.OrdinalIgnoreCase));
        }

        private bool CulturesMatch(AssemblyName targetAssembly)
        {
            string effectiveCulture = EffectiveAssemblyName.CultureName;
            string targetCulture = targetAssembly.CultureName;
            return MSBuildExeConfigResolver.IsNeutralCulture(effectiveCulture)
                ? MSBuildExeConfigResolver.IsNeutralCulture(targetCulture)
                : string.Equals(effectiveCulture, targetCulture, StringComparison.OrdinalIgnoreCase);
        }

        private bool ProcessorArchitecturesMatch(AssemblyName targetAssembly)
        {
            string configuredArchitecture = DependentAssembly.ProcessorArchitecture;
            if (!string.IsNullOrEmpty(configuredArchitecture))
            {
                return string.Equals(
                    configuredArchitecture,
                    targetAssembly.ProcessorArchitecture.ToString(),
                    StringComparison.OrdinalIgnoreCase);
            }

            return EffectiveAssemblyName.ProcessorArchitecture == ProcessorArchitecture.None ||
                EffectiveAssemblyName.ProcessorArchitecture == targetAssembly.ProcessorArchitecture;
        }
    }
}

#endif
