#nullable enable

using System;
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.UI.Reactor.VsExtension.Embed;
using Xunit;

namespace Reactor.VsExtension.Tests
{
    [Collection("JobObjectCounterTests")]
    public sealed class JobObjectTests
    {
        [Fact]
        public void JobObject_KillsAssignedProcessOnDispose()
        {
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c ping -t 127.0.0.1",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                Assert.True(process.Start());

                try
                {
                    using (var job = new JobObject())
                    {
                        try
                        {
                            job.AssignProcess(process);
                        }
                        catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
                        {
                            TryKill(process);
                            return;
                        }
                    }

                    Assert.True(process.WaitForExit(1500), "Disposing the job object should terminate the assigned process.");
                }
                finally
                {
                    TryKill(process);
                }
            }
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(1500);
                }
            }
            catch (InvalidOperationException)
            {
            }
            catch (Win32Exception)
            {
            }
        }
    }
}
