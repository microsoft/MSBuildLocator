// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Framework;
using Microsoft.Build.Locator;

namespace BuilderApp
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            Header();
            var projectFilePath = Args(args);

            // Before we can build we need to resolve MSBuild assemblies. We could:
            //   1) Use defaults and call: MSBuildLocator.RegisterDefaults();
            //   2) Do something fancier and ask the user. As an example we'll do that.
            var instances = MSBuildLocator.QueryVisualStudioInstances().ToList();
            var instanceToUse = AskWhichVisualStudioInstanceToUse(instances);

            // Calling RegisterInstance will subscribe to AssemblyResolve event. After this we can now
            // safely call code that use MSBuild types (in the Builder class).
            MSBuildLocator.RegisterInstance(instanceToUse);

            Console.WriteLine($"Using VS Instance: {instanceToUse.Name} - {instanceToUse.Version}");
            Console.WriteLine();

            var result = new Builder().Build(projectFilePath);
            Console.WriteLine();

            Console.ForegroundColor = result ? ConsoleColor.Green : ConsoleColor.Red;
            Console.WriteLine($"Build result: {result}");
            Console.ResetColor();
        }

        private static VisualStudioInstance AskWhichVisualStudioInstanceToUse(List<VisualStudioInstance> instances)
        {
            if (instances.Count == 0)
            {
                Console.WriteLine("MSBuild not found! Exiting.");
                Environment.Exit(-1);
            }

            for (var i = 1; i <= instances.Count; i++)
            {
                var instance = instances[i - 1];
                var recommended = string.Empty;

                // The dev console is probably always the right choice because the user explicitly opened
                // one associated with a Visual Studio install. It will always be first in the list.
                if (instance.DiscoveryType == DiscoveryType.DeveloperConsole)
                    recommended = " (Recommended!)";

                Console.WriteLine($"{i}) {instance.Name} - {instance.Version}{recommended}");
            }

            Console.WriteLine();
            Console.WriteLine("Select an instance of MSBuild: ");
            var answer = Console.ReadLine();
            VisualStudioInstance instanceUsed = null;

            if (int.TryParse(answer, out int instanceChoice) && instanceChoice > 0 && instanceChoice <= instances.Count)
            {
                instanceUsed = instances[instanceChoice - 1];
            }
            else
            {
                Console.WriteLine($"{answer} is not a valid response.");
                Environment.Exit(-1);
            }

            return instanceUsed;
        }

        private static void Header()
        {
            Console.WriteLine($"Sample MSBuild Builder App {ThisAssembly.AssemblyInformationalVersion}.");
            Console.WriteLine();
        }

        private static string Args(string[] args)
        {
            if (args.Length < 1 || !File.Exists(args[0])) Usage();
            var projectFilePath = args[0];
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
        public bool Build(string projectFile)
        {
            var assembly = typeof(Project).Assembly;
            FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(assembly.Location);

            Console.WriteLine();
            Console.WriteLine($"BuildApp running using MSBuild version {fvi.FileVersion}");
            Console.WriteLine(Path.GetDirectoryName(assembly.Location));
            Console.WriteLine();

            var pre = ProjectRootElement.Open(projectFile);
            var project = new Project(pre);
            return project.Build(new Logger());
        }

        private class Logger : ILogger
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
