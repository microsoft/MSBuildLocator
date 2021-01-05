using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace Microsoft.Build.Locator
{
    public static partial class MSBuildLocator
    {
        private static readonly string s_monoMSBuildDll_Current_RelativePath = Path.Combine ("lib", "mono", "msbuild", "Current", "bin", "MSBuild.dll");
        private static readonly string s_monoMSBuildDll_15_0_RelativePath    = Path.Combine ("lib", "mono", "msbuild", "15.0", "bin", "MSBuild.dll");
        private static readonly string s_monoOSXBasePath = "/Library/Frameworks/Mono.framework/Versions";

        internal static IEnumerable<VisualStudioInstance> GetMonoMSBuildInstances()
        {
            // $prefix/lib/mono/4.5/mscorlib.dll
            var runningMonoPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof (object).Assembly.Location), "..", "..", ".."));
            if (TryGetValidMonoVersion (runningMonoPath, out var version))
            {
                yield return new VisualStudioInstance("Mono", runningMonoPath, version, DiscoveryType.Mono);
            }

            if (!IsOSX)
            {
                // Returning just one instance on !osx
                yield break;
            }

            foreach(var dirPath in Directory.EnumerateDirectories(s_monoOSXBasePath))
            {
                if (string.Equals(Path.GetFileName(dirPath), "Current") || // skip the `Current` symlink
                    string.Equals(dirPath, runningMonoPath))               // and the running mono version
                {
                    continue;
                }

                if (TryGetValidMonoVersion(dirPath, out version))
                {
                    yield return new VisualStudioInstance("Mono", dirPath, version, DiscoveryType.Mono);
                }
            }

            bool TryGetValidMonoVersion(string path, out Version ver)
            {
                ver = null;
                if (!File.Exists(Path.Combine(path, s_monoMSBuildDll_Current_RelativePath)) &&
                        !File.Exists(Path.Combine(path, s_monoMSBuildDll_15_0_RelativePath)))
                {
                    return false;
                }

                if (TryGetMonoVersionFromMonoBinary(path, out ver) || Version.TryParse(Path.GetFileName(path), out ver))
                {
                    return true;
                }

                // The path has a valid mono, but we can't find the version
                // so, let's return the instance at least but with version 0.0.0
                ver = new Version(0, 0, 0);
                return true;
            }

            bool TryGetMonoVersionFromMonoBinary(string monoPrefixPath, out Version ver)
            {
                ver = null;
                try
                {
                    var p = new Process();
                    p.StartInfo.FileName = Path.Combine(monoPrefixPath, "bin", "mono");
                    p.StartInfo.Arguments = "--version=number";
                    p.StartInfo.UseShellExecute = false;
                    p.StartInfo.RedirectStandardOutput = true;
                    p.StartInfo.RedirectStandardError = true;

                    // Don't pollute caller's console
                    p.OutputDataReceived += (s, e) => {};
                    p.ErrorDataReceived += (s, e) => {};

                    p.Start();
                    p.WaitForExit();

                    var stdout_str = p.StandardOutput.ReadToEnd();
                    return Version.TryParse(stdout_str, out ver);
                } catch (Win32Exception) {
                }

                return false;
            }
        }

        // Taken from MSBuild/NativeMethodsShared
        private static readonly object IsRunningOnMonoLock = new object();

        private static bool? _isRunningOnMono;

        /// <summary>
        /// Gets a flag indicating if we are running under MONO
        /// </summary>
        internal static bool IsRunningOnMono
        {
            get
            {
                if (_isRunningOnMono.HasValue) return _isRunningOnMono.Value;

                lock (IsRunningOnMonoLock)
                {
                    if (_isRunningOnMono == null)
                    {
                        // There could be potentially expensive TypeResolve events, so cache IsMono.
                        _isRunningOnMono = Type.GetType("Mono.Runtime") != null;
                    }
                }

                return _isRunningOnMono.Value;
            }
        }

        // Taken from MSBuild/NativeMethodsShared
        private static bool? _isOSX;

        /// <summary>
        /// Gets a flag indicating if we are running under Mac OSX
        /// </summary>
        internal static bool IsOSX
        {
            get
            {
                if (!_isOSX.HasValue)
                {
                    _isOSX = File.Exists("/usr/lib/libc.dylib");
                }

                return _isOSX.Value;
            }
        }

    }
}
