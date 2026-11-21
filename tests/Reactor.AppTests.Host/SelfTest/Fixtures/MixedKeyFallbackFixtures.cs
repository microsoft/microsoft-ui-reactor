using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal static class MixedKeyFallbackFixtures
{
    private static class First
    {
        internal sealed record Row(string Text) : Element;
    }

    private static class Second
    {
        internal sealed record Row(string Text) : Element;
    }

    private static Element K(string key, string? text = null) => new First.Row(text ?? key) { Key = key };
    private static Element U(string text = "unkeyed") => new First.Row(text);

    internal sealed class Identity(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            Run("Reorder", [K("a"), U(), K("b")], [K("b"), U("changed"), K("a")], [2, 1, 0]);
            Run("Shift", [K("a"), U(), K("b")], [K("b"), K("a"), U("shifted")], [2, 0, -1]);
            Run("Insert", [K("a"), U(), K("b")], [K("b"), K("x"), U(), K("a")], [2, -1, -1, 0]);
            Run("Remove", [K("a"), U(), U("removed"), K("b")], [K("b"), U(), K("a")], [3, 1, 0]);
            Run("Offset", [K("prefix"), K("a"), U(), K("b"), K("suffix")],
                [K("prefix"), K("b"), U(), K("a"), K("suffix")], [0, 3, 2, 1, 4]);
            Run("Suffix", [K("a"), U()], [K("b"), K("a"), U()], [-1, 0, 1]);
            Run("ToKeyed", [K("a"), U(), K("b")], [K("b"), K("new"), K("a")], [2, -1, 0]);
            Run("ToUnkeyed", [K("a"), K("old"), K("b")], [K("b"), U(), K("a")], [2, -1, 0]);
            Run("SameDisplayName", [K("a"), U(), K("b")],
                [K("b"), new Second.Row("different runtime type"), K("a")], [2, -1, 0]);
            Run("ExplicitShadowsFallback", [K("a"), U(), K("__pos_1_Row"), K("b")],
                [K("b"), U(), K("__pos_1_Row"), K("a")], [3, -1, 2, 0]);
            Run("FallbackShadowsExplicit", [K("a"), K("__pos_2_Row"), U(), K("b")],
                [K("b"), K("__pos_2_Row"), U(), K("a")], [3, -1, 2, 0]);
            Run("NewExplicitResemblesFallback", [K("a"), U(), K("b")],
                [K("b"), K("__pos_1_Row"), K("a")], [2, -1, 0]);
            Run("DuplicateOldKey", [K("a"), K("duplicate", "first"), K("duplicate", "last"), K("b")],
                [K("b"), K("duplicate", "updated"), K("a")], [3, 2, 0]);
            Run("FilteredPosition", [K("a"), Empty(), U(), K("b")],
                [K("b"), Empty(), U(), K("a")], [2, 1, 0]);
            var shared = U();
            Run("SharedElement", [K("a"), shared, K("b")], [K("b"), shared, K("a")], [2, 1, 0]);
            RunNested();
            return Task.CompletedTask;
        }

        private void Run(string name, Element[] oldChildren, Element[] newChildren, int[] expected)
        {
            using var reconciler = new Reconciler();
            int mounts = 0;
            reconciler.RegisterType<First.Row, TextBlock>(
                mount: (_, element, _) => { mounts++; return new TextBlock { Text = element.Text }; },
                update: (_, _, element, control, _) => { control.Text = element.Text; return null; });
            reconciler.RegisterType<Second.Row, TextBlock>(
                mount: (_, element, _) => { mounts++; return new TextBlock { Text = element.Text }; },
                update: (_, _, element, control, _) => { control.Text = element.Text; return null; });
            var oldRoot = VStack(oldChildren);
            var newRoot = VStack(newChildren);
            var panel = (StackPanel)reconciler.Mount(oldRoot, NoOp)!;
            var before = panel.Children.ToArray();
            int oldMounts = mounts;
            try
            {
                reconciler.Reconcile(oldRoot, newRoot, panel, NoOp);
                var filtered = newChildren.Where(e => e is not EmptyElement).ToArray();
                H.Check($"MixedFallback_{name}_Count", panel.Children.Count == expected.Length);
                H.Check($"MixedFallback_{name}_Mounts", mounts - oldMounts == expected.Count(i => i < 0));
                for (int i = 0; i < expected.Length; i++)
                {
                    var control = panel.Children[i];
                    H.Check($"MixedFallback_{name}_Identity{i}", expected[i] >= 0
                        ? ReferenceEquals(control, before[expected[i]])
                        : !before.Any(old => ReferenceEquals(old, control)));
                    string text = filtered[i] switch
                    {
                        First.Row row => row.Text,
                        Second.Row row => row.Text,
                        _ => throw new InvalidOperationException("Unexpected test element.")
                    };
                    H.Check($"MixedFallback_{name}_Content{i}", control is TextBlock label && label.Text == text);
                }
            }
            finally
            {
                reconciler.UnmountChild(panel);
            }
        }

        private void RunNested()
        {
            using var reconciler = new Reconciler();
            var innerA = VStack(TextBlock("inner a").WithKey("a"), TextBlock("middle"), TextBlock("inner b").WithKey("b"));
            var innerB = VStack(TextBlock("inner b").WithKey("b"), TextBlock("updated"), TextBlock("inner a").WithKey("a"));
            var oldRoot = VStack(TextBlock("outer a").WithKey("a"), innerA, TextBlock("outer b").WithKey("b"));
            var newRoot = VStack(TextBlock("outer b").WithKey("b"), innerB, TextBlock("outer a").WithKey("a"));
            var panel = (StackPanel)reconciler.Mount(oldRoot, NoOp)!;
            var outer = panel.Children.ToArray();
            var inner = (StackPanel)outer[1];
            var children = inner.Children.ToArray();
            try
            {
                reconciler.Reconcile(oldRoot, newRoot, panel, NoOp);
                H.Check("MixedFallback_Nested_OuterIdentity", panel.Children.SequenceEqual([outer[2], outer[1], outer[0]]));
                H.Check("MixedFallback_Nested_InnerIdentity", inner.Children.SequenceEqual([children[2], children[1], children[0]]));
                H.Check("MixedFallback_Nested_Content", children[1] is TextBlock { Text: "updated" });
            }
            finally
            {
                reconciler.UnmountChild(panel);
            }
        }

        private static readonly Action NoOp = static () => { };
    }
}
