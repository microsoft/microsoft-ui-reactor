using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace WinUI.Dock;

[ContentProperty(Name = nameof(Children))]
public abstract partial class DockContainer : DockModule
{
    protected DockContainer()
    {
        Children.CollectionChanged += (_, e) =>
        {
            if (ValidateChildren())
            {
                foreach (DockModule module in Children)
                {
                    module.Attach(this);
                }

                if (e.Action is NotifyCollectionChangedAction.Reset)
                {
                    InitChildren();
                }
                else
                {
                    SynchronizeChildren(e.OldItems?.Cast<DockModule>().ToArray() ?? [],
                                        e.OldStartingIndex,
                                        e.NewItems?.Cast<DockModule>().ToArray() ?? [],
                                        e.NewStartingIndex);
                }
            }
        };
    }

    public ObservableCollection<DockModule> Children { get; } = [];

    internal void DetachEmptyContainer()
    {
        for (int i = Children.Count - 1; i >= 0; i--)
        {
            if (Children[i] is DockContainer container)
            {
                container.DetachEmptyContainer();
            }
        }

        if (Children.Count is 0 && ConfirmEmptyContainer())
        {
            Detach();
        }
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        InitTemplate();
        InitChildren();
    }

    protected abstract void InitTemplate();

    protected abstract void InitChildren();

    protected abstract void SynchronizeChildren(DockModule[] oldChildren,
                                                int oldStartingIndex,
                                                DockModule[] newChildren,
                                                int newStartingIndex);

    protected abstract bool ValidateChildren();

    protected abstract bool ConfirmEmptyContainer();

    protected override void OnRootChanged(DockManager? oldRoot, DockManager? newRoot)
    {
        foreach (DockModule module in Children)
        {
            module.Root = newRoot;
        }
    }
}
