using System;
using Android.Runtime;
using Microsoft.UI.Reactor;

namespace ReactorUnoShowcase.Droid;

[global::Android.App.ApplicationAttribute(
    Label = "@string/ApplicationName",
    LargeHeap = true,
    HardwareAccelerated = true,
    Theme = "@style/AppTheme"
)]
public class Application : Microsoft.UI.Xaml.NativeApplication
{
    public Application(IntPtr javaReference, JniHandleOwnership transfer)
        : base(
            () => ReactorApp.CreateApplication<Showcase>("Reactor Showcase (Uno)"),
            javaReference,
            transfer)
    {
    }
}
