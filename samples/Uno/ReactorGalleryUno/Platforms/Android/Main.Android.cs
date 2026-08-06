using System;
using Android.Runtime;
using Microsoft.UI.Reactor;
using WinUIGalleryReactor;

namespace ReactorGalleryUno.Droid;

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
            () => ReactorApp.CreateApplication<GalleryShell>("Reactor Gallery (Uno)"),
            javaReference,
            transfer)
    {
    }
}
