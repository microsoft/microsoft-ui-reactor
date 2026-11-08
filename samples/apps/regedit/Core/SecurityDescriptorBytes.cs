using System.Buffers.Binary;

namespace ReactorRegedit.Core;

/// <summary>Helpers on self-relative security descriptors (byte layout per SECURITY_DESCRIPTOR_RELATIVE).</summary>
public static class SecurityDescriptorBytes
{
    private const ushort SE_DACL_PRESENT = 0x0004, SE_DACL_AUTO_INHERITED = 0x0400, SE_SELF_RELATIVE = 0x8000;
    private const byte INHERITED_ACE = 0x10;

    /// <summary>
    /// Sets SE_DACL_AUTO_INHERITED when the DACL holds inherited ACEs but the flag is missing
    /// (keys created by RegCreateKey often look like that). Without it the security editor
    /// shows inherited ACEs as editable and hands them back as explicit ones, so a simple
    /// apply-then-revert duplicates every inherited entry. Returns true when it changed.
    /// </summary>
    public static bool MarkDaclAutoInherited(byte[] sd)
    {
        if (sd.Length < 20) return false;
        var control = BinaryPrimitives.ReadUInt16LittleEndian(sd.AsSpan(2));
        if ((control & SE_SELF_RELATIVE) == 0 || (control & SE_DACL_PRESENT) == 0 || (control & SE_DACL_AUTO_INHERITED) != 0)
            return false;
        var daclOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(sd.AsSpan(16));
        if (daclOffset == 0 || daclOffset + 8 > sd.Length) return false;
        int aceCount = BinaryPrimitives.ReadUInt16LittleEndian(sd.AsSpan(daclOffset + 4));
        int pos = daclOffset + 8;
        bool anyInherited = false;
        for (int i = 0; i < aceCount && pos + 4 <= sd.Length; i++)
        {
            if ((sd[pos + 1] & INHERITED_ACE) != 0) { anyInherited = true; break; }
            int size = BinaryPrimitives.ReadUInt16LittleEndian(sd.AsSpan(pos + 2));
            if (size < 4) break;
            pos += size;
        }
        if (!anyInherited) return false;
        BinaryPrimitives.WriteUInt16LittleEndian(sd.AsSpan(2), (ushort)(control | SE_DACL_AUTO_INHERITED));
        return true;
    }
}
