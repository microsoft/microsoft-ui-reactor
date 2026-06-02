using Microsoft.UI.Reactor.Controls;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

public class MaskEngineTests
{
    // ════════════════════════════════════════════════════════════════
    //  Token types
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Required_Digit_Accepts_Digits()
    {
        var engine = new MaskEngine("000");
        Assert.Equal("123", engine.Apply("123"));
    }

    [Fact]
    public void Required_Digit_Rejects_Letters()
    {
        var engine = new MaskEngine("000");
        var result = engine.Apply("a1b");
        // 'a' is invalid for required digit → placeholder, '1' fills, 'b' → placeholder
        Assert.Equal("_1_", result);
    }

    [Fact]
    public void Optional_Digit_Accepts_Digits()
    {
        var engine = new MaskEngine("999");
        Assert.Equal("12_", engine.Apply("12"));
    }

    [Fact]
    public void Optional_Digit_Leaves_Placeholder_For_Missing()
    {
        var engine = new MaskEngine("999");
        Assert.Equal("1__", engine.Apply("1"));
    }

    [Fact]
    public void Required_Letter_Accepts_Letters()
    {
        var engine = new MaskEngine("AAA");
        Assert.Equal("abc", engine.Apply("abc"));
    }

    [Fact]
    public void Required_Letter_Rejects_Digits()
    {
        var engine = new MaskEngine("AAA");
        Assert.Equal("_b_", engine.Apply("1b2"));
    }

    [Fact]
    public void Optional_Letter_Accepts_Letters()
    {
        var engine = new MaskEngine("aaa");
        Assert.Equal("ab_", engine.Apply("ab"));
    }

    [Fact]
    public void Required_Alphanumeric_Accepts_Both()
    {
        var engine = new MaskEngine("***");
        Assert.Equal("a1b", engine.Apply("a1b"));
    }

    // ════════════════════════════════════════════════════════════════
    //  Literal auto-insertion
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Literals_Auto_Inserted()
    {
        var engine = new MaskEngine("000-0000");
        Assert.Equal("123-4567", engine.Apply("1234567"));
    }

    [Fact]
    public void Literals_In_Phone_Format()
    {
        var engine = new MaskEngine("(000) 000-0000");
        Assert.Equal("(555) 123-4567", engine.Apply("5551234567"));
    }

    [Fact]
    public void Literals_Skipped_In_Input_If_Match()
    {
        var engine = new MaskEngine("000-000");
        // Input already has the dash — engine should handle it
        Assert.Equal("123-456", engine.Apply("123-456"));
    }

    // ════════════════════════════════════════════════════════════════
    //  Raw value extraction
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void GetRawValue_Strips_Literals()
    {
        var engine = new MaskEngine("(000) 000-0000");
        Assert.Equal("5551234567", engine.GetRawValue("(555) 123-4567"));
    }

    [Fact]
    public void GetRawValue_Strips_Placeholders()
    {
        var engine = new MaskEngine("000-0000");
        Assert.Equal("123", engine.GetRawValue("123-____"));
    }

    [Fact]
    public void GetRawValue_Empty_For_All_Placeholders()
    {
        var engine = new MaskEngine("000");
        Assert.Equal("", engine.GetRawValue("___"));
    }

    // ════════════════════════════════════════════════════════════════
    //  Completion detection
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void IsComplete_True_When_All_Required_Filled()
    {
        var engine = new MaskEngine("000-0000");
        Assert.True(engine.IsComplete("123-4567"));
    }

    [Fact]
    public void IsComplete_False_When_Placeholders_Remain()
    {
        var engine = new MaskEngine("000-0000");
        Assert.False(engine.IsComplete("123-45__"));
    }

    [Fact]
    public void IsComplete_True_With_Optional_Unfilled()
    {
        var engine = new MaskEngine("009");
        Assert.True(engine.IsComplete("12_"));
    }

    // ════════════════════════════════════════════════════════════════
    //  Cursor navigation helpers
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void IsLiteral_True_For_Literal_Position()
    {
        var engine = new MaskEngine("000-0000");
        Assert.True(engine.IsLiteral(3)); // dash position
    }

    [Fact]
    public void IsLiteral_False_For_Input_Position()
    {
        var engine = new MaskEngine("000-0000");
        Assert.False(engine.IsLiteral(0));
    }

    [Fact]
    public void NextInputPosition_Skips_Literals()
    {
        var engine = new MaskEngine("000-0000");
        Assert.Equal(4, engine.NextInputPosition(3)); // skip dash at 3
    }

    [Fact]
    public void PreviousInputPosition_Skips_Literals()
    {
        var engine = new MaskEngine("000-0000");
        Assert.Equal(2, engine.PreviousInputPosition(3)); // skip dash at 3
    }

    // ════════════════════════════════════════════════════════════════
    //  Mask presets
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Preset_PhoneUS()
    {
        var engine = new MaskEngine(MaskPreset.PhoneUS);
        Assert.Equal("(555) 123-4567", engine.Apply("5551234567"));
    }

    [Fact]
    public void Preset_SSN()
    {
        var engine = new MaskEngine(MaskPreset.SSN);
        Assert.Equal("123-45-6789", engine.Apply("123456789"));
    }

    [Fact]
    public void Preset_ZipCode()
    {
        var engine = new MaskEngine(MaskPreset.ZipCode);
        Assert.Equal("90210", engine.Apply("90210"));
    }

    [Fact]
    public void Preset_ZipCodePlus4()
    {
        var engine = new MaskEngine(MaskPreset.ZipCodePlus4);
        Assert.Equal("90210-1234", engine.Apply("902101234"));
    }

    [Fact]
    public void Preset_CreditCard()
    {
        var engine = new MaskEngine(MaskPreset.CreditCard);
        Assert.Equal("1234 5678 9012 3456", engine.Apply("1234567890123456"));
    }

    [Fact]
    public void Preset_Date()
    {
        var engine = new MaskEngine(MaskPreset.Date);
        Assert.Equal("01/15/2024", engine.Apply("01152024"));
    }

    [Fact]
    public void Preset_Time()
    {
        var engine = new MaskEngine(MaskPreset.Time);
        Assert.Equal("14:30", engine.Apply("1430"));
    }

    [Fact]
    public void Preset_IPv4()
    {
        var engine = new MaskEngine(MaskPreset.IPv4);
        var result = engine.Apply("192168001001");
        Assert.Equal("192.168.001.001", result);
    }

    // ════════════════════════════════════════════════════════════════
    //  MaskedTextBoxElement
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void MaskedTextBox_RawValue()
    {
        var el = MaskedTextBoxDsl.MaskedTextBox(
            "(555) 123-4567",
            mask: MaskPreset.PhoneUS);
        Assert.Equal("5551234567", el.RawValue);
    }

    [Fact]
    public void MaskedTextBox_IsComplete_True()
    {
        var el = MaskedTextBoxDsl.MaskedTextBox(
            "(555) 123-4567",
            mask: MaskPreset.PhoneUS);
        Assert.True(el.IsComplete);
    }

    [Fact]
    public void MaskedTextBox_IsComplete_False()
    {
        var el = MaskedTextBoxDsl.MaskedTextBox(
            "(555) 123-____",
            mask: MaskPreset.PhoneUS);
        Assert.False(el.IsComplete);
    }

    [Fact]
    public void MaskedTextBox_No_Mask_RawValue_Is_Value()
    {
        var el = MaskedTextBoxDsl.MaskedTextBox("hello");
        Assert.Equal("hello", el.RawValue);
    }

    [Fact]
    public void MaskedTextBox_No_Mask_IsComplete_True()
    {
        var el = MaskedTextBoxDsl.MaskedTextBox("hello");
        Assert.True(el.IsComplete);
    }

    [Fact]
    public void MaskedTextField_ObsoleteShim_ReturnsMaskedTextBoxElement()
    {
        // Guards the one-release [Obsolete] compatibility shim for issue #389.
#pragma warning disable CS0618
        var el = MaskedTextFieldDsl.MaskedTextField(
            "(555) 123-4567",
            mask: MaskPreset.PhoneUS);
#pragma warning restore CS0618
        Assert.IsType<MaskedTextBoxElement>(el);
        Assert.Equal("5551234567", el.RawValue);
    }

    // ════════════════════════════════════════════════════════════════
    //  Edge cases
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Empty_Input_Produces_All_Placeholders()
    {
        var engine = new MaskEngine("000");
        Assert.Equal("___", engine.Apply(""));
    }

    [Fact]
    public void Extra_Input_Ignored()
    {
        var engine = new MaskEngine("000");
        Assert.Equal("123", engine.Apply("123456"));
    }

    [Fact]
    public void Custom_Placeholder()
    {
        var engine = new MaskEngine("000");
        Assert.Equal("1##", engine.Apply("1", '#'));
    }
}
