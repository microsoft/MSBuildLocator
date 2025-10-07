# Releasing MSBuildLocator

These instructions can only be followed by Microsoft-internal MSBuild maintainers.

## Release Process

1. **Create a PR** in https://github.com/microsoft/MSBuildLocator
2. **Have it reviewed** by the team.
3. Once approved, **merge it**.
4. It will automatically start a pipeline build [here](https://dev.azure.com/devdiv/DevDiv/_build?definitionId=27492).
5. Once the build succeeds, **manually run the release pipeline**:
   - Navigate to the [release pipeline](https://dev.azure.com/devdiv/DevDiv/_release?definitionId=408)
   - Click **"Run pipeline"** (top-right)
   - Select the build artifact from step 4 (it will show the build number like `20251002.3`)
   - Click **"Run"**

## Release Pipeline Stages

The release pipeline consists of two stages:

### Stage 1: Retain Build
This stage runs automatically and archives symbols to Symweb.

### Stage 2: Public Release
This stage includes:
1. **Push to NuGet.org** - Automatically pushes the package to NuGet.org
2. **Manual validation steps**:
   - You'll see manual validation tasks that pause the pipeline
   - Click **"Resume"** or **"Reject"** as appropriate
   - Follow the instructions provided in each manual validation step
3. **GitHub Release** - Manual step to create the GitHub release

## Releasing a Non-Preview Version of MSBuildLocator

The above steps will push a **preview package** to NuGet.org. To make a **final release** (non-preview) version:

1. Merge the latest changes into a release branch like `release/1.10`
2. Follow the same steps as above
3. The pipeline will detect the release branch and publish a release package (without preview suffix) to NuGet.org

## Changing the Version

Nerdbank.GitVersioning automatically updates the build version with every commit. The major and minor versions (as well as the assembly version) are set manually in `version.json`.

