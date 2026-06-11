// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// Derived from https://github.com/Microsoft/msbuild/blob/6851538897f5d7b08024a6d8435bc44be5869e53/src/Shared/VisualStudioLocationHelper.cs

#if FEATURE_VISUALSTUDIOSETUP

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Microsoft.Build.Locator
{
    /// <summary>
    ///     Helper class that queries the Visual Studio Setup Configuration API for instances installed
    ///     on the machine. Uses struct-based COM (manual vtable calls) rather than runtime-generated
    ///     RCWs so that enumerating an instance's packages does not allocate a managed wrapper per
    ///     package - on a typical machine that is well over a thousand short-lived objects per query.
    /// </summary>
    internal static unsafe class VisualStudioLocationHelper
    {
        private const string MSBuildComponentId = "Microsoft.Component.MSBuild";
        private const uint CLSCTX_INPROC_SERVER = 1;
        private const int REGDB_E_CLASSNOTREG = unchecked((int) 0x80040154);

        // InstanceState flags (from the Setup Configuration API).
        private const uint InstanceState_Registered = 2;
        private const uint InstanceState_NoRebootRequired = 4;
        private const uint InstanceState_Complete = 0xFFFFFFFF;

        /// <summary>
        ///     Query the Visual Studio setup API to get instances of Visual Studio installed
        ///     on the machine. Will not include anything before Visual Studio "15".
        /// </summary>
        /// <returns>Enumerable list of Visual Studio instances</returns>
        internal static IList<VisualStudioInstance> GetInstances()
        {
            var validInstances = new List<VisualStudioInstance>();

            try
            {
                ISetupConfiguration2* config = GetQuery();
                if (config == null)
                {
                    return validInstances;
                }

                try
                {
                    EnumerateInstances(config, validInstances);
                }
                finally
                {
                    config->Release();
                }
            }
            catch (DllNotFoundException)
            {
                // This is OK, VS "15" or greater likely not installed.
            }
            catch (COMException)
            {
            }

            return validInstances;
        }

        /// <summary>
        ///     Acquire <c>ISetupConfiguration2</c>, falling back to the app-local native helper export
        ///     when the COM class is not registered (mirrors the original RCW-based behaviour).
        /// </summary>
        private static ISetupConfiguration2* GetQuery()
        {
            Guid clsid = SetupConfigurationNativeMethods.CLSID_SetupConfiguration;
            Guid iidConfig2 = ISetupConfiguration2.IID;

            int hr = SetupConfigurationNativeMethods.CoCreateInstance(in clsid, IntPtr.Zero, CLSCTX_INPROC_SERVER, in iidConfig2, out IntPtr ppv);
            if (hr >= 0)
            {
                return (ISetupConfiguration2*) ppv;
            }

            if (hr != REGDB_E_CLASSNOTREG)
            {
                return null;
            }

            // Try to get the class object using an app-local call.
            ISetupConfiguration* config1 = null;
            int result = SetupConfigurationNativeMethods.GetSetupConfiguration(&config1, IntPtr.Zero);
            if (result < 0 || config1 == null)
            {
                return null;
            }

            try
            {
                Guid iid = ISetupConfiguration2.IID;
                void* config2 = null;
                if (config1->QueryInterface(&iid, &config2) >= 0 && config2 != null)
                {
                    return (ISetupConfiguration2*) config2;
                }

                return null;
            }
            finally
            {
                config1->Release();
            }
        }

        private static void EnumerateInstances(ISetupConfiguration2* config, List<VisualStudioInstance> validInstances)
        {
            IEnumSetupInstances* enumInstances = null;
            if (config->EnumAllInstances(&enumInstances) < 0 || enumInstances == null)
            {
                return;
            }

            try
            {
                while (true)
                {
                    ISetupInstance* instance = null;
                    uint fetched = 0;

                    // Call Next to query for the next instance (single item or nothing returned).
                    if (enumInstances->Next(1, &instance, &fetched) < 0 || fetched == 0 || instance == null)
                    {
                        break;
                    }

                    try
                    {
                        if (TryGetInstance(instance, out VisualStudioInstance vsInstance))
                        {
                            validInstances.Add(vsInstance);
                        }
                    }
                    finally
                    {
                        instance->Release();
                    }
                }
            }
            finally
            {
                enumInstances->Release();
            }
        }

        private static bool TryGetInstance(ISetupInstance* instance, out VisualStudioInstance result)
        {
            result = null;

            Guid iidInstance2 = ISetupInstance2.IID;
            void* pInstance2 = null;
            if (instance->QueryInterface(&iidInstance2, &pInstance2) < 0 || pInstance2 == null)
            {
                return false;
            }

            var instance2 = (ISetupInstance2*) pInstance2;
            try
            {
                uint state;
                if (instance2->GetState(&state) < 0)
                {
                    return false;
                }

                // If the install was complete and a valid version, consider it.
                bool stateUsable = state == InstanceState_Complete ||
                    ((state & InstanceState_Registered) != 0 && (state & InstanceState_NoRebootRequired) != 0);
                if (!stateUsable)
                {
                    return false;
                }

                char* versionBstr = null;
                if (instance2->GetInstallationVersion(&versionBstr) < 0)
                {
                    return false;
                }

                Version version;
                try
                {
                    if (!Version.TryParse(BstrToString(versionBstr), out version))
                    {
                        return false;
                    }
                }
                finally
                {
                    SetupConfigurationNativeMethods.SysFreeString(versionBstr);
                }

                if (!InstanceHasMSBuild(instance2))
                {
                    return false;
                }

                char* nameBstr = null;
                if (instance2->GetDisplayName(0, &nameBstr) < 0)
                {
                    return false;
                }

                string name;
                try
                {
                    name = BstrToString(nameBstr);
                }
                finally
                {
                    SetupConfigurationNativeMethods.SysFreeString(nameBstr);
                }

                char* pathBstr = null;
                if (instance2->GetInstallationPath(&pathBstr) < 0)
                {
                    return false;
                }

                string path;
                try
                {
                    path = BstrToString(pathBstr);
                }
                finally
                {
                    SetupConfigurationNativeMethods.SysFreeString(pathBstr);
                }

                result = new VisualStudioInstance(name, path, version, DiscoveryType.VisualStudioSetup);
                return true;
            }
            finally
            {
                instance2->Release();
            }
        }

        private static bool InstanceHasMSBuild(ISetupInstance2* instance)
        {
            IntPtr safeArray;
            if (instance->GetPackages(&safeArray) < 0 || safeArray == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                if (SetupConfigurationNativeMethods.SafeArrayGetLBound(safeArray, 1, out int lowerBound) < 0 ||
                    SetupConfigurationNativeMethods.SafeArrayGetUBound(safeArray, 1, out int upperBound) < 0)
                {
                    return false;
                }

                int count = upperBound - lowerBound + 1;
                if (count <= 0)
                {
                    return false;
                }

                if (SetupConfigurationNativeMethods.SafeArrayAccessData(safeArray, out void* data) < 0)
                {
                    return false;
                }

                try
                {
                    // The SAFEARRAY owns each element interface pointer, so they must not be released here.
                    var packages = (void**) data;
                    for (int i = 0; i < count; i++)
                    {
                        var package = (ISetupPackageReference*) packages[i];
                        if (package == null)
                        {
                            continue;
                        }

                        char* idBstr = null;
                        if (package->GetId(&idBstr) < 0 || idBstr == null)
                        {
                            continue;
                        }

                        try
                        {
                            if (BstrEqualsOrdinalIgnoreCase(idBstr, MSBuildComponentId))
                            {
                                return true;
                            }
                        }
                        finally
                        {
                            SetupConfigurationNativeMethods.SysFreeString(idBstr);
                        }
                    }

                    return false;
                }
                finally
                {
                    SetupConfigurationNativeMethods.SafeArrayUnaccessData(safeArray);
                }
            }
            finally
            {
                SetupConfigurationNativeMethods.SafeArrayDestroy(safeArray);
            }
        }

        private static string BstrToString(char* bstr)
        {
            if (bstr == null)
            {
                return null;
            }

            return new string(bstr, 0, (int) SetupConfigurationNativeMethods.SysStringLen(bstr));
        }

        /// <summary>
        ///     Compares a BSTR against an ASCII identifier without allocating a managed string for the BSTR.
        /// </summary>
        private static bool BstrEqualsOrdinalIgnoreCase(char* bstr, string value)
        {
            uint length = SetupConfigurationNativeMethods.SysStringLen(bstr);
            if (length != (uint) value.Length)
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                char a = bstr[i];
                char b = value[i];
                if (a != b && char.ToUpperInvariant(a) != char.ToUpperInvariant(b))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
#endif
