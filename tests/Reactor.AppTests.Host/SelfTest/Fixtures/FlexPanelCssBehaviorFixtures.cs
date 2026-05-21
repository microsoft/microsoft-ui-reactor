using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Validates FlexPanel's implementation of CSS Flexbox, organized as:
///
///   - **Section A — `CssStack_*`**: scenarios where CSS-computed values and
///     WinUI <see cref="StackPanel"/> output are identical. Three-way parity:
///     StackPanel actual size, FlexPanel actual size, and the hand-computed
///     CSS expected value (derived from the CSS Flexbox algorithm and
///     verified against a real browser during fixture design). These define
///     the drop-in-replacement safety zone for StackPanel → FlexPanel.
///
///   - **Section B — `CssDiverge_*`**: scenarios where CSS defines behavior
///     StackPanel cannot express (<c>flex-grow</c>, <c>justify-content</c>,
///     <c>flex-wrap</c>, <c>align-self</c>, explicit size honor). Only
///     FlexPanel is compared against the CSS expected value; fixtures
///     inline-document why StackPanel is excluded.
///
/// See <c>docs/research/FlexPanel-vs-StackPanel.md</c> for the full matrix.
///
/// Note on WebView2: an attempt was made to render each scenario's HTML in a
/// real WebView2 and read <c>getBoundingClientRect()</c> back for dynamic
/// CSS-truth comparison. In the selftest harness's hosting configuration,
/// <c>CoreWebView2</c> does not initialize (confirmed in MarkdownHtmlFixtures
/// as well), so the approach falls back to hand-computed CSS values with
/// inline comments citing the relevant CSS rule. If the harness acquires
/// a compositor-ready WebView2 runtime later, <c>WebViewCssMeasurement</c>
/// is kept in-tree as the plug-in point.
/// </summary>
internal static class FlexPanelCssBehaviorFixtures
{
    private const double Tolerance = 1.5;

    private static bool Near(double a, double b, double tol = Tolerance)
        => Math.Abs(a - b) <= tol;

    private static void CheckNear(Harness H, string name, double actual, double expected, double tol = Tolerance)
    {
        var ok = Near(actual, expected, tol);
        if (!ok)
            Console.WriteLine($"# {name}: expected~{expected:F2} actual={actual:F2} delta={actual - expected:F2}");
        H.Check(name, ok);
    }

    // ─────────────────────────────────────────────────────────────────
    //  Shared: mount two sibling panels inside a parent Grid so both are
    //  laid out with the same outer constraints, then measure.
    // ─────────────────────────────────────────────────────────────────

    private static async Task MountSideBySide(Harness H, Panel? stack, FlexPanel flex,
        double columnWidth = 400, double hostHeight = 500)
    {
        var root = new Grid
        {
            Width = columnWidth * (stack is null ? 1 : 2) + (stack is null ? 0 : 16),
            Height = hostHeight,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        if (stack is not null)
        {
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(columnWidth) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        }
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(columnWidth) });

        int col = 0;
        if (stack is not null)
        {
            stack.HorizontalAlignment = HorizontalAlignment.Left;
            stack.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(stack, col);
            root.Children.Add(stack);
            col += 2;
        }
        flex.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(flex, col);
        root.Children.Add(flex);

        H.SetContent(root);
        await Harness.Render();
    }

    private static Border MakeChild(double? width, double? height, string tag, Thickness? margin = null)
    {
        var b = new Border
        {
            Tag = tag,
            Background = new SolidColorBrush(Microsoft.UI.Colors.LightGray),
            Child = new TextBlock { Text = tag },
        };
        if (width is not null) b.Width = width.Value;
        if (height is not null) b.Height = height.Value;
        if (margin is not null) b.Margin = margin.Value;
        return b;
    }

    private static Border? FindByTag(Panel panel, string tag)
    {
        foreach (var child in panel.Children)
            if (child is Border b && (string?)b.Tag == tag) return b;
        return null;
    }

    private static double X(FrameworkElement child, FrameworkElement relativeTo) =>
        child.TransformToVisual(relativeTo).TransformPoint(new global::Windows.Foundation.Point(0, 0)).X;
    private static double Y(FrameworkElement child, FrameworkElement relativeTo) =>
        child.TransformToVisual(relativeTo).TransformPoint(new global::Windows.Foundation.Point(0, 0)).Y;

    // ════════════════════════════════════════════════════════════════════
    //  SECTION A — CSS ↔ StackPanel agreement
    //    Expected CSS values hand-computed from the flexbox algorithm.
    //    Each assertion checks BOTH StackPanel and FlexPanel against the
    //    same CSS-expected number.
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 3 × 120×40 vertical children, no spacing.
    /// CSS: container fit-content = 120 × 120.
    /// </summary>
    internal class CssStack_BasicVerticalNoSpacing(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 };
            stack.Children.Add(MakeChild(120, 40, "A"));
            stack.Children.Add(MakeChild(120, 40, "B"));
            stack.Children.Add(MakeChild(120, 40, "C"));

            var flex = new FlexPanel
            {
                Direction = FlexDirection.Column,
                RowGap = 0,
                AlignItems = FlexAlign.FlexStart,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            flex.Children.Add(MakeChild(120, 40, "A"));
            flex.Children.Add(MakeChild(120, 40, "B"));
            flex.Children.Add(MakeChild(120, 40, "C"));

            await MountSideBySide(H, stack, flex);

            // CSS fit-content column with items 120 wide, 40 tall each:
            //   width = max(item widths) = 120, height = sum = 120
            CheckNear(H, "A_BasicVertNoSpacing_Stack_W", stack.ActualWidth, 120);
            CheckNear(H, "A_BasicVertNoSpacing_Flex_W",  flex.ActualWidth,  120);
            CheckNear(H, "A_BasicVertNoSpacing_Stack_H", stack.ActualHeight, 120);
            CheckNear(H, "A_BasicVertNoSpacing_Flex_H",  flex.ActualHeight,  120);
        }
    }

    /// <summary>
    /// Vertical, 3 × 40, gap=8. CSS: 40 × 3 + 8 × 2 = 136.
    /// </summary>
    internal class CssStack_VerticalWithSpacing(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8 };
            stack.Children.Add(MakeChild(120, 40, "A"));
            stack.Children.Add(MakeChild(120, 40, "B"));
            stack.Children.Add(MakeChild(120, 40, "C"));

            var flex = new FlexPanel
            {
                Direction = FlexDirection.Column, RowGap = 8,
                AlignItems = FlexAlign.FlexStart,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            flex.Children.Add(MakeChild(120, 40, "A"));
            flex.Children.Add(MakeChild(120, 40, "B"));
            flex.Children.Add(MakeChild(120, 40, "C"));

            await MountSideBySide(H, stack, flex);

            CheckNear(H, "A_VertSpacing_Stack_H", stack.ActualHeight, 136);
            CheckNear(H, "A_VertSpacing_Flex_H",  flex.ActualHeight,  136);
        }
    }

    /// <summary>
    /// Horizontal, 3 × 60 widths + 10 px gap = 200.
    /// </summary>
    internal class CssStack_HorizontalWithSpacing(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            stack.Children.Add(MakeChild(60, 40, "A"));
            stack.Children.Add(MakeChild(60, 40, "B"));
            stack.Children.Add(MakeChild(60, 40, "C"));

            var flex = new FlexPanel
            {
                Direction = FlexDirection.Row, ColumnGap = 10,
                AlignItems = FlexAlign.FlexStart,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            flex.Children.Add(MakeChild(60, 40, "A"));
            flex.Children.Add(MakeChild(60, 40, "B"));
            flex.Children.Add(MakeChild(60, 40, "C"));

            await MountSideBySide(H, stack, flex);

            CheckNear(H, "A_HorizSpacing_Stack_W", stack.ActualWidth, 200);
            CheckNear(H, "A_HorizSpacing_Flex_W",  flex.ActualWidth,  200);
        }
    }

    /// <summary>
    /// Margins do NOT collapse: 16 + 40 + 16 + 8 + 16 + 40 + 16 = 152.
    /// (XAML additive; CSS flex items also don't collapse block margins.)
    /// </summary>
    internal class CssStack_VerticalChildMarginsAndSpacing(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8 };
            stack.Children.Add(MakeChild(120, 40, "A", new Thickness(0, 16, 0, 16)));
            stack.Children.Add(MakeChild(120, 40, "B", new Thickness(0, 16, 0, 16)));

            var flex = new FlexPanel
            {
                Direction = FlexDirection.Column, RowGap = 8,
                AlignItems = FlexAlign.FlexStart,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            flex.Children.Add(MakeChild(120, 40, "A", new Thickness(0, 16, 0, 16)));
            flex.Children.Add(MakeChild(120, 40, "B", new Thickness(0, 16, 0, 16)));

            await MountSideBySide(H, stack, flex);

            CheckNear(H, "A_Margins_Stack_H", stack.ActualHeight, 152);
            CheckNear(H, "A_Margins_Flex_H",  flex.ActualHeight,  152);
        }
    }

    /// <summary>
    /// Empty panel reports 0 × 0 (CSS: fit-content on an empty block = 0).
    /// </summary>
    internal class CssStack_EmptyPanel(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8 };
            var flex = new FlexPanel
            {
                Direction = FlexDirection.Column, RowGap = 8,
                HorizontalAlignment = HorizontalAlignment.Left,
            };

            await MountSideBySide(H, stack, flex);

            CheckNear(H, "A_Empty_Stack_H", stack.ActualHeight, 0, tol: 0.5);
            CheckNear(H, "A_Empty_Flex_H",  flex.ActualHeight,  0, tol: 0.5);
        }
    }

    /// <summary>
    /// Single child: no spacing/gap applied. 40 in both.
    /// </summary>
    internal class CssStack_SingleChild(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8 };
            stack.Children.Add(MakeChild(120, 40, "A"));

            var flex = new FlexPanel
            {
                Direction = FlexDirection.Column, RowGap = 8,
                AlignItems = FlexAlign.FlexStart,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            flex.Children.Add(MakeChild(120, 40, "A"));

            await MountSideBySide(H, stack, flex);

            CheckNear(H, "A_Single_Stack_H", stack.ActualHeight, 40);
            CheckNear(H, "A_Single_Flex_H",  flex.ActualHeight,  40);
        }
    }

    /// <summary>
    /// WinUI Visibility=Collapsed ≡ CSS display:none.
    /// Both contribute 0 to the main axis AND drop their gap slot.
    /// 40 + 8 + 40 = 88.
    /// </summary>
    internal class CssStack_CollapsedChildExcluded(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8 };
            stack.Children.Add(MakeChild(120, 40, "A"));
            var sHidden = MakeChild(120, 40, "B");
            sHidden.Visibility = Visibility.Collapsed;
            stack.Children.Add(sHidden);
            stack.Children.Add(MakeChild(120, 40, "C"));

            var flex = new FlexPanel
            {
                Direction = FlexDirection.Column, RowGap = 8,
                AlignItems = FlexAlign.FlexStart,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            flex.Children.Add(MakeChild(120, 40, "A"));
            var fHidden = MakeChild(120, 40, "B");
            fHidden.Visibility = Visibility.Collapsed;
            flex.Children.Add(fHidden);
            flex.Children.Add(MakeChild(120, 40, "C"));

            await MountSideBySide(H, stack, flex);

            CheckNear(H, "A_Collapsed_Stack_H", stack.ActualHeight, 88);
            CheckNear(H, "A_Collapsed_Flex_H",  flex.ActualHeight,  88);
        }
    }

    /// <summary>
    /// Horizontal panel Height=80, children no height: both stretch to 80
    /// (XAML default VerticalAlignment=Stretch == CSS align-items: stretch).
    /// </summary>
    internal class CssStack_HorizontalCrossStretch(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0, Height = 80 };
            stack.Children.Add(MakeChild(60, null, "A"));
            stack.Children.Add(MakeChild(60, null, "B"));

            var flex = new FlexPanel
            {
                Direction = FlexDirection.Row, ColumnGap = 0, Height = 80,
                AlignItems = FlexAlign.Stretch,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            flex.Children.Add(MakeChild(60, null, "A"));
            flex.Children.Add(MakeChild(60, null, "B"));

            await MountSideBySide(H, stack, flex);

            CheckNear(H, "A_XStretch_Stack_ChildH",
                FindByTag(stack, "A")!.ActualHeight, 80);
            CheckNear(H, "A_XStretch_Flex_ChildH",
                FindByTag(flex, "A")!.ActualHeight, 80);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  SECTION B — CSS diverges from StackPanel; FlexPanel follows CSS.
    //  Only FlexPanel is asserted against the CSS expected value.
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Explicit Height < child content. CSS honors the height; StackPanel's
    /// MeasureOverride quirk (see StackPanel.cpp in microsoft-ui-xaml-lift)
    /// returns the unclamped sum instead — so we DON'T reproduce it.
    /// FlexPanel: ActualHeight = 100. Children keep their 40 px.
    /// </summary>
    internal class CssDiverge_ExplicitHeightHonored(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var flex = new FlexPanel
            {
                Direction = FlexDirection.Column, RowGap = 0, Height = 100,
                AlignItems = FlexAlign.FlexStart,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            flex.Children.Add(MakeChild(120, 40, "A"));
            flex.Children.Add(MakeChild(120, 40, "B"));
            flex.Children.Add(MakeChild(120, 40, "C"));
            flex.Children.Add(MakeChild(120, 40, "D"));

            await MountSideBySide(H, stack: null, flex);

            CheckNear(H, "B_ExplicitHeight_Flex_100", flex.ActualHeight, 100);
            CheckNear(H, "B_ExplicitHeight_ChildD_40",
                FindByTag(flex, "D")!.ActualHeight, 40);
        }
    }

    /// <summary>
    /// justify-content: center on a column of fixed Height=300 with 3×40
    /// children → first child at y = (300 - 120) / 2 = 90, last at y=170.
    /// No StackPanel equivalent.
    /// </summary>
    internal class CssDiverge_JustifyContentCenter(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var flex = new FlexPanel
            {
                Direction = FlexDirection.Column, RowGap = 0,
                Width = 200, Height = 300,
                JustifyContent = FlexJustify.Center,
                AlignItems = FlexAlign.FlexStart,
            };
            flex.Children.Add(MakeChild(120, 40, "A"));
            flex.Children.Add(MakeChild(120, 40, "B"));
            flex.Children.Add(MakeChild(120, 40, "C"));

            await MountSideBySide(H, stack: null, flex);

            var flexA = FindByTag(flex, "A")!;
            var flexC = FindByTag(flex, "C")!;
            CheckNear(H, "B_JustifyCenter_FirstY_90",  Y(flexA, flex), 90);
            CheckNear(H, "B_JustifyCenter_LastY_170", Y(flexC, flex), 170);
        }
    }

    /// <summary>
    /// flex-grow: 1 on a middle child. Container width 300, siblings fixed
    /// at 50 each → grow child absorbs the remaining 200.
    /// </summary>
    internal class CssDiverge_FlexGrowDistributesSpace(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var growChild = MakeChild(null, 40, "G");
            FlexPanel.SetGrow(growChild, 1);

            var flex = new FlexPanel
            {
                Direction = FlexDirection.Row, ColumnGap = 0,
                Width = 300, Height = 40,
                AlignItems = FlexAlign.Stretch,
            };
            flex.Children.Add(MakeChild(50, 40, "A"));
            flex.Children.Add(growChild);
            flex.Children.Add(MakeChild(50, 40, "C"));

            await MountSideBySide(H, stack: null, flex);

            CheckNear(H, "B_FlexGrow_GrowWidth_200",
                FindByTag(flex, "G")!.ActualWidth, 200);
        }
    }

    /// <summary>
    /// justify-content: space-between in a 300-wide row with two 50px
    /// children → A at x=0, B at x=250.
    /// </summary>
    internal class CssDiverge_JustifyContentSpaceBetween(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var flex = new FlexPanel
            {
                Direction = FlexDirection.Row, ColumnGap = 0,
                Width = 300, Height = 40,
                JustifyContent = FlexJustify.SpaceBetween,
                AlignItems = FlexAlign.Stretch,
            };
            flex.Children.Add(MakeChild(50, 40, "A"));
            flex.Children.Add(MakeChild(50, 40, "B"));

            await MountSideBySide(H, stack: null, flex);

            var a = FindByTag(flex, "A")!;
            var b = FindByTag(flex, "B")!;
            CheckNear(H, "B_SpaceBetween_AX_0",   X(a, flex), 0);
            CheckNear(H, "B_SpaceBetween_BX_250", X(b, flex), 250);
        }
    }

    /// <summary>
    /// Per-child align-self: center inside a row with align-items:flex-start.
    /// Container 200×80; child B 50×40 with align-self:center → y = 20.
    /// </summary>
    internal class CssDiverge_AlignSelfPerChild(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var selfChild = MakeChild(50, 40, "B");
            FlexPanel.SetAlignSelf(selfChild, FlexAlign.Center);

            var flex = new FlexPanel
            {
                Direction = FlexDirection.Row, ColumnGap = 0,
                Width = 200, Height = 80,
                AlignItems = FlexAlign.FlexStart,
            };
            flex.Children.Add(MakeChild(50, 40, "A"));
            flex.Children.Add(selfChild);

            await MountSideBySide(H, stack: null, flex);

            var b = FindByTag(flex, "B")!;
            CheckNear(H, "B_AlignSelf_BY_20", Y(b, flex), 20);
        }
    }

    /// <summary>
    /// flex-wrap: wrap with 300-wide row and three 120px children: two on
    /// line 1 (240), third wraps to line 2 at y = max(line-1 heights) = 40.
    /// </summary>
    internal class CssDiverge_FlexWrap(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var flex = new FlexPanel
            {
                Direction = FlexDirection.Row, ColumnGap = 0, RowGap = 0,
                Width = 300, Wrap = FlexWrap.Wrap,
                AlignItems = FlexAlign.FlexStart,
                AlignContent = FlexAlign.FlexStart,
            };
            flex.Children.Add(MakeChild(120, 40, "A"));
            flex.Children.Add(MakeChild(120, 40, "B"));
            flex.Children.Add(MakeChild(120, 40, "C"));

            await MountSideBySide(H, stack: null, flex);

            var c = FindByTag(flex, "C")!;
            CheckNear(H, "B_Wrap_CY_40", Y(c, flex), 40);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  SECTION C — CSS Flexbox §4.5 min-sizing semantics (issue #364).
    //  The MeasureFunc bridge clamps Exactly-mode returns to Yoga's slot,
    //  and ApplyAttachedProperties sets node.MinDimensions per CSS spec:
    //    • main axis only (cross axis has no auto-min)
    //    • basis explicitly 0 → 0
    //    • basis explicitly N>0 → min(N, min-content)
    //    • basis auto → min-content (approximated via Measure(0, ∞))
    //    • explicit .Flex(minWidth/minHeight: x) → x (wins, even 0)
    //    • ScrollViewer / ScrollView children → 0 (no virtualized realize)
    // ════════════════════════════════════════════════════════════════════

    private static TextBlock MakeLongTextChild(string tag, string text, double? width = null)
    {
        var tb = new TextBlock
        {
            Tag = tag,
            Text = text,
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.NoWrap,
        };
        if (width is { } w) tb.Width = w;
        return tb;
    }

    private static Border MakeFixedWidthBorder(double w, double h, string tag)
    {
        return new Border
        {
            Tag = tag,
            Background = new SolidColorBrush(Microsoft.UI.Colors.LightCoral),
            Child = new TextBlock { Text = tag },
            Width = w,
            Height = h,
        };
    }

    private static TextBlock? FindTextByTag(Panel panel, string tag)
    {
        foreach (var child in panel.Children)
            if (child is TextBlock tb && (string?)tb.Tag == tag) return tb;
        return null;
    }

    /// <summary>
    /// Two siblings, basis: 0, grow: 1/2. Total 300 wide. Per CSS Flexbox
    /// `flex: 1 1 0%` distributes by grow weight: A = 100, B = 200.
    /// One sibling has content (a wide Border) that exceeds its slot.
    /// With basis:0, automatic min-width = min(0, min-content) = 0, so
    /// Yoga's grow distribution wins — the wide content overflows visually
    /// but does not push the panel.
    /// </summary>
    internal class CssMin_Basis0_GrowDistributesIgnoresContentSize(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var flex = new FlexPanel
            {
                Direction = FlexDirection.Row, ColumnGap = 0,
                Width = 300, Height = 40,
                AlignItems = FlexAlign.Stretch,
            };
            // Pane A: oversized content (500 wide) inside basis:0/grow:1 slot.
            var paneA = new Border
            {
                Tag = "A",
                Background = new SolidColorBrush(Microsoft.UI.Colors.LightBlue),
                Child = new Border { Width = 500, Height = 30,
                    Background = new SolidColorBrush(Microsoft.UI.Colors.SteelBlue) },
            };
            Microsoft.UI.Reactor.Layout.FlexPanel.SetGrow(paneA, 1);
            Microsoft.UI.Reactor.Layout.FlexPanel.SetShrink(paneA, 1);
            Microsoft.UI.Reactor.Layout.FlexPanel.SetBasis(paneA, 0);

            var paneB = MakeChild(null, null, "B");
            Microsoft.UI.Reactor.Layout.FlexPanel.SetGrow(paneB, 2);
            Microsoft.UI.Reactor.Layout.FlexPanel.SetShrink(paneB, 1);
            Microsoft.UI.Reactor.Layout.FlexPanel.SetBasis(paneB, 0);

            flex.Children.Add(paneA);
            flex.Children.Add(paneB);

            await MountSideBySide(H, stack: null, flex);

            var a = FindByTag(flex, "A")!;
            var b = FindByTag(flex, "B")!;
            // CSS: A=100 (grow 1), B=200 (grow 2). Container stays 300.
            CheckNear(H, "C_Basis0_PaneA_100", a.ActualWidth, 100);
            CheckNear(H, "C_Basis0_PaneB_200", b.ActualWidth, 200);
            CheckNear(H, "C_Basis0_Container_300", flex.ActualWidth, 300);
        }
    }

    /// <summary>
    /// Same as above, but with `basis: 0` on the wide-content pane only:
    /// drag-style splitter check. Updates grow weights and asserts a
    /// 50-DIP shift on the leading pane width. This is the regression
    /// witness for the docking splitter bug class from issue #364.
    /// </summary>
    internal class CssMin_SplitterStyleGrowReallocation(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var flex = new FlexPanel
            {
                Direction = FlexDirection.Row, ColumnGap = 0,
                Width = 400, Height = 40,
                AlignItems = FlexAlign.Stretch,
            };
            // Two panes; pane B carries oversized content (TabView would do
            // this in practice; here a fat Border simulates it).
            var paneA = MakeChild(null, null, "A");
            Microsoft.UI.Reactor.Layout.FlexPanel.SetGrow(paneA, 1);
            Microsoft.UI.Reactor.Layout.FlexPanel.SetShrink(paneA, 1);
            Microsoft.UI.Reactor.Layout.FlexPanel.SetBasis(paneA, 0);

            var paneB = new Border
            {
                Tag = "B",
                Background = new SolidColorBrush(Microsoft.UI.Colors.LightYellow),
                Child = new Border { Width = 600, Height = 30,
                    Background = new SolidColorBrush(Microsoft.UI.Colors.Goldenrod) },
            };
            Microsoft.UI.Reactor.Layout.FlexPanel.SetGrow(paneB, 1);
            Microsoft.UI.Reactor.Layout.FlexPanel.SetShrink(paneB, 1);
            Microsoft.UI.Reactor.Layout.FlexPanel.SetBasis(paneB, 0);

            flex.Children.Add(paneA);
            flex.Children.Add(paneB);

            await MountSideBySide(H, stack: null, flex);

            var a = FindByTag(flex, "A")!;
            var b = FindByTag(flex, "B")!;
            // Initial: 1:1 grow → 200/200.
            CheckNear(H, "C_Splitter_Initial_A_200", a.ActualWidth, 200);
            CheckNear(H, "C_Splitter_Initial_B_200", b.ActualWidth, 200);

            // "Drag" 50 DIP right by updating grow weights to 5:3 (A: 250, B: 150).
            Microsoft.UI.Reactor.Layout.FlexPanel.SetGrow(paneA, 5);
            Microsoft.UI.Reactor.Layout.FlexPanel.SetGrow(paneB, 3);
            flex.InvalidateMeasure();
            await Harness.Render();

            CheckNear(H, "C_Splitter_After_A_250", a.ActualWidth, 250, tol: 2);
            CheckNear(H, "C_Splitter_After_B_150", b.ActualWidth, 150, tol: 2);
            CheckNear(H, "C_Splitter_After_Container_400", flex.ActualWidth, 400);
        }
    }

    /// <summary>
    /// `basis: auto` (no explicit basis), shrink: 1, oversized child.
    /// Container 100; child Border with explicit Width=200. CSS Flexbox §4.5
    /// auto-min-content kicks in: item shrinks down to its content min
    /// (= 200 for the fixed-width Border, since Border doesn't shrink below
    /// its Width). Item ends at 200 (overflows the panel).
    /// </summary>
    internal class CssMin_BasisAuto_FloorIsMinContent(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var flex = new FlexPanel
            {
                Direction = FlexDirection.Row, ColumnGap = 0,
                Width = 100, Height = 40,
                AlignItems = FlexAlign.Stretch,
            };
            var paneA = MakeFixedWidthBorder(200, 30, "A");
            Microsoft.UI.Reactor.Layout.FlexPanel.SetShrink(paneA, 1);
            // No grow, no basis → defaults: grow=0, shrink=1, basis=NaN(auto)

            flex.Children.Add(paneA);

            await MountSideBySide(H, stack: null, flex);

            var a = FindByTag(flex, "A")!;
            // CSS: min-width: auto = min-content = 200 (fixed-width Border).
            // Item refuses to shrink below 200.
            CheckNear(H, "C_BasisAuto_A_MinContent_200", a.ActualWidth, 200, tol: 2);
        }
    }

    /// <summary>
    /// Explicit `.Flex(minWidth: 0)` overrides the auto-min-content rule
    /// even when basis is auto. With container 100 and content of intrinsic
    /// size 200 (Border containing a fixed-width child, but no Border.Width),
    /// item is clamped to slot (100). User opted out of min-content floor.
    /// </summary>
    internal class CssMin_ExplicitMinWidth0_AllowsShrinkBelowContent(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var flex = new FlexPanel
            {
                Direction = FlexDirection.Row, ColumnGap = 0,
                Width = 100, Height = 40,
                AlignItems = FlexAlign.Stretch,
            };
            // Border with no own Width (so WinUI doesn't enforce a hard size),
            // but with a child Border of fixed Width=200 → content intrinsic
            // size is 200, but the outer Border is shrinkable.
            var paneA = new Border
            {
                Tag = "A",
                Background = new SolidColorBrush(Microsoft.UI.Colors.LightCoral),
                Child = new Border { Width = 200, Height = 30,
                    Background = new SolidColorBrush(Microsoft.UI.Colors.DarkRed) },
            };
            Microsoft.UI.Reactor.Layout.FlexPanel.SetShrink(paneA, 1);
            Microsoft.UI.Reactor.Layout.FlexPanel.SetMinWidth(paneA, 0);
            // basis defaults to auto.

            flex.Children.Add(paneA);

            await MountSideBySide(H, stack: null, flex);

            var a = FindByTag(flex, "A")!;
            // With min:0 explicit, item shrinks to slot = 100 (content
            // overflows visually but the layout box is 100).
            CheckNear(H, "C_ExplicitMinWidth0_A_100", a.ActualWidth, 100, tol: 2);
        }
    }

    /// <summary>
    /// Explicit `.Flex(minWidth: 50)` provides a non-zero floor. Container
    /// 30 (smaller than min); item is clamped UP to 50.
    /// </summary>
    internal class CssMin_ExplicitMinWidth_NonZero_FloorHonored(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var flex = new FlexPanel
            {
                Direction = FlexDirection.Row, ColumnGap = 0,
                Width = 30, Height = 40,
                AlignItems = FlexAlign.Stretch,
            };
            var paneA = MakeChild(null, 30, "A");
            Microsoft.UI.Reactor.Layout.FlexPanel.SetGrow(paneA, 1);
            Microsoft.UI.Reactor.Layout.FlexPanel.SetShrink(paneA, 1);
            Microsoft.UI.Reactor.Layout.FlexPanel.SetBasis(paneA, 0);
            Microsoft.UI.Reactor.Layout.FlexPanel.SetMinWidth(paneA, 50);

            flex.Children.Add(paneA);

            await MountSideBySide(H, stack: null, flex);

            var a = FindByTag(flex, "A")!;
            // CSS: BoundAxis clamps the grow-resolved 30-wide slot up to min 50.
            // Item overflows the 30-wide container, but its width is 50.
            CheckNear(H, "C_ExplicitMinWidth50_Honored", a.ActualWidth, 50, tol: 1);
        }
    }

    /// <summary>
    /// Auto-min applies only on the MAIN axis. Row container; cross-axis
    /// (height) defaults to 0. With AlignItems=FlexStart and a fixed-height
    /// inner content of 80, the child can be shrunk on the cross axis below
    /// 80 without min-content interference (item gets its content height = 80).
    /// Asserts cross-axis behavior is untouched by main-axis min logic.
    /// </summary>
    internal class CssMin_AutoMin_MainAxisOnly_RowCrossUnconstrained(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var flex = new FlexPanel
            {
                Direction = FlexDirection.Row, ColumnGap = 0,
                Width = 200, Height = 40,   // container Height < child content Height
                AlignItems = FlexAlign.FlexStart,
            };
            // Child with fixed inner height of 80; container only 40 tall.
            // Without main-axis-only auto-min, this might falsely add a
            // height floor; with the spec-correct rule, cross-axis MinHeight
            // remains Undefined and the child renders at its natural size
            // (Border with fixed inner Height=80 → child reports 80).
            var paneA = new Border
            {
                Tag = "A",
                Background = new SolidColorBrush(Microsoft.UI.Colors.LightGray),
                Child = new Border { Width = 100, Height = 80,
                    Background = new SolidColorBrush(Microsoft.UI.Colors.DarkSlateBlue) },
            };
            Microsoft.UI.Reactor.Layout.FlexPanel.SetBasis(paneA, 0);
            Microsoft.UI.Reactor.Layout.FlexPanel.SetGrow(paneA, 1);

            flex.Children.Add(paneA);

            await MountSideBySide(H, stack: null, flex);

            var a = FindByTag(flex, "A")!;
            // Container is 40 tall. AlignItems=FlexStart → child uses its
            // own desired height (80) for cross-axis, overflowing vertically.
            // The pane's main-axis width is grow-driven (basis 0 → 200).
            CheckNear(H, "C_CrossAxis_PaneA_200", a.ActualWidth, 200, tol: 2);
        }
    }

    /// <summary>
    /// Column container; main axis is height. Auto-min applies to height
    /// (basis 0 → 0). Cross-axis (width) keeps its WinUI/Yoga default
    /// behavior (stretch to container width).
    /// </summary>
    internal class CssMin_AutoMin_MainAxisOnly_ColumnDirection(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var flex = new FlexPanel
            {
                Direction = FlexDirection.Column, RowGap = 0,
                Width = 100, Height = 200,
                AlignItems = FlexAlign.Stretch,
            };
            // Pane A oversized content (height-wise) with basis:0.
            var paneA = new Border
            {
                Tag = "A",
                Background = new SolidColorBrush(Microsoft.UI.Colors.LightBlue),
                Child = new Border { Width = 50, Height = 500,
                    Background = new SolidColorBrush(Microsoft.UI.Colors.SteelBlue) },
            };
            Microsoft.UI.Reactor.Layout.FlexPanel.SetGrow(paneA, 1);
            Microsoft.UI.Reactor.Layout.FlexPanel.SetShrink(paneA, 1);
            Microsoft.UI.Reactor.Layout.FlexPanel.SetBasis(paneA, 0);

            var paneB = MakeChild(null, null, "B");
            Microsoft.UI.Reactor.Layout.FlexPanel.SetGrow(paneB, 1);
            Microsoft.UI.Reactor.Layout.FlexPanel.SetShrink(paneB, 1);
            Microsoft.UI.Reactor.Layout.FlexPanel.SetBasis(paneB, 0);

            flex.Children.Add(paneA);
            flex.Children.Add(paneB);

            await MountSideBySide(H, stack: null, flex);

            var a = FindByTag(flex, "A")!;
            var b = FindByTag(flex, "B")!;
            // Column main axis = height. basis:0/grow:1 each → 100/100.
            CheckNear(H, "C_Column_PaneA_H_100", a.ActualHeight, 100, tol: 2);
            CheckNear(H, "C_Column_PaneB_H_100", b.ActualHeight, 100, tol: 2);
        }
    }

    /// <summary>
    /// Nested FlexPanel inside another FlexPanel. Both basis:0/grow:1.
    /// Verifies the auto-min-content discovery composes — outer uses inner
    /// FlexPanel's Measure(0, ∞) result as min-content, but with basis:0
    /// short-circuit the pre-measure is skipped entirely (min = 0).
    /// Layout: outer 300 wide row, two inner FlexPanels each 150 wide.
    /// </summary>
    internal class CssMin_NestedFlex_ComposableBasis0(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var outerFlex = new FlexPanel
            {
                Direction = FlexDirection.Row, ColumnGap = 0,
                Width = 300, Height = 60,
                AlignItems = FlexAlign.Stretch,
            };

            var innerA = new FlexPanel
            {
                Tag = "InnerA",
                Direction = FlexDirection.Column,
                Background = new SolidColorBrush(Microsoft.UI.Colors.LightYellow),
            };
            Microsoft.UI.Reactor.Layout.FlexPanel.SetGrow(innerA, 1);
            Microsoft.UI.Reactor.Layout.FlexPanel.SetBasis(innerA, 0);
            // Inner has oversized content.
            innerA.Children.Add(new Border { Width = 500, Height = 20,
                Background = new SolidColorBrush(Microsoft.UI.Colors.DarkGoldenrod) });

            var innerB = new FlexPanel
            {
                Tag = "InnerB",
                Direction = FlexDirection.Column,
                Background = new SolidColorBrush(Microsoft.UI.Colors.LightPink),
            };
            Microsoft.UI.Reactor.Layout.FlexPanel.SetGrow(innerB, 1);
            Microsoft.UI.Reactor.Layout.FlexPanel.SetBasis(innerB, 0);

            outerFlex.Children.Add(innerA);
            outerFlex.Children.Add(innerB);

            await MountSideBySide(H, stack: null, outerFlex);

            // Inner panels grow 1:1 → 150 each.
            CheckNear(H, "C_NestedFlex_InnerA_150", innerA.ActualWidth, 150, tol: 2);
            CheckNear(H, "C_NestedFlex_InnerB_150", innerB.ActualWidth, 150, tol: 2);
            CheckNear(H, "C_NestedFlex_Outer_300",  outerFlex.ActualWidth, 300);
        }
    }

    /// <summary>
    /// ScrollViewer as a flex child should NOT be force-realized by an
    /// auto-min-content pre-measure. The IsScrollLikeContainer special case
    /// short-circuits min-width to 0; the ScrollViewer then occupies its
    /// flex-allocated slot exactly and its internal content scrolls.
    /// </summary>
    internal class CssMin_ScrollViewer_NoPreMeasureRealize(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var flex = new FlexPanel
            {
                Direction = FlexDirection.Row, ColumnGap = 0,
                Width = 200, Height = 100,
                AlignItems = FlexAlign.Stretch,
            };
            var sv = new ScrollViewer
            {
                Tag = "SV",
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollMode = ScrollMode.Enabled,
                Content = new Border { Width = 800, Height = 80,
                    Background = new SolidColorBrush(Microsoft.UI.Colors.LightGreen) },
            };
            // No explicit minWidth, basis auto → would normally trigger
            // min-content pre-measure. The ScrollViewer special-case ensures
            // we don't realize a 800-wide content during a sizing-only pass.
            Microsoft.UI.Reactor.Layout.FlexPanel.SetShrink(sv, 1);
            Microsoft.UI.Reactor.Layout.FlexPanel.SetGrow(sv, 1);

            flex.Children.Add(sv);

            await MountSideBySide(H, stack: null, flex);

            // SV should occupy the slot — no content-driven overflow.
            CheckNear(H, "C_ScrollViewer_Slot_200", sv.ActualWidth, 200, tol: 2);
        }
    }

    /// <summary>
    /// Pool/reuse hygiene: a child rented from a pool with stale MinWidth /
    /// MinHeight attached values must have them cleared. After clearing, a
    /// fresh layout pass with no .Flex(minWidth:...) leaves the floor at the
    /// auto (NaN) default, NOT the stale value.
    /// </summary>
    internal class CssMin_StaleMinWidthCleared(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var flex = new FlexPanel
            {
                Direction = FlexDirection.Row, ColumnGap = 0,
                Width = 100, Height = 40,
                AlignItems = FlexAlign.Stretch,
            };
            var paneA = MakeChild(null, 30, "A");
            // Simulate prior layout that set MinWidth=200.
            Microsoft.UI.Reactor.Layout.FlexPanel.SetMinWidth(paneA, 200);
            // Then a "release to pool" cycle: ClearValue mirrors ElementPool.
            paneA.ClearValue(Microsoft.UI.Reactor.Layout.FlexPanel.FlexMinWidthProperty);

            Microsoft.UI.Reactor.Layout.FlexPanel.SetGrow(paneA, 1);
            Microsoft.UI.Reactor.Layout.FlexPanel.SetShrink(paneA, 1);
            Microsoft.UI.Reactor.Layout.FlexPanel.SetBasis(paneA, 0);

            flex.Children.Add(paneA);

            await MountSideBySide(H, stack: null, flex);

            var a = FindByTag(flex, "A")!;
            // basis:0 + grow:1 → slot 100; auto-min short-circuits to 0;
            // pane is exactly 100 (NOT 200, which would happen if stale
            // MinWidth value leaked).
            CheckNear(H, "C_StaleMinCleared_A_100", a.ActualWidth, 100, tol: 2);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  SECTION D — CSS ↔ WinUI documented divergences
    //
    //  These fixtures pin behavior where CSS Flexbox and WinUI FrameworkElement
    //  fight, so that any future regression yields a loud failure rather than
    //  silent drift. The pattern in every fixture: build a scenario where
    //  CSS would do X, document that WinUI does Y, and assert Y.
    //
    //  The historical motivation: a CssMin test was originally written
    //  assuming CSS semantics (`width:200; flex-shrink:1; min-width:0` allows
    //  shrink below 200), discovered WinUI's hard-constraint behavior, and
    //  had to be rewritten. These fixtures encode that lesson directly.
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// **WinUI divergence**: `FrameworkElement.Width = 200` is a hard size
    /// constraint that does NOT participate in flex-shrink. CSS would allow
    /// the item to shrink to slot (100) when min-width:0 + shrink:1, but
    /// WinUI's MeasureOverride/ArrangeOverride enforces Width=200 regardless
    /// of the slot offered by Yoga.
    ///
    /// Documented in flex-layout.md.dt — users should prefer
    /// `.Flex(basis: 200, shrink: 1)` over `fe.Width = 200` inside FlexPanel.
    /// </summary>
    internal class CssWinUI_ExplicitWidthIsHardConstraint(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var flex = new FlexPanel
            {
                Direction = FlexDirection.Row, ColumnGap = 0,
                Width = 100, Height = 40,
                AlignItems = FlexAlign.Stretch,
            };
            // Hard fe.Width=200 on a single child in a 100-wide row.
            var paneA = MakeFixedWidthBorder(200, 30, "A");
            Microsoft.UI.Reactor.Layout.FlexPanel.SetShrink(paneA, 1);
            Microsoft.UI.Reactor.Layout.FlexPanel.SetMinWidth(paneA, 0);
            flex.Children.Add(paneA);

            await MountSideBySide(H, stack: null, flex);

            var a = FindByTag(flex, "A")!;
            // WinUI keeps fe.Width=200 even though CSS would shrink to 100.
            // Pin the WinUI behavior: ActualWidth == 200.
            CheckNear(H, "D_ExplicitWidth_Hard_200", a.ActualWidth, 200, tol: 2);
        }
    }

    /// <summary>
    /// **WinUI divergence**: `FrameworkElement.Height = N` in a column flex
    /// container with shrink:1 still keeps N, mirroring the Width case on
    /// the column axis.
    /// </summary>
    internal class CssWinUI_ExplicitHeightIsHardConstraint(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var flex = new FlexPanel
            {
                Direction = FlexDirection.Column, RowGap = 0,
                Width = 80, Height = 100,
                AlignItems = FlexAlign.Stretch,
            };
            var paneA = MakeFixedWidthBorder(60, 200, "A");
            Microsoft.UI.Reactor.Layout.FlexPanel.SetShrink(paneA, 1);
            Microsoft.UI.Reactor.Layout.FlexPanel.SetMinHeight(paneA, 0);
            flex.Children.Add(paneA);

            await MountSideBySide(H, stack: null, flex);

            var a = FindByTag(flex, "A")!;
            CheckNear(H, "D_ExplicitHeight_Hard_200", a.ActualHeight, 200, tol: 2);
        }
    }

    /// <summary>
    /// **WinUI divergence**: `FrameworkElement.MinWidth` (inherited DP) and
    /// `FlexPanel.FlexMinWidth` (our attached DP) are TWO different properties.
    /// The inherited one applies during WinUI Measure; the attached one is
    /// what Yoga reads as the flex auto-min floor. Setting one does not
    /// affect the other.
    ///
    /// Scenario: 100-wide row, child with `fe.MinWidth = 80` (WinUI),
    /// `Flex(shrink:1, minWidth: 0)` (Yoga sees floor=0), basis auto, no
    /// explicit Width. The WinUI MinWidth=80 acts as the child's MeasureOverride
    /// minimum → DesiredSize.Width ≥ 80. Yoga gets a basis of ≥80, sees
    /// shrink:1 and Yoga-min=0, would shrink to slot 100, but the WinUI
    /// hard min keeps it at 80 (slot fits, no shrink needed).
    /// </summary>
    internal class CssWinUI_NativeMinWidthSeparateFromFlexMinWidth(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var flex = new FlexPanel
            {
                Direction = FlexDirection.Row, ColumnGap = 0,
                Width = 200, Height = 40,
                AlignItems = FlexAlign.Stretch,
            };
            var paneA = new Border
            {
                Tag = "A",
                MinWidth = 80,                            // WinUI inherited DP
                Background = new SolidColorBrush(Microsoft.UI.Colors.LightCoral),
                Child = new TextBlock { Text = "A" },
            };
            Microsoft.UI.Reactor.Layout.FlexPanel.SetShrink(paneA, 1);
            Microsoft.UI.Reactor.Layout.FlexPanel.SetMinWidth(paneA, 0);  // Flex attached DP
            flex.Children.Add(paneA);

            await MountSideBySide(H, stack: null, flex);

            var a = FindByTag(flex, "A")!;
            // WinUI MinWidth holds; pane is ≥80. With slot 200 and no
            // grow, basis = max(content, MinWidth) ≈ 80, stays at 80.
            H.Check("D_NativeMinW_AtLeast_80", a.ActualWidth >= 78);
            // And without grow, doesn't expand to slot.
            H.Check("D_NativeMinW_NoGrow_LT_200", a.ActualWidth < 198);
        }
    }

    /// <summary>
    /// **WinUI divergence**: `FrameworkElement.MaxWidth = N` caps the
    /// arranged size of an item even when flex-grow would push it larger.
    /// Yoga's basis would resolve to slot/grow share, but WinUI's MaxWidth
    /// short-circuits in ArrangeOverride.
    /// </summary>
    internal class CssWinUI_NativeMaxWidthHonored(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var flex = new FlexPanel
            {
                Direction = FlexDirection.Row, ColumnGap = 0,
                Width = 300, Height = 40,
                AlignItems = FlexAlign.Stretch,
            };
            var paneA = new Border
            {
                Tag = "A",
                MaxWidth = 120,
                Background = new SolidColorBrush(Microsoft.UI.Colors.LightCoral),
                Child = new TextBlock { Text = "A" },
            };
            Microsoft.UI.Reactor.Layout.FlexPanel.SetGrow(paneA, 1);
            Microsoft.UI.Reactor.Layout.FlexPanel.SetBasis(paneA, 0);
            flex.Children.Add(paneA);

            await MountSideBySide(H, stack: null, flex);

            var a = FindByTag(flex, "A")!;
            // grow:1 with basis:0 would normally take all 300; WinUI MaxWidth
            // caps at 120.
            CheckNear(H, "D_NativeMaxW_Capped_120", a.ActualWidth, 120, tol: 2);
        }
    }

    /// <summary>
    /// **WinUI divergence**: A child's `HorizontalAlignment` is normally used
    /// by parent panels to position the child in the slot. Inside FlexPanel
    /// the main-axis position is owned by FlexPanel (JustifyContent on the
    /// container; main-axis Start placement of items along the line). The
    /// child's `HorizontalAlignment.Center` does NOT center the item in its
    /// flex slot.
    ///
    /// On the cross axis: `VerticalAlignment` on children behaves like CSS
    /// `align-self` and DOES influence position — but that's covered by the
    /// CssDiverge_AlignSelfPerChild fixture (FlexPanel respects FlexAlign).
    /// </summary>
    internal class CssWinUI_HorizontalAlignmentIgnoredInsideFlex(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var flex = new FlexPanel
            {
                Direction = FlexDirection.Row, ColumnGap = 0,
                Width = 300, Height = 40,
                AlignItems = FlexAlign.Stretch,
                JustifyContent = FlexJustify.FlexStart,
            };
            var paneA = MakeFixedWidthBorder(60, 30, "A");
            paneA.HorizontalAlignment = HorizontalAlignment.Center; // ignored
            flex.Children.Add(paneA);

            await MountSideBySide(H, stack: null, flex);

            var a = FindByTag(flex, "A")!;
            // FlexStart on container: item starts at x=0 regardless of
            // child's HorizontalAlignment.Center.
            CheckNear(H, "D_HAlignIgnored_X_0", X(a, flex), 0, tol: 2);
            CheckNear(H, "D_HAlignIgnored_W_60", a.ActualWidth, 60, tol: 2);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  SECTION E — Flex algorithm edge cases
    //
    //  Targeted coverage for grow/shrink corner cases, position absolute,
    //  reverse directions, multi-line wrap, gap interactions, mixed
    //  grow/shrink, and nested min-content propagation.
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// `shrink: 0` items keep their basis even when total exceeds the
    /// container (CSS: shrink-factor of 0 makes the item inflexible
    /// on the negative side). The overflow appears at the end of the line.
    /// </summary>
    internal class FlexEdge_ShrinkZero_OverflowNotShrunk(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var flex = new FlexPanel
            {
                Direction = FlexDirection.Row, ColumnGap = 0,
                Width = 100, Height = 40, // container 100
                AlignItems = FlexAlign.Stretch,
            };
            // Two items basis 80 each, shrink:0 → total 160 in 100-wide.
            var a = MakeChild(null, 30, "A");
            Microsoft.UI.Reactor.Layout.FlexPanel.SetBasis(a, 80);
            Microsoft.UI.Reactor.Layout.FlexPanel.SetShrink(a, 0);
            var b = MakeChild(null, 30, "B");
            Microsoft.UI.Reactor.Layout.FlexPanel.SetBasis(b, 80);
            Microsoft.UI.Reactor.Layout.FlexPanel.SetShrink(b, 0);
            flex.Children.Add(a); flex.Children.Add(b);

            await MountSideBySide(H, stack: null, flex);

            CheckNear(H, "E_ShrinkZero_A_80", FindByTag(flex, "A")!.ActualWidth, 80, tol: 2);
            CheckNear(H, "E_ShrinkZero_B_80", FindByTag(flex, "B")!.ActualWidth, 80, tol: 2);
        }
    }

    /// <summary>
    /// Fractional grow shares: with free space 200, items grow:2 and grow:1
    /// split as 133.33 / 66.67 (CSS: free space × grow / total-grow).
    /// </summary>
    internal class FlexEdge_FractionalGrowShares(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var flex = new FlexPanel
            {
                Direction = FlexDirection.Row, ColumnGap = 0,
                Width = 200, Height = 40,
                AlignItems = FlexAlign.Stretch,
            };
            var a = MakeChild(null, 30, "A");
            Microsoft.UI.Reactor.Layout.FlexPanel.SetGrow(a, 2);
            Microsoft.UI.Reactor.Layout.FlexPanel.SetBasis(a, 0);
            var b = MakeChild(null, 30, "B");
            Microsoft.UI.Reactor.Layout.FlexPanel.SetGrow(b, 1);
            Microsoft.UI.Reactor.Layout.FlexPanel.SetBasis(b, 0);
            flex.Children.Add(a); flex.Children.Add(b);

            await MountSideBySide(H, stack: null, flex);

            CheckNear(H, "E_FracGrow_A_133", FindByTag(flex, "A")!.ActualWidth, 133.33, tol: 2);
            CheckNear(H, "E_FracGrow_B_67",  FindByTag(flex, "B")!.ActualWidth,  66.67, tol: 2);
        }
    }

    /// <summary>
    /// Fractional shrink shares: overflow distributed weighted by
    /// `shrink × basis` (CSS spec uses scaled shrink factor). With
    /// equal basis but shrink:2 vs shrink:1, item with shrink:2 gives
    /// up 2× the space.
    /// </summary>
    internal class FlexEdge_FractionalShrinkShares(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var flex = new FlexPanel
            {
                Direction = FlexDirection.Row, ColumnGap = 0,
                Width = 200, Height = 40, // overflow 100
                AlignItems = FlexAlign.Stretch,
            };
            // Each basis 150, total 300, overflow 100. Weighted shrink:
            // A scaled = 2 × 150 = 300, B scaled = 1 × 150 = 150.
            // Total scaled = 450. A gives up 100 × 300/450 ≈ 66.67,
            // B gives up 100 × 150/450 ≈ 33.33.
            // → A = 150 - 66.67 ≈ 83.33, B = 150 - 33.33 ≈ 116.67.
            var a = MakeChild(null, 30, "A");
            Microsoft.UI.Reactor.Layout.FlexPanel.SetBasis(a, 150);
            Microsoft.UI.Reactor.Layout.FlexPanel.SetShrink(a, 2);
            Microsoft.UI.Reactor.Layout.FlexPanel.SetMinWidth(a, 0);
            var b = MakeChild(null, 30, "B");
            Microsoft.UI.Reactor.Layout.FlexPanel.SetBasis(b, 150);
            Microsoft.UI.Reactor.Layout.FlexPanel.SetShrink(b, 1);
            Microsoft.UI.Reactor.Layout.FlexPanel.SetMinWidth(b, 0);
            flex.Children.Add(a); flex.Children.Add(b);

            await MountSideBySide(H, stack: null, flex);

            CheckNear(H, "E_FracShrink_A_83", FindByTag(flex, "A")!.ActualWidth, 83.33, tol: 3);
            CheckNear(H, "E_FracShrink_B_117", FindByTag(flex, "B")!.ActualWidth, 116.67, tol: 3);
        }
    }

    /// <summary>
    /// Default `grow: 0` + `basis: auto` means the item holds its
    /// content-size and does NOT expand to fill free space. (Common bug:
    /// expecting items to auto-fill the panel without setting grow.)
    /// </summary>
    internal class FlexEdge_GrowZero_BasisAuto_HoldsContent(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var flex = new FlexPanel
            {
                Direction = FlexDirection.Row, ColumnGap = 0,
                Width = 300, Height = 40,
                AlignItems = FlexAlign.Stretch,
                JustifyContent = FlexJustify.FlexStart,
            };
            var a = MakeFixedWidthBorder(60, 30, "A"); // intrinsic 60
            // grow default 0, basis default null/auto.
            flex.Children.Add(a);

            await MountSideBySide(H, stack: null, flex);

            // Stays at content size 60, does NOT inflate to 300.
            CheckNear(H, "E_GrowZero_HoldsContent_60", FindByTag(flex, "A")!.ActualWidth, 60, tol: 2);
        }
    }

    /// <summary>
    /// `position: absolute` items are removed from the flex flow — they
    /// don't take space and don't shift siblings. The remaining (relative)
    /// item takes the full row.
    /// </summary>
    internal class FlexEdge_AbsolutePositionRemovedFromFlow(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var flex = new FlexPanel
            {
                Direction = FlexDirection.Row, ColumnGap = 0,
                Width = 200, Height = 40,
                AlignItems = FlexAlign.Stretch,
                JustifyContent = FlexJustify.FlexStart,
            };
            var abs = MakeFixedWidthBorder(50, 30, "ABS");
            Microsoft.UI.Reactor.Layout.FlexPanel.SetPosition(abs, FlexPositionType.Absolute);
            Microsoft.UI.Reactor.Layout.FlexPanel.SetLeft(abs, 10);
            Microsoft.UI.Reactor.Layout.FlexPanel.SetTop(abs, 5);

            var rel = MakeChild(null, 30, "REL");
            Microsoft.UI.Reactor.Layout.FlexPanel.SetGrow(rel, 1);
            Microsoft.UI.Reactor.Layout.FlexPanel.SetBasis(rel, 0);

            flex.Children.Add(abs);
            flex.Children.Add(rel);

            await MountSideBySide(H, stack: null, flex);

            var relCtrl = FindByTag(flex, "REL")!;
            // REL takes full 200; absolute didn't consume any flow space.
            CheckNear(H, "E_AbsPos_REL_Full_200", relCtrl.ActualWidth, 200, tol: 2);
            // REL starts at x=0 — abs didn't push it.
            CheckNear(H, "E_AbsPos_REL_X_0", X(relCtrl, flex), 0, tol: 2);
            // ABS positioned at (10, 5).
            var absCtrl = FindByTag(flex, "ABS")!;
            CheckNear(H, "E_AbsPos_ABS_Left_10", X(absCtrl, flex), 10, tol: 2);
            CheckNear(H, "E_AbsPos_ABS_Top_5",   Y(absCtrl, flex), 5, tol: 2);
        }
    }

    /// <summary>
    /// `RowReverse` direction reverses visual order: item declared first
    /// is laid out at the right edge of the container.
    /// </summary>
    internal class FlexEdge_RowReverse_LastFirst(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var flex = new FlexPanel
            {
                Direction = FlexDirection.RowReverse, ColumnGap = 0,
                Width = 200, Height = 40,
                AlignItems = FlexAlign.Stretch,
                JustifyContent = FlexJustify.FlexStart,
            };
            var a = MakeFixedWidthBorder(60, 30, "A");
            var b = MakeFixedWidthBorder(60, 30, "B");
            flex.Children.Add(a); flex.Children.Add(b);

            await MountSideBySide(H, stack: null, flex);

            // RowReverse: A (first) goes to right edge; B sits left of A.
            // FlexStart in row-reverse = main-start = right.
            // A.X = 200 - 60 = 140, B.X = 200 - 120 = 80.
            CheckNear(H, "E_RowReverse_A_X_140", X(FindByTag(flex, "A")!, flex), 140, tol: 2);
            CheckNear(H, "E_RowReverse_B_X_80",  X(FindByTag(flex, "B")!, flex), 80, tol: 2);
        }
    }

    /// <summary>
    /// Wrap with row-gap: multi-line layout adds RowGap between lines.
    /// Three 80×30 items in 200-wide container → 2 per line, 2 lines.
    /// With RowGap=10, total height ≈ 30 + 10 + 30 = 70.
    /// </summary>
    internal class FlexEdge_WrapMultiLine_RowGap(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var flex = new FlexPanel
            {
                Direction = FlexDirection.Row,
                Wrap = FlexWrap.Wrap,
                ColumnGap = 0, RowGap = 10,
                Width = 200,
                AlignItems = FlexAlign.FlexStart,
                JustifyContent = FlexJustify.FlexStart,
            };
            flex.Children.Add(MakeFixedWidthBorder(80, 30, "A"));
            flex.Children.Add(MakeFixedWidthBorder(80, 30, "B"));
            flex.Children.Add(MakeFixedWidthBorder(80, 30, "C"));

            await MountSideBySide(H, stack: null, flex);

            // Container height = 30 (line1) + 10 (gap) + 30 (line2) = 70.
            CheckNear(H, "E_Wrap_Container_H_70", flex.ActualHeight, 70, tol: 2);
            // C wraps to second line at y = 40.
            CheckNear(H, "E_Wrap_C_Y_40", Y(FindByTag(flex, "C")!, flex), 40, tol: 2);
        }
    }

    /// <summary>
    /// `AlignContent: SpaceBetween` on a multi-line wrap with extra cross-axis
    /// space distributes free space between wrapped lines (CSS:
    /// align-content: space-between). With container height 200, two lines
    /// of height 30 each, free 140 space sits between them.
    /// </summary>
    internal class FlexEdge_AlignContent_SpaceBetween_MultiLine(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var flex = new FlexPanel
            {
                Direction = FlexDirection.Row,
                Wrap = FlexWrap.Wrap,
                ColumnGap = 0, RowGap = 0,
                Width = 200, Height = 200,
                AlignItems = FlexAlign.FlexStart,
                AlignContent = FlexAlign.SpaceBetween,
                JustifyContent = FlexJustify.FlexStart,
            };
            flex.Children.Add(MakeFixedWidthBorder(80, 30, "A"));
            flex.Children.Add(MakeFixedWidthBorder(80, 30, "B"));
            flex.Children.Add(MakeFixedWidthBorder(80, 30, "C"));

            await MountSideBySide(H, stack: null, flex);

            // Line 1 (A,B) sits at y=0. Line 2 (C) sits at y=200-30=170.
            CheckNear(H, "E_AlignContent_A_Y_0",   Y(FindByTag(flex, "A")!, flex), 0,   tol: 2);
            CheckNear(H, "E_AlignContent_C_Y_170", Y(FindByTag(flex, "C")!, flex), 170, tol: 2);
        }
    }

    /// <summary>
    /// Gap consumes space BEFORE grow distribution, not after. Container 200
    /// with two grow:1 items and ColumnGap=20 → each item gets (200-20)/2 = 90.
    /// </summary>
    internal class FlexEdge_GapDoesntCountInBasisDistribution(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var flex = new FlexPanel
            {
                Direction = FlexDirection.Row, ColumnGap = 20,
                Width = 200, Height = 40,
                AlignItems = FlexAlign.Stretch,
            };
            var a = MakeChild(null, 30, "A");
            Microsoft.UI.Reactor.Layout.FlexPanel.SetGrow(a, 1);
            Microsoft.UI.Reactor.Layout.FlexPanel.SetBasis(a, 0);
            var b = MakeChild(null, 30, "B");
            Microsoft.UI.Reactor.Layout.FlexPanel.SetGrow(b, 1);
            Microsoft.UI.Reactor.Layout.FlexPanel.SetBasis(b, 0);
            flex.Children.Add(a); flex.Children.Add(b);

            await MountSideBySide(H, stack: null, flex);

            // (200 - 20) / 2 = 90.
            CheckNear(H, "E_GapVsBasis_A_90", FindByTag(flex, "A")!.ActualWidth, 90, tol: 2);
            CheckNear(H, "E_GapVsBasis_B_90", FindByTag(flex, "B")!.ActualWidth, 90, tol: 2);
            // B starts at 90 + 20 = 110.
            CheckNear(H, "E_GapVsBasis_B_X_110", X(FindByTag(flex, "B")!, flex), 110, tol: 2);
        }
    }

    /// <summary>
    /// Mixed grow + shrink in same container: when total basis < container,
    /// only grow items expand (shrink is dormant); when total basis >
    /// container, only shrink items contract. This fixture verifies the
    /// underflow case.
    /// </summary>
    internal class FlexEdge_MixedGrowShrink_SameContainer(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var flex = new FlexPanel
            {
                Direction = FlexDirection.Row, ColumnGap = 0,
                Width = 200, Height = 40,
                AlignItems = FlexAlign.Stretch,
            };
            // A: basis 50, grow:1, shrink:1.
            // B: basis 50, grow:0, shrink:1.
            // Total basis 100, free 100. Only A grows → A = 50 + 100 = 150.
            var a = MakeChild(null, 30, "A");
            Microsoft.UI.Reactor.Layout.FlexPanel.SetBasis(a, 50);
            Microsoft.UI.Reactor.Layout.FlexPanel.SetGrow(a, 1);
            Microsoft.UI.Reactor.Layout.FlexPanel.SetShrink(a, 1);
            var b = MakeChild(null, 30, "B");
            Microsoft.UI.Reactor.Layout.FlexPanel.SetBasis(b, 50);
            Microsoft.UI.Reactor.Layout.FlexPanel.SetGrow(b, 0);
            Microsoft.UI.Reactor.Layout.FlexPanel.SetShrink(b, 1);
            flex.Children.Add(a); flex.Children.Add(b);

            await MountSideBySide(H, stack: null, flex);

            CheckNear(H, "E_MixedGS_A_150", FindByTag(flex, "A")!.ActualWidth, 150, tol: 2);
            CheckNear(H, "E_MixedGS_B_50",  FindByTag(flex, "B")!.ActualWidth, 50, tol: 2);
        }
    }

    /// <summary>
    /// Nested FlexPanel: outer flex's min-content includes inner items'
    /// min-content. An outer Row with one shrinkable child that is itself
    /// a Row containing two min-content cells with basis:0 grow:1 — the
    /// inner Row has min-content = 0 (cells short-circuit), so outer
    /// honoring inner min-content allows full shrink.
    /// </summary>
    internal class FlexEdge_NestedFlex_InnerMinContentPropagatesToOuter(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var outer = new FlexPanel
            {
                Direction = FlexDirection.Row, ColumnGap = 0,
                Width = 100, Height = 40,
                AlignItems = FlexAlign.Stretch,
            };
            var inner = new FlexPanel
            {
                Direction = FlexDirection.Row, ColumnGap = 0,
                AlignItems = FlexAlign.Stretch,
            };
            inner.Tag = "INNER";
            var c1 = MakeChild(null, 30, "C1");
            Microsoft.UI.Reactor.Layout.FlexPanel.SetGrow(c1, 1);
            Microsoft.UI.Reactor.Layout.FlexPanel.SetBasis(c1, 0);
            var c2 = MakeChild(null, 30, "C2");
            Microsoft.UI.Reactor.Layout.FlexPanel.SetGrow(c2, 1);
            Microsoft.UI.Reactor.Layout.FlexPanel.SetBasis(c2, 0);
            inner.Children.Add(c1); inner.Children.Add(c2);

            Microsoft.UI.Reactor.Layout.FlexPanel.SetShrink(inner, 1);
            // Default basis: auto → inner's min-content (0 because cells
            // short-circuit on basis:0) propagates as outer's auto-min.
            outer.Children.Add(inner);

            await MountSideBySide(H, stack: null, outer);

            // Inner fills the 100-wide slot; cells get 50 each.
            CheckNear(H, "E_NestedMinContent_Inner_100", inner.ActualWidth, 100, tol: 2);
            CheckNear(H, "E_NestedMinContent_C1_50", c1.ActualWidth, 50, tol: 2);
            CheckNear(H, "E_NestedMinContent_C2_50", c2.ActualWidth, 50, tol: 2);
        }
    }
}
