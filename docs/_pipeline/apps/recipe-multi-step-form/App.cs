using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<MultiStepFormApp>("Multi-Step Form Recipe", width: 420, height: 460
#if DEBUG
    , preview: true
#endif
);

class MultiStepFormApp : Component
{
    public override Element Render() => Component<Wizard>();
}

class Wizard : Component
{
    public override Element Render()
    {
        // <snippet:state>
        // One UseState for the step index plus one per field. The fields
        // are declared at the top of Render so they survive every step
        // transition — Reactor never unmounts Wizard, so the hooks keep
        // their values as the user moves forward and back.
        var (step, setStep) = UseState(0);
        var (name, setName) = UseState("");
        var (email, setEmail) = UseState("");
        var (role, setRole) = UseState(-1);
        var (newsletter, setNewsletter) = UseState(false);
        // </snippet:state>

        // <snippet:validate>
        // canAdvance is a pure function of the step index plus the
        // current field values. The Next button binds to it directly;
        // no debounce, no separate validation pass.
        bool canAdvance = step switch
        {
            0 => name.Trim().Length >= 2
                 && email.Contains('@') && email.Contains('.'),
            1 => role >= 0,
            _ => true,
        };
        // </snippet:validate>

        // <snippet:step1>
        Element StepAccount() => VStack(10,
            SubHeading("Step 1 of 3 — Account"),
            TextField(name, setName, placeholder: "Your name",
                header: "Name").Width(340),
            TextField(email, setEmail, placeholder: "you@example.com",
                header: "Email").Width(340)
        );
        // </snippet:step1>

        // <snippet:step2>
        Element StepPreferences() => VStack(10,
            SubHeading("Step 2 of 3 — Preferences"),
            TextBlock("Pick the role that fits best.").Opacity(0.7),
            RadioButtons(new[] { "Engineer", "Designer", "Manager" },
                role, setRole),
            CheckBox(newsletter, setNewsletter,
                label: "Send me product updates")
        );
        // </snippet:step2>

        Element StepSummary() => VStack(8,
            SubHeading("Step 3 of 3 — Confirm"),
            TextBlock($"Name: {name}"),
            TextBlock($"Email: {email}"),
            TextBlock($"Role: {(role < 0 ? "—" : new[] { "Engineer", "Designer", "Manager" }[role])}"),
            TextBlock($"Newsletter: {(newsletter ? "yes" : "no")}")
        );

        // <snippet:render>
        // The orchestrator picks which step renders, then lays the
        // Back / Next buttons under it. Back is disabled on step 0;
        // Next is disabled until canAdvance; the last step swaps the
        // Next label to "Submit".
        Element body = step switch
        {
            0 => StepAccount(),
            1 => StepPreferences(),
            _ => StepSummary(),
        };

        return VStack(16,
            Heading("Create your account"),
            body,
            HStack(8,
                Button("Back", () => setStep(step - 1)).IsEnabled(step != 0),
                Button(step == 2 ? "Submit" : "Next",
                    () => setStep(step + 1)).IsEnabled(canAdvance && step != 2)
            )
        ).Padding(20).Width(380);
        // </snippet:render>
    }
}
