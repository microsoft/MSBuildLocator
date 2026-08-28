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

        [Fact]
        public void GetCodeBaseCandidates_DistinguishesEntriesThatShareASimpleName()
        {
            AssemblyBindingPolicy policy = ReadPolicy($@"
<dependentAssembly>
  <assemblyIdentity name=""Foo"" publicKeyToken=""{PublicKeyToken}"" culture=""neutral"" processorArchitecture=""msil"" />
  <codeBase version=""1.0.0.0"" href=""msil\Foo.dll"" />
</dependentAssembly>
<dependentAssembly>
  <assemblyIdentity name=""Foo"" publicKeyToken=""{PublicKeyToken}"" culture=""neutral"" processorArchitecture=""amd64"" />
  <codeBase version=""1.0.0.0"" href=""amd64\Foo.dll"" />
</dependentAssembly>
<dependentAssembly>
  <assemblyIdentity name=""Foo"" publicKeyToken=""31bf3856ad364e35"" culture=""neutral"" />
  <codeBase version=""1.0.0.0"" href=""other\Foo.dll"" />
</dependentAssembly>");

            // AssemblyResolve usually reports no architecture, which cannot rule out either architecture-qualified
            // entry, so both remain candidates in config order and the manifest check decides.
            CodeBasePaths(policy, $"Foo, Version=1.0.0.0, Culture=neutral, PublicKeyToken={PublicKeyToken}")
                .ShouldBe(new[] { @"C:\Config\msil\Foo.dll", @"C:\Config\amd64\Foo.dll" });

            CodeBasePaths(policy, $"Foo, Version=1.0.0.0, Culture=neutral, PublicKeyToken={PublicKeyToken}, processorArchitecture=amd64")
                .ShouldBe(new[] { @"C:\Config\amd64\Foo.dll" });

            CodeBasePaths(policy, "Foo, Version=1.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35")
                .ShouldBe(new[] { @"C:\Config\other\Foo.dll" });
        }

        [Fact]
        public void GetCodeBaseCandidates_RequiresTheConfiguredPublicKeyToken()
        {
            AssemblyBindingPolicy policy = ReadPolicy($@"
<dependentAssembly>
  <assemblyIdentity name=""Foo"" publicKeyToken=""{PublicKeyToken}"" culture=""neutral"" />
  <codeBase version=""1.0.0.0"" href=""Foo.dll"" />
</dependentAssembly>");

            CodeBasePaths(policy, $"Foo, Version=1.0.0.0, Culture=neutral, PublicKeyToken={PublicKeyToken}")
                .ShouldBe(new[] { @"C:\Config\Foo.dll" });
            CodeBasePaths(policy, "Foo, Version=1.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35").ShouldBeEmpty();
            CodeBasePaths(policy, "Foo, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null").ShouldBeEmpty();
        }

        [Fact]
        public void GetCodeBaseCandidates_TreatsAnUnspecifiedOrNullTokenAsUnsigned()
        {
            AssemblyBindingPolicy policy = ReadPolicy(@"
<dependentAssembly>
  <assemblyIdentity name=""Foo"" publicKeyToken=""null"" culture=""neutral"" />
  <codeBase version=""1.0.0.0"" href=""Foo.dll"" />
</dependentAssembly>");

            CodeBasePaths(policy, "Foo, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null")
                .ShouldBe(new[] { @"C:\Config\Foo.dll" });
            CodeBasePaths(policy, $"Foo, Version=1.0.0.0, Culture=neutral, PublicKeyToken={PublicKeyToken}").ShouldBeEmpty();
        }

        [Fact]
        public void GetCodeBaseCandidates_RequiresTheConfiguredCulture()
        {
            AssemblyBindingPolicy policy = ReadPolicy($@"
<dependentAssembly>
  <assemblyIdentity name=""Resources"" publicKeyToken=""{PublicKeyToken}"" culture=""de"" />
  <codeBase version=""1.0.0.0"" href=""de\Resources.dll"" />
</dependentAssembly>
<dependentAssembly>
  <assemblyIdentity name=""Neutral"" publicKeyToken=""{PublicKeyToken}"" culture=""neutral"" />
  <codeBase version=""1.0.0.0"" href=""Neutral.dll"" />
</dependentAssembly>");

            CodeBasePaths(policy, $"Resources, Version=1.0.0.0, Culture=de, PublicKeyToken={PublicKeyToken}")
                .ShouldBe(new[] { @"C:\Config\de\Resources.dll" });
            CodeBasePaths(policy, $"Resources, Version=1.0.0.0, Culture=neutral, PublicKeyToken={PublicKeyToken}").ShouldBeEmpty();
            CodeBasePaths(policy, $"Neutral, Version=1.0.0.0, Culture=de, PublicKeyToken={PublicKeyToken}").ShouldBeEmpty();
        }

        [Fact]
        public void GetCodeBaseCandidates_SelectsTheCodeBaseForTheEffectiveVersion()
        {
            AssemblyBindingPolicy policy = ReadPolicy($@"
<dependentAssembly>
  <assemblyIdentity name=""Foo"" publicKeyToken=""{PublicKeyToken}"" culture=""neutral"" />
  <bindingRedirect oldVersion=""0.0.0.0-1.9.9.9"" newVersion=""2.0.0.0"" />
  <bindingRedirect oldVersion=""2.0.0.1-2.5.0.0"" newVersion=""3.0.0.0"" />
  <codeBase version=""2.0.0.0"" href=""two\Foo.dll"" />
  <codeBase version=""3.0.0.0"" href=""three\Foo.dll"" />
  <codeBase version=""4.0.0.0"" href=""four\Foo.dll"" />
</dependentAssembly>");

            CodeBasePaths(policy, Requested("1.0.0.0")).ShouldBe(new[] { @"C:\Config\two\Foo.dll" });
            CodeBasePaths(policy, Requested("2.3.0.0")).ShouldBe(new[] { @"C:\Config\three\Foo.dll" });

            // A version no redirect covers keeps its own version, which is how a standalone code base is reached.
            CodeBasePaths(policy, Requested("4.0.0.0")).ShouldBe(new[] { @"C:\Config\four\Foo.dll" });
            CodeBasePaths(policy, Requested("5.0.0.0")).ShouldBeEmpty();

            string Requested(string version) =>
                $"Foo, Version={version}, Culture=neutral, PublicKeyToken={PublicKeyToken}";
        }

        [Fact]
        public void GetCodeBaseCandidates_ComparesShortConfiguredVersionsAsFourComponents()
        {
            AssemblyBindingPolicy policy = ReadPolicy($@"
<dependentAssembly>
  <assemblyIdentity name=""Foo"" publicKeyToken=""{PublicKeyToken}"" culture=""neutral"" />
  <bindingRedirect oldVersion=""0.0-9"" newVersion=""9"" />
  <codeBase version=""9"" href=""Foo.dll"" />
</dependentAssembly>");

            AssemblyCodeBaseCandidate candidate = SingleCandidate(
                policy, $"Foo, Version=1.0.0.0, Culture=neutral, PublicKeyToken={PublicKeyToken}");

            candidate.Path.ShouldBe(@"C:\Config\Foo.dll");
            candidate.EffectiveAssemblyName.Version.ShouldBe(new Version(9, 0, 0, 0));
        }

        [Fact]
        public void GetCodeBaseCandidates_ProducesNothingForARedirectWithoutACodeBase()
        {
            AssemblyBindingPolicy policy = ReadPolicy($@"
<dependentAssembly>
  <assemblyIdentity name=""Foo"" publicKeyToken=""{PublicKeyToken}"" culture=""neutral"" />
  <bindingRedirect oldVersion=""0.0.0.0-2.0.0.0"" newVersion=""2.0.0.0"" />
</dependentAssembly>");

            CodeBasePaths(policy, $"Foo, Version=1.0.0.0, Culture=neutral, PublicKeyToken={PublicKeyToken}").ShouldBeEmpty();
        }

        [Fact]
        public void GetCodeBaseCandidates_AcceptsAbsolutePathsAndFileUris()
        {
            AssemblyBindingPolicy policy = ReadPolicy($@"
<dependentAssembly>
  <assemblyIdentity name=""Foo"" publicKeyToken=""{PublicKeyToken}"" culture=""neutral"" />
  <codeBase version=""1.0.0.0"" href=""C:\Absolute\Foo.dll"" />
  <codeBase version=""1.0.0.0"" href=""file:///C:/Uri/Foo.dll"" />
  <codeBase version=""1.0.0.0"" href=""relative\..\normalized\Foo.dll"" />
</dependentAssembly>");

            CodeBasePaths(policy, $"Foo, Version=1.0.0.0, Culture=neutral, PublicKeyToken={PublicKeyToken}").ShouldBe(new[]
            {
                @"C:\Absolute\Foo.dll",
                @"C:\Uri\Foo.dll",
                @"C:\Config\normalized\Foo.dll",
            });
        }

        [Fact]
        public void GetCodeBaseCandidates_SkipsCodeBasesThatAreNotLocalFiles()
        {
            AssemblyBindingPolicy policy = ReadPolicy($@"
<dependentAssembly>
  <assemblyIdentity name=""Foo"" publicKeyToken=""{PublicKeyToken}"" culture=""neutral"" />
  <codeBase version=""1.0.0.0"" href=""http://example.invalid/Foo.dll"" />
  <codeBase version=""1.0.0.0"" href=""local\Foo.dll"" />
</dependentAssembly>");

            CodeBasePaths(policy, $"Foo, Version=1.0.0.0, Culture=neutral, PublicKeyToken={PublicKeyToken}")
                .ShouldBe(new[] { @"C:\Config\local\Foo.dll" });
        }

        [Fact]
        public void GetCodeBaseCandidates_SkipsRelativeCodeBasesWithoutAConfigDirectory()
        {
            AssemblyBindingPolicy policy = ReadPolicy($@"
<dependentAssembly>
  <assemblyIdentity name=""Foo"" publicKeyToken=""{PublicKeyToken}"" culture=""neutral"" />
  <codeBase version=""1.0.0.0"" href=""Foo.dll"" />
</dependentAssembly>");

            MSBuildExeConfigResolver.GetCodeBaseCandidates(
                new AssemblyName($"Foo, Version=1.0.0.0, Culture=neutral, PublicKeyToken={PublicKeyToken}"),
                policy,
                configDirectory: null).ShouldBeEmpty();
        }

        [Theory]
        [InlineData("Resolver")]
        [InlineData("Resolver, Culture=neutral")]
        [InlineData("Resolver, Culture=neutral, PublicKeyToken=null")]
        public void GetCodeBaseCandidates_QualifiesPartialRequestsInEveryForm(string partialName)
        {
            AssemblyCodeBaseCandidate candidate = SingleCandidate(QualifiedResolverPolicy, partialName);

            candidate.Path.ShouldBe(@"C:\Config\Resolver.dll");
            candidate.EffectiveAssemblyName.Version.ShouldBe(new Version(8, 0, 0, 0));
        }

        [Fact]
        public void GetCodeBaseCandidates_DoesNotQualifyARequestThatAlreadyStatesAnIdentity()
        {
            // Like the runtime, a qualifyAssembly mapping only completes a partial reference. A request that
            // already carries a version binds on its own, and here its unsigned identity cannot match.
            CodeBasePaths(QualifiedResolverPolicy, "Resolver, Version=8.0.0.0").ShouldBeEmpty();
        }

        [Fact]
        public void GetCodeBaseCandidates_ReturnsNothingWithoutAUsableRequestOrPolicy()
        {
            var assemblyName = new AssemblyName($"Foo, Version=1.0.0.0, Culture=neutral, PublicKeyToken={PublicKeyToken}");

            MSBuildExeConfigResolver.GetCodeBaseCandidates(null, AssemblyBindingPolicy.Empty, @"C:\Config").ShouldBeEmpty();
            MSBuildExeConfigResolver.GetCodeBaseCandidates(assemblyName, null, @"C:\Config").ShouldBeEmpty();
            MSBuildExeConfigResolver.GetCodeBaseCandidates(assemblyName, AssemblyBindingPolicy.Empty, @"C:\Config").ShouldBeEmpty();
        }

        [Fact]
        public void HasCompatibleIdentity_RejectsAMismatchedTokenOrCulture()
        {
            AssemblyBindingPolicy policy = ReadPolicy($@"
<dependentAssembly>
  <assemblyIdentity name=""Foo"" publicKeyToken=""{PublicKeyToken}"" culture=""neutral"" />
  <codeBase version=""1.0.0.0"" href=""Foo.dll"" />
</dependentAssembly>");

            AssemblyCodeBaseCandidate candidate = SingleCandidate(
                policy, $"Foo, Version=1.0.0.0, Culture=neutral, PublicKeyToken={PublicKeyToken}");

            candidate.HasCompatibleIdentity(new AssemblyName($"Foo, Version=1.0.0.0, Culture=neutral, PublicKeyToken={PublicKeyToken}"))
                .ShouldBeTrue();
            candidate.HasCompatibleIdentity(new AssemblyName("Foo, Version=1.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35"))
                .ShouldBeFalse();
            candidate.HasCompatibleIdentity(new AssemblyName("Foo, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"))
                .ShouldBeFalse();
            candidate.HasCompatibleIdentity(new AssemblyName($"Foo, Version=1.0.0.0, Culture=de, PublicKeyToken={PublicKeyToken}"))
                .ShouldBeFalse();
            candidate.HasCompatibleIdentity(null).ShouldBeFalse();
        }

        [Fact]
        public void HasCompatibleIdentity_RequiresTheConfiguredProcessorArchitecture()
        {
            AssemblyBindingPolicy policy = ReadPolicy($@"
<dependentAssembly>
  <assemblyIdentity name=""Foo"" publicKeyToken=""{PublicKeyToken}"" culture=""neutral"" processorArchitecture=""amd64"" />
  <codeBase version=""1.0.0.0"" href=""Foo.dll"" />
</dependentAssembly>");

            AssemblyCodeBaseCandidate candidate = SingleCandidate(
                policy, $"Foo, Version=1.0.0.0, Culture=neutral, PublicKeyToken={PublicKeyToken}");

            candidate.HasCompatibleIdentity(new AssemblyName(
                $"Foo, Version=1.0.0.0, Culture=neutral, PublicKeyToken={PublicKeyToken}, processorArchitecture=amd64"))
                .ShouldBeTrue();
            candidate.HasCompatibleIdentity(new AssemblyName(
                $"Foo, Version=1.0.0.0, Culture=neutral, PublicKeyToken={PublicKeyToken}, processorArchitecture=MSIL"))
                .ShouldBeFalse();
        }

        [Fact]
        public void HasCompatibleIdentity_AcceptsAnyVersionWhenTheRequestDidNotStateOne()
        {
            AssemblyBindingPolicy policy = ReadPolicy(@"
<dependentAssembly>
  <assemblyIdentity name=""Unsigned"" culture=""neutral"" />
  <codeBase href=""Unsigned.dll"" />
</dependentAssembly>");

            AssemblyCodeBaseCandidate candidate = SingleCandidate(policy, "Unsigned");

            candidate.EffectiveAssemblyName.Version.ShouldBeNull();
            candidate.HasCompatibleIdentity(new AssemblyName("Unsigned, Version=3.0.0.0, Culture=neutral, PublicKeyToken=null"))
                .ShouldBeTrue();
            candidate.HasCompatibleIdentity(new AssemblyName($"Unsigned, Version=3.0.0.0, Culture=neutral, PublicKeyToken={PublicKeyToken}"))
                .ShouldBeFalse();
        }

        [Fact]
        public void FindConfigFilePath_SkipsSearchPathsWithoutMSBuildExe() => RunInTemporaryDirectory(directory =>
        {
            string nugetDirectory = Path.Combine(directory, "NuGet");
            string binDirectory = Path.Combine(directory, "Bin");
            Directory.CreateDirectory(nugetDirectory);
            Directory.CreateDirectory(binDirectory);
            File.WriteAllText(Path.Combine(binDirectory, "MSBuild.exe"), string.Empty);
            File.WriteAllText(Path.Combine(binDirectory, "MSBuild.exe.config"), string.Empty);

            MSBuildExeConfigResolver.FindConfigFilePath(new[] { nugetDirectory, binDirectory })
                .ShouldBe(Path.Combine(binDirectory, "MSBuild.exe.config"));
        });

        [Fact]
        public void FindConfigFilePath_IsNullWhenNoSearchPathContainsMSBuildExe() => RunInTemporaryDirectory(directory =>
            MSBuildExeConfigResolver.FindConfigFilePath(new[] { directory }).ShouldBeNull());

        [Fact]
        public void FindConfigFilePath_NamesTheAdjacentConfigEvenWhenItDoesNotExist() => RunInTemporaryDirectory(directory =>
        {
            File.WriteAllText(Path.Combine(directory, "MSBuild.exe"), string.Empty);

            string configFilePath = MSBuildExeConfigResolver.FindConfigFilePath(new[] { directory });

            configFilePath.ShouldBe(Path.Combine(directory, "MSBuild.exe.config"));

            // A deployment without a config declares no policy, which leaves ordinary probing in charge.
            MSBuildExeConfigReader.Read(configFilePath).IsEmpty.ShouldBeTrue();
        });

        /// <summary>
        ///     A code base only reachable by qualifying a partial request, as the amd64 config of a Visual Studio
        ///     installation declares for the .NET SDK resolver.
        /// </summary>
        private static AssemblyBindingPolicy QualifiedResolverPolicy => ReadPolicy($@"
<dependentAssembly>
  <assemblyIdentity name=""Resolver"" publicKeyToken=""{PublicKeyToken}"" culture=""neutral"" />
  <codeBase version=""8.0.0.0"" href=""Resolver.dll"" />
</dependentAssembly>
<qualifyAssembly partialName=""Resolver"" fullName=""Resolver, Version=8.0.0.0, Culture=neutral, PublicKeyToken={PublicKeyToken}"" />");

        private static string[] CodeBasePaths(AssemblyBindingPolicy policy, string assemblyName) =>
            MSBuildExeConfigResolver.GetCodeBaseCandidates(new AssemblyName(assemblyName), policy, @"C:\Config")
                .Select(candidate => candidate.Path).ToArray();

        private static AssemblyCodeBaseCandidate SingleCandidate(AssemblyBindingPolicy policy, string assemblyName) =>
            MSBuildExeConfigResolver.GetCodeBaseCandidates(new AssemblyName(assemblyName), policy, @"C:\Config").Single();

        private static AssemblyBindingPolicy ReadPolicy(string bindingContent) =>
            MSBuildExeConfigReader.ReadXml($@"<configuration><runtime>
<assemblyBinding xmlns=""urn:schemas-microsoft-com:asm.v1"">
{bindingContent}
</assemblyBinding>
</runtime></configuration>");

        private static void RunInTemporaryDirectory(Action<string> test) =>
            TemporaryDirectory.Run(nameof(MSBuildExeConfigResolverTests), test);
    }
}

#endif
