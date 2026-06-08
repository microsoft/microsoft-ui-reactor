#nullable enable

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
 
namespace Microsoft.UI.Reactor.VsExtension.Embed
{
    internal sealed class JobObject : IDisposable
    {
        private static int s_aliveCount;
        private IntPtr _handle;
        private bool _disposed;

        internal static int AliveCountForTests => Volatile.Read(ref s_aliveCount);

        internal static void ResetAliveCountForTests()
        {
            Interlocked.Exchange(ref s_aliveCount, 0);
        }
 
        public JobObject()
        {
            _handle = EmbedNativeMethods.CreateJobObjectW(IntPtr.Zero, null);
            if (_handle == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateJobObject failed");
            }

            try
            {
                var info = new EmbedNativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                info.BasicLimitInformation.LimitFlags = EmbedNativeMethods.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
                var len = Marshal.SizeOf(typeof(EmbedNativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                var ptr = Marshal.AllocHGlobal(len);
                try
                {
                    Marshal.StructureToPtr(info, ptr, fDeleteOld: false);
                    if (!EmbedNativeMethods.SetInformationJobObject(_handle, EmbedNativeMethods.JobObjectExtendedLimitInformation, ptr, (uint)len))
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "SetInformationJobObject failed");
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(ptr);
                }

                Interlocked.Increment(ref s_aliveCount);
            }
            catch
            {
                if (_handle != IntPtr.Zero)
                {
                    EmbedNativeMethods.CloseHandle(_handle);
                    _handle = IntPtr.Zero;
                }

                throw;
            }
        }

        public void AssignProcess(Process process)
        {
            if (process == null)
            {
                throw new ArgumentNullException(nameof(process));
            }

            // If Visual Studio itself is inside a job object that disallows nested jobs,
            // older Windows can fail here with ERROR_ACCESS_DENIED. VS 2022 on Win11 is
            // expected to allow the nested-job happy path used by this preview.
            if (!EmbedNativeMethods.AssignProcessToJobObject(_handle, process.Handle))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "AssignProcessToJobObject failed");
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_handle != IntPtr.Zero)
            {
                EmbedNativeMethods.CloseHandle(_handle);
                _handle = IntPtr.Zero;
                Interlocked.Decrement(ref s_aliveCount);
            }
        }
    }
}
