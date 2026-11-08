using System.Security.AccessControl;
using ReactorRegedit.Core;
using Xunit;

namespace ReactorRegedit.Tests;

public class SecurityDescriptorBytesTests
{
    private static byte[] Bytes(string sddl)
    {
        var sd = new RawSecurityDescriptor(sddl);
        var b = new byte[sd.BinaryLength];
        sd.GetBinaryForm(b, 0);
        return b;
    }

    private static string Sddl(byte[] b) => new RawSecurityDescriptor(b, 0).GetSddlForm(AccessControlSections.All);

    [Fact]
    public void InheritedAcesWithoutFlag_GetAutoInherited()
    {
        var b = Bytes("O:BAG:SYD:(A;OICIID;KA;;;SY)(A;OICIID;KR;;;RC)");
        Assert.True(SecurityDescriptorBytes.MarkDaclAutoInherited(b));
        Assert.Equal("O:BAG:SYD:AI(A;OICIID;KA;;;SY)(A;OICIID;KR;;;RC)", Sddl(b));
    }

    [Fact]
    public void ExplicitOnlyOrAlreadyFlagged_Unchanged()
    {
        var explicitOnly = Bytes("D:(A;OICI;KA;;;SY)");
        Assert.False(SecurityDescriptorBytes.MarkDaclAutoInherited(explicitOnly));
        Assert.Equal("D:(A;OICI;KA;;;SY)", Sddl(explicitOnly));

        var flagged = Bytes("D:AI(D;;KR;;;WD)(A;OICIID;KA;;;SY)");
        Assert.False(SecurityDescriptorBytes.MarkDaclAutoInherited(flagged));

        var protectedDacl = Bytes("D:P(A;OICI;KA;;;SY)");
        Assert.False(SecurityDescriptorBytes.MarkDaclAutoInherited(protectedDacl));
    }

    [Fact]
    public void InheritedAceAfterExplicit_IsFound()
    {
        var b = Bytes("D:(D;;KR;;;WD)(A;OICIID;KA;;;SY)");
        Assert.True(SecurityDescriptorBytes.MarkDaclAutoInherited(b));
        Assert.StartsWith("D:AI", Sddl(b));
    }

    [Fact]
    public void NoDaclOrTruncated_Unchanged()
    {
        Assert.False(SecurityDescriptorBytes.MarkDaclAutoInherited(Bytes("O:BA")));
        Assert.False(SecurityDescriptorBytes.MarkDaclAutoInherited(new byte[8]));
    }
}
