namespace ChatSample.Chat.UI;

public static class Res
{
    public static Brush Get(string key) => (Brush)Application.Current.Resources[key];
}

public static class TimeFormat
{
    public static string Relative(DateTimeOffset time)
    {
        var diff = DateTimeOffset.Now - time;
        if (diff.TotalMinutes < 1) return "just now";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
        if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}d ago";
        return time.ToString("MMM d");
    }
}
