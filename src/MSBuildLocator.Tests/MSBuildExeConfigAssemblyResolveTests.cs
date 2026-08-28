// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if !NETCOREAPP

using System;
using System.IO;
using System.Reflection;
using Shouldly;
using Xunit;

namespace Microsoft.Build.Locator.Tests
{
    /// <summary>
    ///     End-to-end coverage of the registered <see cref="AppDomain.AssemblyResolve"/> handler: a real
    ///     registration against a Visual Studio-shaped layout, followed by real assembly loads. Each test runs
    ///     in its own child AppDomain because registration is permanent within an AppDomain.
    /// </summary>
    public class MSBuildExeConfigAssemblyResolveTests
    {
        private const string MSBuildExePathVariable = "MSBUILD_EXE_PATH";
        private static readonly Version FixtureVersion = new Version(9, 0, 0, 0);

        [Fact]
        public void LoadsAnAssemblyFromACodeBaseOutsideTheRegisteredSearchPaths() => RunInChildAppDomain(
            nameof(LoadsAnAssemblyFromACodeBaseOutsideTheRegisteredSearchPaths),
            (install, runner) =>
            {
                const string name = "SacFixtureShared";
                string fixture = FixtureAssembly.Emit(install.SharedAssemblies, name, FixtureVersion);
                install.WriteAmd64Config(CodeBaseEntry(name, fixture, $@"{FakeVisualStudioInstall.Amd64ToRoot}\SharedAssemblies\{name}.dll"));

                runner.Register(new[] { install.Bin }).ShouldBeNull();

                AssemblyLoadResult result = runner.Load(FullName(name, fixture, FixtureVersion));

                result.Succeeded.ShouldBeTrue(result.Error);
                result.Location.ShouldBe(fixture, StringCompareShould.IgnoreCase);
            });

        [Fact]
        public void AppliesABindingRedirectAndReusesTheAssemblyItLoaded() => RunInChildAppDomain(
            nameof(AppliesABindingRedirectAndReusesTheAssemblyItLoaded),
            (install, runner) =>
            {
                const string name = "SacFixtureRedirected";
                string fixture = FixtureAssembly.Emit(install.SharedAssemblies, name, FixtureVersion);
                string token = FixtureAssembly.GetPublicKeyToken(fixture);
                install.WriteAmd64Config($@"
      <dependentAssembly>
        <assemblyIdentity name=""{name}"" publicKeyToken=""{token}"" culture=""neutral"" processorArchitecture=""msil"" />
        <bindingRedirect oldVersion=""0.0.0.0-9.0.0.0"" newVersion=""9.0.0.0"" />
        <codeBase version=""9.0.0.0"" href=""{FakeVisualStudioInstall.Amd64ToRoot}\SharedAssemblies\{name}.dll"" />
      </dependentAssembly>");

                runner.Register(new[] { install.Bin }).ShouldBeNull();

                AssemblyLoadResult result = runner.LoadTwice(
                    FullName(name, fixture, new Version(1, 0, 0, 0)),
                    FullName(name, fixture, FixtureVersion));

                result.Succeeded.ShouldBeTrue(result.Error);
                result.FullName.ShouldBe($"{name}, Version=9.0.0.0, Culture=neutral, PublicKeyToken={token}");
                result.Location.ShouldBe(fixture, StringCompareShould.IgnoreCase);

                // Caching aliases means the pre-policy request and the effective identity share one assembly.
                result.SameInstance.ShouldBeTrue();
                result.LoadedCount.ShouldBe(1);
            });

        [Fact]
        public void PrefersAConfiguredCodeBaseOverAFileInARegisteredSearchPath() => RunInChildAppDomain(
            nameof(PrefersAConfiguredCodeBaseOverAFileInARegisteredSearchPath),
            (install, runner) =>
            {
                const string name = "SacFixturePrecedence";
                string fixture = FixtureAssembly.Emit(install.SharedAssemblies, name, FixtureVersion);
                string searchPathCopy = Path.Combine(install.Bin, name + ".dll");
                File.Copy(fixture, searchPathCopy);
                install.WriteAmd64Config(CodeBaseEntry(name, fixture, $@"{FakeVisualStudioInstall.Amd64ToRoot}\SharedAssemblies\{name}.dll"));

                runner.Register(new[] { install.Bin }).ShouldBeNull();

                AssemblyLoadResult result = runner.Load(FullName(name, fixture, FixtureVersion));

                result.Succeeded.ShouldBeTrue(result.Error);
                result.Location.ShouldBe(fixture, StringCompareShould.IgnoreCase);
                string.Equals(result.Location, searchPathCopy, StringComparison.OrdinalIgnoreCase).ShouldBeFalse();
            });

        [Fact]
        public void FallsBackToSearchPathProbingWhenTheCodeBaseTargetIsMissing() => RunInChildAppDomain(
            nameof(FallsBackToSearchPathProbingWhenTheCodeBaseTargetIsMissing),
            (install, runner) =>
            {
                const string name = "SacFixtureMissingTarget";
                string fixture = FixtureAssembly.Emit(install.Bin, name, FixtureVersion);
                install.WriteAmd64Config(CodeBaseEntry(name, fixture, $@"{FakeVisualStudioInstall.Amd64ToRoot}\SharedAssemblies\{name}.dll"));

                runner.Register(new[] { install.Bin }).ShouldBeNull();

                AssemblyLoadResult result = runner.Load(FullName(name, fixture, FixtureVersion));

                result.Succeeded.ShouldBeTrue(result.Error);
                result.Location.ShouldBe(fixture, StringCompareShould.IgnoreCase);
            });

        [Fact]
        public void FallsBackToSearchPathProbingWhenTheCodeBaseTargetIsAnotherAssembly() => RunInChildAppDomain(
            nameof(FallsBackToSearchPathProbingWhenTheCodeBaseTargetIsAnotherAssembly),
            (install, runner) =>
            {
                const string name = "SacFixtureIdentityChecked";
                string fixture = FixtureAssembly.Emit(install.Bin, name, FixtureVersion);

                // The configured code base points at an assembly with a different identity, which the handler
                // must detect from the target's manifest instead of loading it for the requested name.
                string imposter = FixtureAssembly.Emit(install.SharedAssemblies, "SacFixtureImposter", FixtureVersion);
                File.Move(imposter, Path.Combine(install.SharedAssemblies, name + ".dll"));

                install.WriteAmd64Config(CodeBaseEntry(name, fixture, $@"{FakeVisualStudioInstall.Amd64ToRoot}\SharedAssemblies\{name}.dll"));

                runner.Register(new[] { install.Bin }).ShouldBeNull();

                AssemblyLoadResult result = runner.Load(FullName(name, fixture, FixtureVersion));

                result.Succeeded.ShouldBeTrue(result.Error);
                result.Location.ShouldBe(fixture, StringCompareShould.IgnoreCase);
            });

        [Fact]
        public void FallsBackToSearchPathProbingWhenTheCodeBaseTargetIsNotAnAssembly() => RunInChildAppDomain(
            nameof(FallsBackToSearchPathProbingWhenTheCodeBaseTargetIsNotAnAssembly),
            (install, runner) =>
            {
                const string name = "SacFixtureBadImage";
                string fixture = FixtureAssembly.Emit(install.Bin, name, FixtureVersion);
                File.WriteAllText(Path.Combine(install.SharedAssemblies, name + ".dll"), "not a managed assembly");
                install.WriteAmd64Config(CodeBaseEntry(name, fixture, $@"{FakeVisualStudioInstall.Amd64ToRoot}\SharedAssemblies\{name}.dll"));

                runner.Register(new[] { install.Bin }).ShouldBeNull();

                AssemblyLoadResult result = runner.Load(FullName(name, fixture, FixtureVersion));

                result.Succeeded.ShouldBeTrue(result.Error);
                result.Location.ShouldBe(fixture, StringCompareShould.IgnoreCase);
            });

        [Fact]
        public void UsesALaterCodeBaseWhenAnEarlierOneCannotBeLoaded() => RunInChildAppDomain(
            nameof(UsesALaterCodeBaseWhenAnEarlierOneCannotBeLoaded),
            (install, runner) =>
            {
                const string name = "SacFixtureSecondCandidate";
                string fixture = FixtureAssembly.Emit(install.SharedAssemblies, name, FixtureVersion);
                install.WriteAmd64Config($@"
      <dependentAssembly>
        <assemblyIdentity name=""{name}"" publicKeyToken=""{FixtureAssembly.GetPublicKeyToken(fixture)}"" culture=""neutral"" />
        <codeBase version=""9.0.0.0"" href=""..\Missing\{name}.dll"" />
        <codeBase version=""9.0.0.0"" href=""{FakeVisualStudioInstall.Amd64ToRoot}\SharedAssemblies\{name}.dll"" />
      </dependentAssembly>");

                runner.Register(new[] { install.Bin }).ShouldBeNull();

                AssemblyLoadResult result = runner.Load(FullName(name, fixture, FixtureVersion));

                result.Succeeded.ShouldBeTrue(result.Error);
                result.Location.ShouldBe(fixture, StringCompareShould.IgnoreCase);
            });

        [Fact]
        public void MalformedConfigLeavesSearchPathProbingIntact() => RunInChildAppDomain(
            nameof(MalformedConfigLeavesSearchPathProbingIntact),
            (install, runner) =>
            {
                const string name = "SacFixtureNoPolicy";
                string fixture = FixtureAssembly.Emit(install.Bin, name, FixtureVersion);
                install.WriteRawAmd64Config("this is not a config file <<<");

                runner.Register(new[] { install.Bin }).ShouldBeNull();

                AssemblyLoadResult result = runner.Load(FullName(name, fixture, FixtureVersion));

                result.Succeeded.ShouldBeTrue(result.Error);
                result.Location.ShouldBe(fixture, StringCompareShould.IgnoreCase);
            });

        [Fact]
        public void CanonicalConfigAnchorsCodeBasesEvenWhenMSBuildExePathIsRewritten() => RunInChildAppDomain(
            nameof(CanonicalConfigAnchorsCodeBasesEvenWhenMSBuildExePathIsRewritten),
            (install, runner) =>
            {
                // Registering the amd64 directory drives both the pre-17.1 MSBUILD_EXE_PATH rewrite, which points
                // at the base bin directory, and code base resolution, which stays anchored at the config.
                const string name = "SacFixtureAnchored";
                string fixture = FixtureAssembly.Emit(install.Bin, name, FixtureVersion);
                install.WriteAmd64Config(CodeBaseEntry(name, fixture, $@"..\{name}.dll"));

                runner.Register(new[] { install.Amd64 }).ShouldBeNull();

                runner.GetEnvironmentVariable(MSBuildExePathVariable)
                    .ShouldBe(Path.Combine(install.Bin, "MSBuild.exe"), StringCompareShould.IgnoreCase);

                AssemblyLoadResult result = runner.Load(FullName(name, fixture, FixtureVersion));

                result.Succeeded.ShouldBeTrue(result.Error);

                // The base bin directory was never registered, so only the amd64-anchored code base can reach it.
                result.Location.ShouldBe(fixture, StringCompareShould.IgnoreCase);
            });

        [Fact]
        public void AnAssemblyNoPolicyOrSearchPathProvidesStillFailsToLoad() => RunInChildAppDomain(
            nameof(AnAssemblyNoPolicyOrSearchPathProvidesStillFailsToLoad),
            (install, runner) =>
            {
                install.WriteAmd64Config(@"
      <dependentAssembly>
        <assemblyIdentity name=""SomethingElse"" publicKeyToken=""b03f5f7f11d50a3a"" culture=""neutral"" />
        <codeBase version=""1.0.0.0"" href=""SomethingElse.dll"" />
      </dependentAssembly>");

                runner.Register(new[] { install.Bin }).ShouldBeNull();

                AssemblyLoadResult result = runner.Load(
                    "SacFixtureAbsent, Version=9.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a");

                result.Succeeded.ShouldBeFalse();
                result.Error.ShouldContain("SacFixtureAbsent");
            });

        [Fact]
        public void AnAssemblyOnlyACodeBaseCanReachDoesNotLoadWithoutOne() => RunInChildAppDomain(
            nameof(AnAssemblyOnlyACodeBaseCanReachDoesNotLoadWithoutOne),
            (install, runner) =>
            {
                // The control for every code base test above: the shared assemblies directory is neither a
                // registered search path nor a directory the runtime probes, so without a configured code base
                // the identical request has to fail.
                const string name = "SacFixtureUnreachable";
                string fixture = FixtureAssembly.Emit(install.SharedAssemblies, name, FixtureVersion);
                install.WriteAmd64Config(CodeBaseEntry("SomethingElse", fixture, "SomethingElse.dll"));

                runner.Register(new[] { install.Bin }).ShouldBeNull();

                AssemblyLoadResult result = runner.Load(FullName(name, fixture, FixtureVersion));

                result.Succeeded.ShouldBeFalse();
                result.Error.ShouldContain(name);
            });

        private static string CodeBaseEntry(string name, string fixturePath, string href) => $@"
      <dependentAssembly>
        <assemblyIdentity name=""{name}"" publicKeyToken=""{FixtureAssembly.GetPublicKeyToken(fixturePath)}"" culture=""neutral"" />
        <codeBase version=""{FixtureVersion}"" href=""{href}"" />
      </dependentAssembly>";

        private static string FullName(string name, string fixturePath, Version version) =>
            $"{name}, Version={version}, Culture=neutral, PublicKeyToken={FixtureAssembly.GetPublicKeyToken(fixturePath)}";

        private static void RunInChildAppDomain(string name, Action<FakeVisualStudioInstall, AssemblyResolutionRunner> test) =>
            TemporaryDirectory.Run(name, directory =>
            {
                FakeVisualStudioInstall install = FakeVisualStudioInstall.Create(Path.Combine(directory, "VisualStudio"));

                // Registration sets MSBUILD_EXE_PATH, which is per-process rather than per-AppDomain.
                string previousMSBuildExePath = Environment.GetEnvironmentVariable(MSBuildExePathVariable);
                AppDomain domain = AppDomain.CreateDomain(
                    name,
                    securityInfo: null,
                    info: new AppDomainSetup { ApplicationBase = TestAssemblyDirectory });

                try
                {
                    var runner = (AssemblyResolutionRunner)domain.CreateInstanceAndUnwrap(
                        typeof(AssemblyResolutionRunner).Assembly.FullName,
                        typeof(AssemblyResolutionRunner).FullName);

                    test(install, runner);
                }
                finally
                {
                    AppDomain.Unload(domain);
                    Environment.SetEnvironmentVariable(MSBuildExePathVariable, previousMSBuildExePath);
                }
            });

        /// <summary>
        ///     The directory the test assembly was built to, which is where the child AppDomain finds this
        ///     assembly and the product assembly. <see cref="Assembly.CodeBase"/> reports that directory even
        ///     when the test runner shadow copies.
        /// </summary>
        private static string TestAssemblyDirectory => Path.GetDirectoryName(
            new Uri(typeof(MSBuildExeConfigAssemblyResolveTests).Assembly.CodeBase).LocalPath);
    }
}

#endif
