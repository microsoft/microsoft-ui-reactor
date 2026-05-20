namespace WinUI.Dock;

internal static class FloatingWindowHelpers
{
    private static readonly Dictionary<DockManager, List<FloatingWindow>> windows = [];

    public static void AddWindow(DockManager manager, FloatingWindow window)
    {
        if (!windows.TryGetValue(manager, out List<FloatingWindow>? value))
        {
            windows[manager] = value = [];
        }

        value.Add(window);
    }

    public static void RemoveWindow(DockManager manager, FloatingWindow window)
    {
        if (windows.TryGetValue(manager, out List<FloatingWindow>? value))
        {
            value.Remove(window);

            if (value.Count is 0)
            {
                windows.Remove(manager);
            }
        }
    }

    public static FloatingWindow[] GetWindows(DockManager manager)
    {
        return windows.TryGetValue(manager, out List<FloatingWindow>? value) ? [.. value] : [];
    }

    public static void CloseEmptyWindows(DockManager manager)
    {
        if (windows.TryGetValue(manager, out List<FloatingWindow>? value))
        {
            foreach (FloatingWindow window in value.Where(static item => item.IsEmpty).ToArray())
            {
                window.Close();
            }
        }
    }

    public static void CloseAllWindows(DockManager manager)
    {
        if (windows.TryGetValue(manager, out List<FloatingWindow>? value))
        {
            foreach (FloatingWindow window in value.ToArray())
            {
                window.Close();
            }
        }
    }
}
