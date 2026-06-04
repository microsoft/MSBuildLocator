---
name: msbuild-loader-netframework
description: >-
  How MSBuildLocator loads MSBuild assemblies and discovers installs on .NET
  Framework (the net46 target / #if NET46 / non-NETCOREAPP branches in
  src/MSBuildLocator). Covers the AppDomain.AssemblyResolve handler, search-path
  probing, the pre-17.1 MSBuild.exe x86/amd64 fix, and Developer Console + Visual
  Studio Setup (COM) discovery. Use when editing or reviewing net46 loader,
  registration, or VS-discovery code, or diagnosing assembly-resolution behavior
  on .NET Framework hosts.
---

# .NET Framework (`net46`) MSBuild loader

Scope: `src/MSBuildLocator/MSBuildLocator.cs` `#if NET46` and Framework (`#else`
of `NETCOREAPP`) branches. `FEATURE_VISUALSTUDIOSETUP` is defined only when
`TargetFramework == net46` in `Microsoft.Build.Locator.csproj`. For build/test
and cross-cutting conventions, see `AGENTS.md`.

## Assembly-resolution handler
- `s_registeredHandler` is a static `ResolveEventHandler`; `IsRegistered` is
  `s_registeredHandler != null`.
- `RegisterMSBuildPathsInternally` stores the handler in the static field before
  subscribing to `AppDomain.CurrentDomain.AssemblyResolve`; the static field
  keeps the delegate alive so it persists.
- `AssemblyResolve` can fire repeatedly for the same assembly; results are cached
  in `loadedAssemblies` keyed by `AssemblyName.FullName`.
- Resolution is explicitly not thread-safe; every cache lookup/load runs under
  `lock (loadedAssemblies)`.
- Handler path: parse `eventArgs.Name` with `new AssemblyName(eventArgs.Name)`;
  for each registered search path, if `<msbuildPath>\<Name>.dll` exists, return
  `Assembly.LoadFrom(targetAssembly)`.
- Search paths come from `RegisterMSBuildPath(...)`, or from
  `RegisterInstance(...)` as `instance.MSBuildPath` plus the VS NuGet path when
  it exists.

## Pre-17.1 MSBuild.exe bitness fix
- net46 reads `FileVersionInfo` from the first registered path containing
  `MSBuild.exe`.
- For versions `< 17.1`, set `MSBUILD_EXE_PATH` to drive MSBuild's own lookup.
- If that pre-17.1 path ends in `\amd64`, strip the trailing folder and point
  `MSBUILD_EXE_PATH` at the sibling x86 `MSBuild.exe`.

## Discovery sources (net46 only)
- Developer command prompt: `GetDevConsoleInstance()` reads `VSINSTALLDIR`, then
  parses `VSCMD_VER` (trimming any suffix after `-`), then falls back to
  `VisualStudioVersion`; yields `DiscoveryType.DeveloperConsole`.
- Visual Studio Setup COM API: under `FEATURE_VISUALSTUDIOSETUP`,
  `VisualStudioLocationHelper.GetInstances()` enumerates VS 2017+ setup instances
  with `Microsoft.Component.MSBuild`; yields `DiscoveryType.VisualStudioSetup`.
- `DiscoveryType.DotNetSdk` exists in the enum but belongs to the Core path; net46
  `GetInstances(...)` does not call SDK discovery.

## What net46 does NOT do
- No `AssemblyLoadContext`, `hostfxr`, or `.NET SDK` discovery — those are
  `#if NETCOREAPP` paths (see the `msbuild-loader-netcore` skill).
- `ApplyDotNetSdkEnvironmentVariables(...)` exists in the file, but both
  `RegisterMSBuildPath(...)` call sites that invoke it are `#if NETCOREAPP`. Do
  not assume SDK env vars (`MSBUILD_EXE_PATH` to `MSBuild.dll`,
  `MSBuildExtensionsPath`, `MSBuildSDKsPath`) are set on net46.

## Register-before-load contract
- `CanRegister` is false once any strong-named `Microsoft.Build*` assembly in
  `s_msBuildAssemblies` is loaded in the current `AppDomain`.
- JIT caveat: a method that references `Microsoft.Build` types can trip the
  contract when JIT-compiled, even if that reference never executes. Keep locator
  calls isolated before any such reference.
