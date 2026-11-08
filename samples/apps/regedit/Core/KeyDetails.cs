using System.Globalization;

namespace ReactorRegedit.Core;

/// <summary>Status-bar text for a key's details (F17).</summary>
public static class KeyDetails
{
    public static string Format(int subKeyCount, int valueCount, DateTime lastWriteLocal, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        string Plural(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n.ToString("N0", culture)} {many}";
        var when = lastWriteLocal.ToString("g", culture);
        return $"{Plural(subKeyCount, "subkey", "subkeys")}, {Plural(valueCount, "value", "values")}, modified {when}";
    }
}
