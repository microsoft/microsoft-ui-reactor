using System;
using Android.Runtime;
using Microsoft.UI.Reactor;

namespace ReactorUnoCounter.Droid;

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
            () => ReactorApp.CreateApplication<CounterApp>("Reactor Counter (Uno)"),
            javaReference,
            transfer)
    {
    }
}
