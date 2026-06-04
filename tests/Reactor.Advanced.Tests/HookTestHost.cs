using System.Reflection;
using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Advanced.Tests;

internal static class HookTestHost
{
    private static readonly MethodInfo BeginRenderMethod = typeof(RenderContext).GetMethod(
        "BeginRender",
        BindingFlags.Instance | BindingFlags.NonPublic,
        binder: null,
        types: [typeof(Action)],
        modifiers: null)!;

    private static readonly MethodInfo FlushEffectsMethod = typeof(RenderContext).GetMethod(
        "FlushEffects",
        BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly MethodInfo RunCleanupsMethod = typeof(RenderContext).GetMethod(
        "RunCleanups",
        BindingFlags.Instance | BindingFlags.NonPublic)!;

    public static void BeginRender(RenderContext ctx) => BeginRenderMethod.Invoke(ctx, [new Action(() => { })]);

    public static void FlushEffects(RenderContext ctx) => FlushEffectsMethod.Invoke(ctx, null);

    public static void RunCleanups(RenderContext ctx) => RunCleanupsMethod.Invoke(ctx, null);
}
