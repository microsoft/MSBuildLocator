// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if !NETCOREAPP

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace Microsoft.Build.Locator.Tests
{
    /// <summary>
    ///     Emits strong-named assemblies for tests that need an assembly no probing path can find. Emitting
    ///     keeps the fixtures out of the test output directory, which the runtime probes on its own, and avoids
    ///     adding a fixture project or a compiler dependency to the test project.
    /// </summary>
    internal static class FixtureAssembly
    {
        /// <summary>
        ///     Emits <paramref name="name"/> at <paramref name="version"/> into <paramref name="directory"/>,
        ///     signed with the same key as the product assemblies.
        /// </summary>
        /// <returns>The full path of the emitted assembly.</returns>
        public static string Emit(string directory, string name, Version version)
        {
            Directory.CreateDirectory(directory);

            var assemblyName = new AssemblyName(name)
            {
                Version = version,
                KeyPair = new StrongNameKeyPair(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "key.snk")))
            };

            AssemblyBuilder builder = AppDomain.CurrentDomain.DefineDynamicAssembly(
                assemblyName,
                AssemblyBuilderAccess.RunAndSave,
                directory);

            // A single type keeps the emitted assembly a valid, loadable image.
            builder.DefineDynamicModule(name, name + ".dll")
                .DefineType(name + ".Marker", TypeAttributes.Public)
                .CreateType();

            builder.Save(name + ".dll");

            return Path.Combine(directory, name + ".dll");
        }

        /// <summary>Gets the public key token of an emitted assembly, formatted as a config file writes it.</summary>
        public static string GetPublicKeyToken(string assemblyPath) => string.Concat(
            AssemblyName.GetAssemblyName(assemblyPath).GetPublicKeyToken().Select(b => b.ToString("x2")));
    }
}

#endif
