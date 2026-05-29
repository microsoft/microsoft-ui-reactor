// id: form-field-wrapper
// intent: FormField wrapper with label and required marker
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Controls.Validation;
using static Microsoft.UI.Reactor.Controls.Validation.FormFieldDsl;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("Form field wrapper", width: 500, height: 400);

class App : Component
{
    public override Element Render()
    {
        var ctx = this.UseValidationContext();
        var (fullName, setFullName) = UseState("");
        var (company, setCompany) = UseState("");
        var (role, setRole) = UseState("");

        return VStack(12,
            Heading("Profile details"),
            FormField(TextField(fullName, v => { setFullName(v); ctx.MarkTouched("fullName"); }, placeholder: "Ada Lovelace")
                    .Validate("fullName", fullName, Validate.Required("Full name is required")),
                label: "Full name", required: true, description: "Shown on receipts"),
            FormField(TextField(company, v => { setCompany(v); ctx.MarkTouched("company"); }, placeholder: "Contoso")
                    .Validate("company", company, Validate.MinLength(2, "Company name looks too short")),
                label: "Company", description: "Optional but useful"),
            FormField(TextField(role, v => { setRole(v); ctx.MarkTouched("role"); }, placeholder: "Product manager"),
                label: "Role", description: "No validation on this field"),
            Button("Show field state", () => ctx.MarkAllTouched()))
            .Padding(24);
    }
}
