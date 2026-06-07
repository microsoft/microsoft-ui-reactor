using System.Runtime.InteropServices;

namespace Microsoft.UI.Reactor.Hosting;

internal sealed class EmbedHostWatchdog : IDisposable
{
    private const uint SYNCHRONIZE = 0x00100000;
    private const uint INFINITE = 0xFFFFFFFF;
    private const uint WAIT_OBJECT_0 = 0x00000000;

    private nint _handle;
    private int _stopped;

    public void Start(int hostPid, Action onParentDied)
    {
        ArgumentNullException.ThrowIfNull(onParentDied);
        if (hostPid <= 0) throw new ArgumentOutOfRangeException(nameof(hostPid));

        Stop();
        var handle = OpenProcess(SYNCHRONIZE, false, hostPid);
        if (handle == 0)
        {
            Console.Error.WriteLine($"[reactor] embed parent pid {hostPid} not found; watchdog disabled.");
            return;
        }

        _handle = handle;
        Volatile.Write(ref _stopped, 0);
        var context = SynchronizationContext.Current;
        var thread = new Thread(() => Watch(handle, context, onParentDied))
        {
            IsBackground = true,
            Name = "Reactor embed host watchdog",
        };
        thread.Start();
    }

    public void Stop()
    {
        Volatile.Write(ref _stopped, 1);
        var handle = Interlocked.Exchange(ref _handle, 0);
        if (handle != 0) CloseHandle(handle);
    }

    public void Dispose() => Stop();

    private void Watch(nint handle, SynchronizationContext? context, Action onParentDied)
    {
        var wait = WaitForSingleObject(handle, INFINITE);
        if (Volatile.Read(ref _stopped) != 0) return;
        if (wait != WAIT_OBJECT_0) return;

        if (context is not null)
            context.Post(_ => onParentDied(), null);
        else
            onParentDied();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(nint hHandle, uint dwMilliseconds);
}
