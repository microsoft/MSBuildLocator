// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if !NETCOREAPP

using System.IO;

namespace Microsoft.Build.Locator.Tests
{
    /// <summary>
    ///     A directory layout shaped like a Visual Studio installation that relocates MSBuild dependencies out
    ///     of the MSBuild bin directory, without requiring Visual Studio to be installed.
    /// </summary>
    internal sealed class FakeVisualStudioInstall
    {
        /// <summary>Relative path from the canonical amd64 config directory back to the installation root.</summary>
        public const string Amd64ToRoot = @"..\..\..\..";

        private FakeVisualStudioInstall(string root)
        {
            Root = root;
            SharedAssemblies = Path.Combine(root, "SharedAssemblies");
            Bin = Path.Combine(root, "MSBuild", "Current", "Bin");
            Amd64 = Path.Combine(Bin, "amd64");
        }

        /// <summary>Root of the installation, the equivalent of <c>C:\Program Files\Microsoft Visual Studio\18\Preview</c>.</summary>
        public string Root { get; }

        /// <summary>Directory holding assemblies shared by the installation rather than deployed beside MSBuild.</summary>
        public string SharedAssemblies { get; }

        /// <summary>The 32-bit MSBuild bin directory.</summary>
        public string Bin { get; }

        /// <summary>The 64-bit MSBuild bin directory, which holds the canonical config.</summary>
        public string Amd64 { get; }

        public static FakeVisualStudioInstall Create(string root)
        {
            var install = new FakeVisualStudioInstall(root);

            Directory.CreateDirectory(install.SharedAssemblies);
            Directory.CreateDirectory(install.Amd64);

            // Only the existence of MSBuild.exe matters to config discovery, so an empty file is enough. Its
            // missing version resource reads as version 0, which also exercises the pre-17.1 compatibility path.
            File.WriteAllText(Path.Combine(install.Bin, "MSBuild.exe"), string.Empty);
            File.WriteAllText(Path.Combine(install.Amd64, "MSBuild.exe"), string.Empty);

            return install;
        }

        /// <summary>Writes the canonical <c>amd64\MSBuild.exe.config</c> around <paramref name="bindingContent"/>.</summary>
        public string WriteAmd64Config(string bindingContent) => WriteConfig(Amd64, ConfigXml(bindingContent));

        /// <summary>Writes the base <c>MSBuild.exe.config</c> around <paramref name="bindingContent"/>.</summary>
        public string WriteBinConfig(string bindingContent) => WriteConfig(Bin, ConfigXml(bindingContent));

        /// <summary>Writes arbitrary content as the canonical config, for malformed-config tests.</summary>
        public string WriteRawAmd64Config(string content) => WriteConfig(Amd64, content);

        public static string ConfigXml(string bindingContent) => $@"<?xml version=""1.0"" encoding=""utf-8""?>
<configuration>
  <runtime>
    <assemblyBinding xmlns=""urn:schemas-microsoft-com:asm.v1"">
{bindingContent}
    </assemblyBinding>
  </runtime>
</configuration>";

        private static string WriteConfig(string directory, string content)
        {
            string path = Path.Combine(directory, "MSBuild.exe.config");
            File.WriteAllText(path, content);
            return path;
        }
    }
}

#endif
