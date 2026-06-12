// Networktool — Floating network monitor widget for Windows
// Author : Teffers
// Version: 1.08
// License: Private

using System.Drawing;
using System.Windows.Forms;

namespace Networktool;

public class PasswordDialog : Form
{
    public string Password { get; private set; } = "";
    private readonly TextBox _pwBox;

    public PasswordDialog(string ssid)
    {
        Text = "Connect to Network";
        BackColor = Color.FromArgb(20, 20, 20);
        ForeColor = Color.White;
        Font = new System.Drawing.Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(320, 160);

        Controls.Add(new Label
        {
            Text = $"Password for  \"{ssid}\":",
            ForeColor = Color.Silver,
            AutoSize = true,
            Location = new Point(14, 16),
            BackColor = Color.Transparent
        });

        _pwBox = new TextBox
        {
            UseSystemPasswordChar = true,
            Bounds = new Rectangle(14, 38, 276, 26),
            BackColor = Color.FromArgb(35, 35, 35),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        _pwBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) Accept(); };
        Controls.Add(_pwBox);

        var showChk = new CheckBox
        {
            Text = "Show password",
            Location = new Point(14, 70),
            AutoSize = true,
            ForeColor = Color.Gray,
            BackColor = Color.Transparent
        };
        showChk.CheckedChanged += (s, e) => _pwBox.UseSystemPasswordChar = !showChk.Checked;
        Controls.Add(showChk);

        var btnOk = new Button { Text = "Connect", Bounds = new Rectangle(120, 100, 80, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 55), ForeColor = Color.White };
        var btnCancel = new Button { Text = "Cancel", Bounds = new Rectangle(210, 100, 80, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(55, 55, 55), ForeColor = Color.White, DialogResult = DialogResult.Cancel };
        btnOk.FlatAppearance.BorderSize = 0;
        btnCancel.FlatAppearance.BorderSize = 0;
        btnOk.Click += (s, e) => Accept();
        Controls.AddRange(new Control[] { btnOk, btnCancel });
    }

    private void Accept()
    {
        Password = _pwBox.Text;
        DialogResult = DialogResult.OK;
        Close();
    }
}
