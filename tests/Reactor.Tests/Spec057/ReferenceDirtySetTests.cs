using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Spec 057 §6.3 — headless tests for the reference-cell dirty set. The non-null
/// sentinels are uninitialized WinUI objects; tests only compare references and
/// never touch native XAML state, so no XAML host is required.
/// </summary>
public class ReferenceDirtySetTests
{
    [Fact]
    public void SetCurrent_Outside_Commit_Dispatches_Immediately()
    {
        var cell = new ElementRef { _current = Sentinel<Button>() };
        var fireCount = 0;
        FrameworkElement? observed = cell.Current;
        cell.CurrentChanged += value =>
        {
            fireCount++;
            observed = value;
        };

        cell.SetCurrent(null);

        Assert.Equal(1, fireCount);
        Assert.Null(observed);
    }

    [Fact]
    public void SetCurrent_Inside_Commit_Defers_Until_EndFlush()
    {
        var cell = new ElementRef { _current = Sentinel<Button>() };
        var fireCount = 0;
        FrameworkElement? observed = cell.Current;
        cell.CurrentChanged += value =>
        {
            fireCount++;
            observed = value;
        };

        ReferenceDirtySet.BeginCommit();
        try
        {
            cell.SetCurrent(null);
            Assert.Equal(0, fireCount);
            Assert.Null(cell.Current);
        }
        finally
        {
            ReferenceDirtySet.EndCommitAndFlush();
        }

        Assert.Equal(1, fireCount);
        Assert.Null(observed);
    }

    [Fact]
    public void Same_Cell_Enqueued_Twice_In_Commit_Flushes_Once_With_Final_Value()
    {
        var first = Sentinel<Button>();
        var final = Sentinel<Grid>();
        var cell = new ElementRef { _current = first };
        var fireCount = 0;
        FrameworkElement? observed = null;
        cell.CurrentChanged += value =>
        {
            fireCount++;
            observed = value;
        };

        ReferenceDirtySet.BeginCommit();
        try
        {
            cell.SetCurrent(null);
            cell.SetCurrent(final);
            Assert.Equal(0, fireCount);
        }
        finally
        {
            ReferenceDirtySet.EndCommitAndFlush();
        }

        Assert.Equal(1, fireCount);
        Assert.Same(final, observed);
    }

    private static T Sentinel<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)] T>()
        where T : FrameworkElement =>
        (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
}
