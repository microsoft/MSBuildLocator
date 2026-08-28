// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if NET46

using System;
using System.IO;
using System.Security;

namespace Microsoft.Build.Locator
{
    /// <summary>
    ///     A single <c>codeBase</c> element of a <c>dependentAssembly</c> entry in a .NET Framework
    ///     application configuration file.
    /// </summary>
    internal class AssemblyCodeBase
    {
        public AssemblyCodeBase(Version version, string href)
        {
            Version = version;
            Href = href;
        }

        /// <summary>
        ///     The assembly version this location provides, or <see langword="null"/> when the config does not
        ///     specify one. The runtime ignores the version for assemblies without a strong name.
        /// </summary>
        public Version Version { get; }

        /// <summary>The location as written in the config: a relative path, an absolute path, or a URI.</summary>
        public string Href { get; }

        /// <summary>
        ///     Converts <see cref="Href"/> to a normalized local path, resolving a relative href against
        ///     <paramref name="baseDirectory"/>, which is the directory containing the config file.
        /// </summary>
        /// <returns>
        ///     <see langword="false"/> when the href is not a local path or <c>file://</c> URI, or when it cannot
        ///     be normalized. Existence of the file is not checked.
        /// </returns>
        public bool TryGetLocalPath(string baseDirectory, out string localPath)
        {
            localPath = null;

            try
            {
                if (Uri.TryCreate(Href, UriKind.Absolute, out Uri uri))
                {
                    // Honor absolute local paths and file:// URIs; ignore http(s) and any other scheme.
                    if (!uri.IsFile)
                    {
                        return false;
                    }

                    localPath = Path.GetFullPath(uri.LocalPath);
                    return true;
                }

                if (string.IsNullOrEmpty(baseDirectory))
                {
                    return false;
                }

                localPath = Path.GetFullPath(Path.Combine(baseDirectory, Href));
                return true;
            }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is IOException || e is SecurityException)
            {
                localPath = null;
                return false;
            }
        }
    }
}

#endif
