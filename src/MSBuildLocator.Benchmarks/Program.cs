// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using Microsoft.VisualStudio.Setup.Configuration;

namespace LocatorBenchmarks
{
    /// <summary>
    /// Measures the cost of Visual Studio discovery via the RCW-based Setup Configuration COM API,
    /// the path the locator uses on net46/net472 today.
    ///
    /// Three benchmarks bound the optimization opportunity of a CsWin32 struct-based COM rewrite:
    ///   * <see cref="Locator_PublicApi"/>          - the real public path (sanity baseline).
    ///   * <see cref="Enumerate_WithPackageScan"/>  - replicates the locator's logic, including the
    ///                                                GetPackages() scan for "Microsoft.Component.MSBuild".
    ///   * <see cref="Enumerate_NoPackageScan"/>    - same, but skips GetPackages().
    ///
    /// (WithPackageScan - NoPackageScan) is the cost of marshalling the package SAFEARRAY into one RCW
    /// per package plus a BSTR per GetId(). A struct-based rewrite still iterates the array but without
    /// per-element RCWs/marshalling, so that delta is the ceiling on what the migration can reclaim.
    /// </summary>
    public class VisualStudioDiscoveryBenchmarks
    {
        private const string MSBuildComponentId = "Microsoft.Component.MSBuild";

        [Benchmark(Baseline = true)]
        public int Locator_PublicApi()
            => Microsoft.Build.Locator.MSBuildLocator.QueryVisualStudioInstances().Count();

        [Benchmark]
        public int Enumerate_WithPackageScan() => Enumerate(scanPackages: true);

        [Benchmark]
        public int Enumerate_NoPackageScan() => Enumerate(scanPackages: false);

        [Benchmark]
        public int StructCom_WithPackageScan() => global::LocatorBenchmarks.StructCom.SetupConfigStructCom.Enumerate(scanPackages: true);

        [Benchmark]
        public int StructCom_NoPackageScan() => global::LocatorBenchmarks.StructCom.SetupConfigStructCom.Enumerate(scanPackages: false);

        private static int Enumerate(bool scanPackages)
        {
            var query = (ISetupConfiguration2)new SetupConfiguration();
            IEnumSetupInstances e = query.EnumAllInstances();

            int count = 0;
            var instances = new ISetupInstance[1];
            int fetched;
            do
            {
                e.Next(1, instances, out fetched);
                if (fetched <= 0)
                {
                    continue;
                }

                var instance = (ISetupInstance2)instances[0];
                InstanceState state = instance.GetState();

                if (!Version.TryParse(instance.GetInstallationVersion(), out Version _))
                {
                    continue;
                }

                if (state == InstanceState.Complete ||
                    (state.HasFlag(InstanceState.Registered) && state.HasFlag(InstanceState.NoRebootRequired)))
                {
                    bool hasMSBuild = false;
                    if (scanPackages)
                    {
                        foreach (ISetupPackageReference package in instance.GetPackages())
                        {
                            if (string.Equals(package.GetId(), MSBuildComponentId, StringComparison.OrdinalIgnoreCase))
                            {
                                hasMSBuild = true;
                                break;
                            }
                        }
                    }

                    if (!scanPackages || hasMSBuild)
                    {
                        // Touch the same properties the locator captures.
                        _ = instance.GetDisplayName();
                        _ = instance.GetInstallationPath();
                        count++;
                    }
                }
            } while (fetched > 0);

            return count;
        }
    }

    public static class Program
    {
        public static void Main(string[] args)
        {
            if (args.Contains("probe"))
            {
                Probe();
                return;
            }

            if (args.Contains("verify"))
            {
                var b = new VisualStudioDiscoveryBenchmarks();
                Console.WriteLine($"Locator_PublicApi          = {b.Locator_PublicApi()}");
                Console.WriteLine($"Enumerate_WithPackageScan  = {b.Enumerate_WithPackageScan()}");
                Console.WriteLine($"Enumerate_NoPackageScan    = {b.Enumerate_NoPackageScan()}");
                Console.WriteLine($"StructCom_WithPackageScan  = {b.StructCom_WithPackageScan()}");
                Console.WriteLine($"StructCom_NoPackageScan    = {b.StructCom_NoPackageScan()}");
                return;
            }

            // In-process toolchain: run benchmarks in this same net472 process. Avoids the
            // child-project generation + NuGet restore that fails under this repo's
            // RestoreUseStaticGraphEvaluation=true setting on the .NET 10 SDK.
            IConfig config = DefaultConfig.Instance
                .AddJob(Job.Default
                    .WithToolchain(InProcessEmitToolchain.Instance)
                    .WithWarmupCount(5)
                    .WithIterationCount(10))
                .AddDiagnoser(MemoryDiagnoser.Default);

            BenchmarkRunner.Run<VisualStudioDiscoveryBenchmarks>(config);
        }

        // Prints per-instance package counts so the allocation figures can be tied to RCW churn.
        private static void Probe()
        {
            var query = (ISetupConfiguration2)new SetupConfiguration();
            IEnumSetupInstances e = query.EnumAllInstances();
            var instances = new ISetupInstance[1];
            int fetched;
            int total = 0;
            do
            {
                e.Next(1, instances, out fetched);
                if (fetched <= 0)
                {
                    continue;
                }

                var instance = (ISetupInstance2)instances[0];
                ISetupPackageReference[] packages = instance.GetPackages();
                total += packages.Length;
                Console.WriteLine($"{instance.GetInstallationName()}  state={instance.GetState()}  packages={packages.Length}");
            } while (fetched > 0);

            Console.WriteLine($"TOTAL packages (RCWs marshalled per discovery): {total}");
        }
    }
}
