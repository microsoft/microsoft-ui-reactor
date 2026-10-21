using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Issue #1396: the window hooks (<c>UseWindow</c>, <c>UseDpi</c>, <c>UseWindowState</c>,
/// <c>UseIsActive</c>, <c>UseWindowSize</c>, <c>UseWindowPosition</c>, <c>UseIsCovered</c>,
/// <c>UseWindowAspectRatio</c>, <c>UseWindowDragMove</c>, <c>UseClosingGuard</c>),
/// <c>UsePersisted(PersistedScope.Window)</c> and the <c>TitleBar</c> handlers resolve the window
/// whose host's reconciler renders the content, not the process-wide
/// <c>ReactorApp.ActiveHostInternal</c>. That field names an unrelated window for virtualized
/// rows (realized outside the host's render pass), for siblings rendered after a window opened
/// mid-render, and for <c>ReactorHostControl</c> content (never scoped). Each scenario first
/// checks that the field really names the wrong window (the oracle guard), so a pass is not
/// something an ambient lookup could produce.
/// </summary>
internal static class WindowHookOwnerFixtures
{
    private const string PersistKey = "window-hook-owner-probe";

    internal sealed class HookRecord
    {
        public int Renders;
        // The mounting render: the one a mid-render open or a virtualized realization affects.
        public ReactorWindow? FirstActiveAtRender;
        public ReactorWindow? FirstWindow;
        public ReactorWindow? ActiveAtRender;
        public ReactorWindow? Window;
        public uint Dpi;
        public WindowState State;
        public bool Active;
        public (double Width, double Height) Size;
        public (double X, double Y) Position;
        public bool Covered;
        public Action? Drag;
        public string? Persisted;
    }

    private sealed record ProbeProps(HookRecord Record, int Revision);

    // Calls every window hook and records what it resolved. Hook order is fixed.
    private sealed class HookProbe : Component<ProbeProps>
    {
        public override Element Render()
        {
            var r = Props.Record;
            r.Renders++;
            r.ActiveAtRender = ReactorApp.ActiveHostInternal?.OwningWindow;
            r.Window = Context.UseWindow();
            if (r.Renders == 1)
            {
                r.FirstActiveAtRender = r.ActiveAtRender;
                r.FirstWindow = r.Window;
            }
            r.Dpi = Context.UseDpi();
            r.State = Context.UseWindowState();
            r.Active = Context.UseIsActive();
            r.Size = Context.UseWindowSize();
            r.Position = Context.UseWindowPosition();
            r.Covered = Context.UseIsCovered();
            Context.UseWindowAspectRatio(2.0);
            r.Drag = Context.UseWindowDragMove();
            Context.UseClosingGuard(static () => true);
            r.Persisted = Context.UsePersisted(PersistKey, "unset", PersistedScope.Window).Value;
            return TextBlock($"probe {r.Renders}").Height(30);
        }
    }

    // Window A's root: nested class and function components render inside A's pass; the second
    // list row only exists after a bump, so the list's layout realizes it after the pass has
    // restored the process-wide host (B).
    private sealed class OwnerRoot : Component
    {
        public HookRecord Nested { get; } = new();
        public HookRecord Row { get; } = new();
        public ReactorWindow? FunctionWindow;
        public Action Bump = null!;

        public override Element Render()
        {
            var (revision, setRevision) = UseState(0);
            Bump = () => setRevision(revision + 1);
            var row = Row;
            IReadOnlyList<string> rows = revision == 0 ? ["row0"] : ["row0", "row1"];
            return VStack(
                TextBlock("window hook owner"),
                Component<HookProbe, ProbeProps>(new(Nested, revision)),
                RenderEachTime(ctx =>
                {
                    FunctionWindow = ctx.UseWindow();
                    return TextBlock("function probe");
                }),
                LazyVStack<string>(rows, x => x, (item, _) => item == "row1"
                    ? Component<HookProbe, ProbeProps>(new(row, 0))
                    : TextBlock(item).Height(30)).Height(120));
        }
    }

    private static async Task<ReactorWindow> Open(string title, Component component, int width, int height)
    {
        var window = ReactorApp.OpenWindow(new WindowSpec { Title = title, Width = width, Height = height }, () => component);
        await window.Host.WaitForIdleAsync();
        await Harness.Render(100);
        return window;
    }

    /// <summary>
    /// A virtualized row in window A, realized while B is the active host, resolves A from
    /// every hook. B carries distinct DPI / state / activation / size / position / persisted
    /// values, so a hook reading B is visible in its value, not just in its identity.
    /// </summary>
    internal sealed class VirtualizedRow(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ReactorApp.UIDispatcher ??= DispatcherQueue.GetForCurrentThread();
            ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;
            var root = new OwnerRoot();
            ReactorWindow? a = null, b = null;
            try
            {
                a = await Open("Hook owner A", root, 320, 220);
                b = await Open("Hook owner B", new ProbeOnlyRoot(), 520, 400);
                b.AppWindow.Move(new global::Windows.Graphics.PointInt32(a.AppWindow.Position.X + 400, a.AppWindow.Position.Y + 200));
                await Harness.Render(100);

                // Distinct ambient values on B. The internal setters change the snapshot the hooks
                // read without raising events, which is all the initial-value oracle needs.
                b.Dpi = a.Dpi + 48;
                b.State = WindowState.Maximized;
                a.State = WindowState.Normal;
                b.IsActive = true;
                a.IsActive = false;
                a.PersistedScope.Set(PersistKey, "A");
                b.PersistedScope.Set(PersistKey, "B");

                H.Check("HookOwner_Nested_ResolvesA", ReferenceEquals(root.Nested.Window, a));
                H.Check("HookOwner_Function_ResolvesA", ReferenceEquals(root.FunctionWindow, a));

                var guardsA = a.ClosingGuardCountForTests;
                var guardsB = b.ClosingGuardCountForTests;
                root.Bump();
                await a.Host.WaitForIdleAsync();
                var realized = await Harness.WaitFor(() => root.Row.Renders > 0, maxPasses: 20, perPassMs: 25);
                H.Check("HookOwner_Row_Realized", realized);
                if (!realized) return;

                var r = root.Row;
                // Oracle guard: the process-wide host named B while the row rendered.
                H.Check("HookOwner_Row_ActiveHostWasB", ReferenceEquals(r.FirstActiveAtRender, b));
                H.Check("HookOwner_Row_UseWindow", ReferenceEquals(r.FirstWindow, a) && ReferenceEquals(r.Window, a));
                H.Check($"HookOwner_Row_UseDpi (a={a.Dpi}, b={b.Dpi}, got={r.Dpi})", r.Dpi == a.Dpi && a.Dpi != b.Dpi);
                H.Check($"HookOwner_Row_UseWindowState (got={r.State})", r.State == WindowState.Normal);
                H.Check("HookOwner_Row_UseIsActive", !r.Active);
                var sizeA = (a.NativeWindow.Bounds.Width, a.NativeWindow.Bounds.Height);
                var sizeB = (b.NativeWindow.Bounds.Width, b.NativeWindow.Bounds.Height);
                H.Check($"HookOwner_Row_UseWindowSize (a={sizeA}, b={sizeB}, got={r.Size})", r.Size == sizeA && sizeA != sizeB);
                H.Check($"HookOwner_Row_UseWindowPosition (a={a.Position}, b={b.Position}, got={r.Position})",
                    r.Position == a.Position && a.Position != b.Position);
                H.Check("HookOwner_Row_UseWindowDragMove", r.Drag?.Target is ReactorWindow dragTarget && ReferenceEquals(dragTarget, a));
                H.Check("HookOwner_Row_UsePersistedWindow", r.Persisted == "A");

                // Effects: the aspect lock, the closing guard and the z-order subscription land on A.
                await Harness.Render(50);
                H.Check("HookOwner_Row_AspectRatioOnA", a.EffectiveAspectRatioForTests == 2.0);
                H.Check("HookOwner_Row_AspectRatioNotOnB", b.EffectiveAspectRatioForTests is null);
                H.Check($"HookOwner_Row_ClosingGuardOnA ({guardsA}->{a.ClosingGuardCountForTests})", a.ClosingGuardCountForTests == guardsA + 1);
                H.Check($"HookOwner_Row_ClosingGuardNotOnB ({guardsB}->{b.ClosingGuardCountForTests})", b.ClosingGuardCountForTests == guardsB);
                b.RaiseZOrderChangedForTests(movedToTop: false, isCovered: true);
                await a.Host.WaitForIdleAsync();
                await Harness.Render(50);
                H.Check("HookOwner_Row_UseIsCovered_IgnoresB", !r.Covered);
                a.RaiseZOrderChangedForTests(movedToTop: false, isCovered: true);
                var covered = await Harness.WaitFor(() => r.Covered, maxPasses: 20, perPassMs: 25);
                H.Check("HookOwner_Row_UseIsCovered_FollowsA", covered);
            }
            finally
            {
                b?.Close();
                a?.Close();
                await Harness.Render(100);
            }
        }
    }

    private sealed class ProbeOnlyRoot : Component
    {
        public override Element Render() => TextBlock("window hook owner B");
    }

    private const string MidRenderWindowKey = "window-hook-owner-mid-render";

    private sealed class MidRenderOpener : Component
    {
        public override Element Render()
        {
            UseOpenWindow(WindowKey.Of(MidRenderWindowKey), new WindowSpec { Title = "Hook owner C", Width = 240, Height = 180 },
                () => new ProbeOnlyRoot());
            return TextBlock("opener");
        }
    }

    private const string SecondMidRenderWindowKey = "window-hook-owner-mid-render-2";

    private sealed class SecondMidRenderOpener : Component
    {
        public override Element Render()
        {
            UseOpenWindow(WindowKey.Of(SecondMidRenderWindowKey), new WindowSpec { Title = "Hook owner D", Width = 240, Height = 180 },
                () => new ProbeOnlyRoot());
            return TextBlock("second opener");
        }
    }

    // Phase 1: after the opener has constructed C's host (repointing the process-wide host to
    // C), the probe and a tall, dark TitleBar mount in the same pass of A. Phase 2: a second
    // opener constructs D's host earlier in the pass that unmounts the TitleBar, so the
    // withdrawal also runs while the process-wide host names another window.
    private sealed class MidRenderRoot : Component
    {
        public HookRecord Sibling { get; } = new();
        public Microsoft.UI.Xaml.Controls.TitleBar? Bar;
        public Action<int>? SetPhase;

        public override Element Render()
        {
            var (phase, setPhase) = UseState(0);
            SetPhase = setPhase;
            return VStack(
                phase >= 1 ? Component<MidRenderOpener>() : TextBlock("closed"),
                phase >= 2 ? Component<SecondMidRenderOpener>() : TextBlock("no second window"),
                phase >= 1 ? Component<HookProbe, ProbeProps>(new(Sibling, 0)) : TextBlock("no probe"),
                phase == 1
                    ? TitleBar("Hook owner mid-render").Tall().PreferredTheme(WindowTitleBarTheme.Dark).Set(b => Bar = b)
                    : TextBlock("no title bar"));
        }
    }

    private static string TestIcoPath =>
        global::System.IO.Path.Join(AppContext.BaseDirectory, "Assets", "SelfTestWindowIcon.ico");

    private static string Describe(ReactorWindow? w, ReactorWindow a, ReactorWindow c)
        => w is null ? "null" : ReferenceEquals(w, a) ? "A" : ReferenceEquals(w, c) ? "C" : "other";

    /// <summary>
    /// A window opened during A's render repoints the process-wide host for the rest of A's
    /// pass. A sibling that mounts after it, and a TitleBar that mounts after it, still bind A:
    /// registration, the inherited icon (A declares one, C does not), the tall caption and the
    /// caption theme all land on A. A TitleBar unmounted after another mid-render open is
    /// withdrawn from A.
    /// </summary>
    internal sealed class MidRenderOpen(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ReactorApp.UIDispatcher ??= DispatcherQueue.GetForCurrentThread();
            ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;
            var root = new MidRenderRoot();
            ReactorWindow? a = null;
            // Empty app root: the convention probe finds no AppIcon.ico, so only A's declared
            // icon can produce an inherited title-bar icon.
            var scratch = global::System.IO.Path.Join(
                global::System.IO.Path.GetTempPath(), "ReactorHookOwner_" + Guid.NewGuid().ToString("N"));
            global::System.IO.Directory.CreateDirectory(scratch);
            Microsoft.UI.Reactor.Core.V1Protocol.TitleBarIconDefault.SetBaseDirectoryForTests(scratch);
            try
            {
                var window = ReactorApp.OpenWindow(
                    new WindowSpec { Title = "Hook owner mid-render", Width = 320, Height = 220, Icon = WindowIcon.FromPath(TestIcoPath) },
                    () => root);
                await window.Host.WaitForIdleAsync();
                await Harness.Render(100);
                a = window;
                a.PersistedScope.Set(PersistKey, "A");
                var guardsA = a.ClosingGuardCountForTests;
                root.SetPhase!(1);
                await a.Host.WaitForIdleAsync();
                await Harness.Render(100);
                var c = ReactorApp.FindWindow(WindowKey.Of(MidRenderWindowKey));
                H.Check("HookOwner_MidRender_OtherWindowOpened", c is not null && !ReferenceEquals(c, a));
                if (c is null) return;
                var r = root.Sibling;
                // Oracle guard: the sibling mounted while the process-wide host named C.
                H.Check($"HookOwner_MidRender_ActiveHostWasC (renders={r.Renders}, first={Describe(r.FirstActiveAtRender, a, c)}, last={Describe(r.ActiveAtRender, a, c)})",
                    ReferenceEquals(r.FirstActiveAtRender, c));
                H.Check("HookOwner_MidRender_UseWindowAtMount", ReferenceEquals(r.FirstWindow, a));
                H.Check("HookOwner_MidRender_UseWindow", ReferenceEquals(r.Window, a));
                H.Check("HookOwner_MidRender_UseWindowDragMove", r.Drag?.Target is ReactorWindow dragTarget && ReferenceEquals(dragTarget, a));
                H.Check("HookOwner_MidRender_UsePersistedWindow", r.Persisted == "A");
                H.Check("HookOwner_MidRender_AspectRatioOnA", a.EffectiveAspectRatioForTests == 2.0 && c.EffectiveAspectRatioForTests is null);
                H.Check("HookOwner_MidRender_ClosingGuardOnA", a.ClosingGuardCountForTests == guardsA + 1 && c.ClosingGuardCountForTests == 0);
                H.Check($"HookOwner_MidRender_TitleBarOnA (a={a.TitleBarIconControlCountForTests}, c={c.TitleBarIconControlCountForTests})",
                    a.TitleBarIconControlCountForTests == 1 && c.TitleBarIconControlCountForTests == 0);
                H.Check("HookOwner_MidRender_TitleBarExtendsA", a.NativeWindow.ExtendsContentIntoTitleBar && !c.NativeWindow.ExtendsContentIntoTitleBar);
                var iconUri = ((root.Bar?.IconSource as Microsoft.UI.Xaml.Controls.ImageIconSource)?.ImageSource
                    as Microsoft.UI.Xaml.Media.Imaging.BitmapImage)?.UriSource;
                H.Check($"HookOwner_MidRender_TitleBarIconFromA (uri={iconUri})",
                    iconUri?.OriginalString.EndsWith("SelfTestWindowIcon.ico", StringComparison.OrdinalIgnoreCase) == true);
                var tallPx = (int)Math.Round(48 * a.DipScale, MidpointRounding.AwayFromZero);
                H.Check($"HookOwner_MidRender_TitleBarTallCaptionOnA (a={a.AppWindow.TitleBar.Height}, want={tallPx})",
                    a.AppWindow.TitleBar.Height == tallPx);
                H.Check($"HookOwner_MidRender_TitleBarThemeOnA (a={a.AppWindow.TitleBar.PreferredTheme}, c={c.AppWindow.TitleBar.PreferredTheme})",
                    a.AppWindow.TitleBar.PreferredTheme == Microsoft.UI.Windowing.TitleBarTheme.Dark
                    && c.AppWindow.TitleBar.PreferredTheme != Microsoft.UI.Windowing.TitleBarTheme.Dark);

                // Phase 2: D opens mid-render, then the TitleBar unmounts later in the same pass.
                root.SetPhase!(2);
                await a.Host.WaitForIdleAsync();
                await Harness.Render(100);
                var d = ReactorApp.FindWindow(WindowKey.Of(SecondMidRenderWindowKey));
                H.Check("HookOwner_MidRender_SecondWindowOpened", d is not null && !ReferenceEquals(d, a));
                H.Check($"HookOwner_MidRender_TitleBarWithdrawnFromA (a={a.TitleBarIconControlCountForTests})",
                    a.TitleBarIconControlCountForTests == 0);
            }
            finally
            {
                Microsoft.UI.Reactor.Core.V1Protocol.TitleBarIconDefault.SetBaseDirectoryForTests(null);
                ReactorApp.FindWindow(WindowKey.Of(SecondMidRenderWindowKey))?.Close();
                ReactorApp.FindWindow(WindowKey.Of(MidRenderWindowKey))?.Close();
                a?.Close();
                await Harness.Render(100);
                try { global::System.IO.Directory.Delete(scratch, recursive: true); }
                catch (Exception ex) when (ex is global::System.IO.IOException or UnauthorizedAccessException)
                {
                    global::System.Diagnostics.Debug.WriteLine($"[Reactor] WindowHookOwner scratch cleanup failed for {scratch}: {ex}");
                }
            }
        }
    }

    /// <summary>
    /// <c>ReactorHostControl</c> content has no owning window, so the hooks return their
    /// "outside a window" values — deliberately no fallback to the process-wide host, which here
    /// names an unrelated window (B).
    /// </summary>
    internal sealed class HostControlHasNoOwner(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ReactorApp.UIDispatcher ??= DispatcherQueue.GetForCurrentThread();
            ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;
            ReactorWindow? b = null;
            // Disposed when the method returns, after the finally has removed it from the tree.
            using var hostControl = new ReactorHostControl();
            try
            {
                b = await Open("Hook owner B", new ProbeOnlyRoot(), 320, 220);
                b.PersistedScope.Set(PersistKey, "B");
                ApplicationPersistedScope.Default.Set(PersistKey, "app");
                b.State = WindowState.Maximized;
                // Distinct from the fallback (true), so reading B would show up.
                b.IsActive = false;
                var guardsB = b.ClosingGuardCountForTests;
                var record = new HookRecord();
                hostControl.Mount(new ProbeHost(record));
                H.SetContent(new Border { Child = hostControl });
                var rendered = await Harness.WaitFor(() => record.Renders > 0, maxPasses: 20, perPassMs: 25);
                H.Check("HookOwner_HostControl_Rendered", rendered);
                if (!rendered) return;
                await Harness.Render(50);

                H.Check("HookOwner_HostControl_ActiveHostWasB", ReferenceEquals(record.ActiveAtRender, b));
                H.Check("HookOwner_HostControl_UseWindowNull", record.Window is null);
                H.Check("HookOwner_HostControl_UseWindowStateFallback", record.State == WindowState.Normal);
                H.Check("HookOwner_HostControl_UseIsActiveFallback", record.Active);
                H.Check("HookOwner_HostControl_UseWindowSizeFallback", record.Size == (0, 0));
                H.Check("HookOwner_HostControl_UsePersistedFallsBackToApplication", record.Persisted == "app");
                H.Check("HookOwner_HostControl_NoAspectRatioOnB", b.EffectiveAspectRatioForTests is null);
                H.Check("HookOwner_HostControl_NoClosingGuardOnB", b.ClosingGuardCountForTests == guardsB);
                // The embed's TitleBar has no window to claim: B is neither handed it nor extended.
                H.Check($"HookOwner_HostControl_TitleBarNotOnB (b={b.TitleBarIconControlCountForTests})",
                    b.TitleBarIconControlCountForTests == 0 && !b.NativeWindow.ExtendsContentIntoTitleBar);
            }
            finally
            {
                ApplicationPersistedScope.Default.Remove(PersistKey);
                H.SetContent(null);
                b?.Close();
                await Harness.Render(100);
            }
        }
    }

    private sealed class ProbeHost(HookRecord record) : Component
    {
        public override Element Render() => VStack(
            Component<HookProbe, ProbeProps>(new(record, 0)),
            TitleBar("Embedded title bar"));
    }

    /// <summary>
    /// One <see cref="RenderContext"/> crossing no-window → window → no-window keeps a stable
    /// hook-slot shape: no <see cref="HookOrderException"/> and no state-type mismatch.
    /// </summary>
    internal sealed class SlotShapeAcrossTransition(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ReactorApp.UIDispatcher ??= DispatcherQueue.GetForCurrentThread();
            ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;
            ReactorWindow? a = null;
            try
            {
                a = await Open("Hook owner slots", new ProbeOnlyRoot(), 320, 220);
                var ctx = new RenderContext();
                int slots = -1;
                Exception? failure = null;
                foreach (var owner in new[] { null, a, null, a })
                {
                    try
                    {
                        ctx.BeginRender(static () => { }, owner);
                        ctx.UseWindow();
                        ctx.UseDpi();
                        ctx.UseWindowState();
                        ctx.UseIsActive();
                        ctx.UseWindowSize();
                        ctx.UseWindowPosition();
                        ctx.UseIsCovered();
                        ctx.UseWindowAspectRatio(1.5);
                        ctx.UseWindowDragMove();
                        ctx.UseClosingGuard(static () => true);
                        if (slots < 0) slots = ctx.DiagnosticHookCount;
                        else if (slots != ctx.DiagnosticHookCount) failure ??= new InvalidOperationException($"slot count {slots} -> {ctx.DiagnosticHookCount}");
                    }
                    // HookOrderException derives from InvalidOperationException; a state-type
                    // mismatch surfaces as an InvalidCastException.
                    catch (InvalidOperationException ex) { failure ??= ex; }
                    catch (InvalidCastException ex) { failure ??= ex; }
                }
                H.Check($"HookOwner_SlotShape_Stable ({failure?.GetType().Name}: {failure?.Message})", failure is null);
            }
            finally
            {
                a?.Close();
                await Harness.Render(100);
            }
        }
    }
}
