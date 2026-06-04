# Microsoft.Build.Locator — Copilot instructions

Small .NET library: locates an MSBuild install (Visual Studio or .NET SDK) and registers an assembly-resolution handler so the host app loads MSBuild's assemblies from that install. Ships as the `Microsoft.Build.Locator` NuGet package.

## Build / test (root `MSBuildLocator.sln`, .NET CLI)
- `dotnet restore` / `dotnet build` (deterministic) / `dotnet test` / `dotnet pack --configuration Debug`
- Single test: `dotnet test --filter "FullyQualifiedName~QueryInstanceTests"` or `--filter "Name=<Method>"`
- Tests: xUnit + Shouldly, in `src/MSBuildLocator.Tests`.
- Versioning: Nerdbank.GitVersioning (`version.json`) → build/pack needs full git history (CI `fetch-depth: 0`).
- PR validation: `.github/workflows/pull-request.yml` (`windows-latest`). Official builds: `azure-pipelines.yml` / `release-pipeline.yml`. Release steps: `Releasing_MSBuildLocator.md`.

## Multi-targeting (central constraint)
Library: `net46` + `net8.0`. Tests: `net472` + `net8.0`. Non-trivial code forks per TFM — must compile/behave on both:
- `#if NETCOREAPP` → `AssemblyLoadContext.Resolving`; `#else` (Framework) → `AppDomain.AssemblyResolve` (`ResolveEventHandler`). See `s_registeredHandler` in `MSBuildLocator.cs`.
- `#if NET46` / `FEATURE_VISUALSTUDIOSETUP` (defined only for `net46` in csproj) gate VS Setup COM-interop discovery (`VisualStudioLocationHelper.cs`). `net8.0` does NO VS discovery — uses SDK path only.
- Always check whether a change must be mirrored/excluded under these conditionals.

## Architecture (namespace `Microsoft.Build.Locator`)
- `MSBuildLocator.cs` — entry point: `RegisterDefaults`, `RegisterInstance`, `RegisterMSBuildPath`, `QueryVisualStudioInstances`, `CanRegister`, handler register/unregister. Both TFM forks live here.
- `DotNetSdkLocationHelper.cs` — `.NET SDK` discovery. `ResolveDotnetPathCandidates` builds an ordered preference list of dotnet locations, tried in order: `DOTNET_ROOT`(`(x86)` on 32-bit) → current process (if run from `dotnet`) → `DOTNET_HOST_PATH` → `DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR` → `PATH`.
- `NativeMethods.cs` — `NETCOREAPP`-only `hostfxr` interop (`hostfxr_resolve_sdk2`, `hostfxr_get_available_sdks`) used by SDK discovery.
- `VisualStudioLocationHelper.cs` — `net46`-only VS Setup (COM) discovery (VS 2017+).
- `VisualStudioInstance.cs`, `VisualStudioInstanceQueryOptions.cs`, `DiscoveryType.cs` — result/option types.
- `Utils/SemanticVersion*.cs`, `VersionComparer.cs` — internal SemVer parse/compare to order instances.
- Props/targets shipped from `src/MSBuildLocator/build/` (packed to `build/` + `buildTransitive/`). `EnsureMSBuildAssembliesNotCopied` emits error **MSBL001** when a consumer references `Microsoft.Build*` packages without `PrivateAssets="all"` / `ExcludeAssets="runtime"` (copying those assemblies locally breaks redirection). Keep the target's package list in sync with MSBuild's layout.

## Conventions
- Contract-stable public API: csproj `EnablePackageValidation` + `PackageValidationBaselineVersion` (1.6.1). Intentional API changes require updating `src/MSBuildLocator/CompatibilitySuppressions.xml`.
- XML doc comments on public members; match existing style.
- Strong-name signed (`key.snk`) — don't remove signing.
- Build settings centralized in `Directory.Build.props` / `Directory.Solution.props` / `Directory.Build.rsp` — edit there, not per-project.
- Register-before-load contract: callers must register via Locator BEFORE any `Microsoft.Build.*` type loads (`CanRegister` → false once loaded). Preserve this + the lazy-loading patterns protecting it when refactoring.
