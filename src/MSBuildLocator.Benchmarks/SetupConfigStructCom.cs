// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//
// Prototype of CsWin32-style struct-based COM interop for the Visual Studio Setup Configuration API,
// used ONLY by the benchmark to measure the before/after of migrating away from the RCW-based
// Microsoft.VisualStudio.Setup.Configuration.Interop wrappers. This is not production code; it is a
// faithful-enough stand-in to quantify the allocation/time delta (no per-package RCW, no built-in
// COM marshalling). IIDs, CLSID, InstanceState values and vtable slots were verified by reflecting
// the interop assembly.

using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Setup.Configuration;

namespace LocatorBenchmarks.StructCom
{
    internal static unsafe class SetupConfigStructCom
    {
        private const string MSBuildComponentId = "Microsoft.Component.MSBuild";
        private const uint CLSCTX_INPROC_SERVER = 1;

        private static readonly Guid CLSID_SetupConfiguration = new Guid("177f0c4a-1cd3-4de7-a32c-71dbbb9fa36d");
        private static readonly Guid IID_ISetupConfiguration2 = new Guid("26aab78c-4a60-49d6-af3b-3c35bc93365d");
        private static readonly Guid IID_ISetupInstance2 = new Guid("89143c9a-05af-49b0-b717-72e218a2185c");

        internal static int Enumerate(bool scanPackages)
        {
            int hr = CoCreateInstance(in CLSID_SetupConfiguration, IntPtr.Zero, CLSCTX_INPROC_SERVER, in IID_ISetupConfiguration2, out IntPtr pConfig);
            if (hr < 0 || pConfig == IntPtr.Zero)
            {
                return 0;
            }

            var config = (ISetupConfiguration2*)pConfig;
            try
            {
                IEnumSetupInstances* pEnum = null;
                if (config->EnumAllInstances(&pEnum) < 0 || pEnum == null)
                {
                    return 0;
                }

                try
                {
                    int count = 0;
                    while (true)
                    {
                        ISetupInstance* pInst = null;
                        uint fetched = 0;
                        if (pEnum->Next(1, &pInst, &fetched) < 0 || fetched == 0 || pInst == null)
                        {
                            break;
                        }

                        try
                        {
                            if (TryReadInstance(pInst, scanPackages))
                            {
                                count++;
                            }
                        }
                        finally
                        {
                            pInst->Release();
                        }
                    }

                    return count;
                }
                finally
                {
                    pEnum->Release();
                }
            }
            finally
            {
                config->Release();
            }
        }

        private static bool TryReadInstance(ISetupInstance* pInst, bool scanPackages)
        {
            Guid iid2 = IID_ISetupInstance2;
            void* p2 = null;
            if (pInst->QueryInterface(&iid2, &p2) < 0 || p2 == null)
            {
                return false;
            }

            var inst2 = (ISetupInstance2*)p2;
            try
            {
                uint stateRaw;
                if (inst2->GetState(&stateRaw) < 0)
                {
                    return false;
                }

                var state = (InstanceState)stateRaw;

                char* verBstr = null;
                if (inst2->GetInstallationVersion(&verBstr) < 0)
                {
                    return false;
                }

                bool versionOk;
                try
                {
                    versionOk = Version.TryParse(BstrToString(verBstr), out _);
                }
                finally
                {
                    SysFreeString(verBstr);
                }

                if (!versionOk)
                {
                    return false;
                }

                if (state != InstanceState.Complete &&
                    !((state & InstanceState.Registered) != 0 && (state & InstanceState.NoRebootRequired) != 0))
                {
                    return false;
                }

                if (scanPackages)
                {
                    IntPtr psa;
                    if (inst2->GetPackages(&psa) < 0 || psa == IntPtr.Zero)
                    {
                        return false;
                    }

                    try
                    {
                        if (!HasMSBuildPackage(psa))
                        {
                            return false;
                        }
                    }
                    finally
                    {
                        SafeArrayDestroy(psa);
                    }
                }

                // Capture the same strings the locator keeps (held only long enough to mirror the work).
                char* nameBstr = null;
                if (inst2->GetDisplayName(0, &nameBstr) < 0)
                {
                    return false;
                }

                try
                {
                    _ = BstrToString(nameBstr);
                }
                finally
                {
                    SysFreeString(nameBstr);
                }

                char* pathBstr = null;
                if (inst2->GetInstallationPath(&pathBstr) < 0)
                {
                    return false;
                }

                try
                {
                    _ = BstrToString(pathBstr);
                }
                finally
                {
                    SysFreeString(pathBstr);
                }

                return true;
            }
            finally
            {
                inst2->Release();
            }
        }

        // Iterates the raw SAFEARRAY of ISetupPackageReference* without wrapping each element in an RCW,
        // and compares each id BSTR in place (no managed string allocation per package).
        private static bool HasMSBuildPackage(IntPtr psa)
        {
            if (SafeArrayGetLBound(psa, 1, out int lb) < 0 || SafeArrayGetUBound(psa, 1, out int ub) < 0)
            {
                return false;
            }

            int count = ub - lb + 1;
            if (count <= 0)
            {
                return false;
            }

            if (SafeArrayAccessData(psa, out void* data) < 0)
            {
                return false;
            }

            try
            {
                var elements = (void**)data;
                for (int i = 0; i < count; i++)
                {
                    var pkg = (ISetupPackageReference*)elements[i];
                    if (pkg == null)
                    {
                        continue;
                    }

                    char* idBstr = null;
                    if (pkg->GetId(&idBstr) < 0 || idBstr == null)
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
                        SysFreeString(idBstr);
                    }
                }

                return false;
            }
            finally
            {
                SafeArrayUnaccessData(psa);
            }
        }

        private static string BstrToString(char* bstr)
            => bstr == null ? null : new string(bstr, 0, (int)SysStringLen(bstr));

        private static bool BstrEqualsOrdinalIgnoreCase(char* bstr, string value)
        {
            uint len = SysStringLen(bstr);
            if (len != (uint)value.Length)
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

        [DllImport("ole32.dll", ExactSpelling = true)]
        private static extern int CoCreateInstance(in Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, in Guid riid, out IntPtr ppv);

        [DllImport("oleaut32.dll", ExactSpelling = true)]
        private static extern void SysFreeString(char* bstr);

        [DllImport("oleaut32.dll", ExactSpelling = true)]
        private static extern uint SysStringLen(char* bstr);

        [DllImport("oleaut32.dll", ExactSpelling = true)]
        private static extern int SafeArrayGetLBound(IntPtr psa, uint nDim, out int plLbound);

        [DllImport("oleaut32.dll", ExactSpelling = true)]
        private static extern int SafeArrayGetUBound(IntPtr psa, uint nDim, out int plUbound);

        [DllImport("oleaut32.dll", ExactSpelling = true)]
        private static extern int SafeArrayAccessData(IntPtr psa, out void* ppvData);

        [DllImport("oleaut32.dll", ExactSpelling = true)]
        private static extern int SafeArrayUnaccessData(IntPtr psa);

        [DllImport("oleaut32.dll", ExactSpelling = true)]
        private static extern int SafeArrayDestroy(IntPtr psa);
    }

    // ---- Manual struct-based COM interface declarations (only the members the benchmark calls). ----
    // Layout: vtable slot 0 = QueryInterface, 1 = AddRef, 2 = Release, then interface methods.

    internal unsafe struct ISetupConfiguration2
    {
        private void** lpVtbl;

        public int EnumAllInstances(IEnumSetupInstances** ppEnumInstances)
        {
            fixed (ISetupConfiguration2* pThis = &this)
            {
                return ((delegate* unmanaged[Stdcall]<ISetupConfiguration2*, IEnumSetupInstances**, int>)lpVtbl[6])(pThis, ppEnumInstances);
            }
        }

        public uint Release()
        {
            fixed (ISetupConfiguration2* pThis = &this)
            {
                return ((delegate* unmanaged[Stdcall]<ISetupConfiguration2*, uint>)lpVtbl[2])(pThis);
            }
        }
    }

    internal unsafe struct IEnumSetupInstances
    {
        private void** lpVtbl;

        public int Next(uint celt, ISetupInstance** rgelt, uint* pceltFetched)
        {
            fixed (IEnumSetupInstances* pThis = &this)
            {
                return ((delegate* unmanaged[Stdcall]<IEnumSetupInstances*, uint, ISetupInstance**, uint*, int>)lpVtbl[3])(pThis, celt, rgelt, pceltFetched);
            }
        }

        public uint Release()
        {
            fixed (IEnumSetupInstances* pThis = &this)
            {
                return ((delegate* unmanaged[Stdcall]<IEnumSetupInstances*, uint>)lpVtbl[2])(pThis);
            }
        }
    }

    internal unsafe struct ISetupInstance
    {
        private void** lpVtbl;

        public int QueryInterface(Guid* riid, void** ppvObject)
        {
            fixed (ISetupInstance* pThis = &this)
            {
                return ((delegate* unmanaged[Stdcall]<ISetupInstance*, Guid*, void**, int>)lpVtbl[0])(pThis, riid, ppvObject);
            }
        }

        public uint Release()
        {
            fixed (ISetupInstance* pThis = &this)
            {
                return ((delegate* unmanaged[Stdcall]<ISetupInstance*, uint>)lpVtbl[2])(pThis);
            }
        }
    }

    internal unsafe struct ISetupInstance2
    {
        private void** lpVtbl;

        public int GetInstallationPath(char** pbstrInstallationPath)
        {
            fixed (ISetupInstance2* pThis = &this)
            {
                return ((delegate* unmanaged[Stdcall]<ISetupInstance2*, char**, int>)lpVtbl[6])(pThis, pbstrInstallationPath);
            }
        }

        public int GetInstallationVersion(char** pbstrInstallationVersion)
        {
            fixed (ISetupInstance2* pThis = &this)
            {
                return ((delegate* unmanaged[Stdcall]<ISetupInstance2*, char**, int>)lpVtbl[7])(pThis, pbstrInstallationVersion);
            }
        }

        public int GetDisplayName(int lcid, char** pbstrDisplayName)
        {
            fixed (ISetupInstance2* pThis = &this)
            {
                return ((delegate* unmanaged[Stdcall]<ISetupInstance2*, int, char**, int>)lpVtbl[8])(pThis, lcid, pbstrDisplayName);
            }
        }

        public int GetState(uint* pState)
        {
            fixed (ISetupInstance2* pThis = &this)
            {
                return ((delegate* unmanaged[Stdcall]<ISetupInstance2*, uint*, int>)lpVtbl[11])(pThis, pState);
            }
        }

        public int GetPackages(IntPtr* ppsaPackages)
        {
            fixed (ISetupInstance2* pThis = &this)
            {
                return ((delegate* unmanaged[Stdcall]<ISetupInstance2*, IntPtr*, int>)lpVtbl[12])(pThis, ppsaPackages);
            }
        }

        public uint Release()
        {
            fixed (ISetupInstance2* pThis = &this)
            {
                return ((delegate* unmanaged[Stdcall]<ISetupInstance2*, uint>)lpVtbl[2])(pThis);
            }
        }
    }

    internal unsafe struct ISetupPackageReference
    {
        private void** lpVtbl;

        public int GetId(char** pbstrId)
        {
            fixed (ISetupPackageReference* pThis = &this)
            {
                return ((delegate* unmanaged[Stdcall]<ISetupPackageReference*, char**, int>)lpVtbl[3])(pThis, pbstrId);
            }
        }
    }
}
