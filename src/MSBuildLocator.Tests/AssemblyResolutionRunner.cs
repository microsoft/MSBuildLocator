// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if !NETCOREAPP

using System;
using System.Linq;
using System.Reflection;

namespace Microsoft.Build.Locator.Tests
{
    /// <summary>
    ///     Drives a real registration inside a child AppDomain. Registration installs a process-wide
    ///     <see cref="AppDomain.AssemblyResolve"/> handler and cannot be undone, so it must not happen in the
    ///     AppDomain running the tests. Statics and assembly loads are per-AppDomain, so a child domain gives
    ///     each test a clean registration that disappears when the domain unloads.
    /// </summary>
    public sealed class AssemblyResolutionRunner : MarshalByRefObject
    {
        /// <summary>Registers <paramref name="msbuildSearchPaths"/>, returning <see langword="null"/> on success.</summary>
        public string Register(string[] msbuildSearchPaths)
        {
            try
            {
                MSBuildLocator.RegisterMSBuildPath(msbuildSearchPaths);
                return null;
            }
            catch (Exception e)
            {
                return e.ToString();
            }
        }

        public string GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);

        /// <summary>Loads <paramref name="assemblyName"/> the way any consumer of MSBuild would.</summary>
        public AssemblyLoadResult Load(string assemblyName)
        {
            try
            {
                Assembly assembly = Assembly.Load(assemblyName);
                return new AssemblyLoadResult
                {
                    Succeeded = true,
                    FullName = assembly.GetName().FullName,
                    Location = assembly.Location,
                    LoadedCount = CountLoaded(assembly.GetName().Name)
                };
            }
            catch (Exception e)
            {
                return new AssemblyLoadResult { Error = e.ToString() };
            }
        }

        /// <summary>
        ///     Loads two identities that policy binds to the same file, reporting whether the second request
        ///     reused the assembly the first one loaded.
        /// </summary>
        public AssemblyLoadResult LoadTwice(string firstAssemblyName, string secondAssemblyName)
        {
            try
            {
                Assembly first = Assembly.Load(firstAssemblyName);
                Assembly second = Assembly.Load(secondAssemblyName);

                return new AssemblyLoadResult
                {
                    Succeeded = true,
                    FullName = second.GetName().FullName,
                    Location = second.Location,
                    LoadedCount = CountLoaded(first.GetName().Name),
                    SameInstance = ReferenceEquals(first, second)
                };
            }
            catch (Exception e)
            {
                return new AssemblyLoadResult { Error = e.ToString() };
            }
        }

        private static int CountLoaded(string simpleName) => AppDomain.CurrentDomain.GetAssemblies()
            .Count(a => string.Equals(a.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The outcome of a load attempt, marshaled back to the AppDomain running the test.</summary>
    [Serializable]
    public sealed class AssemblyLoadResult
    {
        public bool Succeeded { get; set; }

        public string FullName { get; set; }

        public string Location { get; set; }

        /// <summary>Number of assemblies with the loaded simple name in the child AppDomain.</summary>
        public int LoadedCount { get; set; }

        /// <summary>Whether two requested identities resolved to the same <see cref="Assembly"/> instance.</summary>
        public bool SameInstance { get; set; }

        public string Error { get; set; }
    }
}

#endif
