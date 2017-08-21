using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Build.Shared;

namespace Microsoft.Build.MSBuildLocator
{
    public static class MSBuildLocator
    {
        private static readonly string[] s_msBuildAssemblies = {"Microsoft.Build", "Microsoft.Build.Framework"};
        private static readonly Lazy<string> s_msbuildPath = new Lazy<string>(GetMSBuildPath);

        public static string LoadDefaults()
        {
            if (s_msbuildPath.Value != null)
            {
                AppDomain.CurrentDomain.AssemblyResolve += (sender, eventArgs) =>
                {
                    var assemblyName = new AssemblyName(eventArgs.Name);
                    if (s_msBuildAssemblies.Contains(assemblyName.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        var targetAssembly = Path.Combine(s_msbuildPath.Value, assemblyName.Name + ".dll");
                        return File.Exists(targetAssembly) ? Assembly.LoadFrom(targetAssembly) : null;
                    }

                    return null;
                };

                return s_msbuildPath.Value;
            }

            return null;
        }

        private static string GetMSBuildPath()
        {
            // Dev console, probably the best case
            var vsinstalldir = Environment.GetEnvironmentVariable("VSINSTALLDIR");
            if (!string.IsNullOrEmpty(vsinstalldir))
                return FindMSBuildFromVisualStudioPath(vsinstalldir);

            // Check for VS instances
            var instances = VisualStudioLocationHelper.GetInstances();

            if (instances.Count == 0)
                throw new Exception("Couldn't find MSBuild");


            return FindMSBuildFromVisualStudioPath(instances.First().Path);
        }

        private static string FindMSBuildFromVisualStudioPath(string visualStudioPath)
        {
            if (string.IsNullOrEmpty(visualStudioPath)) return null;

            var path = Path.Combine(visualStudioPath, "MSBuild", "15.0", "Bin");
            return path;

            // TODO: Check if it exists?
            //var msBuildExe = Path.Combine(path, "MSBuild.exe");
            //var msBuildExeConfig = Path.Combine(path, "MSBuild.exe");
            //if (File.Exists(msb) && File.Exists(msBuildExeConfig)) {  ... }
        }
    }
}