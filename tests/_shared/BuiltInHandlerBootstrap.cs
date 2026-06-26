// Spec-048 §3.4 test bootstrap.
//
// `Reconciler.RegisterV1BuiltInHandlers()` was removed so the trimmer can drop
// unreferenced WinUI controls in shipping apps. Production code is expected to
// either (a) call a factory (e.g. `TextBlock("hi")`) which auto-registers via
// its closed-generic `Reg<>` cctor latch, (b) call `ControlRegistry.Register<,>`
// explicitly, or (c) opt into the whole catalog with the public
// `ReactorApp.RegisterAllBuiltIns()` (spec-048 §3.4 option A, issue #486).
//
// Test assemblies, however, exercise direct-record-ctor patterns extensively
// (`new TextBlockElement("hi")` — see issue #486). Forcing every test to call
// a factory first would be invasive and would mask genuine "missing handler"
// regressions. Instead, this file registers every built-in handler globally via
// a `[ModuleInitializer]` that simply delegates to the public
// `ReactorApp.RegisterAllBuiltIns()` — so the catalog list lives in exactly one
// place (`src/Reactor/Hosting/ReactorApp.BuiltIns.cs`) and the test bootstrap
// can never drift out of sync with production.
//
// Using a `[ModuleInitializer]` here (rooted in the *test* assembly) is allowed:
// the spec only forbids `[ModuleInitializer]` in the shipping `Reactor.dll`,
// where it would unconditionally root every handler and defeat trimming.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core.V1Protocol;

namespace Reactor.Tests.Bootstrap;

internal static class BuiltInHandlerBootstrap
{
    /// <summary>
    /// Snapshot of every Reactor-assembly built-in element type that
    /// <see cref="ReactorApp.RegisterAllBuiltIns"/> registered, captured here at
    /// module-init time — immediately after the single bulk-registration call and
    /// <i>before</i> any test exercises a factory. The catalog-drift guard
    /// (<c>UnregisteredHandlerAndRegisterAllBuiltInsTests</c>) compares this
    /// against its expected mirror. Capturing the snapshot at this point — rather
    /// than reading the live, process-wide registry at test-run time — is what
    /// makes the guard sound: a built-in dropped from <c>RegisterAllBuiltIns()</c>
    /// can no longer be masked by some unrelated test having lazily registered the
    /// same built-in through its factory, because nothing else has run yet.
    /// </summary>
    internal static IReadOnlyCollection<Type> RegisteredBuiltInElementTypes { get; private set; }
        = Array.Empty<Type>();

    [ModuleInitializer]
    internal static void Initialize()
    {
        ReactorApp.RegisterAllBuiltIns();

        var reactorAssembly = typeof(ReactorApp).Assembly;
        RegisteredBuiltInElementTypes = ControlRegistry.RegisteredElementTypes
            .Concat(ControlRegistry.RegisteredBaseElementTypes)
            .Where(t => t.Assembly == reactorAssembly)
            .ToArray();
    }
}
