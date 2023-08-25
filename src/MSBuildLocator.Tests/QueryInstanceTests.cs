// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Shouldly;
using System.Linq;
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
    }
}
