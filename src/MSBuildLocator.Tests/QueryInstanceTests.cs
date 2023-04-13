// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Shouldly;
using System.Linq;
using System.Runtime.InteropServices;
using Xunit;


namespace Microsoft.Build.Locator.Tests
{
    public class QueryInstancesTests
    {
        [Fact]
        public void DefaultInstanceTest()
        {
            VisualStudioInstance instance = MSBuildLocator.QueryVisualStudioInstances(VisualStudioInstanceQueryOptions.Default).FirstOrDefault();

            instance.ShouldNotBeNull();

#if NETCOREAPP
            instance.DiscoveryType.ShouldBe(DiscoveryType.DotNetSdk);
#else
            instance.DiscoveryType.ShouldNotBe(DiscoveryType.DotNetSdk);
#endif
        }

#if NETCOREAPP
        [Fact]
        public void MultipleInstancesTest()
        {
            var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            if (!isWindows)
            {
                return;
            }
            var queryOptions = new VisualStudioInstanceQueryOptions
            {
                DiscoveryTypes = DiscoveryType.VisualStudioSetup | DiscoveryType.DotNetSdk,
            };
            var instances = MSBuildLocator.QueryVisualStudioInstances(queryOptions).ToList();

            // We should have at least one VS install and at least one .NET SDK install.
            instances
                .Select(inst => inst.DiscoveryType)
                .Distinct()
                .Count()
                .ShouldBe(2);
        }
#endif
    }
}
