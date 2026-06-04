---
name: msbuild-loader-netcore
description: >-
  How MSBuildLocator loads MSBuild assemblies and discovers the .NET SDK on .NET
  / .NET Core (the net8.0 target / #if NETCOREAPP branches in src/MSBuildLocator).
  Covers the AssemblyLoadContext.Default.Resolving handler, search-path probing,
  the SDK environment variables set on registration, hostfxr-based SDK discovery,
  the dotnet location probe order, and the AllowQueryAll* widening flags. Use when
  editing or reviewing net8.0 loader, registration, or SDK-discovery code, or
  diagnosing assembly-resolution behavior on .NET Core hosts.
---

# .NET / .NET Core (`net8.0` / `NETCOREAPP`) MSBuild loader

Scope: `src/MSBuildLocator/MSBuildLocator.cs` `#if NETCOREAPP` branches, plus
`DotNetSdkLocationHelper.cs` and `NativeMethods.cs`. For build/test and
cross-cutting conventions, see `AGENTS.md`.

## Assembly-resolution handler
- `s_registeredHandler` is a static
  `Func<AssemblyLoadContext, AssemblyName, Assembly>`; registration hooks
  `AssemblyLoadContext.Default.Resolving`.
- Resolving can fire repeatedly; successful loads are cached in the local
  `loadedAssemblies` dictionary keyed by `AssemblyName.FullName`.
- Resolution is not thread-safe; keep the `loadedAssemblies` lock around cache
  lookup, path probing, `Assembly.LoadFrom`, and cache insert.
- The resolver receives `AssemblyName` directly — do not parse an event-args
  name string (that is the net46 path).
- For each registered `msbuildSearchPaths` entry, probe
  `Path.Combine(msbuildPath, assemblyName.Name + ".dll")` and load with
  `Assembly.LoadFrom(targetAssembly)`.

## SDK environment variables (NETCOREAPP only)
- Registration sets MSBuild SDK env vars via `ApplyDotNetSdkEnvironmentVariables`:
  - `MSBUILD_EXE_PATH` = `<sdk>\MSBuild.dll`
  - `MSBuildExtensionsPath` = `<sdk>`
  - `MSBuildSDKsPath` = `<sdk>\Sdks`
- `RegisterMSBuildPath(string)` applies these for that path before registering.
- `RegisterMSBuildPath(string[])` applies these for the first search path only,
  then registers all paths.
- `RegisterInstance` applies these only when
  `instance.DiscoveryType == DiscoveryType.DotNetSdk`.
- Do not move this setup into net46; Framework has its own `MSBUILD_EXE_PATH`
  compatibility branch under `#if NET46` (see the `msbuild-loader-netframework`
  skill).

## SDK discovery
- Lives in `DotNetSdkLocationHelper`; instances are
  `VisualStudioInstance(name: ".NET Core SDK", ..., DiscoveryType.DotNetSdk)`.
- Uses `NativeMethods` hostfxr P/Invoke under `#if NETCOREAPP` only:
  - `hostfxr_resolve_sdk2` — best SDK, honoring `global.json` via
    `WorkingDirectory`.
  - `hostfxr_get_available_sdks` — installed SDK enumeration.
- `ResolveDotnetPathCandidates` preference order (tried in order):
  `DOTNET_ROOT` (`DOTNET_ROOT(x86)` in a 32-bit process) → current process
  directory when running under `dotnet` → `DOTNET_HOST_PATH` →
  `DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR` → `PATH`.
- A successful `hostfxr_resolve_sdk2` sets `DOTNET_HOST_PATH` (if empty) to
  `<dotnetPath>\dotnet(.exe)`.
- Default query returns the best SDK first, then unique SDK versions
  newest-first from the available SDKs.

## Widening flags
- `AllowQueryAllRuntimeVersions` / `VisualStudioInstanceQueryOptions.AllowAllRuntimeVersions`
  include SDKs whose major/minor runtime exceeds `Environment.Version`.
- `AllowQueryAllDotnetLocations` / `VisualStudioInstanceQueryOptions.AllowAllDotnetLocations`
  keep probing all dotnet candidate locations instead of stopping after the
  first location that has SDKs.

## What net8.0 does NOT do
- No Developer Console or Visual Studio Setup COM discovery;
  `FEATURE_VISUALSTUDIOSETUP` package references/constants are net46-only in the
  csproj.

## Register-before-load contract
- `CanRegister` is false when already registered, or once any signed
  `Microsoft.Build*` core assembly is loaded.
- JIT caveat: JIT-compilation of a method referencing `Microsoft.Build` types is
  enough to load those assemblies and break registration. Keep locator calls
  isolated before any such reference.
