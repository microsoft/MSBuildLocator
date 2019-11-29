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
        private static readonly Regex SdkRegex = new Regex(@"(\d+\.\d+\.\d+) \[(.*)]$", RegexOptions.Multiline);

        public static VisualStudioInstance GetInstance(string workingDirectory, string dotNetSdkPath)
        {            
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
                discoveryType: DiscoveryType.DotNetSdk);
        }

        public static IEnumerable<VisualStudioInstance> GetInstances(string workingDirectory)
        {
            var basePaths = GetDotNetBasePaths(workingDirectory);

            foreach (var basePath in basePaths)
            {
                yield return GetInstance(workingDirectory, basePath);
            }
        }

        private static List<string> GetDotNetBasePaths(string workingDirectory)
        {
            const string DOTNET_CLI_UI_LANGUAGE = nameof(DOTNET_CLI_UI_LANGUAGE);

            List<string> basePaths = new List<string>();

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

            var lineSdkIndex = lines.FindIndex(line => line.Contains(".NET Core SDKs installed"));

            if (lineSdkIndex != -1)
            {
                lineSdkIndex++; 

                while (lineSdkIndex < lines.Count && !lines[lineSdkIndex].Contains(".NET Core runtimes installed"))
                {
                    var ma = SdkRegex.Match(lines[lineSdkIndex]);

                    if (!ma.Success)
                        break;

                    var version = ma.Groups[1].Value.Trim();                     
                    var path = ma.Groups[2].Value.Trim();

                    path = Path.Combine(path, version) + "\\";

                    //basePaths.Add(path);

                    // We insert at index 0 so that instance list will be sorted descending so that instances.FirstOrDefault() 
                    // will always return the latest installed version of dotnet SDK 
                    basePaths.Insert(0, path);      

                    lineSdkIndex++;
                }
            }

            var matched = DotNetBasePathRegex.Match(outputString);
            if (!matched.Success)
            {
                return null;
            }

            //basePaths.Add(matched.Groups[1].Value.Trim());
            basePaths.Insert(0, matched.Groups[1].Value.Trim());    // Insert at index 0 to not break FirstOrDefault() returning the latest SDK

            return basePaths;
        }
    }
}
#endif
