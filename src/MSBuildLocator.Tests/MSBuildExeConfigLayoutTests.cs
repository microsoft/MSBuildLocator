// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if !NETCOREAPP

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Shouldly;
using Xunit;

namespace Microsoft.Build.Locator.Tests
{
    /// <summary>
    ///     Config selection and code base resolution against a directory layout shaped like a Visual Studio
    ///     installation, without requiring one to be installed.
    /// </summary>
    public class MSBuildExeConfigLayoutTests
    {
        private const string PublicKeyToken = "b03f5f7f11d50a3a";

        [Fact]
        public void CanonicalConfigResolvesCodeBasesBackToBinAndSharedAssemblies() => RunWithInstall(install =>
        {
            string sharedAssembly = Path.Combine(install.SharedAssemblies, "Shared.dll");
            string binAssembly = Path.Combine(install.Bin, "Local.dll");
            File.WriteAllText(sharedAssembly, string.Empty);
            File.WriteAllText(binAssembly, string.Empty);

            install.WriteAmd64Config($@"
      <dependentAssembly>
        <assemblyIdentity name=""Shared"" publicKeyToken=""{PublicKeyToken}"" culture=""neutral"" processorArchitecture=""msil"" />
        <bindingRedirect oldVersion=""0.0.0.0-9.0.0.0"" newVersion=""9.0.0.0"" />
        <codeBase version=""9.0.0.0"" href=""{FakeVisualStudioInstall.Amd64ToRoot}\SharedAssemblies\Shared.dll"" />
      </dependentAssembly>
      <dependentAssembly>
        <assemblyIdentity name=""Local"" publicKeyToken=""{PublicKeyToken}"" culture=""neutral"" />
        <codeBase version=""9.0.0.0"" href=""..\Local.dll"" />
      </dependentAssembly>");

            AssemblyBindingPolicy policy = ReadCanonicalPolicy(install, out string configDirectory);

            // A relative href in the amd64 config reaches the installation-wide shared assemblies directory.
            CodeBasePath(policy, configDirectory, "Shared", "1.0.0.0").ShouldBe(sharedAssembly);
            File.Exists(sharedAssembly).ShouldBeTrue();

            // And reaches back into the 32-bit bin directory the amd64 directory lives in.
            CodeBasePath(policy, configDirectory, "Local", "9.0.0.0").ShouldBe(binAssembly);
            File.Exists(binAssembly).ShouldBeTrue();
        });

        [Fact]
        public void RegisteringTheBinDirectoryUsesTheCanonicalAmd64Policy() => RunWithInstall(install =>
        {
            install.WriteBinConfig(Entry("OnlyInBaseConfig"));
            install.WriteAmd64Config(Entry("OnlyInAmd64Config"));

            AssemblyBindingPolicy policy = ReadCanonicalPolicy(install, out _);

            policy.DependentAssemblies.Select(entry => entry.Name).ShouldBe(new[] { "OnlyInAmd64Config" });
        });

        [Fact]
        public void RegisteringTheAmd64DirectoryUsesItsAdjacentPolicy() => RunWithInstall(install =>
        {
            install.WriteBinConfig(Entry("OnlyInBaseConfig"));
            install.WriteAmd64Config(Entry("OnlyInAmd64Config"));

            string configFilePath = MSBuildExeConfigResolver.FindConfigFilePath(new[] { install.Amd64 });

            configFilePath.ShouldBe(Path.Combine(install.Amd64, "MSBuild.exe.config"));
            MSBuildExeConfigReader.Read(configFilePath).DependentAssemblies.Select(entry => entry.Name)
                .ShouldBe(new[] { "OnlyInAmd64Config" });
        });

        [Fact]
        public void AnOlderLayoutWithoutAnAmd64ConfigUsesTheBaseConfig() => RunWithInstall(install =>
        {
            install.WriteBinConfig(Entry("OnlyInBaseConfig"));

            AssemblyBindingPolicy policy = ReadCanonicalPolicy(install, out string configDirectory);

            configDirectory.ShouldBe(install.Bin);
            policy.DependentAssemblies.Select(entry => entry.Name).ShouldBe(new[] { "OnlyInBaseConfig" });
        });

        [Fact]
        public void AConfigThatCannotBeParsedLeavesNoPolicy() => RunWithInstall(install =>
        {
            install.WriteBinConfig(Entry("OnlyInBaseConfig"));
            install.WriteRawAmd64Config("<configuration><runtime>truncated");

            // The canonical config still wins; being unusable simply means no policy, not a base config fallback.
            ReadCanonicalPolicy(install, out _).IsEmpty.ShouldBeTrue();
        });

        private static string Entry(string name) => $@"
      <dependentAssembly>
        <assemblyIdentity name=""{name}"" publicKeyToken=""{PublicKeyToken}"" culture=""neutral"" />
        <codeBase version=""9.0.0.0"" href=""{name}.dll"" />
      </dependentAssembly>";

        private static AssemblyBindingPolicy ReadCanonicalPolicy(FakeVisualStudioInstall install, out string configDirectory)
        {
            string configFilePath = MSBuildExeConfigResolver.FindConfigFilePath(new[] { install.Bin });
            configDirectory = Path.GetDirectoryName(configFilePath);
            return MSBuildExeConfigReader.Read(configFilePath);
        }

        private static string CodeBasePath(
            AssemblyBindingPolicy policy,
            string configDirectory,
            string name,
            string version) => MSBuildExeConfigResolver.GetCodeBaseCandidates(
                new AssemblyName($"{name}, Version={version}, Culture=neutral, PublicKeyToken={PublicKeyToken}"),
                policy,
                configDirectory).Single().Path;

        private static void RunWithInstall(Action<FakeVisualStudioInstall> test) =>
            TemporaryDirectory.Run(nameof(MSBuildExeConfigLayoutTests), directory =>
                test(FakeVisualStudioInstall.Create(Path.Combine(directory, "VisualStudio"))));
    }
}

#endif
