// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Framework;
using Microsoft.Build.Locator;

namespace BuilderApp
{
    internal sealed class Program
    {
        private static void Main(string[] args)
        {
            Header();
            string projectFilePath = Args(args);

            // Before we can build we need to resolve MSBuild assemblies. We could:
            //   1) Use defaults and call: MSBuildLocator.RegisterDefaults();
            //   2) Do something fancier and ask the user. As an example we'll do that.
            var instances = MSBuildLocator.QueryVisualStudioInstances().ToList();
            (VisualStudioInstance VSInstance, string MSBuildPath) = AskWhichMSBuildToUse(instances);

            // Calling Register methods will subscribe to AssemblyResolve event. After this we can
            // safely call code that use MSBuild types (in the Builder class).
            if (VSInstance != null)
            {
                Console.WriteLine($"Using MSBuild from VS Instance: {VSInstance.Name} - {VSInstance.Version}");
                Console.WriteLine();

                MSBuildLocator.RegisterInstance(VSInstance);
            }
            else
            {
                Console.WriteLine($"Using MSBuild from path: {MSBuildPath}");
                Console.WriteLine();

                MSBuildLocator.RegisterMSBuildPath(MSBuildPath);
            }

            bool result = Builder.Build(projectFilePath);
            Console.WriteLine();

            Console.ForegroundColor = result ? ConsoleColor.Green : ConsoleColor.Red;
            Console.WriteLine($"Build result: {result}");
            Console.ResetColor();
        }

        private static (VisualStudioInstance VSInstance, string MSBuildPath) AskWhichMSBuildToUse(List<VisualStudioInstance> instances)
        {
            if (instances.Count == 0)
            {
                Console.WriteLine("No Visual Studio instances found!");
            }

            Console.WriteLine($"0) Custom path");
            for (int i = 1; i <= instances.Count; i++)
            {
                VisualStudioInstance instance = instances[i - 1];
                string recommended = string.Empty;

                // The dev console is probably always the right choice because the user explicitly opened
                // one associated with a Visual Studio install. It will always be first in the list.
                if (instance.DiscoveryType == DiscoveryType.DeveloperConsole)
                    recommended = " (Recommended!)";

                Console.WriteLine($"{i}) {instance.Name} - {instance.Version}{recommended}");
            }

            Console.WriteLine();
            Console.WriteLine("Select an instance of MSBuild: ");
            string answer = Console.ReadLine();

            if (int.TryParse(answer, out int instanceChoice) && instanceChoice >= 0 && instanceChoice <= instances.Count)
            {
                if (instanceChoice == 0)
                {
                    Console.WriteLine("Input path to MSBuild deployment:");
                    string msBuildPath = Console.ReadLine();

                    if (!Directory.Exists(msBuildPath))
                    {
                        Console.WriteLine($"Directory does not exist: {msBuildPath}");
                        Environment.Exit(-1);
                    }

                    return (null, msBuildPath);

                }
                else
                {
                    VisualStudioInstance instanceUsed = instances[instanceChoice - 1];
                    return (instanceUsed, null);
                }
            }
            else
            {
                Console.WriteLine($"{answer} is not a valid response.");
                Environment.Exit(-1);
            }

            throw new Exception("Invalid parsing");
        }

        private static void Header()
        {
            Console.WriteLine($"Sample MSBuild Builder App {ThisAssembly.AssemblyInformationalVersion}.");
            Console.WriteLine();
        }

        private static string Args(string[] args)
        {
            if (args.Length < 1 || !File.Exists(args[0])) Usage();
            string projectFilePath = args[0];
            return projectFilePath;
        }

        private static void Usage()
        {
            Console.WriteLine("BuilderApp.exe <path>");
            Console.WriteLine("    path = path to .*proj file to build");
            Environment.Exit(-1);
        }
    }

    /// <summary>
    /// Class for performing the project build
    /// </summary>
    /// <remarks>
    /// The Microsoft.Build namespaces must be referenced from a method that is called
    /// after RegisterInstance so that it has a chance to change their load behavior.
    /// Here, we put Microsoft.Build calls into a separate class
    /// that is only referenced after calling RegisterInstance.
    /// </remarks>
    public class Builder
    {
        public static bool Build(string projectFile)
        {
            Assembly assembly = typeof(Project).Assembly;
            FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(assembly.Location);

            Console.WriteLine();
            Console.WriteLine($"BuildApp running using MSBuild version {fvi.FileVersion}");
            Console.WriteLine(Path.GetDirectoryName(assembly.Location));
            Console.WriteLine();

            var pre = ProjectRootElement.Open(projectFile);
            var project = new Project(pre);
            return project.Build(new Logger());
        }

        private sealed class Logger : ILogger
        {
            public void Initialize(IEventSource eventSource)
            {
                eventSource.AnyEventRaised += (_, args) => { Console.WriteLine(args.Message); };
            }

            public void Shutdown()
            {
            }

            public LoggerVerbosity Verbosity { get; set; }
            public string Parameters { get; set; }
        }
    }
}
