# MSBL001 - MSBuild runtime package is referenced without Runtime Assets excluded

## Error Message

> A PackageReference to the package '{PackageId}' at version '{Version}' is present in this project without ExcludeAssets="runtime" and PrivateAssets="all" set. This can cause errors at run-time due to MSBuild assembly-loading.

## Cause

This error occurs when your project references MSBuild NuGet packages (such as `Microsoft.Build`, `Microsoft.Build.Framework`, `Microsoft.Build.Utilities.Core`, etc.) without excluding their runtime assets and marking them as private. When you use Microsoft.Build.Locator, you want MSBuildLocator to load MSBuild assemblies from an installed Visual Studio or .NET SDK instance, not from the NuGet packages in your output directory.

## Why This Is a Problem

When MSBuild runtime assemblies are copied to your application's output directory, your application may load these assemblies instead of the MSBuild installation that MSBuildLocator registered. This can lead to several runtime issues:

1. **Assembly version conflicts**: Your application loads MSBuild assemblies from your output directory while MSBuildLocator tries to load from a different MSBuild installation
2. **Missing SDKs and build logic**: The MSBuild assemblies in your output directory don't include the SDKs, targets, and build logic needed to build real projects
3. **Inconsistent behavior**: Your application may behave differently than `MSBuild.exe`, `dotnet build`, or Visual Studio when evaluating projects

Additionally, without `PrivateAssets="all"`, downstream projects that reference your project may still get these runtime assets transitively, causing the same issues in consuming projects.

## Example Runtime Error

Without the proper asset exclusions, you may encounter errors like:

```
System.IO.FileNotFoundException: Could not load file or assembly 'Microsoft.Build, Version=15.1.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a' or one of its dependencies.
```

Or:

```
System.InvalidOperationException: SDK 'Microsoft.NET.Sdk' could not be resolved. The SDK resolver "Microsoft.DotNet.MSBuildSdkResolver" returned null.
```

This happens because your application loads MSBuild assemblies from your bin folder (e.g., version 15.5.180) while MSBuildLocator has registered a different MSBuild installation (e.g., version 17.0) to use at runtime. The .NET runtime gets confused about which assemblies to use, leading to version conflicts and missing dependencies.

## Solution

Add `ExcludeAssets="runtime"` and `PrivateAssets="all"` to all MSBuild PackageReferences in your project file:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Build" ExcludeAssets="runtime" PrivateAssets="all" />
  <PackageReference Include="Microsoft.Build.Framework" ExcludeAssets="runtime" PrivateAssets="all" />
  <PackageReference Include="Microsoft.Build.Utilities.Core" ExcludeAssets="runtime" PrivateAssets="all" />
  ...
</ItemGroup>
```

- `ExcludeAssets="runtime"` tells NuGet to use these packages only for compilation, not at runtime. At runtime, MSBuildLocator will load MSBuild assemblies from the registered Visual Studio or .NET SDK installation.
- `PrivateAssets="all"` prevents the package reference metadata from flowing to downstream projects, ensuring that projects referencing your library don't inadvertently get runtime assets from these packages.

> [!NOTE]
> Make sure that you don't add `ExcludeAssets` and `PrivateAssets` to the `Microsoft.Build.Locator` `PackageReference` itself - you need its run-time assets in order to use it!

### What if I get errors for PackageReferences I don't have?

It's possible that you may get `MSBL001` errors for packages that you don't directly reference - these packages are _transitive_ references, pulled in by other packages you _do_ reference.
To solve this, you'll need to add new PackageReference items to your project and add the ExcludeAssets/PrivateAssets metadata onto them. In the future we hope to have the ability to 'flow' this metadata from a parent PackageReference to transitive PackageReferences so that you don't need to do this.

## Alternative: Disable the Check (Not Recommended)

If you need to distribute MSBuild assemblies with your application (not recommended), you can disable this check by setting the following property in your project file:

```xml
<PropertyGroup>
  <DisableMSBuildAssemblyCopyCheck>true</DisableMSBuildAssemblyCopyCheck>
</PropertyGroup>
```

**Warning**: Disabling this check means you must distribute all of MSBuild and its associated toolset with your application, which is generally not recommended. The MSBuild team does not support this scenario, and you may encounter issues with SDK resolution and build logic.

## Related Documentation

- [Use Microsoft.Build.Locator](https://learn.microsoft.com/visualstudio/msbuild/updating-an-existing-application#use-microsoftbuildlocator)
- [MSBuildLocator on GitHub](https://github.com/microsoft/MSBuildLocator)

