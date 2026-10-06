// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if NET46

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security;
using System.Xml;

namespace Microsoft.Build.Locator
{
    /// <summary>
    ///     Reads the assembly binding information of a .NET Framework application configuration file such as
    ///     MSBuild.exe.config. Applications that use MSBuild through this library do not run as MSBuild.exe, so
    ///     the runtime never applies that config; reading it lets assembly resolution honor the locations the
    ///     selected MSBuild deployment declares for its dependencies.
    /// </summary>
    internal static class MSBuildExeConfigReader
    {
        private const string AssemblyBindingNamespacePrefix = "asm";
        private const string AssemblyBindingNamespace = "urn:schemas-microsoft-com:asm.v1";
        private const string DependentAssemblyPath = "/*/runtime/asm:assemblyBinding/asm:dependentAssembly";
        private const string QualifyAssemblyPath = "/*/runtime/asm:assemblyBinding/asm:qualifyAssembly";

        /// <summary>
        ///     Reads the policy declared by the config file at <paramref name="configFilePath"/>. A missing,
        ///     unreadable, or malformed config yields <see cref="AssemblyBindingPolicy.Empty"/>.
        /// </summary>
        public static AssemblyBindingPolicy Read(string configFilePath)
        {
            if (string.IsNullOrEmpty(configFilePath))
            {
                return AssemblyBindingPolicy.Empty;
            }

            try
            {
                if (!File.Exists(configFilePath))
                {
                    return AssemblyBindingPolicy.Empty;
                }

                using (XmlReader reader = XmlReader.Create(configFilePath, CreateReaderSettings()))
                {
                    return Parse(reader);
                }
            }
            catch (Exception e) when (IsUnusableConfigException(e))
            {
                return AssemblyBindingPolicy.Empty;
            }
        }

        /// <summary>
        ///     Reads the policy declared by config XML. Malformed XML yields <see cref="AssemblyBindingPolicy.Empty"/>.
        /// </summary>
        public static AssemblyBindingPolicy ReadXml(string xml)
        {
            try
            {
                using (var input = new StringReader(xml ?? string.Empty))
                using (XmlReader reader = XmlReader.Create(input, CreateReaderSettings()))
                {
                    return Parse(reader);
                }
            }
            catch (Exception e) when (IsUnusableConfigException(e))
            {
                return AssemblyBindingPolicy.Empty;
            }
        }

        /// <summary>
        ///     Parses an assembly version of one to four components, normalizing it to four components so that
        ///     comparison against a fully specified assembly version is meaningful.
        /// </summary>
        public static bool TryParseAssemblyVersion(string value, out Version version)
        {
            version = null;

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string[] parts = value.Trim().Split('.');
            if (parts.Length > 4)
            {
                return false;
            }

            var components = new int[4];
            for (int i = 0; i < parts.Length; i++)
            {
                // NumberStyles.None rejects signs, whitespace, and separators, so only plain digits parse.
                if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out int component) ||
                    component > ushort.MaxValue)
                {
                    return false;
                }

                components[i] = component;
            }

            version = new Version(components[0], components[1], components[2], components[3]);
            return true;
        }

        private static XmlReaderSettings CreateReaderSettings()
        {
            // A config file never legitimately needs a DTD or any other external resource, and resolving one
            // would let a malformed or hostile config reach outside the file.
            return new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };
        }

        private static AssemblyBindingPolicy Parse(XmlReader reader)
        {
            var document = new XmlDocument { XmlResolver = null };
            document.Load(reader);

            var namespaces = new XmlNamespaceManager(document.NameTable);
            namespaces.AddNamespace(AssemblyBindingNamespacePrefix, AssemblyBindingNamespace);

            IReadOnlyList<DependentAssembly> dependentAssemblies = ParseDependentAssemblies(document, namespaces);
            IReadOnlyDictionary<string, AssemblyName> qualifiedAssemblies = ParseQualifiedAssemblies(document, namespaces);

            return dependentAssemblies.Count == 0 && qualifiedAssemblies.Count == 0
                ? AssemblyBindingPolicy.Empty
                : new AssemblyBindingPolicy(dependentAssemblies, qualifiedAssemblies);
        }

        private static IReadOnlyList<DependentAssembly> ParseDependentAssemblies(
            XmlDocument document,
            XmlNamespaceManager namespaces)
        {
            var dependentAssemblies = new List<DependentAssembly>();

            foreach (XmlNode node in SelectNodes(document, DependentAssemblyPath, namespaces))
            {
                XmlNode identity = node.SelectSingleNode("asm:assemblyIdentity", namespaces);
                string name = GetAttribute(identity, "name");
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                IReadOnlyList<AssemblyBindingRedirect> redirects = ParseBindingRedirects(node, namespaces);
                IReadOnlyList<AssemblyCodeBase> codeBases = ParseCodeBases(node, namespaces);

                // An entry that declares neither cannot affect where an assembly loads from.
                if (redirects.Count == 0 && codeBases.Count == 0)
                {
                    continue;
                }

                dependentAssemblies.Add(new DependentAssembly(
                    name,
                    GetAttribute(identity, "publicKeyToken"),
                    GetAttribute(identity, "culture"),
                    GetAttribute(identity, "processorArchitecture"),
                    redirects,
                    codeBases));
            }

            return dependentAssemblies;
        }

        private static IReadOnlyList<AssemblyBindingRedirect> ParseBindingRedirects(
            XmlNode dependentAssembly,
            XmlNamespaceManager namespaces)
        {
            var redirects = new List<AssemblyBindingRedirect>();

            foreach (XmlNode node in SelectNodes(dependentAssembly, "asm:bindingRedirect", namespaces))
            {
                if (!TryParseOldVersionRange(GetAttribute(node, "oldVersion"), out Version low, out Version high) ||
                    !TryParseAssemblyVersion(GetAttribute(node, "newVersion"), out Version newVersion))
                {
                    continue;
                }

                redirects.Add(new AssemblyBindingRedirect(low, high, newVersion));
            }

            return redirects;
        }

        private static IReadOnlyList<AssemblyCodeBase> ParseCodeBases(
            XmlNode dependentAssembly,
            XmlNamespaceManager namespaces)
        {
            var codeBases = new List<AssemblyCodeBase>();

            foreach (XmlNode node in SelectNodes(dependentAssembly, "asm:codeBase", namespaces))
            {
                string href = GetAttribute(node, "href");
                if (string.IsNullOrEmpty(href))
                {
                    continue;
                }

                // The version is optional in that the runtime ignores it for assemblies without a strong name,
                // but one that is present and unparsable makes the entry unusable.
                string versionAttribute = GetAttribute(node, "version");
                Version version = null;
                if (!string.IsNullOrEmpty(versionAttribute) && !TryParseAssemblyVersion(versionAttribute, out version))
                {
                    continue;
                }

                codeBases.Add(new AssemblyCodeBase(version, href));
            }

            return codeBases;
        }

        private static IReadOnlyDictionary<string, AssemblyName> ParseQualifiedAssemblies(
            XmlDocument document,
            XmlNamespaceManager namespaces)
        {
            var qualifiedAssemblies = new Dictionary<string, AssemblyName>(StringComparer.OrdinalIgnoreCase);

            foreach (XmlNode node in SelectNodes(document, QualifyAssemblyPath, namespaces))
            {
                string partialName = GetAttribute(node, "partialName");
                string fullName = GetAttribute(node, "fullName");
                if (string.IsNullOrEmpty(partialName) ||
                    string.IsNullOrEmpty(fullName) ||
                    qualifiedAssemblies.ContainsKey(partialName))
                {
                    continue;
                }

                AssemblyName qualifiedName;
                try
                {
                    qualifiedName = new AssemblyName(fullName);
                }
                catch (Exception e) when (e is ArgumentException || e is FileLoadException)
                {
                    continue;
                }

                qualifiedAssemblies.Add(partialName, qualifiedName);
            }

            return qualifiedAssemblies;
        }

        private static bool TryParseOldVersionRange(string oldVersion, out Version low, out Version high)
        {
            low = null;
            high = null;

            if (string.IsNullOrEmpty(oldVersion))
            {
                return false;
            }

            int separator = oldVersion.IndexOf('-');
            if (separator < 0)
            {
                if (!TryParseAssemblyVersion(oldVersion, out low))
                {
                    return false;
                }

                high = low;
                return true;
            }

            if (!TryParseAssemblyVersion(oldVersion.Substring(0, separator), out low) ||
                !TryParseAssemblyVersion(oldVersion.Substring(separator + 1), out high) ||
                low > high)
            {
                low = null;
                high = null;
                return false;
            }

            return true;
        }

        private static IEnumerable<XmlNode> SelectNodes(XmlNode node, string xpath, XmlNamespaceManager namespaces)
        {
            XmlNodeList nodes = node.SelectNodes(xpath, namespaces);
            if (nodes == null)
            {
                yield break;
            }

            foreach (XmlNode selected in nodes)
            {
                yield return selected;
            }
        }

        private static string GetAttribute(XmlNode node, string name) => node?.Attributes?[name]?.Value;

        private static bool IsUnusableConfigException(Exception e) =>
            e is XmlException ||
            e is IOException ||
            e is UnauthorizedAccessException ||
            e is SecurityException ||
            e is NotSupportedException ||
            e is ArgumentException;
    }
}

#endif
