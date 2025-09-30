// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if NETCOREAPP
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Microsoft.Build.Locator
{
    internal partial class NativeMethods
    {
        internal const string HostFxrName = "hostfxr";

        internal enum hostfxr_resolve_sdk2_flags_t
        {
            disallow_prerelease = 0x1,
        };

        private enum hostfxr_resolve_sdk2_result_key_t
        {
            resolved_sdk_dir = 0,
            global_json_path = 1,
        };

        internal static int hostfxr_resolve_sdk2(string exe_dir, string working_dir, hostfxr_resolve_sdk2_flags_t flags, out string resolved_sdk_dir, out string global_json_path)
        {
            Debug.Assert(t_resolve_sdk2_resolved_sdk_dir is null);
            Debug.Assert(t_resolve_sdk2_global_json_path is null);
            try
            {
                unsafe
                {
                    int result = hostfxr_resolve_sdk2(exe_dir, working_dir, flags, &hostfxr_resolve_sdk2_callback);
                    resolved_sdk_dir = t_resolve_sdk2_resolved_sdk_dir;
                    global_json_path = t_resolve_sdk2_global_json_path;
                    return result;
                }
            }
            finally
            {
                t_resolve_sdk2_resolved_sdk_dir = null;
                t_resolve_sdk2_global_json_path = null;
            }
        }

        [LibraryImport(HostFxrName, StringMarshallingCustomType = typeof(AutoStringMarshaller))]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        private static unsafe partial int hostfxr_resolve_sdk2(
            string exe_dir,
            string working_dir,
            hostfxr_resolve_sdk2_flags_t flags,
            delegate* unmanaged[Cdecl]<hostfxr_resolve_sdk2_result_key_t, void*, void> result);

        [ThreadStatic]
        private static string t_resolve_sdk2_resolved_sdk_dir, t_resolve_sdk2_global_json_path;

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static unsafe void hostfxr_resolve_sdk2_callback(hostfxr_resolve_sdk2_result_key_t key, void* value)
        {
            string str = AutoStringMarshaller.ConvertToManaged(value);
            switch (key)
            {
                case hostfxr_resolve_sdk2_result_key_t.resolved_sdk_dir:
                    t_resolve_sdk2_resolved_sdk_dir = str;
                    break;
                case hostfxr_resolve_sdk2_result_key_t.global_json_path:
                    t_resolve_sdk2_global_json_path = str;
                    break;
            }
        }

        internal static int hostfxr_get_available_sdks(string exe_dir, out string[] sdks)
        {
            Debug.Assert(t_get_available_sdks_result is null);
            try
            {
                unsafe
                {
                    int result = hostfxr_get_available_sdks(exe_dir, &hostfxr_get_available_sdks_callback);
                    sdks = t_get_available_sdks_result;
                    return result;
                }
            }
            finally
            {
                t_get_available_sdks_result = null;
            }
        }

        [LibraryImport(HostFxrName, StringMarshallingCustomType = typeof(AutoStringMarshaller))]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        private static unsafe partial int hostfxr_get_available_sdks(string exe_dir, delegate* unmanaged[Cdecl]<int, void**, void> result);

        [ThreadStatic]
        private static string[] t_get_available_sdks_result;

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static unsafe void hostfxr_get_available_sdks_callback(int count, void** sdks)
        {
            string[] result = new string[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = AutoStringMarshaller.ConvertToManaged(sdks[i]);
            }
            t_get_available_sdks_result = result;
        }

        [CustomMarshaller(typeof(string), MarshalMode.Default, typeof(AutoStringMarshaller))]
        internal static unsafe class AutoStringMarshaller
        {
            public static void* ConvertToUnmanaged(string s) => (void*)Marshal.StringToCoTaskMemAuto(s);

            public static void Free(void* ptr) => Marshal.FreeCoTaskMem((nint)ptr);

            public static string ConvertToManaged(void* ptr) => Marshal.PtrToStringAuto((nint)ptr);
        }
    }
}
#endif
