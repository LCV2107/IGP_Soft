namespace IGP_Soft;

internal sealed class SettingsPage : Page
{
    private readonly ComboBox _cboStartup = new();
    private readonly ToggleSwitch _chkWindows = new("Iniciar IGP_Soft al encender Windows");
    private readonly ToggleSwitch _chkKeep = new("Recordar cookies e inicios de sesión de las páginas");
    private bool _loading;

    public SettingsPage(ComboBox browser, ToggleSwitch preview, ToggleSwitch skipPrimary)
        : base("Ajustes", "Todo se aplica y se guarda al instante.")
    {
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true,
            BackColor = Theme.Background, Padding = new Padding(0, 0, 0, 12),
        };

        flow.Controls.Add(Section("▦", "Pantallas", "Cómo se muestran las páginas en los monitores.",
            Row("Navegador", browser), Toggle(preview), Toggle(skipPrimary)));

        _cboStartup.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboStartup.FlatStyle = FlatStyle.Flat;
        _cboStartup.Font = Theme.Body;
        _cboStartup.Width = 320;
        _cboStartup.SelectedIndexChanged += (_, _) =>
        {
            if (_loading) return;
            Startup.Profile = _cboStartup.SelectedIndex <= 0 ? Startup.Nothing : (string)_cboStartup.SelectedItem!;
        };
        _chkWindows.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            Startup.WithWindows = _chkWindows.Checked;
            if (Startup.WithWindows == _chkWindows.Checked) return;
            MessageBox.Show(this, "No se pudo cambiar el inicio automático con Windows.", "Ajustes",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _loading = true;
            _chkWindows.Checked = Startup.WithWindows;
            _loading = false;
        };
        flow.Controls.Add(Section("▶", "Al iniciar el programa", "Abre tus pantallas automáticamente, sin hacer nada.",
            Row("Abrir", _cboStartup), Toggle(_chkWindows)));

        _chkKeep.CheckedChanged += (_, _) => { if (!_loading) Profiles.KeepData = _chkKeep.Checked; };
        ModernButton wipe = ToolButton("✕  Borrar sesiones guardadas", () =>
        {
            if (MessageBox.Show(this,
                    "Se borrarán las cookies e inicios de sesión guardados de todas las pantallas.\n" +
                    "Las pantallas abiertas ahora pueden conservar algunos hasta que se cierren. ¿Continuar?",
                    "Borrar sesiones", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK)
                Profiles.DeleteKept();
        });
        wipe.Kind = ButtonKind.Danger;
        wipe.Margin = new Padding(0, 8, 0, 0);
        flow.Controls.Add(Section("◈", "Sesiones de navegación", "Para páginas que piden usuario y contraseña: inicias sesión una sola vez.",
            Toggle(_chkKeep), wipe));

        Body.Controls.Add(flow);
    }

    public override void OnPageShown()
    {
        _loading = true;
        _cboStartup.Items.Clear();
        _cboStartup.Items.Add("Nada (el panel arranca vacío)");
        _cboStartup.Items.Add(ProfileStore.LastSession);
        foreach (LayoutProfile p in ProfileStore.Named) _cboStartup.Items.Add(p.Name);
        string current = Startup.Profile;
        int si = _cboStartup.Items.Cast<string>().ToList()
                            .FindIndex(n => string.Equals(n, current, StringComparison.OrdinalIgnoreCase));
        _cboStartup.SelectedIndex = Math.Max(0, si);
        _chkWindows.Checked = Startup.WithWindows;
        _chkKeep.Checked = Profiles.KeepData;
        _loading = false;
    }

    private static Control Toggle(ToggleSwitch t)
    {
        t.Width = 600;
        t.Margin = new Padding(0, 2, 0, 2);
        return t;
    }

    private static Card Section(string glyph, string title, string description, params Control[] content)
    {
        var card = new Card { Width = 680, Margin = new Padding(0, 0, 0, 14), Padding = new Padding(22, 18, 22, 18) };
        var head = new Panel { Dock = DockStyle.Top, Height = 54, BackColor = Theme.Surface };
        head.Paint += (_, e) =>
        {
            Theme.Smooth(e.Graphics);
            var icon = new Rectangle(0, 4, 36, 36);
            Theme.FillRound(e.Graphics, Theme.AccentSoft, icon, 9);
            TextRenderer.DrawText(e.Graphics, glyph, Theme.BodyBold, icon, Theme.Accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(e.Graphics, title, Theme.Section, new Point(48, 2), Theme.Text);
            TextRenderer.DrawText(e.Graphics, description, Theme.Small, new Point(49, 24), Theme.TextMuted);
        };
        var inner = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Surface,
            Padding = new Padding(48, 0, 0, 0), AutoSize = true,
        };
        inner.Controls.AddRange(content);
        card.Controls.Add(inner);
        card.Controls.Add(head);
        card.Height = 54 + content.Sum(c => c.Height + c.Margin.Vertical) + card.Padding.Vertical + 6;
        return card;
    }

    private static Control Row(string label, Control field)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 6), BackColor = Theme.Surface };
        row.Controls.Add(new Label
        {
            Text = label, AutoSize = false, Size = new Size(96, 30), TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Theme.TextMuted, Margin = new Padding(0),
        });
        field.Margin = new Padding(0, 3, 0, 0);
        row.Controls.Add(field);
        row.Height = 34;
        return row;
    }
}
