﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if NETCOREAPP

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using Microsoft.Build.Locator.Utils;

#nullable enable

namespace Microsoft.Build.Locator
{
    internal static class DotNetSdkLocationHelper
    {
        private static readonly Regex s_versionRegex = new(@"^(\d+)\.(\d+)\.(\d+)", RegexOptions.Multiline);
        private static readonly bool s_isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        private static readonly string s_exeName = s_isWindows ? "dotnet.exe" : "dotnet";
        private static readonly Lazy<IList<string>> s_dotnetPathCandidates = new(() => ResolveDotnetPathCandidates());

        public static VisualStudioInstance? GetInstance(string dotNetSdkPath)
        {
            if (string.IsNullOrWhiteSpace(dotNetSdkPath) || !File.Exists(Path.Combine(dotNetSdkPath, "Microsoft.Build.dll")))
            {
                return null;
            }

            string versionPath = Path.Combine(dotNetSdkPath, ".version");
            if (!File.Exists(versionPath))
            {
                return null;
            }

            // Preview versions contain a hyphen after the numeric part of the version. Version.TryParse doesn't accept that.
            Match versionMatch = s_versionRegex.Match(File.ReadAllText(versionPath));

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
            foreach (string basePath in GetDotNetBasePaths(workingDirectory))
            {
                VisualStudioInstance? dotnetSdk = GetInstance(basePath);
                if (dotnetSdk != null)
                {
                    yield return dotnetSdk;
                }
            }
        }

        private static IEnumerable<string> GetDotNetBasePaths(string workingDirectory)
        {
            try
            {
                AddUnmanagedDllResolver();

                string? bestSDK = GetSdkFromGlobalSettings(workingDirectory);
                if (!string.IsNullOrEmpty(bestSDK))
                {
                    yield return bestSDK;
                }

                string[] dotnetPaths = GetAllAvailableSDKs();
                // We want to return the newest SDKs first, however, so iterate over the list in reverse order.
                // If basePath is disqualified because it was later
                // than the runtime version, this ensures that RegisterDefaults will return the latest valid
                // SDK instead of the earliest installed.
                for (int i = dotnetPaths.Length - 1; i >= 0; i--)
                {
                    if (dotnetPaths[i] != bestSDK)
                    {
                        yield return dotnetPaths[i];
                    }
                }
            }
            finally
            {
                RemoveUnmanagedDllResolver();
            }
        }

        private static void AddUnmanagedDllResolver() => ModifyUnmanagedDllResolver(loadContext => loadContext.ResolvingUnmanagedDll += HostFxrResolver);

        private static void RemoveUnmanagedDllResolver() => ModifyUnmanagedDllResolver(loadContext => loadContext.ResolvingUnmanagedDll -= HostFxrResolver);

        private static void ModifyUnmanagedDllResolver(Action<AssemblyLoadContext> resolverAction)
        {
            // For Windows hostfxr is loaded in the process.
            if (!s_isWindows)
            {
                var loadContext = AssemblyLoadContext.GetLoadContext(Assembly.GetExecutingAssembly());
                if (loadContext != null)
                {
                    resolverAction(loadContext);
                }
            }
        }

        private static IntPtr HostFxrResolver(Assembly assembly, string libraryName)
        {
            // Library name for libhostfxr
            string hostFxrLibName = "libhostfxr";
            // Library extension for the current platform
            string libExtension = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "dylib" : "so";

            // If the requested library name is not libhostfxr, return IntPtr.Zero
            if (!hostFxrLibName.Equals(libraryName))
            {
                return IntPtr.Zero;
            }

            // Get the dotnet path candidates
            foreach (string dotnetPath in s_dotnetPathCandidates.Value)
            {
                string hostFxrRoot = Path.Combine(dotnetPath, "host", "fxr");

                // Check if the host/fxr directory exists
                if (Directory.Exists(hostFxrRoot))
                {
                    // Get a list of hostfxr assembly directories (e.g., 6.0.3, 7.0.1-preview.2.4)
                    IList<SemanticVersion> hostFxrAssemblyDirs = Directory.GetDirectories(hostFxrRoot)
                        .Select(path => SemanticVersionParser.TryParse(Path.GetFileName(path), out SemanticVersion? version) ? version : null)
                        .Where(v => v != null)
                        .Cast<SemanticVersion>()
                        .OrderByDescending(v => v)
                        .ToList();

                    foreach (SemanticVersion hostFxrDir in hostFxrAssemblyDirs)
                    {
                        string hostFxrAssemblyPath = Path.Combine(hostFxrRoot, hostFxrDir.OriginalValue, $"{hostFxrLibName}.{libExtension}");

                        if (File.Exists(hostFxrAssemblyPath))
                        {
                            if (NativeLibrary.TryLoad(hostFxrAssemblyPath, out IntPtr handle))
                            {
                                return handle;
                            }
                        }
                    }
                }
            }

            return IntPtr.Zero;
        }


        private static string SdkResolutionExceptionMessage(string methodName) => $"Failed to find all versions of .NET Core MSBuild. Call to {methodName}. There may be more details in stderr.";

        /// <summary>
        /// Determines the directory location of the SDK accounting for
        /// global.json and multi-level lookup policy.
        /// </summary>
        private static string? GetSdkFromGlobalSettings(string workingDirectory)
        {
            string? resolvedSdk = null;
            foreach (string dotnetPath in s_dotnetPathCandidates.Value)
            {
                int rc = NativeMethods.hostfxr_resolve_sdk2(exe_dir: dotnetPath, working_dir: workingDirectory, flags: 0, result: (key, value) =>
                {
                    if (key == NativeMethods.hostfxr_resolve_sdk2_result_key_t.resolved_sdk_dir)
                    {
                        resolvedSdk = value;
                    }
                });

                if (rc == 0)
                {
                    SetEnvironmentVariableIfEmpty("DOTNET_HOST_PATH", dotnetPath);
                    return resolvedSdk;
                }
            }
            
            return string.IsNullOrEmpty(resolvedSdk)
                ? throw new InvalidOperationException(SdkResolutionExceptionMessage(nameof(NativeMethods.hostfxr_resolve_sdk2)))
                : resolvedSdk;
        }

        private static IList<string> ResolveDotnetPathCandidates()
        {
            var pathCandidates = new List<string>();
            AddIfValid(GetDotnetPathFromROOT());

            string? dotnetExePath = GetCurrentProcessPath();
            bool isRunFromDotnetExecutable = !string.IsNullOrEmpty(dotnetExePath)
                && Path.GetFileName(dotnetExePath).Equals(s_exeName, StringComparison.InvariantCultureIgnoreCase);

            if (isRunFromDotnetExecutable)
            {
                AddIfValid(Path.GetDirectoryName(dotnetExePath));
            }

            AddIfValid(FindDotnetPathFromEnvVariable("DOTNET_HOST_PATH"));
            AddIfValid(FindDotnetPathFromEnvVariable("DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR"));
            AddIfValid(GetDotnetPathFromPATH());

            return pathCandidates.Count == 0
                ? throw new InvalidOperationException("Path to dotnet executable is not set. " +
                    "The probed variables are: DOTNET_ROOT, DOTNET_HOST_PATH, DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR and PATH. " +
                    "Make sure, that at least one of the listed variables points to the existing dotnet executable.")
                : pathCandidates;

            void AddIfValid(string? path)
            {
                if (!string.IsNullOrEmpty(path))
                {
                    pathCandidates.Add(path);
                }
            }
        }

        private static string? GetDotnetPathFromROOT()
        {
            // 32-bit architecture has (x86) suffix
            string envVarName = (IntPtr.Size == 4) ? "DOTNET_ROOT(x86)" : "DOTNET_ROOT";
            string? dotnetPath = FindDotnetPathFromEnvVariable(envVarName);

            return dotnetPath;
        }

        private static string? GetCurrentProcessPath() => Environment.ProcessPath;

        private static string? GetDotnetPathFromPATH()
        {
            string? dotnetPath = null;
            // We will generally find the dotnet exe on the path, but on linux, it is often just a 'dotnet' symlink (possibly even to more symlinks) that we have to resolve
            // to the real dotnet executable.
            // This will work as often as just invoking dotnet from the command line, but we can be more confident in finding a dotnet executable by following
            // https://github.com/dotnet/designs/blob/main/accepted/2021/install-location-per-architecture.md
            // This could be done using the nethost library, but this is currently shipped as metadata package (Microsoft.NETCore.DotNetAppHost) and requires the customers
            // to specify <RuntimeIdentifier> for resolving runtime assembly.
            string[] paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? Array.Empty<string>();
            foreach (string dir in paths)
            {
                string? filePath = ValidatePath(dir);
                if (!string.IsNullOrEmpty(filePath))
                {
                    dotnetPath = filePath;
                    break;
                }
            }

            return dotnetPath;
        }

        /// <summary>
        /// Returns the list of all available SDKs ordered by ascending version.
        /// </summary>
        private static string[] GetAllAvailableSDKs()
        {
            string[]? resolvedPaths = null;
            foreach (string dotnetPath in s_dotnetPathCandidates.Value)
            {
                int rc = NativeMethods.hostfxr_get_available_sdks(exe_dir: dotnetPath, result: (key, value) => resolvedPaths = value);

                if (rc == 0 && resolvedPaths != null && resolvedPaths.Length > 0)
                {
                    break;
                }
            }

            // Errors are automatically printed to stderr. We should not continue to try to output anything if we failed.
            return resolvedPaths ?? throw new InvalidOperationException(SdkResolutionExceptionMessage(nameof(NativeMethods.hostfxr_get_available_sdks)));
        }

        /// <summary>
        /// This native method call determines the actual location of path, including
        /// resolving symbolic links.
        /// </summary>
        private static string? realpath(string path)
        {
            IntPtr ptr = NativeMethods.realpath(path, IntPtr.Zero);
            string? result = Marshal.PtrToStringAuto(ptr);
            NativeMethods.free(ptr);

            return result;
        }

        private static void SetEnvironmentVariableIfEmpty(string name, string value)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name)))
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        private static string? FindDotnetPathFromEnvVariable(string environmentVariable)
        {
            string? dotnetPath = Environment.GetEnvironmentVariable(environmentVariable);

            return string.IsNullOrEmpty(dotnetPath) ? null : ValidatePath(dotnetPath);
        }

        private static string? ValidatePath(string dotnetPath)
        {
            string fullPathToDotnetFromRoot = Path.Combine(dotnetPath, s_exeName);
            if (File.Exists(fullPathToDotnetFromRoot))
            {
                if (!s_isWindows)
                {
                    fullPathToDotnetFromRoot = realpath(fullPathToDotnetFromRoot) ?? fullPathToDotnetFromRoot;
                    return File.Exists(fullPathToDotnetFromRoot) ? Path.GetDirectoryName(fullPathToDotnetFromRoot) : null;
                }

                return dotnetPath;
            }

            return null;
        }
    }
}
#endif
