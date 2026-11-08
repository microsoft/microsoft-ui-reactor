namespace ReactorRegedit.Core;

public enum ValueSortColumn { Name, Type, Data }

public static class ValueSort
{
    /// <summary>
    /// Sorts values by column. The (Default) row always stays first regardless of
    /// column or direction, like regedit. Ties fall back to name order.
    /// </summary>
    public static RegValue[] Sort(IEnumerable<RegValue> values, ValueSortColumn column, bool descending)
    {
        var list = values.ToList();
        var defaults = list.Where(v => v.IsDefault).ToList();
        var rest = list.Where(v => !v.IsDefault);

        Func<RegValue, string> key = column switch
        {
            ValueSortColumn.Type => v => v.TypeName,
            ValueSortColumn.Data => v => v.DisplayData,
            _ => v => v.Name,
        };

        var cmp = StringComparer.OrdinalIgnoreCase;
        var ordered = descending
            ? rest.OrderByDescending(key, cmp).ThenByDescending(v => v.Name, cmp)
            : rest.OrderBy(key, cmp).ThenBy(v => v.Name, cmp);

        return [.. defaults, .. ordered];
    }
}
