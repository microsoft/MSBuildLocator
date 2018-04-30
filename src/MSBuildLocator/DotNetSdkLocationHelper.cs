// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// Taken from https://github.com/dotnet/cli/blob/8f7b58dd665f60300221a4eb5910a691749396aa/src/Microsoft.DotNet.MSBuildSdkResolver/Interop.NETStandard.cs

#if NETSTANDARD2_0
using System.Diagnostics;

namespace Microsoft.Build.Locator
{

    using System;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Text;

    internal static class DotNetSdkLocationHelper
    {
        public static VisualStudioInstance GetInstance(string workingDir)
        {
            string dotNetSdkPath = hostfxr_resolve_sdk(workingDir);

            if (String.IsNullOrWhiteSpace(dotNetSdkPath))
            {
                return null;
            }

            if (!File.Exists(Path.Combine(dotNetSdkPath, "Microsoft.Build.dll")))
            {
                return null;
            }

            FileInfo dotnetAssemblyPath = new FileInfo(Path.Combine(dotNetSdkPath, "dotnet.dll"));

            if (!dotnetAssemblyPath.Exists)
            {
                return null;
            }

            FileVersionInfo fileVersionInfo = FileVersionInfo.GetVersionInfo(dotnetAssemblyPath.FullName);

            return new VisualStudioInstance(
                name: ".NET Core SDK",
                path: dotNetSdkPath,
                version: new Version(fileVersionInfo.FileMajorPart, fileVersionInfo.FileMinorPart, fileVersionInfo.FileBuildPart),
                discoveryType: DiscoveryType.DotNetSdk);
        }

        private static string hostfxr_resolve_sdk(string workingDir)
        {
            Process process = Process.GetCurrentProcess();

            string dotNetExePath = process?.MainModule?.FileName;

            if (String.IsNullOrWhiteSpace(dotNetExePath))
            {
                return null;
            }

            string dotNetExeDirectory = Path.GetDirectoryName(dotNetExePath);

            if (String.IsNullOrWhiteSpace(dotNetExeDirectory) || !Directory.Exists(dotNetExeDirectory))
            {
                return null;
            }

            var buffer = new StringBuilder(capacity: 64);

            for (;;)
            {
                int size = Interop.hostfxr_resolve_sdk(dotNetExeDirectory, workingDir, buffer, buffer.Capacity);
                if (size <= 0)
                {
                    return null;
                }

                if (size <= buffer.Capacity)
                {
                    break;
                }

                buffer.Capacity = size;
            }

            return buffer.ToString();
        }

        private static class Interop
        {
            private static readonly bool RunningOnWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

            public static string realpath(string path)
            {
                var ptr = unix_realpath(path, IntPtr.Zero);
                var result = Marshal.PtrToStringAnsi(ptr); // uses UTF8 on Unix
                unix_free(ptr);
                return result;
            }

            public static int hostfxr_resolve_sdk(string exeDir, string workingDir, [Out] StringBuilder buffer, int bufferSize)
            {
                // hostfxr string encoding is platform -specific so dispatch to the
                // appropriately annotated P/Invoke for the current platform.
                return RunningOnWindows
                    ? windows_hostfxr_resolve_sdk(exeDir, workingDir, buffer, bufferSize)
                    : unix_hostfxr_resolve_sdk(exeDir, workingDir, buffer, bufferSize);
            }

            [DllImport("hostfxr", EntryPoint = nameof(hostfxr_resolve_sdk), CharSet = CharSet.Unicode, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
            private static extern int windows_hostfxr_resolve_sdk(string exeDir, string workingDir, [Out] StringBuilder buffer, int bufferSize);

            // CharSet.Ansi is UTF8 on Unix
            [DllImport("hostfxr", EntryPoint = nameof(hostfxr_resolve_sdk), CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
            private static extern int unix_hostfxr_resolve_sdk(string exeDir, string workingDir, [Out] StringBuilder buffer, int bufferSize);

            // CharSet.Ansi is UTF8 on Unix
            [DllImport("libc", EntryPoint = nameof(realpath), CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
            private static extern IntPtr unix_realpath(string path, IntPtr buffer);

            [DllImport("libc", EntryPoint = "free", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
            private static extern void unix_free(IntPtr ptr);
        }
    }
}
#endif