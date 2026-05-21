using System;
using System.Threading.Tasks;
using Microsoft.UI.Reactor;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Spec 036 §11.3 — <see cref="JumpList"/> static state, argument
/// validation, and unpackaged-path AppUserModelId guard. The actual COM /
/// WinRT update paths need a real shell context; here we test the
/// caller-error gates that run synchronously before any platform call.
///
/// Each test uses <c>JumpList.ResetForTests()</c> to isolate the static
/// state.
/// </summary>
public class JumpListUpdateValidationTests : IDisposable
{
    public JumpListUpdateValidationTests() => JumpList.ResetForTests();
    public void Dispose() => JumpList.ResetForTests();

    // ══════════════════════════════════════════════════════════════
    //  Static state — `JumpListStateTests` already covers basic
    //  AppUserModelId / ShowRecent / ShowFrequent round-trip. This
    //  file focuses on `UpdateAsync` validation and the unpackaged-
    //  path AppUserModelId guard.
    // ══════════════════════════════════════════════════════════════

    // ══════════════════════════════════════════════════════════════
    //  UpdateAsync argument validation
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task UpdateAsync_Null_Items_Throws_ArgumentNullException()
    {
        // The ArgumentNullException.ThrowIfNull guard. Bug shape: a
        // regression that called .ToList() on null would NRE inside
        // the framework — much harder to diagnose than the explicit
        // ANE with a parameter name.
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await JumpList.UpdateAsync(null!));
    }

    [Fact]
    public async Task UpdateAsync_Null_Entry_In_List_Throws_ArgumentException()
    {
        // Validation pass before any platform API. The `snapshot[i] ?? throw`
        // catches null elements in an otherwise non-null collection.
        JumpList.AppUserModelId = "Test.App";
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await JumpList.UpdateAsync(new JumpListItem?[] { null! }!));
    }

    [Fact]
    public async Task UpdateAsync_Task_Item_With_Empty_Title_Throws_ArgumentException()
    {
        // The non-separator empty-title gate. Separators are allowed to
        // have empty title (and we test that below); tasks must have a
        // visible label.
        JumpList.AppUserModelId = "Test.App";
        var bad = new JumpListItem(Title: "", Arguments: "args", Kind: JumpListItemKind.Task);
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await JumpList.UpdateAsync(new[] { bad }));
    }

    [Fact]
    public async Task UpdateAsync_Custom_Item_With_Empty_Title_Throws_ArgumentException()
    {
        // The same gate applies to Custom kind — both surfaces show
        // user-visible labels.
        JumpList.AppUserModelId = "Test.App";
        var bad = new JumpListItem(
            Title: "", Arguments: "args",
            Kind: JumpListItemKind.Custom, GroupCategory: "Group");
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await JumpList.UpdateAsync(new[] { bad }));
    }

    [Fact]
    public async Task UpdateAsync_Separator_With_Empty_Title_Is_Allowed()
    {
        // Pin: separators bypass the empty-title check (they have no
        // text to render). Setting AppUserModelId so the unpackaged path
        // is reachable, then expecting NO exception from the validation
        // loop. The downstream COM call will fail (no shell context in
        // unit tests) — that's fine; we only care that validation passed.
        JumpList.AppUserModelId = "Test.App";
        var sep = new JumpListItem(Title: "", Arguments: "", Kind: JumpListItemKind.Separator);
        // Should not throw ArgumentException or InvalidOperationException from
        // the validation loop. COM exceptions are expected and acceptable.
        try
        {
            await JumpList.UpdateAsync(new[] { sep });
        }
        catch (InvalidOperationException)
        {
            Assert.Fail("Separator-with-empty-title must pass validation when AppUserModelId is set.");
        }
        catch (ArgumentException)
        {
            Assert.Fail("Separator-with-empty-title must not raise an ArgumentException.");
        }
        catch (global::System.Runtime.InteropServices.COMException)
        {
            // Expected — no shell context in unit tests.
        }
        catch (global::System.EntryPointNotFoundException)
        {
            // Expected — propsys.dll entry points may be unavailable in test context.
        }
    }

    [Fact]
    public async Task UpdateAsync_Unpackaged_Without_AppUserModelId_Throws_InvalidOperation()
    {
        // The synchronous AppUserModelId check before Task.Run. A
        // regression that elided this gate would surface as a silent
        // no-op (the inner COM call would fail and be swallowed),
        // leaving developers staring at an empty jump list.
        Assert.Null(JumpList.AppUserModelId);
        var item = new JumpListItem("Open", "/path");
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await JumpList.UpdateAsync(new[] { item }));
    }

    [Fact]
    public async Task ClearAsync_Delegates_To_UpdateAsync_Empty()
    {
        // ClearAsync is `=> UpdateAsync(Array.Empty<JumpListItem>())`.
        // Without AppUserModelId, the empty-list path STILL hits the
        // unpackaged AppUserModelId gate (the gate fires post-validation
        // even on empty input). Pin that ClearAsync inherits the same
        // gate — a regression that special-cased empty inputs to skip
        // the gate would let unconfigured apps clear nothing, silently.
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await JumpList.ClearAsync());
    }

    // ══════════════════════════════════════════════════════════════
    //  UpdateAsync happy(-ish) path — items pass validation, the
    //  COM call inside Task.Run fails, the exception propagates.
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task UpdateAsync_With_Valid_Items_And_AumiD_Throws_When_COM_Unavailable()
    {
        // With exceptions no longer swallowed, COM/platform failures (test
        // context, remote-desktop sessions without taskbar, group-policy-
        // locked shells) propagate to callers. Callers who want fire-and-
        // forget semantics should catch at their call site.
        JumpList.AppUserModelId = "Test.App";
        var items = new[]
        {
            new JumpListItem("Open", "open --doc /path"),
            new JumpListItem("Help", "help"),
        };
        // In unit test context, the platform call fails — must be a platform
        // exception (COM/EntryPoint/DllNotFound), NOT a validation exception.
        var ex = await Assert.ThrowsAnyAsync<Exception>(
            async () => await JumpList.UpdateAsync(items));
        Assert.False(ex is ArgumentException or InvalidOperationException,
            $"Expected a platform failure, not a validation exception: {ex.GetType().Name}: {ex.Message}");
    }
}
