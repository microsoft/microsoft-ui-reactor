using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Controls.Validation;
using System.Threading;
using static Microsoft.UI.Reactor.Controls.Validation.FormFieldDsl;
using Microsoft.UI.Reactor.Controls;
using static Microsoft.UI.Reactor.Factories;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

ReactorApp.Run<FormsApp>("Forms");

// <snippet:controlled-input>
class ControlledInputDemo : Component
{
    public override Element Render()
    {
        var (name, setName) = UseState("");

        return VStack(12,
            SubHeading("Controlled Input"),
            TextBox(name, setName, placeholderText: "Type your name",
                header: "Name"),
            TextBlock($"You typed: {name}").Opacity(0.6)
        ).Padding(24);
    }
}
// </snippet:controlled-input>

// <snippet:input-types>
class InputTypesDemo : Component
{
    public override Element Render()
    {
        var (text, setText) = UseState("");
        var (password, setPassword) = UseState("");
        var (volume, setVolume) = UseState(50.0);
        var (count, setCount) = UseState(1.0);
        var (agree, setAgree) = UseState(false);
        var (notify, setNotify) = UseState(true);
        var (role, setRole) = UseState(0);
        var (priority, setPriority) = UseState(0);

        return VStack(12,
            TextBox(text, setText, placeholderText: "Email",
                header: "Email"),
            PasswordBox(password, setPassword,
                placeholderText: "Enter password")
                .Header("Password"),
            Slider(volume, 0, 100, setVolume).Header("Volume"),
            NumberBox(count, setCount, header: "Quantity"),
            CheckBox(agree, setAgree, label: "I agree to the terms"),
            ToggleSwitch(notify, setNotify,
                header: "Notifications"),
            ComboBox(["Admin", "Editor", "Viewer"],
                role, setRole).Header("Role"),
            (RadioButtons(["Low", "Medium", "High"],
                priority, setPriority) with { Header = "Priority" })
        ).Padding(24);
    }
}
// </snippet:input-types>

// <snippet:validation>
class ValidationDemo : Component
{
    public override Element Render()
    {
        var (email, setEmail) = UseState("");
        var (age, setAge) = UseState(0.0);
        var (showErrors, setShowErrors) = UseState(false);

        var emailValid = email.Contains('@') && email.Contains('.');
        var ageValid = age >= 18 && age <= 120;
        var formValid = emailValid && ageValid
            && !string.IsNullOrWhiteSpace(email);

        return VStack(12,
            SubHeading("Simple Validation"),
            TextBox(email, setEmail, placeholderText: "user@example.com",
                header: "Email"),
            When(!string.IsNullOrEmpty(email) && !emailValid, () =>
                TextBlock("Enter a valid email address")
                    .Foreground(Theme.SystemCritical).FontSize(12)),
            NumberBox(age, setAge, header: "Age"),
            When((showErrors || age > 0) && !ageValid, () =>
                TextBlock("Age must be between 18 and 120")
                    .Foreground(Theme.SystemCritical).FontSize(12)),
            Button("Submit", () =>
            {
                setShowErrors(true);
                if (!formValid) return;
                // submit...
            }).Margin(0, 8, 0, 0)
        ).Padding(24);
    }
}
// </snippet:validation>

// <snippet:keep-submit-reachable>
class KeepSubmitReachableDemo : Component
{
    public override Element Render()
    {
        var (email, setEmail) = UseState("");
        var (age, setAge) = UseState(0.0);

        var emailValid = email.Contains('@') && email.Contains('.');
        var ageValid = age >= 18 && age <= 120;
        var formValid = emailValid && ageValid;

        return VStack(12,
            SubHeading("Keeping Submit Reachable"),
            TextBox(email, setEmail, header: "Email",
                placeholderText: "user@example.com"),

            // .Immediate() switches NumberBox from commit-on-blur to
            // commit-on-keystroke, so validation reacts as the user types.
            NumberBox(age, setAge, header: "Age").Immediate(),

            // .IsDisabledFocusable() keeps the button tab-reachable and
            // visually dimmed while preventing invocation. Pattern mirrors
            // Fluent UI's `disabledFocusable` and ARIA `aria-disabled`.
            Button("Submit", () => { /* submit */ })
                .IsDisabledFocusable(!formValid)
                .Margin(0, 8, 0, 0)
        ).Padding(24);
    }
}
// </snippet:keep-submit-reachable>

// <snippet:validation-context>
class ValidationContextDemo : Component
{
    public override Element Render()
    {
        var ctx = this.UseValidationContext();
        var (email, setEmail) = UseState("");
        var (password, setPassword) = UseState("");
        var (submitted, setSubmitted) = UseState(false);

        return VStack(12,
            SubHeading("Validation Context"),
            TextBox(email, v => { setEmail(v); ctx.NotifyValueChanged("email", v); },
                placeholderText: "user@example.com", header: "Email")
                .Validate("email", email,
                    Validate.Required(),
                    Validate.Email()),
            When(ctx.IsTouched("email") && ctx.HasError("email"), () =>
                TextBlock(ctx.GetMessages("email").First().Text)
                    .Foreground(Theme.SystemCritical).FontSize(12)),
            PasswordBox(password, v => { setPassword(v); ctx.NotifyValueChanged("password", v); },
                placeholderText: "Min 8 characters")
                .Header("Password")
                .Validate("password", password,
                    Validate.Required(),
                    Validate.MinLength(8)),
            When(ctx.IsTouched("password") && ctx.HasError("password"), () =>
                TextBlock(ctx.GetMessages("password").First().Text)
                    .Foreground(Theme.SystemCritical).FontSize(12)),
            Button("Register", () =>
            {
                ctx.MarkAllTouched();
                if (ctx.IsValid()) setSubmitted(true);
            }).IsEnabled(!submitted),
            When(submitted, () =>
                TextBlock("Registration successful!")
                    .Foreground(Theme.SystemSuccess).SemiBold())
        ).Padding(24);
    }
}
// </snippet:validation-context>

// <snippet:async-validation>
class AsyncValidationDemo : Component
{
    static async Task<bool> IsEmailFree(string value)
    {
        await Task.Delay(300);
        return value != "taken@example.com";
    }

    public override Element Render()
    {
        var ctx = this.UseValidationContext();
        var (email, setEmail) = UseState("");

        // Async validators are never run for you: a render pass is synchronous, so
        // there is nowhere for it to await them. Drive them from an effect, through
        // ValidateFieldAsync, whose generation guard discards a result the user has
        // already typed past.
        UseEffect(() =>
        {
            var cts = new CancellationTokenSource();
            if (email.Length > 0)
            {
                // Observed, not discarded. `_ = SomeTask()` drops the returned task on
                // the floor, so a uniqueness check that fails for a real reason — the
                // network is down, the service 500s — vanishes silently and the field
                // just never gets a verdict. Await it inside a local async helper and
                // handle the two outcomes separately.
                _ = RunCheckAsync(cts.Token);
            }
            // Cancel on cleanup so the superseded check cannot install its verdict, and
            // dispose the source with it — the effect allocates a fresh one per run.
            // Note this does not interrupt work already in flight: `Validate.MustAsync`
            // awaits your predicate without a token and only observes cancellation once
            // it returns. Take a `CancellationToken` in the predicate itself if you need
            // the request abandoned rather than its result discarded.
            return () =>
            {
                try { cts.Cancel(); }
                finally { cts.Dispose(); }
            };

            async Task RunCheckAsync(CancellationToken token)
            {
                try
                {
                    await ValidationReconciler.ValidateFieldAsync(
                        ctx, "email", email,
                        [Validate.MustAsync<string>(IsEmailFree, "Email is taken")],
                        token);
                }
                catch (OperationCanceledException)
                {
                    // Expected: the user typed again and this check was superseded.
                }
                catch (Exception ex)
                    when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    // Anything else is a real failure — the network is down, the service
                    // erroring. Surface it however your app reports background faults;
                    // here, as a message on the field so it cannot pass silently.
                    //
                    // Deliberately broad rather than a list of expected exception types:
                    // the predicate is yours, so the framework cannot know what it can
                    // throw, and enumerating types means the one you forgot disappears.
                    // The filter excludes only the two that must never be caught. This
                    // is the same shape the framework itself uses for app callbacks
                    // (see CompositeLifecycle.RunAsyncRuleAsync).
                    ctx.AddExternal("email", $"Could not check availability: {ex.Message}");
                }
            }
        }, email);

        return VStack(12,
            SubHeading("Async Validation"),
            TextBox(email, v => { setEmail(v); ctx.NotifyValueChanged("email", v); },
                placeholderText: "user@example.com", header: "Email"),
            When(ctx.HasError("email"), () =>
                TextBlock(ctx.GetMessages("email").First().Text)
                    .Foreground(Theme.SystemCritical).FontSize(12))
        ).Padding(24);
    }
}
// </snippet:async-validation>

// <snippet:form-field>
class FormFieldDemo : Component
{
    public override Element Render()
    {
        var ctx = this.UseValidationContext();
        var (name, setName) = UseState("");
        var (email, setEmail) = UseState("");

        return VStack(12,
            SubHeading("FormField Helper"),
            FormField(
                TextBox(name, v => { setName(v); ctx.NotifyValueChanged("name", v); })
                    .AutomationName("Full Name")
                    .Validate("name", name, Validate.Required()),
                label: "Full Name",
                required: true,
                description: "As it appears on your ID"),
            FormField(
                TextBox(email, v => { setEmail(v); ctx.NotifyValueChanged("email", v); })
                    .AutomationName("Email Address")
                    .Validate("email", email,
                        Validate.Required(), Validate.Email()),
                label: "Email Address",
                required: true)
        ).Padding(24);
    }
}
// </snippet:form-field>

// <snippet:masked-input>
class MaskedInputDemo : Component
{
    public override Element Render()
    {
        var phoneMask = UseMemo(() => new MaskEngine(MaskPreset.PhoneUS));
        var dateMask = UseMemo(() => new MaskEngine(MaskPreset.Date));
        var (phone, setPhone) = UseState("");
        var (date, setDate) = UseState("");

        return VStack(12,
            SubHeading("Masked Input"),
            TextBox(phoneMask.Apply(phone), v => setPhone(phoneMask.GetRawValue(v)),
                placeholderText: "(___) ___-____", header: "Phone"),
            TextBlock($"Raw: {phone}").FontSize(12).Opacity(0.6),
            TextBox(dateMask.Apply(date), v => setDate(dateMask.GetRawValue(v)),
                placeholderText: "__/__/____", header: "Date"),
            TextBlock($"Raw: {date}").FontSize(12).Opacity(0.6)
        ).Padding(24);
    }
}
// </snippet:masked-input>

// <snippet:input-formatters>
class InputFormattersDemo : Component
{
    public override Element Render()
    {
        var (currency, setCurrency) = UseState("");
        var (upper, setUpper) = UseState("");

        var currencyFmt = UseMemo(() => InputFormatter.Currency());
        var upperFmt = UseMemo(() => InputFormatter.UpperCase);

        return VStack(12,
            SubHeading("Input Formatters"),
            TextBox(currencyFmt.Format(currency, 0).Output,
                v => setCurrency(currencyFmt.Parse(v)),
                placeholderText: "$0.00", header: "Amount"),
            TextBox(upperFmt.Format(upper, 0).Output,
                v => setUpper(upperFmt.Parse(v)),
                placeholderText: "UPPERCASE", header: "Code")
        ).Padding(24);
    }
}
// </snippet:input-formatters>

// <snippet:textbox-config>
class TextBoxConfigDemo : Component
{
    public override Element Render()
    {
        var (qty, setQty) = UseState("");
        var (email, setEmail) = UseState("");
        var (url, setUrl) = UseState("");
        var (phone, setPhone) = UseState("");
        var (search, setSearch) = UseState("");
        var (note, setNote) = UseState("");

        return VStack(12,
            TextBox(qty, setQty, header: "Quantity")
                .NumericInput(),
            TextBox(email, setEmail, header: "Email")
                .EmailInput(),
            TextBox(url, setUrl, header: "URL")
                .UrlInput(),
            TextBox(phone, setPhone, header: "Phone")
                .PhoneInput(),
            TextBox(search, setSearch, placeholderText: "Search…",
                header: "Search")
                .SearchInput(),
            TextBox(note, setNote, header: "Reference code")
                .MaxLength(8)
                .CharacterCasing(CharacterCasing.Upper)
                .TextAlignment(TextAlignment.Center)
                .IsSpellCheckEnabled(false)
                .Description("Eight characters, automatically uppercased.")
        ).Padding(24);
    }
}
// </snippet:textbox-config>

// <snippet:auto-suggest>
class AutoSuggestDemo : Component
{
    static readonly string[] Catalog =
    [
        "Aardvark", "Albatross", "Antelope", "Badger",
        "Beaver", "Buffalo", "Camel", "Capybara"
    ];

    public override Element Render()
    {
        var (text, setText) = UseState("");

        var matches = string.IsNullOrEmpty(text)
            ? Array.Empty<string>()
            : Catalog.Where(c =>
                c.StartsWith(text, StringComparison.OrdinalIgnoreCase))
                .ToArray();

        return VStack(8,
            SubHeading("AutoSuggestBox"),
            AutoSuggestBox(text, setText,
                onQuerySubmitted: q => setText(q))
                .Header("Animal")
                .QueryIcon(SymbolIcon("Find"))
                .Width(280),
            // Suggestion list — bind to AutoSuggestBox.ItemsSource via .Set
            // when you need the in-control dropdown; the inline list below
            // is a custom presentation that gives full styling control.
            When(matches.Length > 0, () =>
                VStack(2,
                    ForEach(matches, m =>
                        TextBlock(m).Padding(8, 4).WithKey(m))
                ).Background(Theme.CardBackground).Width(280))
        ).Padding(24);
    }
}
// </snippet:auto-suggest>

// <snippet:date-picker>
class DatePickerDemo : Component
{
    public override Element Render()
    {
        var (date, setDate) = UseState(DateTimeOffset.Now);
        var (optionalDate, setOptionalDate) = UseState<DateTimeOffset?>(null);

        return VStack(8,
            SubHeading("DatePicker — always-set value"),
            DatePicker(date, setDate)
                .DayFormat("{day.integer(2)}")
                .MonthFormat("{month.abbreviated}")
                .YearFormat("{year.full}"),
            TextBlock($"Selected: {date:yyyy-MM-dd}").Opacity(0.6),
            SubHeading("CalendarDatePicker — nullable, popup calendar"),
            CalendarDatePicker(optionalDate, setOptionalDate)
                .DateFormat("{month.abbreviated} {day.integer(2)}, {year.full}")
                .IsTodayHighlighted(),
            TextBlock(optionalDate is null
                ? "No date selected."
                : $"Selected: {optionalDate:yyyy-MM-dd}").Opacity(0.6)
        ).Padding(24);
    }
}
// </snippet:date-picker>

// <snippet:time-picker>
class TimePickerDemo : Component
{
    public override Element Render()
    {
        var (time, setTime) = UseState(TimeSpan.FromHours(9));

        return VStack(8,
            SubHeading("TimePicker"),
            TimePicker(time, setTime),
            TextBlock($"Selected: {time:hh\\:mm}").Opacity(0.6)
        ).Padding(24);
    }
}
// </snippet:time-picker>

// <snippet:calendar-view>
class CalendarViewDemo : Component
{
    public override Element Render()
    {
        var (dates, setDates) = UseState<IReadOnlyList<DateTimeOffset>>(
            Array.Empty<DateTimeOffset>());

        return VStack(8,
            SubHeading("CalendarView — month grid"),
            CalendarView()
                .MinDate(DateTimeOffset.Now.AddYears(-1))
                .MaxDate(DateTimeOffset.Now.AddYears(1))
                .NumberOfWeeksInView(6)
                .SelectedDatesChanged(setDates)
                .SelectedDates(dates),
            TextBlock($"{dates.Count} day(s) selected").Opacity(0.6)
        ).Padding(24);
    }
}
// </snippet:calendar-view>

// <snippet:color-picker>
class ColorPickerDemo : Component
{
    public override Element Render()
    {
        var (color, setColor) = UseState(
            global::Windows.UI.Color.FromArgb(255, 0, 120, 215));

        return VStack(8,
            SubHeading("ColorPicker"),
            ColorPicker(color, setColor)
                .AlphaEnabled()
                .HexInputVisible(true)
                .ColorSpectrumShape(
                    Microsoft.UI.Xaml.Controls.ColorSpectrumShape.Ring),
            TextBlock($"Selected: #{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}")
                .FontSize(12)
                .Foreground(Theme.SecondaryText)
        ).Padding(24);
    }
}
// </snippet:color-picker>

class FormsApp : Component
{
    public override Element Render()
    {
        return ScrollView(
            VStack(24,
                Heading("Forms and Input"),
                Component<ControlledInputDemo>(),
                Component<InputTypesDemo>(),
                Component<TextBoxConfigDemo>(),
                Component<ValidationDemo>(),
                Component<KeepSubmitReachableDemo>(),
                Component<ValidationContextDemo>(),
                Component<AsyncValidationDemo>(),
                Component<FormFieldDemo>(),
                Component<MaskedInputDemo>(),
                Component<InputFormattersDemo>(),
                Component<AutoSuggestDemo>(),
                Component<DatePickerDemo>(),
                Component<TimePickerDemo>(),
                Component<CalendarViewDemo>(),
                Component<ColorPickerDemo>()
            ).Padding(24)
        );
    }
}
