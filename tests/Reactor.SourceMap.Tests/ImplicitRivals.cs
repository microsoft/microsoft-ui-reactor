using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Diagnostics;

namespace Microsoft.UI.Reactor.SourceMap.Tests.ImplicitRivals;

/// <summary>Imported only by <see cref="WrapperImplicitReceiverTests"/>.</summary>
public static class SetHelper
{
    [ReactorSourceTransparent]
    public static Element Set(global::System.Action<FakeDial> configure) => Microsoft.UI.Reactor.Factories.TextBlock("helper");
}
