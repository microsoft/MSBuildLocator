// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if FEATURE_VISUALSTUDIOSETUP

using System;
using System.Runtime.InteropServices;

namespace Microsoft.Build.Locator
{
    /// <summary>
    ///     Native entry points and SAFEARRAY/BSTR helpers used by <see cref="VisualStudioLocationHelper" />.
    /// </summary>
    internal static unsafe class SetupConfigurationNativeMethods
    {
        internal static readonly Guid CLSID_SetupConfiguration = new Guid("177f0c4a-1cd3-4de7-a32c-71dbbb9fa36d");

        [DllImport("ole32.dll", ExactSpelling = true)]
        internal static extern int CoCreateInstance(in Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, in Guid riid, out IntPtr ppv);

        [DllImport("Microsoft.VisualStudio.Setup.Configuration.Native.dll", ExactSpelling = true, PreserveSig = true)]
        internal static extern int GetSetupConfiguration(ISetupConfiguration** configuration, IntPtr reserved);

        [DllImport("oleaut32.dll", ExactSpelling = true)]
        internal static extern void SysFreeString(char* bstr);

        [DllImport("oleaut32.dll", ExactSpelling = true)]
        internal static extern uint SysStringLen(char* bstr);

        [DllImport("oleaut32.dll", ExactSpelling = true)]
        internal static extern int SafeArrayGetLBound(IntPtr psa, uint nDim, out int plLbound);

        [DllImport("oleaut32.dll", ExactSpelling = true)]
        internal static extern int SafeArrayGetUBound(IntPtr psa, uint nDim, out int plUbound);

        [DllImport("oleaut32.dll", ExactSpelling = true)]
        internal static extern int SafeArrayAccessData(IntPtr psa, out void* ppvData);

        [DllImport("oleaut32.dll", ExactSpelling = true)]
        internal static extern int SafeArrayUnaccessData(IntPtr psa);

        [DllImport("oleaut32.dll", ExactSpelling = true)]
        internal static extern int SafeArrayDestroy(IntPtr psa);
    }

    // Manually-defined, struct-based COM declarations for the Visual Studio Setup Configuration API
    // (matching the CsWin32 struct-based COM pattern). Only the members the locator calls are declared.
    // This avoids the RCW-based Microsoft.VisualStudio.Setup.Configuration.Interop package and the
    // per-package RCW + built-in marshalling it incurs.
    //
    // Every interface derives from IUnknown, so the vtable layout is:
    //   slot 0 = QueryInterface, 1 = AddRef, 2 = Release, then interface methods in IDL order.
    // The IIDs, CLSID and slot indices were verified against the interop assembly's metadata.

    internal unsafe struct ISetupConfiguration
    {
        private void** _lpVtbl;

        public int QueryInterface(Guid* riid, void** ppvObject)
        {
            fixed (ISetupConfiguration* pThis = &this)
            {
                return ((delegate* unmanaged[Stdcall]<ISetupConfiguration*, Guid*, void**, int>)_lpVtbl[0])(pThis, riid, ppvObject);
            }
        }

        public uint Release()
        {
            fixed (ISetupConfiguration* pThis = &this)
            {
                return ((delegate* unmanaged[Stdcall]<ISetupConfiguration*, uint>)_lpVtbl[2])(pThis);
            }
        }
    }

    internal unsafe struct ISetupConfiguration2
    {
        internal static readonly Guid IID = new Guid("26aab78c-4a60-49d6-af3b-3c35bc93365d");

        private void** _lpVtbl;

        // ISetupConfiguration2 : ISetupConfiguration. EnumAllInstances is the only v2 addition,
        // so it sits after the three v1 methods (EnumInstances/GetInstanceForCurrentProcess/GetInstanceForPath).
        public int EnumAllInstances(IEnumSetupInstances** ppEnumInstances)
        {
            fixed (ISetupConfiguration2* pThis = &this)
            {
                return ((delegate* unmanaged[Stdcall]<ISetupConfiguration2*, IEnumSetupInstances**, int>)_lpVtbl[6])(pThis, ppEnumInstances);
            }
        }

        public uint Release()
        {
            fixed (ISetupConfiguration2* pThis = &this)
            {
                return ((delegate* unmanaged[Stdcall]<ISetupConfiguration2*, uint>)_lpVtbl[2])(pThis);
            }
        }
    }

    internal unsafe struct IEnumSetupInstances
    {
        private void** _lpVtbl;

        public int Next(uint celt, ISetupInstance** rgelt, uint* pceltFetched)
        {
            fixed (IEnumSetupInstances* pThis = &this)
            {
                return ((delegate* unmanaged[Stdcall]<IEnumSetupInstances*, uint, ISetupInstance**, uint*, int>)_lpVtbl[3])(pThis, celt, rgelt, pceltFetched);
            }
        }

        public uint Release()
        {
            fixed (IEnumSetupInstances* pThis = &this)
            {
                return ((delegate* unmanaged[Stdcall]<IEnumSetupInstances*, uint>)_lpVtbl[2])(pThis);
            }
        }
    }

    internal unsafe struct ISetupInstance
    {
        private void** _lpVtbl;

        public int QueryInterface(Guid* riid, void** ppvObject)
        {
            fixed (ISetupInstance* pThis = &this)
            {
                return ((delegate* unmanaged[Stdcall]<ISetupInstance*, Guid*, void**, int>)_lpVtbl[0])(pThis, riid, ppvObject);
            }
        }

        public uint Release()
        {
            fixed (ISetupInstance* pThis = &this)
            {
                return ((delegate* unmanaged[Stdcall]<ISetupInstance*, uint>)_lpVtbl[2])(pThis);
            }
        }
    }

    internal unsafe struct ISetupInstance2
    {
        internal static readonly Guid IID = new Guid("89143c9a-05af-49b0-b717-72e218a2185c");

        private void** _lpVtbl;

        // ISetupInstance2 : ISetupInstance, so the eight v1 methods occupy slots 3-10 and the
        // v2 additions begin at slot 11. The inherited methods below are reachable on this pointer.
        public int GetInstallationPath(char** pbstrInstallationPath)
        {
            fixed (ISetupInstance2* pThis = &this)
            {
                return ((delegate* unmanaged[Stdcall]<ISetupInstance2*, char**, int>)_lpVtbl[6])(pThis, pbstrInstallationPath);
            }
        }

        public int GetInstallationVersion(char** pbstrInstallationVersion)
        {
            fixed (ISetupInstance2* pThis = &this)
            {
                return ((delegate* unmanaged[Stdcall]<ISetupInstance2*, char**, int>)_lpVtbl[7])(pThis, pbstrInstallationVersion);
            }
        }

        public int GetDisplayName(int lcid, char** pbstrDisplayName)
        {
            fixed (ISetupInstance2* pThis = &this)
            {
                return ((delegate* unmanaged[Stdcall]<ISetupInstance2*, int, char**, int>)_lpVtbl[8])(pThis, lcid, pbstrDisplayName);
            }
        }

        public int GetState(uint* pState)
        {
            fixed (ISetupInstance2* pThis = &this)
            {
                return ((delegate* unmanaged[Stdcall]<ISetupInstance2*, uint*, int>)_lpVtbl[11])(pThis, pState);
            }
        }

        public int GetPackages(IntPtr* ppsaPackages)
        {
            fixed (ISetupInstance2* pThis = &this)
            {
                return ((delegate* unmanaged[Stdcall]<ISetupInstance2*, IntPtr*, int>)_lpVtbl[12])(pThis, ppsaPackages);
            }
        }

        public uint Release()
        {
            fixed (ISetupInstance2* pThis = &this)
            {
                return ((delegate* unmanaged[Stdcall]<ISetupInstance2*, uint>)_lpVtbl[2])(pThis);
            }
        }
    }

    internal unsafe struct ISetupPackageReference
    {
        private void** _lpVtbl;

        public int GetId(char** pbstrId)
        {
            fixed (ISetupPackageReference* pThis = &this)
            {
                return ((delegate* unmanaged[Stdcall]<ISetupPackageReference*, char**, int>)_lpVtbl[3])(pThis, pbstrId);
            }
        }
    }
}

#endif
