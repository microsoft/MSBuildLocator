// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Build.Locator.Utils
{
    internal class SemanticVersion : IComparable<SemanticVersion>
    {
        private readonly IEnumerable<string> _releaseLabels;
        private readonly Version _version;

        public SemanticVersion(Version version, IEnumerable<string> releaseLabels, string originalValue)
        {
            _version = version ?? throw new ArgumentNullException(nameof(version));

            if (releaseLabels != null)
            {
                _releaseLabels = releaseLabels.ToArray();
            }

            OriginalValue = originalValue;
        }

        /// <summary>
        /// Major version X (X.y.z)
        /// </summary>
        public int Major => _version.Major;

        /// <summary>
        /// Minor version Y (x.Y.z)
        /// </summary>
        public int Minor => _version.Minor;

        /// <summary>
        /// Patch version Z (x.y.Z)
        /// </summary>
        public int Patch => _version.Build;

        /// <summary>
        /// A collection of pre-release labels attached to the version.
        /// </summary>
        public IEnumerable<string> ReleaseLabels => _releaseLabels ?? Enumerable.Empty<string>();

        public string OriginalValue { get; }

        /// <summary>
        /// The full pre-release label for the version.
        /// </summary>
        public string Release => _releaseLabels != null ? string.Join(".", _releaseLabels) : string.Empty;

        /// <summary>
        /// True if pre-release labels exist for the version.
        /// </summary>
        public bool IsPrerelease
        {
            get
            {
                if (ReleaseLabels != null)
                {
                    IEnumerator<string> enumerator = ReleaseLabels.GetEnumerator();
                    return enumerator.MoveNext() && !string.IsNullOrEmpty(enumerator.Current);
                }

                return false;
            }
        }

        /// <summary>
        /// Compare versions.
        /// </summary>
        public int CompareTo(SemanticVersion other) => VersionComparer.Compare(this, other);
    }
}
