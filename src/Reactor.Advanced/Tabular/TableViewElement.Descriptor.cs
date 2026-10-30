using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor;
using WinUITableView = Microsoft.UI.Xaml.Controls.Tabular.TableView;

namespace Microsoft.UI.Reactor.Advanced.Tabular;

public sealed partial record TableViewElement
{
    // The hand-written entries for the [WrapManual] properties. The generated descriptor appends
    // the scalar OneWay entries after these, and registers the result from its static constructor.
    private static partial ControlDescriptor<TableViewElement, WinUITableView> Customize(
        ControlDescriptor<TableViewElement, WinUITableView> d) =>
        d.OneWay<bool>(
                static e => TableViewSync.EffectiveTableReadOnly(e) is bool readOnly ? readOnly : Optional<bool>.Unset,
                static (c, v) => c.IsReadOnly = v,
                WinUITableView.IsReadOnlyProperty)
            .ImperativeBridged(TableViewSync.Mount, TableViewSync.Update)
            .WithUnmount(TableViewSync.Unmount);
}
