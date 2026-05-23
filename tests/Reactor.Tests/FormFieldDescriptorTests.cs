using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Data;
using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.Controls.Validation;
using static Microsoft.UI.Reactor.Controls.Validation.FormFieldDsl;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for the FormField overload that auto-wires from FieldDescriptor.
/// </summary>
public class FormFieldDescriptorTests
{
    private enum TestColor { Red, Green, Blue }

    [Fact]
    public void FormField_Renders_Label_From_DisplayName()
    {
        var fd = new FieldDescriptor
        {
            Name = "FirstName",
            DisplayName = "First Name",
            FieldType = typeof(string),
            GetValue = _ => "Alice",
        };

        var el = FormField(fd, "Alice", _ => { });
        Assert.Equal("First Name", el.Label);
    }

    [Fact]
    public void FormField_Falls_Back_To_Name_When_No_DisplayName()
    {
        var fd = new FieldDescriptor
        {
            Name = "Age",
            FieldType = typeof(int),
            GetValue = _ => 25,
        };

        var el = FormField(fd, 25, _ => { });
        Assert.Equal("Age", el.Label);
    }

    [Fact]
    public void FormField_Shows_Required_When_RequiredValidator_Present()
    {
        var fd = new FieldDescriptor
        {
            Name = "Email",
            FieldType = typeof(string),
            GetValue = _ => "",
            Validators = new IValidator[] { Validate.Required() },
        };

        var el = FormField(fd, "", _ => { });
        Assert.True(el.Required);
    }

    [Fact]
    public void FormField_Not_Required_Without_RequiredValidator()
    {
        var fd = new FieldDescriptor
        {
            Name = "Notes",
            FieldType = typeof(string),
            GetValue = _ => "",
        };

        var el = FormField(fd, "", _ => { });
        Assert.False(el.Required);
    }

    [Fact]
    public void FormField_Renders_Resolved_Editor_For_String()
    {
        var registry = new TypeRegistry();
        var fd = new FieldDescriptor
        {
            Name = "Name",
            FieldType = typeof(string),
            GetValue = _ => "test",
        };

        var el = FormField(fd, "test", _ => { }, registry);
        Assert.IsType<TextBoxElement>(el.Content);
    }

    [Fact]
    public void FormField_Renders_Resolved_Editor_For_Bool()
    {
        var registry = new TypeRegistry();
        var fd = new FieldDescriptor
        {
            Name = "Active",
            FieldType = typeof(bool),
            GetValue = _ => true,
        };

        var el = FormField(fd, true, _ => { }, registry);
        Assert.IsType<ToggleSwitchElement>(el.Content);
    }

    [Fact]
    public void FormField_Renders_Resolved_Editor_For_Enum()
    {
        var registry = new TypeRegistry();
        var fd = new FieldDescriptor
        {
            Name = "Color",
            FieldType = typeof(TestColor),
            GetValue = _ => TestColor.Red,
        };

        var el = FormField(fd, TestColor.Red, _ => { }, registry);
        Assert.IsType<ComboBoxElement>(el.Content);
    }

    [Fact]
    public void FormField_Uses_Description_From_FieldDescriptor()
    {
        var fd = new FieldDescriptor
        {
            Name = "Bio",
            FieldType = typeof(string),
            GetValue = _ => "",
            Description = "A short biography",
        };

        var el = FormField(fd, "", _ => { });
        Assert.Equal("A short biography", el.Description);
    }

    [Fact]
    public void FormField_Uses_Custom_Editor_From_FieldDescriptor()
    {
        var fd = new FieldDescriptor
        {
            Name = "Custom",
            FieldType = typeof(string),
            GetValue = _ => "hi",
            Editor = (val, onChange) => TextBlock($"Custom: {val}"),
        };

        var el = FormField(fd, "hi", _ => { });
        Assert.IsType<TextBlockElement>(el.Content);
    }

    [Fact]
    public void FormField_Sets_FieldName_From_Descriptor()
    {
        var fd = new FieldDescriptor
        {
            Name = "MyField",
            FieldType = typeof(string),
            GetValue = _ => "",
        };

        var el = FormField(fd, "", _ => { });
        Assert.Equal("MyField", el.FieldName);
    }

    [Fact]
    public void Validation_Fires_On_Value_Change()
    {
        var required = Validate.Required("Name is required");
        var minLen = Validate.MinLength(3, "At least 3 characters");

        var fd = new FieldDescriptor
        {
            Name = "Name",
            FieldType = typeof(string),
            GetValue = _ => "",
            Validators = new IValidator[] { required, minLen },
        };

        // Simulate value change with empty string → required fires
        var msg1 = fd.Validators[0].Validate("", "Name");
        Assert.NotNull(msg1);
        Assert.Contains("required", msg1!.Text, StringComparison.OrdinalIgnoreCase);

        // Simulate value change with short string → minLength fires
        var msg2 = fd.Validators[1].Validate("ab", "Name");
        Assert.NotNull(msg2);
        Assert.Contains("3", msg2!.Text);

        // Simulate value change with valid string → both pass
        var msg3 = fd.Validators[0].Validate("Alice", "Name");
        Assert.Null(msg3);

        var msg4 = fd.Validators[1].Validate("Alice", "Name");
        Assert.Null(msg4);
    }
}
