namespace ReactorRegedit.Core;

/// <summary>
/// Raw Win32 registry value types (winnt.h REG_*). The app keeps every value as
/// <c>(type, bytes)</c> so that types .NET's <c>RegistryValueKind</c> cannot represent
/// (REG_LINK, the resource lists, unknown numeric types) survive display, edit and
/// export byte-for-byte.
/// </summary>
public static class RegType
{
    public const uint None = 0;
    public const uint Sz = 1;
    public const uint ExpandSz = 2;
    public const uint Binary = 3;
    public const uint Dword = 4;
    public const uint DwordBigEndian = 5;
    public const uint Link = 6;
    public const uint MultiSz = 7;
    public const uint ResourceList = 8;
    public const uint FullResourceDescriptor = 9;
    public const uint ResourceRequirementsList = 10;
    public const uint Qword = 11;

    /// <summary>Type name as shown in regedit's Type column.</summary>
    public static string Name(uint type) => type switch
    {
        None => "REG_NONE",
        Sz => "REG_SZ",
        ExpandSz => "REG_EXPAND_SZ",
        Binary => "REG_BINARY",
        Dword => "REG_DWORD",
        DwordBigEndian => "REG_DWORD_BIG_ENDIAN",
        Link => "REG_LINK",
        MultiSz => "REG_MULTI_SZ",
        ResourceList => "REG_RESOURCE_LIST",
        FullResourceDescriptor => "REG_FULL_RESOURCE_DESCRIPTOR",
        ResourceRequirementsList => "REG_RESOURCE_REQUIREMENTS_LIST",
        Qword => "REG_QWORD",
        _ => $"0x{type:x8}",
    };

    /// <summary>String-family values get the "ab" icon; everything else the binary icon.</summary>
    public static bool IsStringFamily(uint type) => type is Sz or ExpandSz or MultiSz;
}
