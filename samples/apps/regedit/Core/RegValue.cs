namespace ReactorRegedit.Core;

/// <summary>
/// One registry value exactly as stored: name, raw REG_* type and raw bytes.
/// </summary>
/// <param name="Name">Value name; empty string is the key's (Default) value.</param>
/// <param name="Type">Raw REG_* type (see <see cref="RegType"/>).</param>
/// <param name="Data">Raw data bytes.</param>
public sealed record RegValue(string Name, uint Type, byte[] Data)
{
    /// <summary>
    /// True for the synthetic "(Default) REG_SZ (value not set)" row shown when a key has
    /// no default value. It does not exist in the registry.
    /// </summary>
    public bool IsUnsetDefault { get; init; }

    public bool IsDefault => Name.Length == 0;

    public string DisplayName => IsDefault ? "(Default)" : Name;

    public string TypeName => RegType.Name(Type);

    public string DisplayData => IsUnsetDefault ? "(value not set)" : ValueFormat.Display(Type, Data);

    public static RegValue UnsetDefault() => new("", RegType.Sz, []) { IsUnsetDefault = true };

    public static RegValue FromString(string name, string value, uint type = RegType.Sz) =>
        new(name, type, ValueFormat.EncodeString(value));

    public static RegValue FromDword(string name, uint value) =>
        new(name, RegType.Dword, BitConverter.GetBytes(value));

    public static RegValue FromQword(string name, ulong value) =>
        new(name, RegType.Qword, BitConverter.GetBytes(value));

    public static RegValue FromMultiString(string name, IReadOnlyList<string> values) =>
        new(name, RegType.MultiSz, ValueFormat.EncodeMultiString(values));
}
