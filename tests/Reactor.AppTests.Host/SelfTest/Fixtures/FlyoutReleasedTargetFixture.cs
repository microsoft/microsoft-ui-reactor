using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal sealed class FlyoutReleasedTargetFixture(Harness h) : SelfTestFixtureBase(h)
{
    public override async Task RunAsync()
    {
        var (flyout, target, plain) = MountAndRelease();
        var (oldFlyout, reusedTarget, viewModel) = ReplaceFlyoutAndUnmount();
        for (int i = 0; i < 5; i++)
        {
            // Let the UI dispatcher service any WinRT releases during finalization.
            await Task.Run(static () =>
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            });
            await Harness.Render();
            if (!IsAlive(plain) && !IsAlive(target) && !IsAlive(reusedTarget) && !IsAlive(viewModel)) break;
        }
        H.Check("PlainUnmountedButtonCollected", !IsAlive(plain));
        H.Check("RetainedFlyoutReleasesUnmountedButton", !IsAlive(target));
        H.Check("ReplacedFlyoutReleasesUnmountedButton", !IsAlive(reusedTarget));
        H.Check("ReplacedFlyoutReleasesButtonCallbackTarget", !IsAlive(viewModel));
        GC.KeepAlive(flyout);
        GC.KeepAlive(oldFlyout);
        await VerifyCallbacksAddedAfterMount();
    }

    private async Task VerifyCallbacksAddedAfterMount()
    {
        using var reconciler = new Reconciler();
        var initial = Flyout(Button("late callbacks"), TextBlock("body")) with { AreOpenCloseAnimationsEnabled = false };
        var button = (Button)reconciler.Mount(initial, static () => { })!;
        var flyout = (Flyout)button.Flyout;
        H.SetContent(button);
        try
        {
            await Harness.Render();
            int opened = 0, closed = 0;
            var next = initial with { OnOpened = () => opened++, OnClosed = () => closed++ };
            reconciler.Update(initial, next, button, static () => { });
            H.Check("LateCallbacksUseOriginalFlyout", ReferenceEquals(button.Flyout, flyout));
            flyout.ShowAt(button);
            await Harness.WaitFor(() => opened > 0);
            flyout.Hide();
            await Harness.WaitFor(() => closed > 0);
            H.Check("LateCallbacksFireOnce", opened == 1 && closed == 1, $"opened={opened}, closed={closed}");
        }
        finally { reconciler.UnmountChild(button); H.SetContent(null); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private (Flyout Flyout, WeakReference<Button> Target, WeakReference<Button> Plain) MountAndRelease()
    {
        using var reconciler = new Reconciler();
        reconciler.Pool.Enabled = false;
        var plain = (Button)reconciler.Mount(Button("plain"), static () => { })!;
        Flyout? saved = null;
        var element = Flyout(Button("target"), TextBlock("body")).Set(f => saved = f);
        var button = (Button)reconciler.Mount(element, static () => { })!;
        H.Check("SavedInstalledFlyout", saved is not null && ReferenceEquals(button.Flyout, saved));
        reconciler.UnmountChild(plain);
        reconciler.UnmountChild(button);
        H.Check("FlyoutDetached", button.Flyout is null && saved is { Content: null });
        return (saved!, new(button), new(plain));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private (Flyout, WeakReference<Button>, WeakReference<SelectionViewModel>) ReplaceFlyoutAndUnmount()
    {
        using var reconciler = new Reconciler();
        reconciler.Pool.Enabled = false;
        var vm = new SelectionViewModel();
        var target = Button("Select", vm.Select);
        var initial = Flyout(target, TextBlock("body"));
        var button = (Button)reconciler.Mount(initial, static () => { })!;
        var oldFlyout = (Flyout)button.Flyout;

        var next = initial with { Target = target.WithFlyout(ContentFlyout(TextBlock("attached"))) };
        var replacement = reconciler.Update(initial, next, button, static () => { });
        H.Check("SameButtonHasDifferentFlyout", (replacement is null || ReferenceEquals(replacement, button))
            && button.Flyout is Flyout current && !ReferenceEquals(current, oldFlyout));
        reconciler.UnmountChild(button);
        return (oldFlyout, new(button), new(vm));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsAlive<T>(WeakReference<T> reference) where T : class => reference.TryGetTarget(out _);

    private sealed class SelectionViewModel
    {
        public void Select() { }
    }
}
