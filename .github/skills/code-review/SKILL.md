---
name: code-review
description: >-
  Repository-specific checks for Microsoft.Build.Locator code reviews. Use for
  loader, discovery, public API, and package changes.
license: MIT
---

# Microsoft.Build.Locator code review

Read `AGENTS.md`. For loader, registration, or discovery changes, also read the
applicable `msbuild-loader-netcore` or `msbuild-loader-netframework` skill; read
both for common code. Verify that the skills accurately describe the code, and
require them to change when behavior changes.

Check these repository-specific invariants:

- Common code must work on both `net46` and `net8.0`; runtime-specific behavior
  must stay behind the correct conditional.
- Registration must happen before any core `Microsoft.Build*` assembly loads.
- .NET Framework discovers Visual Studio; .NET 8+ discovers the .NET SDK.
- .NET Framework changes must remain compatible across supported Visual Studio
  versions; do not assume only the latest MSBuild layout or behavior.
- `Microsoft.Build.Locator.dll` must retain minimal framework-only dependencies.
- Intentional public API changes require compatibility suppressions and an
  appropriate version change.
