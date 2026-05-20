namespace WinUI.Dock;

internal static class DragDropHelpers
{
    private static readonly Dictionary<string, DockManager> managers = [];
    private static readonly Dictionary<string, Document> documents = [];
    private static readonly Lock @lock = new();

    public const string ManagerKey = "Dock.Manager";
    public const string DocumentKey = "Dock.Document";

    public static string GetManagerKey(DockManager dockManager)
    {
        using Lock.Scope scope = @lock.EnterScope();

        string key = Guid.NewGuid().ToString();

        managers[key] = dockManager;

        return key;
    }

    public static DockManager? GetManager(string key)
    {
        using Lock.Scope scope = @lock.EnterScope();

        managers.TryGetValue(key, out DockManager? dockManager);

        return dockManager;
    }

    public static void RemoveManagerKey(string key)
    {
        using Lock.Scope scope = @lock.EnterScope();

        managers.Remove(key);
    }

    public static string GetDocumentKey(Document document)
    {
        using Lock.Scope scope = @lock.EnterScope();

        string key = Guid.NewGuid().ToString();

        documents[key] = document;

        return key;
    }

    public static Document? GetDocument(string key)
    {
        using Lock.Scope scope = @lock.EnterScope();

        documents.TryGetValue(key, out Document? document);

        return document;
    }

    public static void RemoveDocumentKey(string key)
    {
        using Lock.Scope scope = @lock.EnterScope();

        documents.Remove(key);
    }
}
