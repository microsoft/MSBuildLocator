// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if NETCOREAPP

using Microsoft.Build.Locator.Utils;
using Shouldly;
using System.Linq;
using Xunit;

namespace Microsoft.Build.Locator.Tests
{
    public class SemanticVersionParserTests
    {
        [Fact]
        public void TryParseTest_ReleaseVersion()
        {
            string version = "7.0.333";

            bool isParsed = SemanticVersionParser.TryParse(version, out SemanticVersion parsedVersion);

            _ = parsedVersion.ShouldNotBeNull();
            isParsed.ShouldBeTrue();
            parsedVersion.Major.ShouldBe(7);
            parsedVersion.Minor.ShouldBe(0);
            parsedVersion.Patch.ShouldBe(333);
            parsedVersion.ReleaseLabels.ShouldBeEmpty();
        }

        [Fact]
        public void TryParseTest_PreviewVersion()
        {
            string version = "8.0.0-preview.6.23329.7";

            bool isParsed = SemanticVersionParser.TryParse(version, out SemanticVersion parsedVersion);

            _ = parsedVersion.ShouldNotBeNull();
            isParsed.ShouldBeTrue();
            parsedVersion.Major.ShouldBe(8);
            parsedVersion.Minor.ShouldBe(0);
            parsedVersion.Patch.ShouldBe(0);
            parsedVersion.ReleaseLabels.ShouldBe(new[] { "preview", "6", "23329", "7" });
        }

        [Fact]
        public void TryParseTest_InvalidInput_LeadingZero()
        {
            string version = "0.0-preview.6";

            bool isParsed = SemanticVersionParser.TryParse(version, out SemanticVersion parsedVersion);

            Assert.Null(parsedVersion);
            isParsed.ShouldBeFalse();
        }

        [Fact]
        public void TryParseTest_InvalidInput_FourPartsVersion()
        {
            string version = "5.0.3.4";

            bool isParsed = SemanticVersionParser.TryParse(version, out SemanticVersion parsedVersion);

            Assert.Null(parsedVersion);
            isParsed.ShouldBeFalse();
        }

        [Fact]
        public void VersionSortingTest_WithPreview()
        {
            string[] versions = new[] { "7.0.7", "8.0.0-preview.6.23329.7", "8.0.0-preview.3.23174.8" };

            SemanticVersion maxVersion = versions.Select(v => SemanticVersionParser.TryParse(v, out SemanticVersion parsedVersion) ? parsedVersion : null).Max();

            maxVersion.OriginalValue.ShouldBe("8.0.0-preview.6.23329.7");
        }

        [Fact]
        public void VersionSortingTest_ReleaseOnly()
        {
            string[] versions = new[] { "7.0.7", "3.7.2", "10.0.0" };

            SemanticVersion maxVersion = versions.Max(v => SemanticVersionParser.TryParse(v, out SemanticVersion parsedVersion) ? parsedVersion : null);

            maxVersion.OriginalValue.ShouldBe("10.0.0");
        }

        [Fact]
        public void VersionSortingTest_WithInvalidFolderNames()
        {
            string[] versions = new[] { "7.0.7", "3.7.2", "dummy", "5.7.8.9" };

            SemanticVersion maxVersion = versions.Max(v => SemanticVersionParser.TryParse(v, out SemanticVersion parsedVersion) ? parsedVersion : null);

            maxVersion.OriginalValue.ShouldBe("7.0.7");
        }

        [Fact]
        public void VersionSortingTest_WithAllInvalidFolderNames()
        {
            string[] versions = new[] { "dummy", "5.7.8.9" };

            SemanticVersion maxVersion = versions.Max(v => SemanticVersionParser.TryParse(v, out SemanticVersion parsedVersion) ? parsedVersion : null);

            maxVersion.ShouldBeNull();
        }
    }
}
#endif
