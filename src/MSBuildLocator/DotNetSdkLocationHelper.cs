// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if NETCOREAPP

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace Microsoft.Build.Locator
{
    internal static class DotNetSdkLocationHelper
    {
        private static readonly Regex DotNetBasePathRegex = new Regex("Base Path:(.*)$", RegexOptions.Multiline);
        private static readonly Regex VersionRegex = new Regex(@"^(\d+)\.(\d+)\.(\d+)", RegexOptions.Multiline);

        public static VisualStudioInstance GetInstance(string workingDirectory)
        {
            string dotNetSdkPath = GetDotNetBasePath(workingDirectory);

            if (string.IsNullOrWhiteSpace(dotNetSdkPath))
            {
                return null;
            }

            if (!File.Exists(Path.Combine(dotNetSdkPath, "Microsoft.Build.dll")))
            {
                return null;
            }

            string versionPath = Path.Combine(dotNetSdkPath, ".version");
            if (!File.Exists(versionPath))
            {
                return null;
            }

            string versionContent = File.ReadAllText(versionPath);
            Match versionMatch = VersionRegex.Match(versionContent);

            if (!versionMatch.Success)
            {
                return null;
            }

            if (!int.TryParse(versionMatch.Groups[1].Value, out int major) ||
                !int.TryParse(versionMatch.Groups[2].Value, out int minor) ||
                !int.TryParse(versionMatch.Groups[3].Value, out int patch))
            {
                return null;
            }

            return new VisualStudioInstance(
                name: ".NET Core SDK",
                path: dotNetSdkPath,
                version: new Version(major, minor, patch),
                discoveryType: DiscoveryType.DotNetSdk,
                productId: null);
        }

        private static string GetDotNetBasePath(string workingDirectory)
        {
            const string DOTNET_CLI_UI_LANGUAGE = nameof(DOTNET_CLI_UI_LANGUAGE);

            Process process;
            try
            {
                var startInfo = new ProcessStartInfo("dotnet", "--info")
                {
                    WorkingDirectory = workingDirectory,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                // Ensure that we set the DOTNET_CLI_UI_LANGUAGE environment variable to "en-US" before
                // running 'dotnet --info'. Otherwise, we may get localized results.
                startInfo.EnvironmentVariables[DOTNET_CLI_UI_LANGUAGE] = "en-US";

                process = Process.Start(startInfo);
            }
            catch
            {
                // when error running dotnet command, consider dotnet as not available
                return null;
            }

            if (process.HasExited)
            {
                return null;
            }

            var lines = new List<string>();
            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    lines.Add(e.Data);
                }
            };

            process.BeginOutputReadLine();

            process.WaitForExit();

            var outputString = string.Join(Environment.NewLine, lines);

            var matched = DotNetBasePathRegex.Match(outputString);
            if (!matched.Success)
            {
                return null;
            }

            return matched.Groups[1].Value.Trim();
        }
    }
}
#endif
