namespace Microsoft.UI.Reactor.Hosting.Devtools;

public static class ReactorDevtoolsBootstrap
{
    private static IReactorDevtoolsHost? _host;
    private static int _loadAttempted;

    public static void Register(IReactorDevtoolsHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        Volatile.Write(ref _host, host);
    }

    internal static IReactorDevtoolsHost? Current
    {
        get
        {
            var host = Volatile.Read(ref _host);
            if (host is not null) return host;

            if (Interlocked.CompareExchange(ref _loadAttempted, 1, 0) == 0)
            {
                try { _ = global::System.Reflection.Assembly.Load("Microsoft.UI.Reactor.Devtools"); }
                catch { }
            }

            return Volatile.Read(ref _host);
        }
    }
}
