using System;
using System.Collections.Generic;

namespace Microsoft.Build.Locator.Tests
{
    /// <summary>
    /// Represents a class for temporarily setting environment variables.
    /// </summary>
    public class TemporaryEnvironmentVariables : IDisposable
    {
        private readonly Dictionary<string, string> _backup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Initializes a new instance of the <see cref="TemporaryEnvironmentVariables" /> class.
        /// </summary>
        /// <param name="environmentVariables">A <see cref="IDictionary{String,String}" /> containing the temporary environment variables to set.</param>
        public TemporaryEnvironmentVariables(IDictionary<string, string> environmentVariables)
        {
            if (environmentVariables == null)
            {
                throw new ArgumentNullException(nameof(environmentVariables));
            }

            foreach (var environmentVariable in environmentVariables)
            {
                // Back up existing value
                _backup[environmentVariable.Key] = Environment.GetEnvironmentVariable(environmentVariable.Key);

                // Set new value
                Environment.SetEnvironmentVariable(environmentVariable.Key, environmentVariable.Value);
            }
        }

        public void Dispose()
        {
            foreach (var environmentVariable in _backup)
            {
                // Restore backed up value
                Environment.SetEnvironmentVariable(environmentVariable.Key, environmentVariable.Value);
            }
        }
    }
}