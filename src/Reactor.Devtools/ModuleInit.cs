using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor.Hosting.Devtools;

namespace Microsoft.UI.Reactor.Devtools;

internal static class ModuleInit
{
    [ModuleInitializer]
    internal static void Init()
    {
        ReactorDevtoolsBootstrap.Register(new DevtoolsHost());
    }
}
