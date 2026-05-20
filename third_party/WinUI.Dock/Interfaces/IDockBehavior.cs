namespace WinUI.Dock;

public interface IDockBehavior
{
    void ActivateMainWindow();

    void OnDocked(Document src, DockManager dest, DockTarget target);

    void OnDocked(Document src, DocumentGroup dest, DockTarget target);

    void OnFloating(Document document);
}
