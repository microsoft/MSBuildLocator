# MSBL001 - MSBuild runtime package is referenced without Runtime Assets excluded

## Error Message

> A PackageReference to the package '{PackageId}' at version '{Version}' is present in this project without ExcludeAssets="runtime" set. This can cause errors at run-time due to MSBuild assembly-loading.

## Cause

This error occurs when your project references MSBuild NuGet packages (such as `Microsoft.Build`, `Microsoft.Build.Framework`, `Microsoft.Build.Utilities.Core`, etc.) without excluding their runtime assets. When you use Microsoft.Build.Locator, you want MSBuildLocator to load MSBuild assemblies from an installed Visual Studio or .NET SDK instance, not from the NuGet packages in your output directory.

## Why This Is a Problem

When MSBuild runtime assemblies are copied to your application's output directory, your application may load these assemblies instead of the MSBuild installation that MSBuildLocator registered. This can lead to several runtime issues:

1. **Assembly version conflicts**: Your application loads MSBuild assemblies from your output directory while MSBuildLocator tries to load from a different MSBuild installation
2. **Missing SDKs and build logic**: The MSBuild assemblies in your output directory don't include the SDKs, targets, and build logic needed to build real projects
3. **Inconsistent behavior**: Your application may behave differently than `MSBuild.exe`, `dotnet build`, or Visual Studio when evaluating projects

## Example Runtime Error

Without `ExcludeAssets="runtime"`, you may encounter errors like:

```
System.IO.FileNotFoundException: Could not load file or assembly 'Microsoft.Build, Version=15.1.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a' or one of its dependencies.
```

Or:

```
System.InvalidOperationException: SDK 'Microsoft.NET.Sdk' could not be resolved. The SDK resolver "Microsoft.DotNet.MSBuildSdkResolver" returned null.
```

This happens because your application loads MSBuild assemblies from your bin folder (e.g., version 15.5.180) while MSBuildLocator has registered a different MSBuild installation (e.g., version 17.0) to use at runtime. The .NET runtime gets confused about which assemblies to use, leading to version conflicts and missing dependencies.

## Solution

Add `ExcludeAssets="runtime"` to all MSBuild PackageReferences in your project file:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Build" ExcludeAssets="runtime" />
  <PackageReference Include="Microsoft.Build.Framework" ExcludeAssets="runtime" />
  <PackageReference Include="Microsoft.Build.Utilities.Core" ExcludeAssets="runtime" />
  ...
</ItemGroup>
```

This tells NuGet to use these packages only for compilation, not at runtime. At runtime, MSBuildLocator will load MSBuild assemblies from the registered Visual Studio or .NET SDK installation.

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

