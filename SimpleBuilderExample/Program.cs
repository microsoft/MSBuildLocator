// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Build.Execution;
using Microsoft.Build.Framework;
using System.Collections.Generic;

namespace SimpleBuilderExample
{
    class Program
    {
        static void Main(string[] args)
        {
            Build();
        }

        private static void Build()
        {
            var p = new BuildParameters
            {
                MaxNodeCount = 4,
                Loggers = new ILogger[] { new Microsoft.Build.Logging.ConsoleLogger(LoggerVerbosity.Normal) },
            };

            var req = new BuildRequestData("ProjectToBeBuilt.proj",
                new Dictionary<string, string>(),
                null,
                new[] { "Build" },
                null,
                BuildRequestDataFlags.None);

            var result = BuildManager.DefaultBuildManager.Build(p, req);
        }
    }
}
