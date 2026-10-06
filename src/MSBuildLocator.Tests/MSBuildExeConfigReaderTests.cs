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
    public class MSBuildExeConfigReaderTests
    {
        /// <summary>
        ///     A config shaped like the MSBuild.exe.config of a Visual Studio installation that relocates
        ///     dependencies out of the MSBuild bin directory.
        /// </summary>
        private const string SampleConfig = @"<?xml version=""1.0"" encoding=""utf-8""?>
<configuration>
  <runtime>
    <assemblyBinding xmlns=""urn:schemas-microsoft-com:asm.v1"">
      <dependentAssembly>
        <assemblyIdentity name=""System.Collections.Immutable"" publicKeyToken=""b03f5f7f11d50a3a"" culture=""neutral"" processorArchitecture=""msil"" />
        <bindingRedirect oldVersion=""0.0.0.0-9.0.0.0"" newVersion=""9.0.0.0"" />
        <codeBase version=""9.0.0.0"" href=""..\..\..\..\SharedAssemblies\System.Collections.Immutable.dll"" />
      </dependentAssembly>
      <dependentAssembly>
        <assemblyIdentity name=""Microsoft.DotNet.MSBuildSdkResolver"" publicKeyToken=""adb9793829ddae60"" culture=""neutral"" />
        <codeBase version=""8.0.0.0"" href=""SdkResolvers\Microsoft.DotNet.MSBuildSdkResolver\Microsoft.DotNet.MSBuildSdkResolver.dll"" />
      </dependentAssembly>
      <dependentAssembly>
        <assemblyIdentity name=""RedirectOnly"" publicKeyToken=""b03f5f7f11d50a3a"" culture=""neutral"" />
        <bindingRedirect oldVersion=""0.0.0.0-2.0.0.0"" newVersion=""2.0.0.0"" />
      </dependentAssembly>
      <qualifyAssembly partialName=""Microsoft.DotNet.MSBuildSdkResolver"" fullName=""Microsoft.DotNet.MSBuildSdkResolver, Version=8.0.0.0, Culture=neutral, PublicKeyToken=adb9793829ddae60"" />
    </assemblyBinding>
  </runtime>
</configuration>";

        [Fact]
        public void ReadXml_CapturesTheFullConfiguredIdentity()
        {
            DependentAssembly entry = MSBuildExeConfigReader.ReadXml(SampleConfig).DependentAssemblies
                .Single(d => d.Name == "System.Collections.Immutable");

            entry.PublicKeyToken.ShouldBe("b03f5f7f11d50a3a");
            entry.Culture.ShouldBe("neutral");
            entry.ProcessorArchitecture.ShouldBe("msil");
        }

        [Fact]
        public void ReadXml_KeepsEntryWithOnlyACodeBase()
        {
            DependentAssembly entry = MSBuildExeConfigReader.ReadXml(SampleConfig).DependentAssemblies
                .Single(d => d.Name == "Microsoft.DotNet.MSBuildSdkResolver");

            entry.BindingRedirects.ShouldBeEmpty();
            entry.CodeBases.Single().Version.ShouldBe(new Version(8, 0, 0, 0));
            entry.CodeBases.Single().Href.ShouldBe(@"SdkResolvers\Microsoft.DotNet.MSBuildSdkResolver\Microsoft.DotNet.MSBuildSdkResolver.dll");
        }

        [Fact]
        public void ReadXml_KeepsEntryWithOnlyABindingRedirect()
        {
            DependentAssembly entry = MSBuildExeConfigReader.ReadXml(SampleConfig).DependentAssemblies
                .Single(d => d.Name == "RedirectOnly");

            entry.CodeBases.ShouldBeEmpty();
            entry.BindingRedirects.Single().NewVersion.ShouldBe(new Version(2, 0, 0, 0));
        }

        [Fact]
        public void ReadXml_KeepsEveryRedirectAndCodeBaseOfAnEntryInConfigOrder()
        {
            const string xml = @"<configuration><runtime>
  <assemblyBinding xmlns=""urn:schemas-microsoft-com:asm.v1"">
    <dependentAssembly>
      <assemblyIdentity name=""Foo"" />
      <bindingRedirect oldVersion=""0.0.0.0-1.0.0.0"" newVersion=""1.0.0.0"" />
      <bindingRedirect oldVersion=""1.0.0.1-2.0.0.0"" newVersion=""2.0.0.0"" />
      <codeBase version=""1.0.0.0"" href=""one\Foo.dll"" />
      <codeBase version=""2.0.0.0"" href=""two\Foo.dll"" />
    </dependentAssembly>
  </assemblyBinding>
</runtime></configuration>";

            DependentAssembly entry = MSBuildExeConfigReader.ReadXml(xml).DependentAssemblies.Single();

            entry.BindingRedirects.Select(r => r.NewVersion)
                .ShouldBe(new[] { new Version(1, 0, 0, 0), new Version(2, 0, 0, 0) });
            entry.CodeBases.Select(c => c.Href).ShouldBe(new[] { @"one\Foo.dll", @"two\Foo.dll" });
        }

        [Fact]
        public void ReadXml_NormalizesShortVersionsToFourComponents()
        {
            const string xml = @"<configuration><runtime>
  <assemblyBinding xmlns=""urn:schemas-microsoft-com:asm.v1"">
    <dependentAssembly>
      <assemblyIdentity name=""Foo"" />
      <bindingRedirect oldVersion=""0.0-9"" newVersion=""9.0"" />
      <codeBase version=""9"" href=""Foo.dll"" />
    </dependentAssembly>
  </assemblyBinding>
</runtime></configuration>";

            DependentAssembly entry = MSBuildExeConfigReader.ReadXml(xml).DependentAssemblies.Single();
            AssemblyBindingRedirect redirect = entry.BindingRedirects.Single();

            redirect.OldVersionLow.ShouldBe(new Version(0, 0, 0, 0));
            redirect.OldVersionHigh.ShouldBe(new Version(9, 0, 0, 0));
            redirect.NewVersion.ShouldBe(new Version(9, 0, 0, 0));
            entry.CodeBases.Single().Version.ShouldBe(new Version(9, 0, 0, 0));
        }

        [Fact]
        public void ReadXml_SingleOldVersionBoundsTheRangeOnBothSides()
        {
            const string xml = @"<configuration><runtime>
  <assemblyBinding xmlns=""urn:schemas-microsoft-com:asm.v1"">
    <dependentAssembly>
      <assemblyIdentity name=""Foo"" />
      <bindingRedirect oldVersion=""1.2.3.4"" newVersion=""2.0.0.0"" />
    </dependentAssembly>
  </assemblyBinding>
</runtime></configuration>";

            AssemblyBindingRedirect redirect = MSBuildExeConfigReader.ReadXml(xml).DependentAssemblies
                .Single().BindingRedirects.Single();

            redirect.OldVersionLow.ShouldBe(new Version(1, 2, 3, 4));
            redirect.OldVersionHigh.ShouldBe(new Version(1, 2, 3, 4));
        }

        [Fact]
        public void ReadXml_InvalidRedirectDoesNotDiscardValidSiblings()
        {
            const string xml = @"<configuration><runtime>
  <assemblyBinding xmlns=""urn:schemas-microsoft-com:asm.v1"">
    <dependentAssembly>
      <assemblyIdentity name=""Foo"" />
      <bindingRedirect oldVersion=""2.0.0.0-1.0.0.0"" newVersion=""1.0.0.0"" />
      <bindingRedirect oldVersion=""not.a.version"" newVersion=""1.0.0.0"" />
      <bindingRedirect oldVersion=""0.0.0.0-1.0.0.0"" />
      <bindingRedirect oldVersion=""0.0.0.0-3.0.0.0"" newVersion=""3.0.0.0"" />
    </dependentAssembly>
  </assemblyBinding>
</runtime></configuration>";

            AssemblyBindingRedirect redirect = MSBuildExeConfigReader.ReadXml(xml).DependentAssemblies
                .Single().BindingRedirects.Single();

            redirect.NewVersion.ShouldBe(new Version(3, 0, 0, 0));
        }

        [Fact]
        public void ReadXml_InvalidCodeBaseDoesNotDiscardValidSiblings()
        {
            const string xml = @"<configuration><runtime>
  <assemblyBinding xmlns=""urn:schemas-microsoft-com:asm.v1"">
    <dependentAssembly>
      <assemblyIdentity name=""Foo"" />
      <codeBase version=""1.2.3.4.5"" href=""bad\Foo.dll"" />
      <codeBase version=""1.0.0.0"" />
      <codeBase version=""2.0.0.0"" href=""good\Foo.dll"" />
    </dependentAssembly>
  </assemblyBinding>
</runtime></configuration>";

            AssemblyCodeBase codeBase = MSBuildExeConfigReader.ReadXml(xml).DependentAssemblies
                .Single().CodeBases.Single();

            codeBase.Href.ShouldBe(@"good\Foo.dll");
        }

        [Fact]
        public void ReadXml_CodeBaseWithoutVersionHasNoVersion()
        {
            const string xml = @"<configuration><runtime>
  <assemblyBinding xmlns=""urn:schemas-microsoft-com:asm.v1"">
    <dependentAssembly>
      <assemblyIdentity name=""Unsigned"" />
      <codeBase href=""Unsigned.dll"" />
    </dependentAssembly>
  </assemblyBinding>
</runtime></configuration>";

            AssemblyCodeBase codeBase = MSBuildExeConfigReader.ReadXml(xml).DependentAssemblies
                .Single().CodeBases.Single();

            codeBase.Version.ShouldBeNull();
            codeBase.Href.ShouldBe("Unsigned.dll");
        }

        [Fact]
        public void ReadXml_KeepsDuplicateSimpleNamesInConfigOrder()
        {
            const string xml = @"<configuration><runtime>
  <assemblyBinding xmlns=""urn:schemas-microsoft-com:asm.v1"">
    <dependentAssembly>
      <assemblyIdentity name=""Foo"" publicKeyToken=""b03f5f7f11d50a3a"" processorArchitecture=""msil"" />
      <codeBase version=""1.0.0.0"" href=""msil\Foo.dll"" />
    </dependentAssembly>
    <dependentAssembly>
      <assemblyIdentity name=""Foo"" publicKeyToken=""b03f5f7f11d50a3a"" processorArchitecture=""amd64"" />
      <codeBase version=""1.0.0.0"" href=""amd64\Foo.dll"" />
    </dependentAssembly>
    <dependentAssembly>
      <assemblyIdentity name=""Foo"" publicKeyToken=""31bf3856ad364e35"" />
      <codeBase version=""1.0.0.0"" href=""other\Foo.dll"" />
    </dependentAssembly>
  </assemblyBinding>
</runtime></configuration>";

            var entries = MSBuildExeConfigReader.ReadXml(xml).DependentAssemblies;

            entries.Count.ShouldBe(3);
            entries.Select(e => e.ProcessorArchitecture).ShouldBe(new[] { "msil", "amd64", null });
            entries.Select(e => e.CodeBases.Single().Href)
                .ShouldBe(new[] { @"msil\Foo.dll", @"amd64\Foo.dll", @"other\Foo.dll" });
        }

        [Fact]
        public void ReadXml_SkipsEntriesThatCannotAffectResolution()
        {
            const string xml = @"<configuration><runtime>
  <assemblyBinding xmlns=""urn:schemas-microsoft-com:asm.v1"">
    <dependentAssembly>
      <assemblyIdentity publicKeyToken=""b03f5f7f11d50a3a"" />
      <codeBase version=""1.0.0.0"" href=""Nameless.dll"" />
    </dependentAssembly>
    <dependentAssembly>
      <assemblyIdentity name=""PolicyOnly"" />
      <publisherPolicy apply=""no"" />
    </dependentAssembly>
    <dependentAssembly>
      <bindingRedirect oldVersion=""0.0.0.0-1.0.0.0"" newVersion=""1.0.0.0"" />
    </dependentAssembly>
    <dependentAssembly>
      <assemblyIdentity name=""Kept"" />
      <codeBase version=""1.0.0.0"" href=""Kept.dll"" />
    </dependentAssembly>
  </assemblyBinding>
</runtime></configuration>";

            MSBuildExeConfigReader.ReadXml(xml).DependentAssemblies.Single().Name.ShouldBe("Kept");
        }

        [Fact]
        public void ReadXml_CombinesMultipleAssemblyBindingSections()
        {
            const string xml = @"<configuration><runtime>
  <assemblyBinding xmlns=""urn:schemas-microsoft-com:asm.v1"">
    <dependentAssembly>
      <assemblyIdentity name=""First"" />
      <codeBase version=""1.0.0.0"" href=""First.dll"" />
    </dependentAssembly>
  </assemblyBinding>
  <assemblyBinding xmlns=""urn:schemas-microsoft-com:asm.v1"">
    <dependentAssembly>
      <assemblyIdentity name=""Second"" />
      <codeBase version=""1.0.0.0"" href=""Second.dll"" />
    </dependentAssembly>
  </assemblyBinding>
</runtime></configuration>";

            MSBuildExeConfigReader.ReadXml(xml).DependentAssemblies.Select(e => e.Name)
                .ShouldBe(new[] { "First", "Second" });
        }

        [Fact]
        public void ReadXml_IgnoresElementsOutsideTheAssemblyBindingNamespace()
        {
            const string xml = @"<configuration><runtime>
  <assemblyBinding>
    <dependentAssembly>
      <assemblyIdentity name=""Foo"" />
      <codeBase version=""1.0.0.0"" href=""Foo.dll"" />
    </dependentAssembly>
  </assemblyBinding>
</runtime></configuration>";

            MSBuildExeConfigReader.ReadXml(xml).IsEmpty.ShouldBeTrue();
        }

        [Fact]
        public void ReadXml_ParsesQualifyAssembly()
        {
            AssemblyName qualified = MSBuildExeConfigReader.ReadXml(SampleConfig)
                .QualifiedAssemblies["microsoft.dotnet.msbuildsdkresolver"];

            qualified.Name.ShouldBe("Microsoft.DotNet.MSBuildSdkResolver");
            qualified.Version.ShouldBe(new Version(8, 0, 0, 0));
            qualified.CultureName.ShouldBe(string.Empty);
            string.Concat(qualified.GetPublicKeyToken().Select(b => b.ToString("x2")))
                .ShouldBe("adb9793829ddae60");
        }

        [Fact]
        public void ReadXml_SkipsUnusableQualifyAssemblyEntries()
        {
            const string xml = @"<configuration><runtime>
  <assemblyBinding xmlns=""urn:schemas-microsoft-com:asm.v1"">
    <qualifyAssembly partialName=""NoFullName"" />
    <qualifyAssembly fullName=""NoPartialName, Version=1.0.0.0"" />
    <qualifyAssembly partialName=""Malformed"" fullName=""Malformed, Version=not.a.version"" />
    <qualifyAssembly partialName=""Good"" fullName=""Good, Version=1.0.0.0"" />
  </assemblyBinding>
</runtime></configuration>";

            var qualified = MSBuildExeConfigReader.ReadXml(xml).QualifiedAssemblies;

            qualified.Count.ShouldBe(1);
            qualified["Good"].Version.ShouldBe(new Version(1, 0, 0, 0));
        }

        [Fact]
        public void ReadXml_FirstQualifyAssemblyForAPartialNameWins()
        {
            const string xml = @"<configuration><runtime>
  <assemblyBinding xmlns=""urn:schemas-microsoft-com:asm.v1"">
    <qualifyAssembly partialName=""Foo"" fullName=""Foo, Version=1.0.0.0"" />
    <qualifyAssembly partialName=""Foo"" fullName=""Foo, Version=2.0.0.0"" />
  </assemblyBinding>
</runtime></configuration>";

            MSBuildExeConfigReader.ReadXml(xml).QualifiedAssemblies["Foo"].Version
                .ShouldBe(new Version(1, 0, 0, 0));
        }

        [Theory]
        [InlineData("<configuration></configuration>")]
        [InlineData(@"<configuration><runtime></runtime></configuration>")]
        [InlineData("this is not xml <<<")]
        [InlineData("")]
        public void ReadXml_UnusableConfigIsEmpty(string xml)
        {
            AssemblyBindingPolicy policy = MSBuildExeConfigReader.ReadXml(xml);

            policy.IsEmpty.ShouldBeTrue();
            policy.DependentAssemblies.ShouldBeEmpty();
            policy.QualifiedAssemblies.ShouldBeEmpty();
        }

        [Fact]
        public void ReadXml_DocumentTypeDefinitionIsRejected()
        {
            const string xml = @"<?xml version=""1.0""?>
<!DOCTYPE configuration [<!ENTITY payload ""Foo"">]>
<configuration><runtime>
  <assemblyBinding xmlns=""urn:schemas-microsoft-com:asm.v1"">
    <dependentAssembly>
      <assemblyIdentity name=""&payload;"" />
      <codeBase version=""1.0.0.0"" href=""Foo.dll"" />
    </dependentAssembly>
  </assemblyBinding>
</runtime></configuration>";

            MSBuildExeConfigReader.ReadXml(xml).IsEmpty.ShouldBeTrue();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void Read_NoPathIsEmpty(string configFilePath) =>
            MSBuildExeConfigReader.Read(configFilePath).IsEmpty.ShouldBeTrue();

        [Fact]
        public void Read_MissingFileIsEmpty() => RunInTemporaryDirectory(directory =>
            MSBuildExeConfigReader.Read(Path.Combine(directory, "MSBuild.exe.config")).IsEmpty.ShouldBeTrue());

        [Fact]
        public void Read_InvalidPathIsEmpty() =>
            MSBuildExeConfigReader.Read("|not*a?path|").IsEmpty.ShouldBeTrue();

        [Fact]
        public void Read_DirectoryInsteadOfFileIsEmpty() => RunInTemporaryDirectory(directory =>
            MSBuildExeConfigReader.Read(directory).IsEmpty.ShouldBeTrue());

        [Fact]
        public void Read_ParsesConfigFile() => RunInTemporaryDirectory(directory =>
        {
            string configFilePath = Path.Combine(directory, "MSBuild.exe.config");
            File.WriteAllText(configFilePath, SampleConfig);

            AssemblyBindingPolicy policy = MSBuildExeConfigReader.Read(configFilePath);

            policy.DependentAssemblies.Select(e => e.Name).ShouldBe(
                new[] { "System.Collections.Immutable", "Microsoft.DotNet.MSBuildSdkResolver", "RedirectOnly" });
            policy.QualifiedAssemblies.Count.ShouldBe(1);
        });

        [Fact]
        public void Read_MalformedConfigFileIsEmpty() => RunInTemporaryDirectory(directory =>
        {
            string configFilePath = Path.Combine(directory, "MSBuild.exe.config");
            File.WriteAllText(configFilePath, "<configuration><runtime>");

            MSBuildExeConfigReader.Read(configFilePath).IsEmpty.ShouldBeTrue();
        });

        [Theory]
        [InlineData("1", 1, 0, 0, 0)]
        [InlineData("1.2", 1, 2, 0, 0)]
        [InlineData("1.2.3", 1, 2, 3, 0)]
        [InlineData("1.2.3.4", 1, 2, 3, 4)]
        [InlineData(" 1.2.3.4 ", 1, 2, 3, 4)]
        [InlineData("65535.65535.65535.65535", 65535, 65535, 65535, 65535)]
        public void TryParseAssemblyVersion_NormalizesValidVersions(string value, int major, int minor, int build, int revision)
        {
            MSBuildExeConfigReader.TryParseAssemblyVersion(value, out Version version).ShouldBeTrue();

            version.ShouldBe(new Version(major, minor, build, revision));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("1.2.3.4.5")]
        [InlineData("1..2")]
        [InlineData("1.2.")]
        [InlineData("-1.0")]
        [InlineData("1.0-preview")]
        [InlineData("1.0.0.0x")]
        [InlineData("65536.0.0.0")]
        [InlineData("99999999999999999999")]
        public void TryParseAssemblyVersion_RejectsInvalidVersions(string value)
        {
            MSBuildExeConfigReader.TryParseAssemblyVersion(value, out Version version).ShouldBeFalse();

            version.ShouldBeNull();
        }

        [Fact]
        public void GetEffectiveVersion_UsesTheFirstRedirectCoveringTheRequestedVersion()
        {
            const string xml = @"<configuration><runtime>
  <assemblyBinding xmlns=""urn:schemas-microsoft-com:asm.v1"">
    <dependentAssembly>
      <assemblyIdentity name=""Foo"" />
      <bindingRedirect oldVersion=""0.0.0.0-1.0.0.0"" newVersion=""1.0.0.0"" />
      <bindingRedirect oldVersion=""0.0.0.0-9.0.0.0"" newVersion=""9.0.0.0"" />
    </dependentAssembly>
  </assemblyBinding>
</runtime></configuration>";

            DependentAssembly entry = MSBuildExeConfigReader.ReadXml(xml).DependentAssemblies.Single();

            entry.GetEffectiveVersion(new Version(0, 5, 0, 0)).ShouldBe(new Version(1, 0, 0, 0));
            entry.GetEffectiveVersion(new Version(1, 0, 0, 0)).ShouldBe(new Version(1, 0, 0, 0));
            entry.GetEffectiveVersion(new Version(2, 0, 0, 0)).ShouldBe(new Version(9, 0, 0, 0));
        }

        [Fact]
        public void GetEffectiveVersion_LeavesAnUncoveredVersionAlone()
        {
            DependentAssembly entry = MSBuildExeConfigReader.ReadXml(SampleConfig).DependentAssemblies
                .Single(d => d.Name == "RedirectOnly");

            entry.GetEffectiveVersion(new Version(3, 0, 0, 0)).ShouldBe(new Version(3, 0, 0, 0));
            entry.GetEffectiveVersion(null).ShouldBeNull();
        }

        [Fact]
        public void TryGetLocalPath_ResolvesRelativeHrefAgainstTheConfigDirectory()
        {
            var codeBase = new AssemblyCodeBase(new Version(9, 0, 0, 0), @"..\..\..\..\SharedAssemblies\Foo.dll");

            codeBase.TryGetLocalPath(@"C:\VS\MSBuild\Current\Bin\amd64", out string localPath).ShouldBeTrue();

            localPath.ShouldBe(@"C:\VS\SharedAssemblies\Foo.dll");
        }

        [Fact]
        public void TryGetLocalPath_AcceptsAbsolutePathsAndFileUris()
        {
            new AssemblyCodeBase(null, @"C:\VS\SharedAssemblies\Foo.dll")
                .TryGetLocalPath(@"C:\VS\MSBuild\Current\Bin", out string absolutePath).ShouldBeTrue();
            absolutePath.ShouldBe(@"C:\VS\SharedAssemblies\Foo.dll");

            new AssemblyCodeBase(null, new Uri(@"C:\VS\SharedAssemblies\Foo.dll").AbsoluteUri)
                .TryGetLocalPath(@"C:\VS\MSBuild\Current\Bin", out string uriPath).ShouldBeTrue();
            uriPath.ShouldBe(@"C:\VS\SharedAssemblies\Foo.dll");
        }

        [Theory]
        [InlineData("http://example.invalid/Foo.dll")]
        [InlineData("https://example.invalid/Foo.dll")]
        [InlineData("ftp://example.invalid/Foo.dll")]
        public void TryGetLocalPath_RejectsNonFileUris(string href)
        {
            new AssemblyCodeBase(null, href).TryGetLocalPath(@"C:\VS\MSBuild\Current\Bin", out string localPath)
                .ShouldBeFalse();

            localPath.ShouldBeNull();
        }

        [Fact]
        public void TryGetLocalPath_RejectsRelativeHrefWithoutADirectory()
        {
            new AssemblyCodeBase(null, @"sub\Foo.dll").TryGetLocalPath(null, out string localPath).ShouldBeFalse();

            localPath.ShouldBeNull();
        }

        [Fact]
        public void TryGetLocalPath_RejectsUnusableHref()
        {
            new AssemblyCodeBase(null, "|not*a?path|").TryGetLocalPath(@"C:\VS\MSBuild\Current\Bin", out string localPath)
                .ShouldBeFalse();

            localPath.ShouldBeNull();
        }

        private static void RunInTemporaryDirectory(Action<string> test)
        {
            string directory = Path.Combine(
                AppContext.BaseDirectory,
                nameof(MSBuildExeConfigReaderTests) + "_" + Guid.NewGuid().ToString("N"));
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
