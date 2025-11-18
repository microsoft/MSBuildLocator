# Microsoft.Build.Locator

Microsoft.Build.Locator helps you locate and register MSBuild assemblies provided with Visual Studio or the .NET SDK. This is essential when you need to use MSBuild APIs in your application to evaluate or build projects.

## Why do I need this?

When using MSBuild's .NET API to load and build projects, you need access to the SDKs and build logic distributed with Visual Studio or the .NET SDK, not just the MSBuild APIs. MSBuildLocator helps you find these installations and set up your application to use them, ensuring your code gets the same view of projects as `MSBuild.exe`, `dotnet build`, or Visual Studio.

## Quick Start

Before using any MSBuild APIs, register MSBuild assemblies:

```csharp
using Microsoft.Build.Locator;
using Microsoft.Build.Evaluation;

// Register defaults before using any MSBuild types
MSBuildLocator.RegisterDefaults();

// Now you can safely use MSBuild APIs.
// NOTE: due the the way that the CLR loads assemblies, you MUST
//       register MSBuild through Locator before any types from 
//       the MSBuild assemblies are used in your application.
//       The safest way to ensure this is to put any MSBuild API
//       access into a separate method.
LoadProject();

void LoadProject()
{
  var project = new Project("MyProject.csproj");
  ...
}
```

For more control over which MSBuild instance to use:

```csharp
using Microsoft.Build.Locator;

// Query available MSBuild instances
var instances = MSBuildLocator.QueryVisualStudioInstances().ToArray();

// Register a specific instance
var instance = instances.OrderByDescending(i => i.Version).First();
MSBuildLocator.RegisterInstance(instance);
```

## Documentation

For complete documentation, see [Use Microsoft.Build.Locator](https://learn.microsoft.com/visualstudio/msbuild/updating-an-existing-application#use-microsoftbuildlocator) on Microsoft Learn.

## Samples

See the [BuilderApp](https://github.com/microsoft/MSBuildLocator/blob/a349ee7ffd889cd7634d3fd8b413bf9f29244b50/samples/BuilderApp) sample for a full
exploration of the MSBuildLocator library and capabilities.


