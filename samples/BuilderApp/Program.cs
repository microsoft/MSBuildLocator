// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Framework;
using Microsoft.Build.MSBuildLocator;

namespace BuilderApp
{
    internal class Program
    {
        static Program()
        {
            var instanceUsed = MSBuildLocator.RegisterDefaults();

            // For display purposes
            Console.WriteLine($"Using VS Instance: {instanceUsed.Name} - {instanceUsed.Version}");
            foreach (var instance in MSBuildLocator.Instances)
                Console.WriteLine($"  Discovered VS Instance: {instance.Name} - {instance.Version}");
        }

        private static void Main(string[] args)
        {
            if (args.Length < 1 || !File.Exists(args[0])) Usage();
            var projectFilePath = args[0];

            var pre = ProjectRootElement.Open(projectFilePath);
            var project = new Project(pre);
            var result = project.Build(new Logger());
            Console.WriteLine($"Build result: {result}");
        }

        private static void Usage()
        {
            Console.WriteLine("BuilderApp.exe <path>");
            Console.WriteLine("    path = path to .*proj file to build");
            Environment.Exit(-1);
        }

        private class Logger : ILogger
        {
            public void Initialize(IEventSource eventSource)
            {
                eventSource.AnyEventRaised += (sender, args) => { Console.WriteLine(args.Message); };
            }

            public void Shutdown()
            {
            }

            public LoggerVerbosity Verbosity { get; set; }
            public string Parameters { get; set; }
        }
    }
}