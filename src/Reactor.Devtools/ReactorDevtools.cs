namespace Microsoft.UI.Reactor.Devtools;

public static class ReactorDevtools
{
    public static void EnsureRegistered() => ModuleInit.Init();
}
