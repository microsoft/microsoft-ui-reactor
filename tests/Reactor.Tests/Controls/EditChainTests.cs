using System.Reflection;
using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Data;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Controls;

/// <summary>
/// Pure-C# tests for the EditChain private class inside PropertyGridComponent.cs
/// and the file's static helpers (RenderReadOnlyValue, IsPrimitiveOrEnum).
/// These are the largest pure-logic surfaces in the file — host-bound
/// Render() and SolidColorBrush-using BlankButton are deliberately skipped.
/// </summary>
public class EditChainTests
{
    // ── Test models ───────────────────────────────────────────────

    private record ImmutablePoint(int X, int Y);
    private record ImmutableTheme(string Name, ImmutablePoint Origin);
    private record ImmutableConfig(string Label, ImmutableTheme Theme);

    private class MutableHolder
    {
        public ImmutablePoint Position { get; set; } = new(0, 0);
    }

    // ══════════════════════════════════════════════════════════════
    //  BuildPath — used to compute the per-property expand-state key
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void BuildPath_Empty_Chain_Returns_Just_Property_Name()
    {
        var registry = new TypeRegistry();
        var meta = registry.Resolve(typeof(ImmutablePoint));
        var chain = new EditChain(new ImmutablePoint(0, 0), meta, _ => { });

        Assert.Equal("X", chain.BuildPath("X"));
    }

    [Fact]
    public void BuildPath_Multi_Level_Joins_With_Dots_Not_Slashes()
    {
        // Pin: a regression that switched to "/" would silently make every
        // saved expand-state key drift, losing user state across renders.
        var registry = new TypeRegistry();
        var config = new ImmutableConfig("c", new ImmutableTheme("t", new ImmutablePoint(0, 0)));
        var configMeta = registry.Resolve(typeof(ImmutableConfig));
        var themeMeta = registry.Resolve(typeof(ImmutableTheme));
        var pointMeta = registry.Resolve(typeof(ImmutablePoint));

        var themeDesc = configMeta.Decompose!(config).First(d => d.Name == "Theme");
        var originDesc = themeMeta.Decompose!(config.Theme).First(d => d.Name == "Origin");

        var chain = new EditChain(config, configMeta, _ => { });
        var themeChain = chain.Push(themeDesc, themeMeta, config.Theme);
        var pointChain = themeChain.Push(originDesc, pointMeta, config.Theme.Origin);

        Assert.Equal("Theme.Origin.X", pointChain.BuildPath("X"));
    }

    // ══════════════════════════════════════════════════════════════
    //  CannotPropagate — gates RenderEditor's read-only fallback
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void CannotPropagate_With_Direct_SetValue_Returns_False()
    {
        // Bug shape: if this returned true when SetValue is present, the
        // editor would render as read-only even on a fully-editable prop.
        var registry = new TypeRegistry();
        var meta = registry.Resolve(typeof(MutableHolder));
        var holder = new MutableHolder();
        var chain = new EditChain(holder, meta, null);

        var descriptors = meta.Decompose!(holder);
        var positionDesc = descriptors.First(d => d.Name == "Position");
        Assert.NotNull(positionDesc.SetValue);

        Assert.False(chain.CannotPropagate(positionDesc));
    }

    [Fact]
    public void CannotPropagate_Empty_Path_Immutable_Root_No_Callback_Returns_True()
    {
        // Bug shape: editing a leaf on a fully-immutable root with no
        // OnRootChanged should yield a disabled editor, not silently
        // drop edits.
        var registry = new TypeRegistry();
        var rootMeta = registry.Resolve(typeof(ImmutablePoint));
        var chain = new EditChain(new ImmutablePoint(0, 0), rootMeta, onRootChanged: null);

        // Descriptor with no SetValue
        var readOnlyDesc = new FieldDescriptor
        {
            Name = "ReadOnly",
            FieldType = typeof(int),
            GetValue = _ => 0,
            SetValue = null,
        };

        // Manually clear Compose on the rootMeta to force the "no Compose" branch.
        // (Can't actually clear it; instead, register a Compose-less metadata.)
        registry.Register<ImmutablePoint>(new TypeMetadata { /* no Compose, no Editor */ });
        var bareMeta = registry.Resolve(typeof(ImmutablePoint));
        var bareChain = new EditChain(new ImmutablePoint(0, 0), bareMeta, onRootChanged: null);
        Assert.True(bareChain.CannotPropagate(readOnlyDesc));
    }

    [Fact]
    public void CannotPropagate_Empty_Path_OnRootChanged_Allows_Propagation()
    {
        // Pin: even an immutable root without Compose is editable when the
        // consumer supplied an OnRootChanged callback to receive new roots.
        var registry = new TypeRegistry();
        registry.Register<ImmutablePoint>(new TypeMetadata { });
        var bareMeta = registry.Resolve(typeof(ImmutablePoint));

        var chain = new EditChain(new ImmutablePoint(0, 0), bareMeta,
            onRootChanged: _ => { });

        var descriptor = new FieldDescriptor
        {
            Name = "X",
            FieldType = typeof(int),
            GetValue = _ => 0,
            SetValue = null,
        };

        Assert.False(chain.CannotPropagate(descriptor));
    }

    [Fact]
    public void CannotPropagate_Empty_Path_Compose_Allows_Propagation()
    {
        // Pin: a root with Compose can absorb edits even without a callback
        // (e.g., dirty-tracking via Push/Compose internally).
        var registry = new TypeRegistry();
        var meta = registry.Resolve(typeof(ImmutablePoint));
        Assert.NotNull(meta.Compose);
        var chain = new EditChain(new ImmutablePoint(0, 0), meta, onRootChanged: null);

        var descriptor = new FieldDescriptor
        {
            Name = "X",
            FieldType = typeof(int),
            GetValue = _ => 0,
            SetValue = null,
        };

        Assert.False(chain.CannotPropagate(descriptor));
    }

    [Fact]
    public void CannotPropagate_Path_Entry_With_SetValue_Short_Circuits_False()
    {
        // Pin: a mutable ancestor in the chain (e.g. List<T>) makes leaves
        // editable even when the leaf itself has no SetValue. A regression
        // that early-returned on the first compose-less entry without
        // checking SetValue would freeze edits on these chains.
        var registry = new TypeRegistry();
        var holder = new MutableHolder { Position = new(1, 2) };
        var holderMeta = registry.Resolve(typeof(MutableHolder));
        var pointMeta = registry.Resolve(typeof(ImmutablePoint));

        var positionDesc = holderMeta.Decompose!(holder).First(d => d.Name == "Position");
        Assert.NotNull(positionDesc.SetValue);

        var chain = new EditChain(holder, holderMeta, null);
        var pointChain = chain.Push(positionDesc, pointMeta, holder.Position);

        var deepDesc = new FieldDescriptor
        {
            Name = "Z",
            FieldType = typeof(int),
            GetValue = _ => 0,
            SetValue = null, // leaf isn't directly settable
        };

        // Even with no leaf SetValue, the mutable Position SetValue in the
        // chain makes this propagatable.
        Assert.False(pointChain.CannotPropagate(deepDesc));
    }

    [Fact]
    public void CannotPropagate_Path_Entry_With_Null_Compose_Stops_With_True()
    {
        // Pin: walking up the chain, the first entry whose SetValue is null
        // AND whose Meta.Compose is null is the terminal "can't go further"
        // boundary — return true immediately without consulting the root.
        var registry = new TypeRegistry();
        registry.Register<ImmutablePoint>(new TypeMetadata
        {
            Decompose = registry.Resolve(typeof(ImmutablePoint)).Decompose,
            // no Compose
        });

        var holder = new MutableHolder { Position = new(1, 2) };
        var holderMeta = registry.Resolve(typeof(MutableHolder));
        var pointMeta = registry.Resolve(typeof(ImmutablePoint));
        Assert.Null(pointMeta.Compose);

        // Build a descriptor for "Position" that doesn't have a SetValue —
        // simulating a read-only intermediate (init-only path through a
        // type registered without Compose).
        var positionDesc = new FieldDescriptor
        {
            Name = "Position",
            FieldType = typeof(ImmutablePoint),
            GetValue = h => ((MutableHolder)h).Position,
            SetValue = null,
        };

        var chain = new EditChain(holder, holderMeta, null);
        var pointChain = chain.Push(positionDesc, pointMeta, holder.Position);

        var leafDesc = new FieldDescriptor
        {
            Name = "X",
            FieldType = typeof(int),
            GetValue = _ => 0,
            SetValue = null,
        };

        Assert.True(pointChain.CannotPropagate(leafDesc));
    }

    // ══════════════════════════════════════════════════════════════
    //  PropagateNewOwner — when SetValue returns a new ref (immutable)
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void PropagateNewOwner_Empty_Path_Calls_OnRootChanged()
    {
        // Pin: an immutable root edit at top level reaches OnRootChanged.
        var registry = new TypeRegistry();
        var meta = registry.Resolve(typeof(ImmutablePoint));
        object? received = null;
        var chain = new EditChain(new ImmutablePoint(0, 0), meta, r => received = r);

        var newPoint = new ImmutablePoint(99, 88);
        chain.PropagateNewOwner("X", newPoint);

        Assert.Same(newPoint, received);
    }

    [Fact]
    public void PropagateNewOwner_Mutable_Ancestor_SetValue_Stops_Propagation()
    {
        // Pin: a mutable ancestor (SetValue returns Same(parent)) is the
        // terminal absorber — propagation must stop there, NOT continue
        // to the root. A regression that always called OnRootChanged would
        // do a redundant root reassignment on every leaf edit.
        var registry = new TypeRegistry();
        var holder = new MutableHolder { Position = new ImmutablePoint(1, 2) };
        var holderMeta = registry.Resolve(typeof(MutableHolder));
        var pointMeta = registry.Resolve(typeof(ImmutablePoint));

        bool rootCallbackFired = false;
        var positionDesc = holderMeta.Decompose!(holder).First(d => d.Name == "Position");

        var chain = new EditChain(holder, holderMeta,
            onRootChanged: _ => rootCallbackFired = true);
        var pointChain = chain.Push(positionDesc, pointMeta, holder.Position);

        // Edit propagation: replace Position via SetValue (mutable parent
        // absorbs, returns Same(holder))
        var newPoint = new ImmutablePoint(42, 99);
        pointChain.PropagateNewOwner("X", newPoint);

        Assert.Equal(new ImmutablePoint(42, 99), holder.Position);
        Assert.False(rootCallbackFired);
    }

    [Fact]
    public void PropagateNewOwner_Multi_Level_Immutable_Compose_Chain_Reaches_Root()
    {
        // Pin: the Compose-chain branch reconstructs each immutable ancestor
        // with the new child, walking all the way to the root.
        var registry = new TypeRegistry();
        var original = new ImmutableConfig(
            "MyConfig",
            new ImmutableTheme("dark", new ImmutablePoint(5, 10)));

        var configMeta = registry.Resolve(typeof(ImmutableConfig));
        var themeMeta = registry.Resolve(typeof(ImmutableTheme));
        var pointMeta = registry.Resolve(typeof(ImmutablePoint));

        ImmutableConfig? received = null;
        var chain = new EditChain(original, configMeta,
            onRootChanged: r => received = (ImmutableConfig)r);

        var themeDesc = configMeta.Decompose!(original).First(d => d.Name == "Theme");
        var originDesc = themeMeta.Decompose!(original.Theme).First(d => d.Name == "Origin");

        var themeChain = chain.Push(themeDesc, themeMeta, original.Theme);
        var pointChain = themeChain.Push(originDesc, pointMeta, original.Theme.Origin);

        var newPoint = new ImmutablePoint(42, 99);
        pointChain.PropagateNewOwner("X", newPoint);

        Assert.NotNull(received);
        Assert.Equal("MyConfig", received!.Label);
        Assert.Equal("dark", received.Theme.Name);
        Assert.Equal(new ImmutablePoint(42, 99), received.Theme.Origin);
        // Original is untouched (immutable contract)
        Assert.Equal(new ImmutablePoint(5, 10), original.Theme.Origin);
    }

    [Fact]
    public void PropagateNewOwner_Returns_Early_When_No_SetValue_And_No_Compose()
    {
        // Pin: a chain entry with neither SetValue nor Compose is a dead end
        // for propagation — must return without invoking OnRootChanged or
        // touching any sibling.
        var registry = new TypeRegistry();
        // Register a Compose-less metadata for ImmutablePoint
        registry.Register<ImmutablePoint>(new TypeMetadata { });
        var holder = new MutableHolder { Position = new ImmutablePoint(1, 2) };
        var holderMeta = registry.Resolve(typeof(MutableHolder));
        var bareMeta = registry.Resolve(typeof(ImmutablePoint));
        Assert.Null(bareMeta.Compose);

        bool rootFired = false;
        var positionDesc = new FieldDescriptor
        {
            Name = "Position",
            FieldType = typeof(ImmutablePoint),
            GetValue = h => ((MutableHolder)h).Position,
            SetValue = null, // simulate read-only intermediate
        };

        var chain = new EditChain(holder, holderMeta, _ => rootFired = true);
        var pointChain = chain.Push(positionDesc, bareMeta, holder.Position);

        pointChain.PropagateNewOwner("X", new ImmutablePoint(99, 99));

        Assert.False(rootFired);
        Assert.Equal(new ImmutablePoint(1, 2), holder.Position);
    }

    // ══════════════════════════════════════════════════════════════
    //  PropagateImmutableEdit — Compose-only chain, no SetValue
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void PropagateImmutableEdit_Mutable_Ancestor_Absorbs_Composed_Child()
    {
        // Pin: the "compose immutable child, then SetValue on mutable parent"
        // branch (lines 379-384). A regression that dropped the SetValue
        // call after Compose would silently lose the edit on a
        // mutable-holds-immutable pattern.
        var registry = new TypeRegistry();
        var holder = new MutableHolder { Position = new ImmutablePoint(1, 2) };
        var holderMeta = registry.Resolve(typeof(MutableHolder));
        var pointMeta = registry.Resolve(typeof(ImmutablePoint));

        var positionDesc = holderMeta.Decompose!(holder).First(d => d.Name == "Position");
        Assert.NotNull(positionDesc.SetValue);

        bool rootFired = false;
        var chain = new EditChain(holder, holderMeta, _ => rootFired = true);
        var pointChain = chain.Push(positionDesc, pointMeta, holder.Position);

        pointChain.PropagateImmutableEdit("X", 42);

        Assert.Equal(new ImmutablePoint(42, 2), holder.Position);
        Assert.False(rootFired); // mutable absorbed, root not invoked
    }

    [Fact]
    public void PropagateImmutableEdit_No_Compose_Chain_Silently_Drops()
    {
        // Pin: the "neither Compose nor mutable SetValue available" branch
        // must NOT throw or invoke OnRootChanged — it returns silently.
        var registry = new TypeRegistry();
        registry.Register<ImmutablePoint>(new TypeMetadata { });
        var bareMeta = registry.Resolve(typeof(ImmutablePoint));

        bool rootFired = false;
        var chain = new EditChain(new ImmutablePoint(1, 2), bareMeta,
            _ => rootFired = true);

        chain.PropagateImmutableEdit("X", 42);

        Assert.False(rootFired);
    }

    // ══════════════════════════════════════════════════════════════
    //  Static helpers in PropertyGridComponent: RenderReadOnlyValue,
    //  IsPrimitiveOrEnum — reached via reflection.
    // ══════════════════════════════════════════════════════════════

    private static MethodInfo GetStatic(string name) =>
        typeof(PropertyGridComponent).GetMethod(
            name, BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException(
            $"PropertyGridComponent.{name} not found via reflection.");

    [Fact]
    public void RenderReadOnlyValue_Bool_Returns_Disabled_ToggleSwitch()
    {
        // Pin: a regression that swapped to TextBlock for bool would lose
        // the disabled-toggle visual and break the contract that bools
        // ALWAYS render as a toggle (even read-only).
        var method = GetStatic("RenderReadOnlyValue");
        var el = (Element)method.Invoke(null, new object?[] { true, typeof(bool) })!;
        var toggle = Assert.IsType<ToggleSwitchElement>(el);
        Assert.True(toggle.IsOn);
        Assert.Equal(false, toggle.Modifiers?.IsEnabled);
    }

    [Fact]
    public void RenderReadOnlyValue_Bool_Null_Coerces_To_False()
    {
        // Pin: null guard — a regression that NRE'd on null would crash
        // the property grid whenever a nullable bool was read-only.
        var method = GetStatic("RenderReadOnlyValue");
        var el = (Element)method.Invoke(null, new object?[] { null, typeof(bool) })!;
        var toggle = Assert.IsType<ToggleSwitchElement>(el);
        Assert.False(toggle.IsOn);
        Assert.Equal(false, toggle.Modifiers?.IsEnabled);
    }

    [Fact]
    public void RenderReadOnlyValue_String_Returns_Disabled_TextField()
    {
        var method = GetStatic("RenderReadOnlyValue");
        var el = (Element)method.Invoke(null, new object?[] { "hello", typeof(string) })!;
        var field = Assert.IsType<TextBoxElement>(el);
        Assert.Equal("hello", field.Value);
        Assert.Equal(false, field.Modifiers?.IsEnabled);
    }

    [Fact]
    public void RenderReadOnlyValue_String_Null_Coerces_To_Empty()
    {
        var method = GetStatic("RenderReadOnlyValue");
        var el = (Element)method.Invoke(null, new object?[] { null, typeof(string) })!;
        var field = Assert.IsType<TextBoxElement>(el);
        Assert.Equal("", field.Value);
    }

    [Fact]
    public void RenderReadOnlyValue_Other_Type_Falls_Through_To_TextBlock()
    {
        var method = GetStatic("RenderReadOnlyValue");
        var el = (Element)method.Invoke(null, new object?[] { 42, typeof(int) })!;
        var tb = Assert.IsType<TextBlockElement>(el);
        Assert.Equal("42", tb.Content);
    }

    [Fact]
    public void RenderReadOnlyValue_Other_Type_Null_Renders_NullPlaceholder()
    {
        // Pin: the "(null)" sentinel string. A regression to empty-string
        // would visually hide null values, masking config issues.
        var method = GetStatic("RenderReadOnlyValue");
        var el = (Element)method.Invoke(null, new object?[] { null, typeof(int) })!;
        var tb = Assert.IsType<TextBlockElement>(el);
        Assert.Equal("(null)", tb.Content);
    }

    [Theory]
    [InlineData(typeof(int), true)]
    [InlineData(typeof(long), true)]
    [InlineData(typeof(double), true)]
    [InlineData(typeof(bool), true)]
    [InlineData(typeof(byte), true)]
    [InlineData(typeof(string), true)]
    [InlineData(typeof(decimal), true)]
    [InlineData(typeof(DayOfWeek), true)]
    [InlineData(typeof(object), false)]
    [InlineData(typeof(MutableHolder), false)]
    [InlineData(typeof(ImmutablePoint), false)]
    public void IsPrimitiveOrEnum_Returns_Expected(Type t, bool expected)
    {
        var method = GetStatic("IsPrimitiveOrEnum");
        Assert.Equal(expected, (bool)method.Invoke(null, new object?[] { t })!);
    }
}
