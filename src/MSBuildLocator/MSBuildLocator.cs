// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Microsoft.Build.Locator
{
    public static class MSBuildLocator
    {
        private const string MSBuildPublicKeyToken = "b03f5f7f11d50a3a";

        private static readonly string[] s_msBuildAssemblies =
        {
            "Microsoft.Build", "Microsoft.Build.Framework", "Microsoft.Build.Tasks.Core",
            "Microsoft.Build.Utilities.Core"
        };

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
            return GetInstances().Where(i => i.DiscoveryType.HasFlag(options.DiscoveryTypes));
        }

        /// <summary>
        ///     Discover instances of Visual Studio and register the first one. See <see cref="RegisterInstance" />.
        /// </summary>
        /// <returns>Instance of Visual Studio found and registered.</returns>
        public static VisualStudioInstance RegisterDefaults()
        {
            var instance = GetInstances().FirstOrDefault();
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
                throw new ArgumentNullException(nameof(instance));

            var loadedMSBuildAssemblies = AppDomain.CurrentDomain.GetAssemblies().Where(IsMSBuildAssembly);
            if (loadedMSBuildAssemblies.Any())
            {
                var loadedAssemblyList = string.Join(Environment.NewLine, loadedMSBuildAssemblies.Select(a => a.GetName()));

                var error = $"{typeof(MSBuildLocator)}.{nameof(RegisterInstance)} was called, but MSBuild assemblies were already loaded." +
                    Environment.NewLine +
                    $"Ensure that {nameof(RegisterInstance)} is called before any method that directly references types in the Microsoft.Build namespace has been called." +
                    Environment.NewLine +
                    "Loaded MSBuild assemblies: " +
                    loadedAssemblyList;

                throw new InvalidOperationException(error);
            }

            AppDomain.CurrentDomain.AssemblyResolve += (_, eventArgs) =>
            {
                var assemblyName = new AssemblyName(eventArgs.Name);
                if (IsMSBuildAssembly(assemblyName))
                {
                    var targetAssembly = Path.Combine(instance.MSBuildPath, assemblyName.Name + ".dll");
                    return File.Exists(targetAssembly) ? Assembly.LoadFrom(targetAssembly) : null;
                }

                return null;
            };
        }

        private static bool IsMSBuildAssembly(Assembly assembly)
        {
            return IsMSBuildAssembly(assembly.GetName());
        }

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

        private static IEnumerable<VisualStudioInstance> GetInstances()
        {
            var devConsole = GetDevConsoleInstance();
            if (devConsole != null)
                yield return devConsole;

            foreach (var instance in VisualStudioLocationHelper.GetInstances())
                yield return instance;
        }

        private static VisualStudioInstance GetDevConsoleInstance()
        {
            var path = Environment.GetEnvironmentVariable("VSINSTALLDIR");
            if (!string.IsNullOrEmpty(path))
            {
                var versionString = Environment.GetEnvironmentVariable("VSCMD_VER");
                Version version;
                Version.TryParse(versionString, out version);

                if (version == null && versionString.Contains('-'))
                {
                    versionString = versionString.Substring(0, versionString.IndexOf('-'));
                    Version.TryParse(versionString, out version);
                }

                if (version == null)
                {
                    versionString = Environment.GetEnvironmentVariable("VisualStudioVersion");
                    Version.TryParse(versionString, out version);
                }

                return new VisualStudioInstance("DEVCONSOLE", path, version, DiscoveryType.DeveloperConsole);
            }

            return null;
        }
    }
}