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
    public class MSBuildExeConfigResolverTests
    {
        private const string PublicKeyToken = "b03f5f7f11d50a3a";

        [Fact]
        public void FindConfigFilePath_UsesTheAdjacentConfigForAnExplicitAmd64Path() => RunInTemporaryDirectory(directory =>
        {
            string amd64Directory = Path.Combine(directory, "amd64");
            Directory.CreateDirectory(amd64Directory);
            File.WriteAllText(Path.Combine(amd64Directory, "MSBuild.exe"), string.Empty);
            File.WriteAllText(Path.Combine(directory, "MSBuild.exe.config"), string.Empty);

            MSBuildExeConfigResolver.FindConfigFilePath(new[] { amd64Directory })
                .ShouldBe(Path.Combine(amd64Directory, "MSBuild.exe.config"));
        });

        [Fact]
        public void FindConfigFilePath_PrefersAnExistingAmd64ConfigForABasePath() => RunInTemporaryDirectory(directory =>
        {
            File.WriteAllText(Path.Combine(directory, "MSBuild.exe"), string.Empty);
            File.WriteAllText(Path.Combine(directory, "MSBuild.exe.config"), string.Empty);
            string amd64Directory = Path.Combine(directory, "amd64");
            Directory.CreateDirectory(amd64Directory);
            File.WriteAllText(Path.Combine(amd64Directory, "MSBuild.exe.config"), string.Empty);

            MSBuildExeConfigResolver.FindConfigFilePath(new[] { directory })
                .ShouldBe(Path.Combine(amd64Directory, "MSBuild.exe.config"));
        });

        [Fact]
        public void FindConfigFilePath_FallsBackToTheAdjacentConfigOnlyWhenCanonicalIsAbsent() => RunInTemporaryDirectory(directory =>
        {
            File.WriteAllText(Path.Combine(directory, "MSBuild.exe"), string.Empty);
            File.WriteAllText(Path.Combine(directory, "MSBuild.exe.config"), string.Empty);

            MSBuildExeConfigResolver.FindConfigFilePath(new[] { directory })
                .ShouldBe(Path.Combine(directory, "MSBuild.exe.config"));
        });

        [Fact]
        public void GetCodeBaseCandidates_AppliesRedirectAndPreservesCodeBaseOrder()
        {
            AssemblyBindingPolicy policy = ReadPolicy(@"
<dependentAssembly>
  <assemblyIdentity name=""Foo"" publicKeyToken=""b03f5f7f11d50a3a"" culture=""neutral"" />
  <bindingRedirect oldVersion=""0.0.0.0-2.0.0.0"" newVersion=""2.0.0.0"" />
  <codeBase version=""2.0.0.0"" href=""first\Foo.dll"" />
  <codeBase version=""2.0.0.0"" href=""second\Foo.dll"" />
</dependentAssembly>");

            AssemblyCodeBaseCandidate[] candidates = MSBuildExeConfigResolver.GetCodeBaseCandidates(
                new AssemblyName($"Foo, Version=1.0.0.0, Culture=neutral, PublicKeyToken={PublicKeyToken}"),
                policy,
                @"C:\Config").ToArray();

            candidates.Select(c => c.Path).ShouldBe(new[]
            {
                @"C:\Config\first\Foo.dll",
                @"C:\Config\second\Foo.dll",
            });
            candidates.Select(c => c.EffectiveAssemblyName.Version).ShouldBe(
                new[] { new Version(2, 0, 0, 0), new Version(2, 0, 0, 0) });
        }

        [Fact]
        public void GetCodeBaseCandidates_QualifiesPartialRequestsForCodeBaseOnlyEntries()
        {
            const string fullName = "Resolver, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a";
            AssemblyBindingPolicy policy = ReadPolicy($@"
<dependentAssembly>
  <assemblyIdentity name=""Resolver"" publicKeyToken=""{PublicKeyToken}"" culture=""neutral"" />
  <codeBase version=""8.0.0.0"" href=""Resolver.dll"" />
</dependentAssembly>
<qualifyAssembly partialName=""Resolver"" fullName=""{fullName}"" />");

            AssemblyCodeBaseCandidate candidate = MSBuildExeConfigResolver.GetCodeBaseCandidates(
                new AssemblyName("Resolver"),
                policy,
                @"C:\Config").Single();

            candidate.Path.ShouldBe(@"C:\Config\Resolver.dll");
            candidate.EffectiveAssemblyName.FullName.ShouldBe(fullName);
        }

        [Fact]
        public void GetCodeBaseCandidates_UsesVersionlessCodeBasesOnlyForUnsignedAssemblies()
        {
            AssemblyBindingPolicy policy = ReadPolicy(@"
<dependentAssembly>
  <assemblyIdentity name=""Unsigned"" culture=""neutral"" />
  <codeBase href=""Unsigned.dll"" />
</dependentAssembly>");

            MSBuildExeConfigResolver.GetCodeBaseCandidates(
                new AssemblyName("Unsigned, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"),
                policy,
                @"C:\Config").Single().Path.ShouldBe(@"C:\Config\Unsigned.dll");

            MSBuildExeConfigResolver.GetCodeBaseCandidates(
                new AssemblyName($"Unsigned, Version=1.0.0.0, Culture=neutral, PublicKeyToken={PublicKeyToken}"),
                policy,
                @"C:\Config").ShouldBeEmpty();
        }

        [Fact]
        public void GetCodeBaseCandidates_DoesNotUseAQualifyAssemblyMappingToADifferentName()
        {
            AssemblyBindingPolicy policy = ReadPolicy(@"
<dependentAssembly>
  <assemblyIdentity name=""Different"" publicKeyToken=""b03f5f7f11d50a3a"" culture=""neutral"" />
  <codeBase version=""1.0.0.0"" href=""Different.dll"" />
</dependentAssembly>
<qualifyAssembly partialName=""Original"" fullName=""Different, Version=1.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a"" />");

            MSBuildExeConfigResolver.GetCodeBaseCandidates(
                new AssemblyName("Original"),
                policy,
                @"C:\Config").ShouldBeEmpty();
        }

        [Fact]
        public void HasCompatibleIdentity_RequiresConfiguredIdentityAndEffectiveVersion()
        {
            AssemblyBindingPolicy policy = ReadPolicy(@"
<dependentAssembly>
  <assemblyIdentity name=""Foo"" publicKeyToken=""b03f5f7f11d50a3a"" culture=""neutral"" processorArchitecture=""msil"" />
  <bindingRedirect oldVersion=""0.0.0.0-2.0.0.0"" newVersion=""2.0.0.0"" />
  <codeBase version=""2.0.0.0"" href=""Foo.dll"" />
</dependentAssembly>");

            AssemblyCodeBaseCandidate candidate = MSBuildExeConfigResolver.GetCodeBaseCandidates(
                new AssemblyName($"Foo, Version=1.0.0.0, Culture=neutral, PublicKeyToken={PublicKeyToken}"),
                policy,
                @"C:\Config").Single();

            candidate.HasCompatibleIdentity(new AssemblyName(
                $"Foo, Version=2.0.0.0, Culture=neutral, PublicKeyToken={PublicKeyToken}, processorArchitecture=MSIL"))
                .ShouldBeTrue();
            candidate.HasCompatibleIdentity(new AssemblyName(
                $"Foo, Version=1.0.0.0, Culture=neutral, PublicKeyToken={PublicKeyToken}, processorArchitecture=MSIL"))
                .ShouldBeFalse();
            candidate.HasCompatibleIdentity(new AssemblyName(
                $"Bar, Version=2.0.0.0, Culture=neutral, PublicKeyToken={PublicKeyToken}, processorArchitecture=MSIL"))
                .ShouldBeFalse();
        }

        private static AssemblyBindingPolicy ReadPolicy(string bindingContent) =>
            MSBuildExeConfigReader.ReadXml($@"<configuration><runtime>
<assemblyBinding xmlns=""urn:schemas-microsoft-com:asm.v1"">
{bindingContent}
</assemblyBinding>
</runtime></configuration>");

        private static void RunInTemporaryDirectory(Action<string> test)
        {
            string directory = Path.Combine(
                AppContext.BaseDirectory,
                nameof(MSBuildExeConfigResolverTests) + "_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);

            try
            {
                test(directory);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}

#endif
