// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

#if NETCOREAPP
using System.Runtime.Loader;
#endif

namespace Microsoft.Build.Locator
{
    public static class MSBuildLocator
    {
        private const string MSBuildPublicKeyToken = "b03f5f7f11d50a3a";

        private static readonly string[] s_msBuildAssemblies =
        {
            "Microsoft.Build",
            "Microsoft.Build.Framework",
            "Microsoft.Build.Tasks.Core",
            "Microsoft.Build.Utilities.Core"
        };

        private static readonly string s_monoMSBuildDll_Current_RelativePath = Path.Combine ("lib", "mono", "msbuild", "Current", "bin", "MSBuild.dll");
        private static readonly string s_monoMSBuildDll_15_0_RelativePath    = Path.Combine ("lib", "mono", "msbuild", "15.0", "bin", "MSBuild.dll");
        private static readonly string s_monoOSXBasePath = "/Library/Frameworks/Mono.framework/Versions";

#if NET46
        private static ResolveEventHandler s_registeredHandler;
#else
        private static Func<AssemblyLoadContext, AssemblyName, Assembly> s_registeredHandler;
#endif

        // Used to determine when it's time to unregister the registeredHandler.
        private static int numResolvedAssemblies;

        /// <summary>
        ///     Gets a value indicating whether an instance of MSBuild is currently registered.
        /// </summary>
        public static bool IsRegistered => s_registeredHandler != null;

        /// <summary>
        ///     Gets a value indicating whether an instance of MSBuild can be registered.
        /// </summary>
        /// <remarks>
        ///     If any Microsoft.Build assemblies are already loaded into the current AppDomain, the value will be false.
        /// </remarks>
        public static bool CanRegister => !IsRegistered && !LoadedMsBuildAssemblies.Any();

        private static IEnumerable<Assembly> LoadedMsBuildAssemblies => AppDomain.CurrentDomain.GetAssemblies().Where(IsMSBuildAssembly);

        /// <summary>
        ///     Query for all Visual Studio instances.
        /// </summary>
        /// <remarks>
        ///     Only includes Visual Studio 2017 (v15.0) and higher.
        /// </remarks>
        /// <returns>Enumeration of all Visual Studio instances detected on the machine.</returns>
        public static IEnumerable<VisualStudioInstance> QueryVisualStudioInstances()
        {
            return QueryVisualStudioInstances(VisualStudioInstanceQueryOptions.Default);
        }

        /// <summary>
        ///     Query for Visual Studio instances matching the given options.
        /// </summary>
        /// <remarks>
        ///     Only includes Visual Studio 2017 (v15.0) and higher.
        /// </remarks>
        /// <param name="options">Query options for Visual Studio instances.</param>
        /// <returns>Enumeration of Visual Studio instances detected on the machine.</returns>
        public static IEnumerable<VisualStudioInstance> QueryVisualStudioInstances(
            VisualStudioInstanceQueryOptions options)
        {
            return QueryVisualStudioInstances(GetInstances(options), options);
        }

        internal static IEnumerable<VisualStudioInstance> QueryVisualStudioInstances(
            IEnumerable<VisualStudioInstance> instances,
            VisualStudioInstanceQueryOptions options)
        {
            return instances.Where(i => options.DiscoveryTypes.HasFlag(i.DiscoveryType));
        }

        /// <summary>
        ///     Discover instances of Visual Studio and register the first one. See <see cref="RegisterInstance" />.
        /// </summary>
        /// <returns>Instance of Visual Studio found and registered.</returns>
        public static VisualStudioInstance RegisterDefaults()
        {
            var instance = GetInstances(VisualStudioInstanceQueryOptions.Default).FirstOrDefault();
            if (instance == null)
            {
                var error = "No instances of MSBuild could be detected." +
                            Environment.NewLine +
                            $"Try calling {nameof(RegisterInstance)} or {nameof(RegisterMSBuildPath)} to manually register one.";

                throw new InvalidOperationException(error);
            }

            RegisterInstance(instance);

            return instance;
        }

        /// <summary>
        ///     Add assembly resolution for Microsoft.Build core dlls in the current AppDomain from the specified
        ///     instance of Visual Studio. See <see cref="QueryVisualStudioInstances()" /> to discover Visual Studio
        ///     instances or use <see cref="RegisterDefaults" />.
        /// </summary>
        /// <param name="instance"></param>
        public static void RegisterInstance(VisualStudioInstance instance)
        {
            if (instance == null)
            {
                throw new ArgumentNullException(nameof(instance));
            }

            if (instance.DiscoveryType == DiscoveryType.DotNetSdk)
            {
                // The dotnet cli sets up these environment variables when msbuild is invoked via `dotnet`,
                // but we are using msbuild dlls directly and therefore need to mimic that.
                ApplyDotNetSdkEnvironmentVariables(instance.MSBuildPath);
            }

            RegisterMSBuildPath(instance.MSBuildPath);
        }

        /// <summary>
        ///     Add assembly resolution for Microsoft.Build core dlls in the current AppDomain from the specified
        ///     path.
        /// </summary>
        /// <param name="msbuildPath">
        ///     Path to the directory containing a deployment of MSBuild binaries.
        ///     A minimal MSBuild deployment would be the publish result of the Microsoft.Build.Runtime package.
        ///
        ///     In order to restore and build real projects, one needs a deployment that contains the rest of the toolchain (nuget, compilers, etc.).
        ///     Such deployments can be found in installations such as Visual Studio or dotnet CLI.
        /// </param>
        public static void RegisterMSBuildPath(string msbuildPath)
        {
            if (string.IsNullOrWhiteSpace(msbuildPath))
            {
                throw new ArgumentException("Value may not be null or whitespace", nameof(msbuildPath));
            }

            if (!Directory.Exists(msbuildPath))
            {
                throw new ArgumentException($"Directory \"{msbuildPath}\" does not exist", nameof(msbuildPath));
            }

            if (!CanRegister)
            {
                var loadedAssemblyList = string.Join(Environment.NewLine, LoadedMsBuildAssemblies.Select(a => a.GetName()));

                var error = $"{typeof(MSBuildLocator)}.{nameof(RegisterInstance)} was called, but MSBuild assemblies were already loaded." +
                    Environment.NewLine +
                    $"Ensure that {nameof(RegisterInstance)} is called before any method that directly references types in the Microsoft.Build namespace has been called." +
                    Environment.NewLine +
                    "Loaded MSBuild assemblies: " +
                    loadedAssemblyList;

                throw new InvalidOperationException(error);
            }

            // AssemblyResolve event can fire multiple times for the same assembly, so keep track of what's already been loaded.
            var loadedAssemblies = new Dictionary<string, Assembly>(s_msBuildAssemblies.Length);

            // Saving the handler in a static field so it can be unregistered later.
#if NET46
            s_registeredHandler = (_, eventArgs) =>
            {
                var assemblyName = new AssemblyName(eventArgs.Name);
                return TryLoadAssembly(new AssemblyName(eventArgs.Name));
            };

            AppDomain.CurrentDomain.AssemblyResolve += s_registeredHandler;
#else
            s_registeredHandler = (assemblyLoadContext, assemblyName) => 
            {
                return TryLoadAssembly(assemblyName);
            };

            AssemblyLoadContext.Default.Resolving += s_registeredHandler;
#endif

            return;

            Assembly TryLoadAssembly(AssemblyName assemblyName)
            {
                // Assembly resolution is not thread-safe.
                lock (loadedAssemblies)
                {
                    Assembly assembly;
                    if (loadedAssemblies.TryGetValue(assemblyName.FullName, out assembly))
                    {
                        return assembly;
                    }

                    if (IsMSBuildAssembly(assemblyName))
                    {
                        var targetAssembly = Path.Combine(msbuildPath, assemblyName.Name + ".dll");
                        if (File.Exists(targetAssembly))
                        {
                            // Automatically unregister the handler once all supported assemblies have been loaded.
                            if (Interlocked.Increment(ref numResolvedAssemblies) == s_msBuildAssemblies.Length)
                            {
                                Unregister();
                            }

                            assembly = Assembly.LoadFrom(targetAssembly);
                            loadedAssemblies.Add(assemblyName.FullName, assembly);
                            return assembly;
                        }
                    }

                    return null;
                }
            }
        }

        /// <summary>
        ///     Remove assembly resolution previously registered via <see cref="RegisterInstance" />, <see cref="RegisterMSBuildPath" />, or <see cref="RegisterDefaults" />.
        /// </summary>
        /// <remarks>
        ///     This will automatically be called once all supported assemblies are loaded into the current AppDomain and so generally is not necessary to call directly.
        /// </remarks>
        public static void Unregister()
        {
            if (!IsRegistered)
            {
                var error = $"{typeof(MSBuildLocator)}.{nameof(Unregister)} was called, but no MSBuild instance is registered." + Environment.NewLine;
                if (numResolvedAssemblies == 0)
                {
                    error += $"Ensure that {nameof(RegisterInstance)}, {nameof(RegisterMSBuildPath)}, or {nameof(RegisterDefaults)} is called before calling this method.";
                }
                else
                {
                    error += "Unregistration automatically occurs once all supported assemblies are loaded into the current AppDomain and so generally is not necessary to call directly.";
                }

                error += Environment.NewLine +
                         $"{nameof(IsRegistered)} should be used to determine whether calling {nameof(Unregister)} is a valid operation.";

                throw new InvalidOperationException(error);
            }

#if NET46
            AppDomain.CurrentDomain.AssemblyResolve -= s_registeredHandler;
#else
            AssemblyLoadContext.Default.Resolving -= s_registeredHandler;
#endif
        }

        /// <summary>
        ///     Ensures the proper MSBuild environment variables are populated for DotNet SDK.
        /// </summary>
        /// <param name="msbuildPath">
        ///     Path to the directory containing the DotNet SDK.
        /// </param>
        private static void ApplyDotNetSdkEnvironmentVariables(string dotNetSdkPath)
        {
            const string MSBUILD_EXE_PATH = nameof(MSBUILD_EXE_PATH);
            const string MSBuildExtensionsPath = nameof(MSBuildExtensionsPath);
            const string MSBuildSDKsPath = nameof(MSBuildSDKsPath);

            var variables = new Dictionary<string, string>
            {
                [MSBUILD_EXE_PATH] = dotNetSdkPath + "MSBuild.dll",
                [MSBuildExtensionsPath] = dotNetSdkPath,
                [MSBuildSDKsPath] = dotNetSdkPath + "Sdks"
            };

            foreach (var kvp in variables)
            {
                Environment.SetEnvironmentVariable(kvp.Key, kvp.Value);
            }
        }

        private static bool IsMSBuildAssembly(Assembly assembly) => IsMSBuildAssembly(assembly.GetName());

        private static bool IsMSBuildAssembly(AssemblyName assemblyName)
        {
            if (!s_msBuildAssemblies.Contains(assemblyName.Name, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            var publicKeyToken = assemblyName.GetPublicKeyToken();
            if (publicKeyToken == null || publicKeyToken.Length == 0)
            {
                return false;
            }

            var sb = new StringBuilder();
            foreach (var b in publicKeyToken)
            {
                sb.Append($"{b:x2}");
            }

            return sb.ToString().Equals(MSBuildPublicKeyToken, StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<VisualStudioInstance> GetInstances(VisualStudioInstanceQueryOptions options)
        {
            if (options.DiscoveryTypes.HasFlag(DiscoveryType.Mono) && IsRunningOnMono)
            {
                foreach(var instance in GetMonoMSBuildInstances())
                    yield return instance;

                yield break;
            }

#if NET46
            var devConsole = GetDevConsoleInstance();
            if (devConsole != null)
                yield return devConsole;

    #if FEATURE_VISUALSTUDIOSETUP
            foreach (var instance in VisualStudioLocationHelper.GetInstances())
                yield return instance;
    #endif
#endif

#if NETCOREAPP
            var dotnetSdk = DotNetSdkLocationHelper.GetInstance(options.WorkingDirectory);
            if (dotnetSdk != null)
                yield return dotnetSdk;
#endif
        }

        static IEnumerable<VisualStudioInstance> GetMonoMSBuildInstances ()
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
                if (string.Compare(Path.GetFileName (dirPath), "Current") == 0 || // skip the `Current` symlink
                    string.Compare(dirPath, runningMonoPath) == 0)               // and the running mono version
                {
                    continue;
                }

                if (TryGetValidMonoVersion(dirPath, out version))
                {
                    yield return new VisualStudioInstance("Mono", dirPath, version, DiscoveryType.Mono);
                }
            }

            bool TryGetValidMonoVersion (string path, out Version ver)
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
                ver = new Version (0, 0, 0);
                return true;
            }

            bool TryGetMonoVersionFromMonoBinary(string monoPrefixPath, out Version ver)
            {
                ver = null;
                try
                {
                    var p = new Process ();
                    p.StartInfo.FileName = Path.Combine (monoPrefixPath, "bin", "mono");
                    p.StartInfo.Arguments = "--version=number";
                    p.StartInfo.UseShellExecute = false;
                    p.StartInfo.RedirectStandardOutput = true;
                    p.StartInfo.RedirectStandardError = true;

                    // Don't pollute caller's console
                    p.OutputDataReceived += (s, e) => {};
                    p.ErrorDataReceived += (s, e) => {};

                    p.Start ();
                    p.WaitForExit ();

                    var stdout_str = p.StandardOutput.ReadToEnd ();
                    return Version.TryParse(stdout_str, out ver);
                } catch (Win32Exception) {
                }

                return false;
            }
        }



#if NET46
        private static VisualStudioInstance GetDevConsoleInstance()
        {
            var path = Environment.GetEnvironmentVariable("VSINSTALLDIR");
            if (!string.IsNullOrEmpty(path))
            {
                var versionString = Environment.GetEnvironmentVariable("VSCMD_VER");
                Version version;
                Version.TryParse(versionString, out version);

                if (version == null && versionString?.Contains('-') == true)
                {
                    versionString = versionString.Substring(0, versionString.IndexOf('-'));
                    Version.TryParse(versionString, out version);
                }

                if (version == null)
                {
                    versionString = Environment.GetEnvironmentVariable("VisualStudioVersion");
                    Version.TryParse(versionString, out version);
                }

                if(version != null)
                {
                    return new VisualStudioInstance("DEVCONSOLE", path, version, DiscoveryType.DeveloperConsole);
                }
            }

            return null;
        }
#endif

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
