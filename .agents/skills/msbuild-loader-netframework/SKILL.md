---
name: msbuild-loader-netframework
description: >-
  How MSBuildLocator loads MSBuild assemblies and discovers installs on .NET
  Framework (the net46 target / #if NET46 / non-NETCOREAPP branches in
  src/MSBuildLocator). Covers the AppDomain.AssemblyResolve handler, search-path
  probing, and Developer Console + Visual Studio Setup (COM) discovery. Use when
  editing or reviewing net46 loader, registration, or VS-discovery code, or
  diagnosing assembly-resolution behavior on .NET Framework hosts.
---

# .NET Framework (`net46`) MSBuild loader

Scope: `src/MSBuildLocator/MSBuildLocator.cs` `#if NET46` and Framework (`#else`
of `NETCOREAPP`) branches. `FEATURE_VISUALSTUDIOSETUP` is defined only when
`TargetFramework == net46` in `Microsoft.Build.Locator.csproj`. For build/test
and cross-cutting conventions, see `AGENTS.md`.

## Assembly-resolution handler
- `s_registeredHandler` is a static `ResolveEventHandler`; `IsRegistered` is
  `s_registeredHandler != null`.
- `RegisterMSBuildPathsInternally` parses an MSBuild configuration file (preferring
  `amd64\MSBuild.exe.config`, falling back to `MSBuild.exe.config` in the registered path).
  The config policy is read once. The selected config path is made absolute during
  registration, so relative `codeBase` paths stay anchored to that config directory
  even if the process working directory later changes.
- `RegisterMSBuildPathsInternally` stores the handler in the static field before
  subscribing to `AppDomain.CurrentDomain.AssemblyResolve`; the event subscription
  keeps the delegate alive, while the field tracks registration state.
- `AssemblyResolve` can fire repeatedly for the same assembly; results are cached
  in `loadedAssemblies` keyed by `AssemblyName.FullName`. This cache also maps original
  requests to the effective full identity when a binding redirect occurs.
- Resolution is explicitly not thread-safe; every cache lookup/load runs under
  `lock (loadedAssemblies)`.
- Handler path: parse `eventArgs.Name` with `new AssemblyName(eventArgs.Name)`.
  - Config-first ordering:
    - Apply `qualifyAssembly` policy if the request has a partial name.
    - Match full identity (name, token, culture, processorArchitecture).
    - If a valid `<bindingRedirect>` applies, map to the `newVersion`.
    - If a `<codeBase>` matches the effective version (whether redirected or standalone),
      resolve its absolute path or `file://` URI against the config directory.
    - Check the loaded assembly's manifest identity matches the target identity before caching.
    - A missing/malformed config yields an empty policy without masking other failures.
  - Fallback:
    - If the config does not resolve the assembly or load fails (e.g. `FileNotFoundException`),
      fall back to search-path probing (the old/custom layout behavior).
    - For each registered search path, if `<msbuildPath>\<Name>.dll` exists, return
      `Assembly.LoadFrom(targetAssembly)`.
- Search paths come from `RegisterMSBuildPath(...)`, or from
  `RegisterInstance(...)` as `instance.MSBuildPath` plus the VS NuGet path when
  it exists.

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

## Register-before-load contract
- `CanRegister` is false once any strong-named `Microsoft.Build*` assembly in
  `s_msBuildAssemblies` is loaded in the current `AppDomain`.
- JIT caveat: a method that references `Microsoft.Build` types can trip the
  contract when JIT-compiled, even if that reference never executes. Keep locator
  calls isolated before any such reference.
