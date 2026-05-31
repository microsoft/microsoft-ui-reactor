using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.HotReload;

/// <summary>
/// Unit tests for spec 049 Phase 2 — hot-reload state migration across
/// record/class shape changes. Two layers are covered:
/// <list type="bullet">
/// <item><see cref="ReactorHotReloadCopier"/> in isolation — the by-name field
/// copier that survives an add/remove/retype edit.</item>
/// <item><see cref="RenderContext.MigrateHooksForHotReload"/> — the hook-cell
/// value-swap that wires the copier into a live context, gated on an active
/// update pass.</item>
/// </list>
/// Real Edit-and-Continue edits a type in place (same <see cref="System.Type"/>
/// identity, a field added/removed), so passing the same type as "updated" is a
/// faithful headless approximation of the migration path.
/// </summary>
[Collection("HotReload")]
public class HotReloadStateMigrationTests
{
    // Distinct "before"/"after" shapes used to exercise the copier directly.
    private class StateV1 { public int Count; public string? Name; }
    private class StateV2 { public int Count; public bool Flag; }      // Name removed, Flag added
    private class StateRetyped { public string? Count; }               // Count int -> string (incompatible)
    private class WithHandle { public int Keep; public nint Handle; }  // native handle must not copy
    private class SelfRef { public int V; public SelfRef? Next; }

    private static HashSet<object> FreshVisited() =>
        new(ReferenceEqualityComparer.Instance);

    [Fact]
    public void Copier_CopiesMatchingField_DefaultsNewField()
    {
        var src = new StateV1 { Count = 7, Name = "keep" };
        var dest = new StateV2();

        Assert.True(ReactorHotReloadCopier.TryMigrate(src, dest, FreshVisited()));

        Assert.Equal(7, dest.Count);   // preserved by name
        Assert.False(dest.Flag);       // brand-new field keeps its default
    }

    [Fact]
    public void Copier_DropsRemovedField_WithoutThrowing()
    {
        // StateV1.Name has no counterpart on StateV2 — it is simply ignored.
        var src = new StateV1 { Count = 1, Name = "gone" };
        var dest = new StateV2();

        var ex = Record.Exception(() => ReactorHotReloadCopier.TryMigrate(src, dest, FreshVisited()));

        Assert.Null(ex);
        Assert.Equal(1, dest.Count);
    }

    [Fact]
    public void Copier_IncompatibleTypeChange_DropsValueAndDoesNotThrow()
    {
        var src = new StateV1 { Count = 42, Name = "x" };
        var dest = new StateRetyped();

        var ex = Record.Exception(() => ReactorHotReloadCopier.TryMigrate(src, dest, FreshVisited()));

        Assert.Null(ex);
        Assert.Null(dest.Count); // int -> string is incompatible: left at default.
    }

    [Fact]
    public void Copier_DoesNotCopyBlockListedNativeHandle()
    {
        var src = new WithHandle { Keep = 9, Handle = 0x1234 };
        var dest = new WithHandle();

        ReactorHotReloadCopier.TryMigrate(src, dest, FreshVisited());

        Assert.Equal(9, dest.Keep);       // ordinary field copies
        Assert.Equal(nint.Zero, dest.Handle); // native handle is never smuggled across
    }

    [Fact]
    public void Copier_SelfReferentialGraph_DoesNotInfiniteLoop()
    {
        var src = new SelfRef { V = 3 };
        src.Next = src; // cycle
        var dest = new SelfRef();

        var ex = Record.Exception(() => ReactorHotReloadCopier.TryMigrate(src, dest, FreshVisited()));

        Assert.Null(ex);
        Assert.Equal(3, dest.V);
    }

    [Fact]
    public void Copier_NullInputs_ReturnFalse()
    {
        Assert.False(ReactorHotReloadCopier.TryMigrate(null, new StateV2(), FreshVisited()));
        Assert.False(ReactorHotReloadCopier.TryMigrate(new StateV1(), null, FreshVisited()));
    }

    // ── Hook-cell migration (RenderContext.MigrateHooksForHotReload) ────────

    private record AppState(int Count, string Name);

    [Fact]
    public void Migrate_ValueSwapsMatchingHook_PreservesFields_NewReference()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        var (_, set) = ctx.UseState(new AppState(1, "a"));
        set(new AppState(5, "z"));

        var before = ctx.SnapshotHooks().Single(h => h.Hook == "useState");
        var beforeValue = before.Value;
        Assert.False(before.Migrated);

        using (HotReloadService.BeginUpdatePass())
        {
            ctx.MigrateHooksForHotReload(new HashSet<Type> { typeof(AppState) });
        }

        var after = ctx.SnapshotHooks().Single(h => h.Hook == "useState");
        var migrated = Assert.IsType<AppState>(after.Value);

        Assert.Equal(5, migrated.Count);          // surviving fields preserved
        Assert.Equal("z", migrated.Name);
        Assert.True(after.Migrated);              // Q3 flag set
        Assert.False(ReferenceEquals(beforeValue, after.Value)); // a fresh instance
    }

    [Fact]
    public void Migrate_NoUpdatedTypes_IsNoOp()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        ctx.UseState(new AppState(1, "a"));
        var before = ctx.SnapshotHooks().Single(h => h.Hook == "useState").Value;

        using (HotReloadService.BeginUpdatePass())
        {
            ctx.MigrateHooksForHotReload(null);
            ctx.MigrateHooksForHotReload(new HashSet<Type>());
        }

        var after = ctx.SnapshotHooks().Single(h => h.Hook == "useState");
        Assert.Same(before, after.Value); // untouched reference
        Assert.False(after.Migrated);
    }

    [Fact]
    public void Migrate_OutsideUpdatePass_IsNoOp()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        ctx.UseState(new AppState(1, "a"));
        var before = ctx.SnapshotHooks().Single(h => h.Hook == "useState").Value;

        // No BeginUpdatePass scope — WithinUpdatePass is false, so migration
        // must short-circuit even though a matching updated type is supplied.
        ctx.MigrateHooksForHotReload(new HashSet<Type> { typeof(AppState) });

        var after = ctx.SnapshotHooks().Single(h => h.Hook == "useState");
        Assert.Same(before, after.Value);
        Assert.False(after.Migrated);
    }

    [Fact]
    public void Migrate_UnrelatedUpdatedType_LeavesHookUntouched()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        ctx.UseState(new AppState(1, "a"));
        var before = ctx.SnapshotHooks().Single(h => h.Hook == "useState").Value;

        using (HotReloadService.BeginUpdatePass())
        {
            ctx.MigrateHooksForHotReload(new HashSet<Type> { typeof(string) });
        }

        var after = ctx.SnapshotHooks().Single(h => h.Hook == "useState");
        Assert.Same(before, after.Value);
        Assert.False(after.Migrated);
    }
}

