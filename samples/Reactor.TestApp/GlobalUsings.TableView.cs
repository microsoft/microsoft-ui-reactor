// The native TableView projection (TableView.Projection.dll) contributes a vendored
// Microsoft.UI.Xaml.Controls.Primitives.SortDirection, which would make the bare name
// `SortDirection` ambiguous in demos that use Microsoft.UI.Reactor.Data. This alias
// restores the pre-existing meaning (Reactor's SortDirection) for unqualified uses.
global using SortDirection = Microsoft.UI.Reactor.Data.SortDirection;
