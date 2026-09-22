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
                TryLoadOptionalDevtoolsPackage();
            }

            return Volatile.Read(ref _host);
        }
    }

    /// <summary>
    /// The registered host without triggering the lazy package load, so a test can
    /// capture and restore it around a <see cref="Register"/> call.
    /// </summary>
    internal static IReactorDevtoolsHost? CurrentForTests => Volatile.Read(ref _host);

    /// <summary>
    /// Restores a value captured from <see cref="CurrentForTests"/>, including
    /// <c>null</c>. <see cref="Register"/> cannot do this — it rejects null, since
    /// unregistering is not a thing the product ever wants.
    /// </summary>
    internal static void RestoreForTests(IReactorDevtoolsHost? host) => Volatile.Write(ref _host, host);

    private static void TryLoadOptionalDevtoolsPackage()
    {
        try
        {
            var assembly = global::System.Reflection.Assembly.Load("Microsoft.UI.Reactor.Devtools");
            if (Volatile.Read(ref _host) is not null) return;

            // Assembly.Load does not guarantee module initializers have executed;
            // explicitly running the module constructor makes the optional package self-register.
            global::System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
        }
        catch
        {
        }
    }
}
