namespace WinUI.Dock;

public interface IDockAdapter
{
    void OnCreated(Document document);

    void OnCreated(DocumentGroup group, Document? draggedDocument);

    object? GetFloatingWindowTitleBar(Document? draggedDocument);
}
