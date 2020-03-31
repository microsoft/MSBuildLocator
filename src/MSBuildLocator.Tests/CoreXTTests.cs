// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Shouldly;
using System;
using System.Collections.Generic;
using Xunit;

namespace Microsoft.Build.Locator.Tests
{
#if NET46
    public class CoreXTTests
    {
        /// <summary>
        /// When there are multiple major versions of Visual Studio installed, the correct one is returned.
        /// </summary>
        [Fact]
        public void DiscoverCoreXT()
        {
            const string expectedMSBuildPath = @"C:\Cx\.A\MsBuild.Corext";
            var expectedVisualStudioVersion = new Version(16, 0);

            var visualStudioInstances = new List<VisualStudioInstance>
            {
                new VisualStudioInstance("14.0", @"C:\14.0", new Version(14, 0), DiscoveryType.VisualStudioSetup),
                new VisualStudioInstance("15.9", @"C:\15.9", new Version(15, 9), DiscoveryType.VisualStudioSetup),
                new VisualStudioInstance("16.0", expectedMSBuildPath, expectedVisualStudioVersion, DiscoveryType.VisualStudioSetup),
            };

            using (new TemporaryEnvironmentVariables(new Dictionary<string, string>
            {
                ["MsBuildToolset"] = "160",
                ["MSBuildToolsPath_160"] = expectedMSBuildPath,
                ["VisualStudioVersion"] = "16.0"
            }))
            {
                var instance = MSBuildLocator.GetCoreXTInstance(visualStudioInstances, directoryExists: (path) => true);

                instance.ShouldNotBeNull();

                instance.VisualStudioRootPath.ShouldBe(expectedMSBuildPath);
                instance.MSBuildPath.ShouldBe(expectedMSBuildPath);
                instance.Version.ShouldBe(expectedVisualStudioVersion);
                instance.DiscoveryType.ShouldBe(DiscoveryType.CoreXT);
                instance.Name.ShouldBe("COREXT");
            }
        }

        /// <summary>
        /// Iterates known versions that should return something (15.0+)
        /// </summary>
        /// <param name="msbuildToolset"></param>
        /// <param name="visualStudioVersion"></param>
        [Theory]
        [InlineData("150", "15.0")]
        [InlineData("160", "16.0")]
        public void ReturnsInstanceWhenEnvironmentFound(string msbuildToolset, string visualStudioVersion)
        {
            const string expectedMSBuildToolsPath = @"D:\Cx\.A\MSBuild.Corext";

            using (new TemporaryEnvironmentVariables(new Dictionary<string, string>
            {
                ["MsBuildToolset"] = msbuildToolset,
                [$"MSBuildToolsPath_{msbuildToolset}"] = expectedMSBuildToolsPath,
                ["VisualStudioVersion"] = visualStudioVersion,
            }))
            {
                var instance = MSBuildLocator.GetCoreXTInstance(new List<VisualStudioInstance>(), directoryExists: (path) => true);

                instance.ShouldNotBeNull();

                instance.VisualStudioRootPath.ShouldBe(expectedMSBuildToolsPath);
                instance.MSBuildPath.ShouldBe(expectedMSBuildToolsPath);
                instance.Version.ShouldBe(Version.Parse(visualStudioVersion));
                instance.DiscoveryType.ShouldBe(DiscoveryType.CoreXT);
                instance.Name.ShouldBe("COREXT");
            }
        }

        /// <summary>
        /// Iterates all conditions under which an instance should not be returned.
        /// </summary>
        [Theory]
        [InlineData(null, "NotNull", "NotNull", true)]
        [InlineData("NotAnInteger", "NotNull", "NotNull", true)]
        [InlineData("149", "NotNull", "NotNull", true)]
        [InlineData("160", null, "NotNull", true)]
        [InlineData("160", @"D:\Cx\.A\MSBuild.Corext", null, true)]
        [InlineData("160", @"D:\Cx\.A\MSBuild.Corext", "16.0", false)]
        public void ReturnsNullWhenEnvironmentNotFound(string msbuildToolset, string msbuildToolsPath, string visualStudioVersion, bool directoryExists)
        {
            var visualStudioInstances = new List<VisualStudioInstance>
            {
                new VisualStudioInstance("16.0", @"C:\16.0", new Version(16, 0), DiscoveryType.VisualStudioSetup),
            };

            using (new TemporaryEnvironmentVariables(new Dictionary<string, string>
            {
                ["MsBuildToolset"] = msbuildToolset,
                ["MSBuildToolsPath_160"] = msbuildToolsPath,
                ["VisualStudioVersion"] = visualStudioVersion,
            }))
            {
                var instance = MSBuildLocator.GetCoreXTInstance(visualStudioInstances, directoryExists: (path) => directoryExists);

                instance.ShouldBeNull();
            }
        }
    }

#endif
}