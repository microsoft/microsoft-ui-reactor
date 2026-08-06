// Name disambiguation for the source-shared gallery pages.
//
// Every gallery page carries `using Microsoft.UI.Xaml.Controls;` alongside
// `using static Microsoft.UI.Reactor.Factories;`. On Uno that pulls Uno's own
// SelectionMode into scope next to Reactor's, and the shared source refers to
// the bare name — so pin it here rather than forking the pages.
//
// Reactor's Reactor.Uno library does exactly the same thing for the same reason
// (see src/Reactor.Uno/GlobalUsings.cs).

global using SelectionMode = Microsoft.UI.Reactor.Controls.SelectionMode;
