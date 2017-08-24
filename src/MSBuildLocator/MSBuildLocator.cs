// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Microsoft.Build.MSBuildLocator
{
    public static class MSBuildLocator
    {
        private static readonly string[] s_msBuildAssemblies =
        {
            "Microsoft.Build", "Microsoft.Build.Framework", "Microsoft.Build.Tasks.Core",
            "Microsoft.Build.Utilities.Core"
        };

        private static readonly Lazy<IList<VisualStudioInstance>> s_instances =
            new Lazy<IList<VisualStudioInstance>>(GetInstances);

        public static IEnumerable<VisualStudioInstance> Instances => s_instances.Value;

        public static VisualStudioInstance RegisterDefaults()
        {
            var instance = Instances.FirstOrDefault();
            RegisterInstance(instance);

            return instance;
        }

        public static void RegisterInstance(VisualStudioInstance instance)
        {
            if (instance == null)
                throw new ArgumentNullException(nameof(instance));

            AppDomain.CurrentDomain.AssemblyResolve += (sender, eventArgs) =>
            {
                var assemblyName = new AssemblyName(eventArgs.Name);
                if (s_msBuildAssemblies.Contains(assemblyName.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var targetAssembly = Path.Combine(instance.MSBuildPath, assemblyName.Name + ".dll");
                    return File.Exists(targetAssembly) ? Assembly.LoadFrom(targetAssembly) : null;
                }

                return null;
            };
        }

        private static List<VisualStudioInstance> GetInstances()
        {
            var instances = new List<VisualStudioInstance>();

            var devConsole = GetDevConsoleInstance();
            if (devConsole != null) instances.Add(devConsole);
            instances.AddRange(VisualStudioLocationHelper.GetInstances());

            return instances;
        }

        private static VisualStudioInstance GetDevConsoleInstance()
        {
            var path = Environment.GetEnvironmentVariable("VSINSTALLDIR");
            if (!string.IsNullOrEmpty(path))
            {
                var versionString = Environment.GetEnvironmentVariable("VSCMD_VER");
                Version version;
                Version.TryParse(versionString, out version);

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