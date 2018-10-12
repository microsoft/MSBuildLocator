// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace Microsoft.Build.Locator
{
    internal static class DotNetSdkLocationHelper
    {
        private static readonly Regex DotNetBasePathRegex = new Regex("Base Path:(.*)$", RegexOptions.Compiled | RegexOptions.Multiline);

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

            FileInfo dotNetAssemblyPath = new FileInfo(Path.Combine(dotNetSdkPath, "dotnet.dll"));
            if (!dotNetAssemblyPath.Exists)
            {
                return null;
            }

            FileVersionInfo fileVersionInfo = FileVersionInfo.GetVersionInfo(dotNetAssemblyPath.FullName);

            return new VisualStudioInstance(
               name: ".NET Core SDK",
               path: dotNetSdkPath,
               version: new Version(fileVersionInfo.FileMajorPart, fileVersionInfo.FileMinorPart, fileVersionInfo.FileBuildPart),
               discoveryType: DiscoveryType.DotNetSdk);
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
