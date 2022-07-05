// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if NETCOREAPP

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Microsoft.Build.Locator
{
    internal static class DotNetSdkLocationHelper
    {
        private static readonly Regex DotNetBasePathRegex = new Regex("Base Path:(.*)$", RegexOptions.Multiline);
        private static readonly Regex VersionRegex = new Regex(@"^(\d+)\.(\d+)\.(\d+)", RegexOptions.Multiline);
        private static readonly Regex SdkRegex = new Regex(@"(\S+) \[(.*?)]$", RegexOptions.Multiline);

        public static VisualStudioInstance GetInstance(string dotNetSdkPath)
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

            // Preview versions contain a hyphen after the numeric part of the version. Version.TryParse doesn't accept that.
            Match versionMatch = VersionRegex.Match(File.ReadAllText(versionPath));

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
            
            // Components of the SDK often have dependencies on the runtime they shipped with, including that several tasks that shipped
            // in the .NET 5 SDK rely on the .NET 5.0 runtime. Assuming the runtime that shipped with a particular SDK has the same version,
            // this ensures that we don't choose an SDK that doesn't work with the runtime of the chosen application. This is not guaranteed
            // to always work but should work for now.
            if (major > Environment.Version.Major ||
                (major == Environment.Version.Major && minor > Environment.Version.Minor))
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
            foreach (var basePath in GetDotNetBasePaths(workingDirectory))
            {
                var dotnetSdk = GetInstance(basePath);
                if (dotnetSdk != null)
                    yield return dotnetSdk;
            }
        }

        enum hostfxr_resolve_sdk2_flags_t
        {
            disallow_prerelease = 0x1,
        };

        enum hostfxr_resolve_sdk2_result_key_t
        {
            resolved_sdk_dir = 0,
            global_json_path = 1,
        };

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Auto)]
        private delegate void hostfxr_resolve_sdk2_result_fn(
                hostfxr_resolve_sdk2_result_key_t key,
                string value);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Auto)]
        private delegate void hostfxr_get_available_sdks_result_fn(
                hostfxr_resolve_sdk2_result_key_t key,
                [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)]
                string[] value);

        [DllImport("hostfxr", CharSet = CharSet.Auto, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        private static extern int hostfxr_resolve_sdk2(
            string exe_dir,
            string working_dir,
            hostfxr_resolve_sdk2_flags_t flags,
            hostfxr_resolve_sdk2_result_fn result);

        [DllImport("hostfxr", CharSet = CharSet.Auto, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        private static extern int hostfxr_get_available_sdks(string exe_dir, hostfxr_get_available_sdks_result_fn result);

        [DllImport("libc", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr realpath(string path, IntPtr buffer);

        [DllImport("libc", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        private static extern void free(IntPtr ptr);

        private static string realpath(string path)
        {
            IntPtr ptr = realpath(path, IntPtr.Zero);
            string result = Marshal.PtrToStringAuto(ptr);
            free(ptr);
            return result;
        }

        private static IEnumerable<string> GetDotNetBasePaths(string workingDirectory)
        {
            string dotnetPath = null;

            // Windows
            foreach (string dir in Environment.GetEnvironmentVariable("PATH").Split(';'))
            {
                if (File.Exists(Path.Combine(dir, "dotnet.exe")))
                {
                    dotnetPath = dir;
                    break;
                }
            }

            if (dotnetPath is null)
            {
                // Unix
                foreach (string dir in Environment.GetEnvironmentVariable("PATH").Split(':'))
                {
                    if (File.Exists(Path.Combine(dir, "dotnet")))
                    {
                        dotnetPath = dir;
                        break;
                    }
                }
            }

            dotnetPath = realpath(dotnetPath) ?? dotnetPath;

            string bestSDK = null;
            int rc = hostfxr_resolve_sdk2(exe_dir: dotnetPath, working_dir: workingDirectory, flags: 0, result: (key, value) =>
            {
                if (key == hostfxr_resolve_sdk2_result_key_t.resolved_sdk_dir)
                {
                    bestSDK = value;
                }
            });

            if (rc == 0 && bestSDK != null)
            {
                yield return bestSDK;
            }

            string[] paths = null;
            hostfxr_get_available_sdks(exe_dir: dotnetPath, result: (key, value) =>
            {
                paths = value;
            });

            if (rc != 0)
            {
                yield break;
            }

            // The paths are sorted in increasing order. We want to return the newest SDKs first, however,
            // so iterate over the list in reverse order. If basePath is disqualified because it was later
            // than the runtime version, this ensures that RegisterDefaults will return the latest valid
            // SDK instead of the earliest installed.
            for (int i = paths.Length - 1; i >= 0; i--)
            {
                if (paths[i] != bestSDK)
                {
                    yield return paths[i];
                }
            }
        }
    }
}
#endif
