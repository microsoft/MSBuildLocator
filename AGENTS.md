# Microsoft.Build.Locator — Copilot instructions

This library exists to locate an MSBuild install (Visual Studio or .NET SDK) and register an assembly-resolution handler so the host app loads MSBuild's assemblies from that install. This is required for any use of the .NET API from an application that is not part of Visual Studio or the .NET SDK.

A .NET Framework application can only locate MSBuild from a Visual Studio installation, and a .NET 8+ application can only locate MSBuild from a .NET SDK. This library's approach to finding the library and loading the assemblies is entirely different on the different runtimes. See .agents/skills/msbuild-loader-netcore/SKILL.md and .agents/skills/msbuild-loader-netframework/SKILL.md for details.

The core `Microsoft.Build.Locator.dll` must have minimal dependencies—nothing outside the core libraries provided by .NET for the relevant TargetFramework.

## Build / test (root `MSBuildLocator.sln`, .NET CLI)
- `dotnet restore` / `dotnet build` / `dotnet test`
- Single test: `dotnet test --filter "FullyQualifiedName~QueryInstancesTests"` or `--filter "Name=<Method>"`
- Tests: xUnit + Shouldly, in `src/MSBuildLocator.Tests`.
- Versioning: Nerdbank.GitVersioning. Use SemVer 2 and update `version.json` on breaking changes or feature additions.

## Multi-targeting (central constraint)
Library: `net46` + `net8.0`. Tests: `net472` + `net8.0`. Non-trivial code forks per TFM via `#if NETCOREAPP`, `#if NET46`, and `FEATURE_VISUALSTUDIOSETUP` (defined only for `net46`). Always check whether a change must be mirrored or excluded across these conditionals; for what each fork actually does, load the `msbuild-loader-netcore` or `msbuild-loader-netframework` skill.

When changing behavior documented by either skill, update the skill in the same change.

## Architecture (namespace `Microsoft.Build.Locator`)
- `MSBuildLocator.cs` — entry point and handler registration. Both TFM forks live here. `Unregister()` is retained for compatibility but is a no-op.
- `DotNetSdkLocationHelper.cs`, `NativeMethods.cs` — .NET SDK discovery (hostfxr); see `msbuild-loader-netcore`.
- `VisualStudioLocationHelper.cs` — `net46`-only Visual Studio Setup discovery; see `msbuild-loader-netframework`.
- `VisualStudioInstance.cs`, `VisualStudioInstanceQueryOptions.cs`, `DiscoveryType.cs` — result/option types.
- `Utils/SemanticVersion*.cs`, `VersionComparer.cs` — internal SemVer parse/compare that is an implementation detail of .NET SDK discovery.
- Props/targets ship from `src/MSBuildLocator/build/` to `build/` and `buildTransitive/`. Never ship MSBuild DLLs with an app: local copies load before Locator's handler. `EnsureMSBuildAssembliesNotCopied` reports **MSBL001**; fix the flagged `<PackageReference>` with `ExcludeAssets="runtime"` and `PrivateAssets="all"`.
- Keep `EnsureMSBuildAssembliesNotCopied`'s hardcoded package list synchronized with MSBuild's redistributable assemblies so it catches new packages.

## Conventions
- Contract-stable public API: csproj `EnablePackageValidation` + `PackageValidationBaselineVersion` (1.6.1). Intentional API changes require updating `src/MSBuildLocator/CompatibilitySuppressions.xml` and an appropriate semver update.
- XML doc comments on public members; match existing style.
- Strong-name signed (`key.snk`) — don't remove signing.
- Build settings centralized in `Directory.Build.props` / `Directory.Solution.props` / `Directory.Build.rsp` — edit there, not per-project.
 - Register-before-load contract: callers must register via Locator BEFORE any core MSBuild assembly loads (`CanRegister` → false once loaded). Preserve this + the lazy-loading patterns protecting it when refactoring.```
