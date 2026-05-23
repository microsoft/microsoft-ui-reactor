using Microsoft.UI.Reactor.Interop.WinForms;
using SWF = System.Windows.Forms;

namespace Microsoft.UI.Reactor.WinFormsTests.Host;

/// <summary>
/// Test form for WinForms interop E2E tests.
///
/// Layout:
///   Left panel (WinForms):  WF_TextBox1, WF_Button1, WF_TextBox2
///   Right panel (Island):   Reactor_TextBox1, Reactor_Button1, Reactor_TextBox2
///   Bottom bar (WinForms):  WF_TextBox3 — Tab stop AFTER the island
///
/// Tab order is controlled by TabIndex on the container panels:
///   leftPanel (TabIndex=0) -> island (TabIndex=1) -> bottomBar (TabIndex=2)
///
/// Expected forward Tab order:
///   WF_TextBox1 -> WF_Button1 -> WF_TextBox2 -> [Island] -> Reactor_TextBox1 ->
///   Reactor_Button1 -> Reactor_TextBox2 -> [exit island] -> WF_TextBox3 -> wrap
/// </summary>
class TestForm : SWF.Form
{
    public TestForm()
    {
        Text = "WinForms Interop Test Host";
        ClientSize = new global::System.Drawing.Size(800, 500);
        BackColor = global::System.Drawing.Color.FromArgb(30, 30, 30);
        ForeColor = global::System.Drawing.Color.White;

        // ── Left panel: WinForms controls (TabIndex=0, first Tab group) ──
        var leftPanel = new SWF.Panel
        {
            Dock = SWF.DockStyle.Left,
            Width = 300,
            Padding = new SWF.Padding(12),
            BackColor = global::System.Drawing.Color.FromArgb(40, 40, 40),
            TabIndex = 0,
        };

        var titleLabel = new SWF.Label
        {
            Text = "WinForms Controls",
            Dock = SWF.DockStyle.Top,
            Height = 30,
            Font = new global::System.Drawing.Font("Segoe UI", 12f, global::System.Drawing.FontStyle.Bold),
            ForeColor = global::System.Drawing.Color.White,
        };
        titleLabel.AccessibleName = "WinForms title";

        var textBox1 = new SWF.TextBox
        {
            Dock = SWF.DockStyle.Top,
            TabIndex = 0,
            BackColor = global::System.Drawing.Color.FromArgb(50, 50, 50),
            ForeColor = global::System.Drawing.Color.White,
            Text = "",
        };
        textBox1.AccessibleName = "WinForms text box 1";
        textBox1.Name = "WF_TextBox1";

        var spacer1 = new SWF.Panel { Dock = SWF.DockStyle.Top, Height = 8 };

        var button1 = new SWF.Button
        {
            Dock = SWF.DockStyle.Top,
            TabIndex = 1,
            Height = 35,
            Text = "WinForms Button",
            BackColor = global::System.Drawing.Color.FromArgb(0, 120, 212),
            ForeColor = global::System.Drawing.Color.White,
            FlatStyle = SWF.FlatStyle.Flat,
        };
        button1.AccessibleName = "WinForms button";
        button1.Name = "WF_Button1";

        var spacer2 = new SWF.Panel { Dock = SWF.DockStyle.Top, Height = 8 };

        var textBox2 = new SWF.TextBox
        {
            Dock = SWF.DockStyle.Top,
            TabIndex = 2,
            BackColor = global::System.Drawing.Color.FromArgb(50, 50, 50),
            ForeColor = global::System.Drawing.Color.White,
            Text = "",
        };
        textBox2.AccessibleName = "WinForms text box 2";
        textBox2.Name = "WF_TextBox2";

        // Add controls in reverse order (Dock=Top stacks bottom-up)
        leftPanel.Controls.Add(textBox2);
        leftPanel.Controls.Add(spacer2);
        leftPanel.Controls.Add(button1);
        leftPanel.Controls.Add(spacer1);
        leftPanel.Controls.Add(textBox1);
        leftPanel.Controls.Add(titleLabel);

        // ── XAML Island (TabIndex=1, second Tab group) ──────────────
        var island = new XamlIslandControl
        {
            Dock = SWF.DockStyle.Fill,
            TabIndex = 1,
            ComponentType = typeof(TestReactorComponent),
        };
        island.AccessibleName = "XAML Island";
        island.Name = "WF_Island";

        // ── Bottom bar: WinForms control after island (TabIndex=2) ──
        var bottomBar = new SWF.Panel
        {
            Dock = SWF.DockStyle.Bottom,
            Height = 36,
            Padding = new SWF.Padding(8, 6, 8, 6),
            BackColor = global::System.Drawing.Color.FromArgb(45, 45, 45),
            TabIndex = 2,
        };

        var textBox3 = new SWF.TextBox
        {
            Dock = SWF.DockStyle.Fill,
            TabIndex = 0,
            BackColor = global::System.Drawing.Color.FromArgb(50, 50, 50),
            ForeColor = global::System.Drawing.Color.White,
            Text = "",
        };
        textBox3.AccessibleName = "WinForms text box 3";
        textBox3.Name = "WF_TextBox3";
        bottomBar.Controls.Add(textBox3);

        // ── Assemble form ───────────────────────────────────────────
        // Dock layout order: Bottom first, then Left, then Fill
        Controls.Add(island);
        Controls.Add(leftPanel);
        Controls.Add(bottomBar);
    }
}
