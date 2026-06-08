using System.Runtime.InteropServices;

namespace Microsoft.UI.Reactor.Hosting;

internal sealed class EmbedHostWatchdog : IDisposable
{
    private const uint SYNCHRONIZE = 0x00100000;
    private const uint INFINITE = 0xFFFFFFFF;
    private const uint WAIT_OBJECT_0 = 0x00000000;

    private nint _processHandle;
    private nint _stopEvent;
    private Thread? _thread;
    private int _stopped;

    public void Start(int hostPid, Action onParentDied)
    {
        ArgumentNullException.ThrowIfNull(onParentDied);
        if (hostPid <= 0) throw new ArgumentOutOfRangeException(nameof(hostPid));

        Stop();
        var processHandle = OpenProcess(SYNCHRONIZE, false, hostPid);
        if (processHandle == 0)
        {
            Console.Error.WriteLine($"[reactor] embed parent pid {hostPid} not found; watchdog disabled.");
            return;
        }

        var stopEvent = CreateEventW(IntPtr.Zero, true, false, null);
        if (stopEvent == 0)
        {
            CloseHandle(processHandle);
            Console.Error.WriteLine($"[reactor] could not create embed watchdog stop event (Win32 error {Marshal.GetLastWin32Error()}); watchdog disabled.");
            return;
        }

        _processHandle = processHandle;
        _stopEvent = stopEvent;
        Volatile.Write(ref _stopped, 0);
        var thread = new Thread(() => Watch(processHandle, stopEvent, onParentDied))
        {
            IsBackground = true,
            Name = "Reactor embed host watchdog",
        };
        _thread = thread;
        thread.Start();
    }

    public void Stop()
    {
        Volatile.Write(ref _stopped, 1);
        var stopEvent = Volatile.Read(ref _stopEvent);
        if (stopEvent != 0)
        {
            SetEvent(stopEvent);
        }

        var thread = _thread;
        if (thread is not null && thread.ManagedThreadId != Environment.CurrentManagedThreadId)
        {
            thread.Join(TimeSpan.FromSeconds(2));
        }

        _thread = null;
        var processHandle = Interlocked.Exchange(ref _processHandle, 0);
        if (processHandle != 0) CloseHandle(processHandle);
        stopEvent = Interlocked.Exchange(ref _stopEvent, 0);
        if (stopEvent != 0) CloseHandle(stopEvent);
    }

    public void Dispose() => Stop();

    private void Watch(nint processHandle, nint stopEvent, Action onParentDied)
    {
        var handles = new[] { processHandle, stopEvent };
        var wait = WaitForMultipleObjects((uint)handles.Length, handles, false, INFINITE);
        if (Volatile.Read(ref _stopped) != 0) return;
        if (wait != WAIT_OBJECT_0) return;

        onParentDied();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForMultipleObjects(uint nCount, nint[] lpHandles, [MarshalAs(UnmanagedType.Bool)] bool bWaitAll, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint CreateEventW(nint lpEventAttributes, [MarshalAs(UnmanagedType.Bool)] bool bManualReset, [MarshalAs(UnmanagedType.Bool)] bool bInitialState, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetEvent(nint hEvent);
}
