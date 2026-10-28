using System.Drawing;
using System.Windows.Forms;

namespace LiftCall.WindowsHelper;

internal sealed class HelperConfigurationForm : Form
{
    private readonly NumericUpDown _portInput = new();
    private readonly TextBox _windowTitleTextBox = new();
    private readonly TextBox _windowClassTextBox = new();
    private readonly TextBox _processNameTextBox = new();
    private readonly CheckBox _maximizeCheckBox = new();
    private readonly CheckBox _launchOnStartCheckBox = new();
    private readonly CheckBox _closeOnExitCheckBox = new();
    private readonly PathSelectorControl _shortcutSelector = new();
    private readonly ListBox _originsList = new();
    private readonly ErrorProvider _errors = new();

    public HelperConfig? Configuration { get; private set; }

    public HelperConfigurationForm(HelperConfig current)
    {
        Text = "Konfiguracja LiftCall";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        ClientSize = new Size(620, 650);
        BackColor = SystemColors.Control;

        _errors.BlinkStyle = ErrorBlinkStyle.NeverBlink;
        ConfigureInputs(current);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(12),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
        Controls.Add(root);

        var sections = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(0),
        };
        // The group border and its top/bottom padding consume space in addition
        // to the field row and NumericUpDown margins. Keep enough room so the
        // complete Port control remains visible at 100%, 125% and 150% DPI.
        sections.RowStyles.Add(new RowStyle(SizeType.Absolute, 70F));
        sections.RowStyles.Add(new RowStyle(SizeType.Absolute, 154F));
        sections.RowStyles.Add(new RowStyle(SizeType.Absolute, 145F));
        sections.RowStyles.Add(new RowStyle(SizeType.Absolute, 150F));
        root.Controls.Add(sections, 0, 0);

        sections.Controls.Add(CreateConnectionGroup(), 0, 0);
        sections.Controls.Add(CreateWindowGroup(), 0, 1);
        sections.Controls.Add(CreatePwaGroup(), 0, 2);
        sections.Controls.Add(CreateSecurityGroup(), 0, 3);

        var buttons = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 8, 0, 0),
        };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.Controls.Add(buttons, 0, 1);

        var resetButton = CreateButton("Przywróć domyślne", 132);
        resetButton.Click += (_, _) => ResetToDefaults();
        buttons.Controls.Add(resetButton, 0, 0);

        var rightButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0),
        };
        buttons.Controls.Add(rightButtons, 1, 0);

        var saveButton = CreateButton("Zapisz", 92);
        saveButton.Click += (_, _) => SaveConfiguration();
        var cancelButton = CreateButton("Anuluj", 92);
        cancelButton.DialogResult = DialogResult.Cancel;
        rightButtons.Controls.Add(saveButton);
        rightButtons.Controls.Add(cancelButton);
        AcceptButton = saveButton;
        CancelButton = cancelButton;

        _portInput.Validating += (_, _) => ValidateConfiguration(false);
        _windowTitleTextBox.Validating += (_, _) => ValidateConfiguration(false);
        _windowClassTextBox.Validating += (_, _) => ValidateConfiguration(false);
        _processNameTextBox.Validating += (_, _) => ValidateConfiguration(false);
        _shortcutSelector.PathChanged += (_, _) => ValidateConfiguration(false);
        _shortcutSelector.BrowseRequested += (_, _) => BrowseForShortcut();
    }

    private GroupBox CreateConnectionGroup()
    {
        var fields = CreateFieldTable(1);
        AddField(fields, 0, "Port", _portInput);
        return CreateGroupBox("Połączenie", fields);
    }

    private GroupBox CreateWindowGroup()
    {
        var fields = CreateFieldTable(4);
        AddField(fields, 0, "Fragment tytułu okna", _windowTitleTextBox);
        AddField(fields, 1, "Klasa okna", _windowClassTextBox);
        AddField(fields, 2, "Nazwa procesu", _processNameTextBox);

        _maximizeCheckBox.Text = "Maksymalizuj okno LiftCall";
        _maximizeCheckBox.AutoSize = true;
        _maximizeCheckBox.Margin = new Padding(4, 3, 4, 2);
        fields.Controls.Add(_maximizeCheckBox, 1, 3);
        fields.SetColumnSpan(_maximizeCheckBox, 2);
        return CreateGroupBox("Okno LiftCall", fields);
    }

    private GroupBox CreatePwaGroup()
    {
        var fields = CreateFieldTable(3);

        _launchOnStartCheckBox.Text = "Uruchamiaj PWA przy starcie helpera";
        _launchOnStartCheckBox.AutoSize = true;
        _launchOnStartCheckBox.Margin = new Padding(4, 2, 4, 1);
        fields.Controls.Add(_launchOnStartCheckBox, 1, 0);
        fields.SetColumnSpan(_launchOnStartCheckBox, 2);

        _closeOnExitCheckBox.Text = "Zamykaj PWA przy zamknięciu helpera";
        _closeOnExitCheckBox.AutoSize = true;
        _closeOnExitCheckBox.Margin = new Padding(4, 1, 4, 2);
        fields.Controls.Add(_closeOnExitCheckBox, 1, 1);
        fields.SetColumnSpan(_closeOnExitCheckBox, 2);

        AddField(fields, 2, "Skrót PWA", _shortcutSelector);
        return CreateGroupBox("PWA", fields);
    }

    private GroupBox CreateSecurityGroup()
    {
        _originsList.Dock = DockStyle.Fill;
        _originsList.IntegralHeight = false;
        _originsList.MinimumSize = new Size(0, 70);
        _originsList.Margin = new Padding(4, 3, 4, 2);

        var originButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(4, 3, 4, 2),
        };
        var addButton = CreateButton("Dodaj", 82);
        addButton.Click += (_, _) => AddOrigin();
        var removeButton = CreateButton("Usuń", 82);
        removeButton.Click += (_, _) => RemoveOrigin();
        originButtons.Controls.Add(addButton);
        originButtons.Controls.Add(removeButton);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(4, 0, 4, 0),
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90F));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.Controls.Add(_originsList, 0, 0);
        content.Controls.Add(originButtons, 1, 0);

        var hint = new Label
        {
            Text = "Origin strony PWA, np. https://liftcall.forge-it.eu",
            AutoSize = true,
            ForeColor = Color.FromArgb(90, 100, 120),
            Margin = new Padding(4, 2, 4, 2),
        };
        content.Controls.Add(hint, 0, 1);
        content.SetColumnSpan(hint, 2);
        return CreateGroupBox("Bezpieczeństwo", content);
    }

    private void ConfigureInputs(HelperConfig current)
    {
        _portInput.Minimum = 1;
        _portInput.Maximum = 65535;
        _portInput.Value = Math.Clamp(current.Port, 1, 65535);
        _portInput.Dock = DockStyle.Left;
        _portInput.Width = 130;
        _windowTitleTextBox.Text = current.WindowTitleContains;
        _windowClassTextBox.Text = current.WindowClass;
        _processNameTextBox.Text = current.ProcessName;
        _maximizeCheckBox.Checked = current.Maximize;
        _launchOnStartCheckBox.Checked = current.LaunchPwaOnStart;
        _closeOnExitCheckBox.Checked = current.ClosePwaOnExit;
        _shortcutSelector.Path = current.PwaShortcutPath;

        _originsList.Items.Clear();
        foreach (var origin in current.AllowedOrigins)
        {
            _originsList.Items.Add(origin);
        }
    }

    private static TableLayoutPanel CreateFieldTable(int rowCount)
    {
        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = rowCount,
            Padding = new Padding(4, 0, 4, 0),
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175F));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 1F));
        for (var row = 0; row < fields.RowCount; row++)
        {
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
        }

        return fields;
    }

    private static void AddField(TableLayoutPanel fields, int row, string labelText, Control control)
    {
        var label = new Label
        {
            Text = labelText,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(4, 5, 8, 2),
        };
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0, 2, 4, 2);
        fields.Controls.Add(label, 0, row);
        fields.Controls.Add(control, 1, row);
        fields.SetColumnSpan(control, 2);
    }

    private static GroupBox CreateGroupBox(string title, Control content)
    {
        var group = new GroupBox
        {
            Text = title,
            Dock = DockStyle.Fill,
            Padding = new Padding(8, 18, 8, 6),
            Margin = new Padding(0, 0, 0, 6),
            ForeColor = Color.FromArgb(26, 54, 93),
        };
        content.Dock = DockStyle.Fill;
        content.ForeColor = SystemColors.ControlText;
        group.Controls.Add(content);
        return group;
    }

    private static Button CreateButton(string text, int width)
    {
        return new Button
        {
            Text = text,
            AutoSize = false,
            Width = width,
            Height = 30,
            MinimumSize = new Size(width, 30),
            Margin = new Padding(6, 2, 0, 2),
            FlatStyle = FlatStyle.Standard,
        };
    }

    private void BrowseForShortcut()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Wybierz skrót PWA LiftCall",
            Filter = "Skróty Windows (*.lnk)|*.lnk|Wszystkie pliki (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };

        var currentDirectory = Path.GetDirectoryName(_shortcutSelector.Path);
        if (!string.IsNullOrWhiteSpace(currentDirectory) && Directory.Exists(currentDirectory))
        {
            dialog.InitialDirectory = currentDirectory;
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _shortcutSelector.Path = dialog.FileName;
        }
    }

    private void AddOrigin()
    {
        using var dialog = new OriginInputForm();
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _originsList.Items.Add(dialog.Origin);
        _originsList.SelectedIndex = _originsList.Items.Count - 1;
        ValidateConfiguration(false);
    }

    private void RemoveOrigin()
    {
        if (_originsList.SelectedIndex >= 0)
        {
            _originsList.Items.RemoveAt(_originsList.SelectedIndex);
            ValidateConfiguration(false);
        }
    }

    private void ResetToDefaults()
    {
        ConfigureInputs(new HelperConfig());
        _errors.Clear();
    }

    private void SaveConfiguration()
    {
        if (!ValidateConfiguration(true))
        {
            return;
        }

        Configuration = new HelperConfig
        {
            Host = "127.0.0.1",
            Port = (int)_portInput.Value,
            WindowTitleContains = _windowTitleTextBox.Text.Trim(),
            WindowClass = _windowClassTextBox.Text.Trim(),
            ProcessName = _processNameTextBox.Text.Trim(),
            Maximize = _maximizeCheckBox.Checked,
            LaunchPwaOnStart = _launchOnStartCheckBox.Checked,
            ClosePwaOnExit = _closeOnExitCheckBox.Checked,
            PwaShortcutPath = _shortcutSelector.Path.Trim(),
            AllowedOrigins = _originsList.Items.Cast<string>().ToList(),
        };
        DialogResult = DialogResult.OK;
        Close();
    }

    private bool ValidateConfiguration(bool focusFirstError)
    {
        _errors.Clear();
        Control? firstError = null;

        if (_portInput.Value is < 1 or > 65535)
        {
            SetError(_portInput, "Port musi mieścić się w zakresie 1–65535.", ref firstError);
        }

        if (string.IsNullOrWhiteSpace(_windowTitleTextBox.Text)
            && string.IsNullOrWhiteSpace(_windowClassTextBox.Text)
            && string.IsNullOrWhiteSpace(_processNameTextBox.Text))
        {
            SetError(_windowTitleTextBox, "Podaj tytuł, klasę albo nazwę procesu okna.", ref firstError);
        }

        if (!string.IsNullOrWhiteSpace(_shortcutSelector.Path)
            && !File.Exists(_shortcutSelector.Path.Trim()))
        {
            SetError(_shortcutSelector, "Wybrany skrót PWA nie istnieje.", ref firstError);
        }

        if (_originsList.Items.Count == 0)
        {
            SetError(_originsList, "Dodaj co najmniej jeden origin PWA.", ref firstError);
        }
        else
        {
            foreach (var item in _originsList.Items.Cast<string>())
            {
                if (!IsValidOrigin(item))
                {
                    SetError(_originsList, $"Niepoprawny origin: {item}", ref firstError);
                    break;
                }
            }
        }

        if (focusFirstError && firstError is not null)
        {
            firstError.Focus();
        }

        return firstError is null;
    }

    private static bool IsValidOrigin(string value)
    {
        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https"
            && !string.IsNullOrWhiteSpace(uri.Host)
            && uri.AbsolutePath is "" or "/"
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment);
    }

    private void SetError(Control control, string message, ref Control? firstError)
    {
        _errors.SetError(control, message);
        firstError ??= control;
    }
}

internal sealed class PathSelectorControl : UserControl
{
    private readonly Label _displayLabel = new();
    private readonly TextBox _editor = new();
    private readonly Button _browseButton = new();
    private readonly ToolTip _toolTip = new();
    private string _path = "";

    public event EventHandler? BrowseRequested;
    public event EventHandler? PathChanged;

    public string Path
    {
        get => _path;
        set
        {
            _path = value ?? "";
            _displayLabel.Text = _path;
            _editor.Text = _path;
            _toolTip.SetToolTip(_displayLabel, _path);
            _toolTip.SetToolTip(_editor, _path);
            PathChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public PathSelectorControl()
    {
        Height = 30;
        MinimumSize = new Size(0, 30);
        Dock = DockStyle.Fill;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92F));
        Controls.Add(layout);

        _displayLabel.Dock = DockStyle.Fill;
        _displayLabel.AutoEllipsis = true;
        _displayLabel.AutoSize = false;
        _displayLabel.BorderStyle = BorderStyle.FixedSingle;
        _displayLabel.BackColor = Color.White;
        _displayLabel.ForeColor = Color.FromArgb(35, 45, 60);
        _displayLabel.TextAlign = ContentAlignment.MiddleLeft;
        _displayLabel.Padding = new Padding(6, 0, 6, 0);
        _displayLabel.Cursor = Cursors.IBeam;
        _displayLabel.Click += (_, _) => BeginEdit();
        layout.Controls.Add(_displayLabel, 0, 0);

        _editor.Dock = DockStyle.Fill;
        _editor.Visible = false;
        _editor.KeyDown += EditorKeyDown;
        _editor.Leave += (_, _) => EndEdit(true);
        layout.Controls.Add(_editor, 0, 0);

        _browseButton.Text = "Wybierz...";
        _browseButton.Dock = DockStyle.Fill;
        _browseButton.Margin = new Padding(6, 0, 0, 0);
        _browseButton.Click += (_, _) => BrowseRequested?.Invoke(this, EventArgs.Empty);
        layout.Controls.Add(_browseButton, 1, 0);
    }

    private void BeginEdit()
    {
        _editor.Text = _path;
        _displayLabel.Visible = false;
        _editor.Visible = true;
        _editor.Focus();
        _editor.SelectAll();
    }

    private void EndEdit(bool commit)
    {
        if (commit)
        {
            Path = _editor.Text;
        }

        _displayLabel.Visible = true;
        _editor.Visible = false;
    }

    private void EditorKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.KeyCode == Keys.Enter)
        {
            EndEdit(true);
            eventArgs.SuppressKeyPress = true;
        }
        else if (eventArgs.KeyCode == Keys.Escape)
        {
            EndEdit(false);
            eventArgs.SuppressKeyPress = true;
        }
    }
}

internal sealed class OriginInputForm : Form
{
    private readonly TextBox _originTextBox = new();
    private readonly ErrorProvider _errors = new();

    public string Origin { get; private set; } = "";

    public OriginInputForm()
    {
        Text = "Dodaj origin PWA";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        ClientSize = new Size(440, 130);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        root.Controls.Add(new Label
        {
            Text = "Origin strony PWA, np. https://liftcall.forge-it.eu",
            AutoSize = true,
            ForeColor = Color.FromArgb(90, 100, 120),
        }, 0, 0);
        _originTextBox.Dock = DockStyle.Fill;
        root.Controls.Add(_originTextBox, 0, 1);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
        };
        var ok = new Button { Text = "Dodaj", Width = 86, Height = 30 };
        ok.Click += (_, _) => SaveOrigin();
        var cancel = new Button { Text = "Anuluj", Width = 86, Height = 30, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        root.Controls.Add(buttons, 0, 2);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    private void SaveOrigin()
    {
        var value = _originTextBox.Text.Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || string.IsNullOrWhiteSpace(uri.Host)
            || uri.AbsolutePath is not ("" or "/")
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            _errors.SetError(_originTextBox, "Podaj poprawny origin HTTP/HTTPS bez ścieżki i parametrów.");
            return;
        }

        Origin = value;
        DialogResult = DialogResult.OK;
        Close();
    }
}
