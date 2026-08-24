// Disambiguation aliases for the Uno port.
//
// Uno defines ElementFactoryGetArgs / ElementFactoryRecycleArgs in BOTH
// Microsoft.UI.Xaml and Microsoft.UI.Xaml.Controls, which makes the unqualified
// name in Core/ElementFactory.cs (it imports both namespaces) ambiguous. On the
// Windows build the type only exists in Microsoft.UI.Xaml, so pin the alias to
// that namespace — matching IElementFactory's member signature.
global using ElementFactoryGetArgs = Microsoft.UI.Xaml.ElementFactoryGetArgs;
global using ElementFactoryRecycleArgs = Microsoft.UI.Xaml.ElementFactoryRecycleArgs;

// Uno's implicit `Microsoft.UI.Xaml.Controls` global using cannot be removed via
// <Using Remove> (Uno injects it from the SDK targets). The only bare-`SelectionMode`
// *type* reference in shared source is DataGridFactories.cs, which wants Reactor's
// enum; the WinUI list controls use control-specific enums (ListViewSelectionMode,
// …), so pinning the bare name to Reactor's type is safe project-wide.
global using SelectionMode = Microsoft.UI.Reactor.Controls.SelectionMode;
