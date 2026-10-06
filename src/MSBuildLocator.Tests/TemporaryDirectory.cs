// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if !NETCOREAPP

using System;
using System.IO;
using System.Threading;

namespace Microsoft.Build.Locator.Tests
{
    /// <summary>
    ///     Runs a test against a directory that exists only for the duration of that test.
    /// </summary>
    internal static class TemporaryDirectory
    {
        public static void Run(string name, Action<string> test)
        {
            string directory = Path.Combine(AppContext.BaseDirectory, name + "_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);

            try
            {
                test(directory);
            }
            finally
            {
                Delete(directory);
            }
        }

        private static void Delete(string directory)
        {
            // An assembly loaded from the directory keeps a file handle open until the AppDomain that loaded it
            // has finished unloading, so a delete that loses that race must not fail a test that already passed.
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    Directory.Delete(directory, recursive: true);
                    return;
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    if (attempt == 4)
                    {
                        return;
                    }

                    Thread.Sleep(100);
                }
            }
        }
    }
}

#endif
