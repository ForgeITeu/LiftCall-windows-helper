using System.Drawing;
using System.Windows.Forms;

namespace LiftCall.WindowsHelper;

internal sealed class PwaAlertDialog : Form
{
    private readonly Label _heading;
    private readonly Label _message;
    private readonly Button _primaryButton;
    private readonly Button _closeButton;

    public PwaAlertDialog(TrayAlertKind alert)
    {
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(620, 280);
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        TopMost = true;
        Padding = new Padding(24);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 8));

        _heading = new Label
        {
            AutoSize = true,
            Font = new Font((SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont).FontFamily, 18f, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 12),
        };
        _message = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Font = new Font((SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont).FontFamily, 13f, FontStyle.Regular),
            Margin = new Padding(0, 0, 0, 12),
        };
        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0),
        };
        _closeButton = new Button
        {
            AutoSize = true,
            Text = "Zamknij",
            Font = new Font((SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont).FontFamily, 10f, FontStyle.Regular),
            DialogResult = DialogResult.Cancel,
            MinimumSize = new Size(95, 38),
            Margin = new Padding(8, 0, 0, 0),
        };
        _primaryButton = new Button
        {
            AutoSize = true,
            Font = new Font((SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont).FontFamily, 10f, FontStyle.Bold),
            DialogResult = DialogResult.OK,
            MinimumSize = new Size(180, 38),
            Margin = new Padding(8, 0, 0, 0),
        };
        _closeButton.Click += (_, _) => Hide();
        _primaryButton.Click += (_, _) =>
        {
            Hide();
            PrimaryActionRequested?.Invoke(Alert);
        };
        buttons.Controls.Add(_closeButton);
        buttons.Controls.Add(_primaryButton);

        layout.Controls.Add(_heading, 0, 0);
        layout.Controls.Add(_message, 0, 1);
        layout.Controls.Add(buttons, 0, 2);
        Controls.Add(layout);
        AcceptButton = _primaryButton;
        CancelButton = _closeButton;
        UpdateAlert(alert);
    }

    public TrayAlertKind Alert { get; private set; }

    public event Action<TrayAlertKind>? PrimaryActionRequested;

    public void UpdateAlert(TrayAlertKind alert)
    {
        Alert = alert;
        var content = alert switch
        {
            TrayAlertKind.PwaClosed => (
                "LiftCall jest zamknięte",
                "LiftCall nie może odbierać powiadomień o połączeniach.",
                "OTWÓRZ PONOWNIE",
                Color.Firebrick),
            TrayAlertKind.LoggedOut => (
                "Użytkownik został wylogowany",
                "Zaloguj się ponownie, aby odbierać powiadomienia o połączeniach.",
                "POKAŻ LIFTCALL",
                Color.DarkGoldenrod),
            TrayAlertKind.HeartbeatInactive => (
                "LiftCall nie odpowiada",
                "Heartbeat LiftCall jest nieaktywny. Sprawdź, czy PWA działa.",
                "POKAŻ LIFTCALL",
                Color.DarkGoldenrod),
            TrayAlertKind.HelperError => (
                "Błąd LiftCall Helper",
                "Helper ma problem lokalny i nie może monitorować PWA.",
                "POKAŻ LIFTCALL",
                Color.Firebrick),
            _ => throw new ArgumentOutOfRangeException(nameof(alert), alert, null),
        };

        Text = content.Item1;
        _heading.Text = content.Item1;
        _message.Text = content.Item2;
        _primaryButton.Text = content.Item3;
        _heading.ForeColor = content.Item4;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        PrimaryActionRequested = null;
        base.OnFormClosed(e);
    }
}
