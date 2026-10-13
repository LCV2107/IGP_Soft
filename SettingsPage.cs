namespace IGP_Soft;

internal sealed class SettingsPage : Page
{
    private readonly ComboBox _cboStartup = new();
    private readonly ToggleSwitch _chkWindows = new("Iniciar IGP_Soft al encender Windows");
    private readonly ToggleSwitch _chkKeep = new("Recordar cookies e inicios de sesión de las páginas");
    private readonly ToggleSwitch _chkGpu = new("Usar la tarjeta de video (aceleración por hardware)");
    private readonly ComboBox _cboReload = new();
    private static readonly int[] ReloadOptions = { 0, 6, 12, 24 };
    private readonly List<(HotkeyAction Action, HotkeyBox Box, Label Status)> _hotkeys = new();
    private bool _loading;

    private readonly Action _restartForUpdate;
    private readonly ComboBox _cboChannel = new();
    private readonly ToggleSwitch _chkNotify = new("Avisarme cuando haya una versión nueva");
    private readonly Label _updVersion = new();
    private readonly Label _updStatus = new();
    private readonly Label _updNotes = new();
    private readonly ProgressBar _updProgress = new();
    private readonly ModernButton _btnCheck = new("Buscar actualizaciones", ButtonKind.Secondary, "⟳");
    private readonly ModernButton _btnInstall = new("Descargar e instalar", ButtonKind.Primary, "⇩");
    private readonly ModernButton _btnPage = new("Novedades en GitHub", ButtonKind.Ghost, "↗");
    private readonly ModernButton _btnRollback = new("Volver a la versión anterior", ButtonKind.Ghost, "↶");
    private Card? _updatesCard;

    public Card? UpdatesCard => _updatesCard;

    public SettingsPage(ComboBox browser, ToggleSwitch preview, ToggleSwitch skipPrimary, Action reloadNow, Action restartForUpdate)
        : base("Ajustes", "Todo se aplica y se guarda al instante.")
    {
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true,
            BackColor = Theme.Background, Padding = new Padding(0, 0, 0, 12),
        };

        flow.Controls.Add(Section("▦", "Pantallas", "Cómo se muestran las páginas en los monitores.",
            Row("Navegador", browser), Toggle(preview), Toggle(skipPrimary)));

        var hotkeyRows = new List<Control>();
        foreach (HotkeyAction a in Hotkeys.Actions) hotkeyRows.Add(HotkeyRow(a));
        ModernButton reset = ToolButton("↺  Restaurar atajos predeterminados", () =>
        {
            Hotkeys.ResetAll();
            RefreshHotkeys();
        });
        reset.Kind = ButtonKind.Ghost;
        reset.Margin = new Padding(0, 6, 0, 0);
        hotkeyRows.Add(reset);
        flow.Controls.Add(Section("⌨", "Atajos de teclado",
            "Funcionan desde cualquier programa. Actúan sobre la zona que está bajo el mouse.", hotkeyRows.ToArray()));

        _chkGpu.CheckedChanged += (_, _) => { if (!_loading) Profiles.HardwareAcceleration = _chkGpu.Checked; };
        _cboReload.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboReload.FlatStyle = FlatStyle.Flat;
        _cboReload.Font = Theme.Body;
        _cboReload.Width = 220;
        _cboReload.Items.AddRange(new object[] { "Nunca", "Cada 6 horas", "Cada 12 horas", "Cada 24 horas" });
        _cboReload.SelectedIndexChanged += (_, _) =>
        {
            if (!_loading && _cboReload.SelectedIndex >= 0) Profiles.ReloadHours = ReloadOptions[_cboReload.SelectedIndex];
        };
        ModernButton reloadButton = ToolButton("⟳  Recargar todo ahora", reloadNow);
        reloadButton.Margin = new Padding(0, 6, 0, 0);
        var gpuNote = new Label
        {
            Text = "Desactívala solo si la tarjeta de video es débil o da problemas. Se aplica al volver a abrir las pantallas.",
            AutoSize = false, Size = new Size(560, 22), ForeColor = Theme.TextMuted, Font = Theme.Small, Margin = new Padding(52, 0, 0, 6),
        };
        flow.Controls.Add(Section("⚡", "Rendimiento",
            "Todas las zonas comparten un solo navegador para usar menos memoria. Recargar libera la memoria acumulada.",
            Toggle(_chkGpu), gpuNote, Row("Recargar", _cboReload), reloadButton));

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

        _restartForUpdate = restartForUpdate;
        _cboChannel.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboChannel.FlatStyle = FlatStyle.Flat;
        _cboChannel.Font = Theme.Body;
        _cboChannel.Width = 360;
        _cboChannel.Items.AddRange(new object[] { "Estable — versiones aprobadas", "Experimental — versiones de prueba" });
        _cboChannel.SelectedIndexChanged += (_, _) =>
        {
            if (_loading) return;
            Updater.Channel = _cboChannel.SelectedIndex == 1 ? UpdateChannel.Experimental : UpdateChannel.Stable;
            _ = CheckNow();
        };
        _chkNotify.CheckedChanged += (_, _) => { if (!_loading) Updater.Notify = _chkNotify.Checked; };
        _updVersion.AutoSize = false;
        _updVersion.Size = new Size(560, 22);
        _updVersion.Font = Theme.BodyBold;
        _updVersion.Margin = new Padding(0, 0, 0, 6);
        _updStatus.AutoSize = false;
        _updStatus.Size = new Size(560, 40);
        _updStatus.Margin = new Padding(0, 6, 0, 0);
        _updNotes.AutoSize = false;
        _updNotes.Size = new Size(560, 96);
        _updNotes.ForeColor = Theme.TextMuted;
        _updNotes.Font = Theme.Small;
        _updNotes.AutoEllipsis = true;
        _updProgress.Size = new Size(560, 8);
        _updProgress.Margin = new Padding(0, 4, 0, 4);
        _updProgress.Visible = false;
        var buttons = new FlowLayoutPanel { Size = new Size(600, 42), WrapContents = false, BackColor = Theme.Surface, Margin = new Padding(0, 4, 0, 0) };
        foreach (ModernButton b in new[] { _btnCheck, _btnInstall, _btnPage })
        {
            b.Size = b.GetPreferredSize(Size.Empty);
            b.Margin = new Padding(0, 0, 8, 0);
            buttons.Controls.Add(b);
        }
        _btnRollback.Size = _btnRollback.GetPreferredSize(Size.Empty);
        _btnRollback.Margin = new Padding(0, 6, 0, 0);
        _btnCheck.Click += async (_, _) => await CheckNow();
        _btnInstall.Click += async (_, _) => await Install();
        _btnPage.Click += (_, _) => { if (Updater.Available is { } u) Updater.OpenPage(u.PageUrl); };
        _btnRollback.Click += (_, _) => Rollback();
        _updatesCard = Section("⬆", "Actualizaciones",
            "Las versiones nuevas se publican en GitHub. Nunca se instalan solas: tú decides cuándo.",
            _updVersion, Row("Canal", _cboChannel), Toggle(_chkNotify), buttons, _updStatus, _updNotes, _updProgress, _btnRollback);
        flow.Controls.Add(_updatesCard);

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
        _chkGpu.Checked = Profiles.HardwareAcceleration;
        _cboChannel.SelectedIndex = Updater.Channel == UpdateChannel.Experimental ? 1 : 0;
        _chkNotify.Checked = Updater.Notify;
        RefreshUpdates();
        _cboReload.SelectedIndex = Math.Max(0, Array.IndexOf(ReloadOptions, Profiles.ReloadHours));
        _loading = false;
        RefreshHotkeys();
    }

    private void RefreshUpdates()
    {
        _updVersion.Text = $"Versión instalada: {Updater.Current}" + (Updater.Current.IsPrerelease ? "  (de prueba)" : "");
        UpdateInfo? u = Updater.Available;
        _btnInstall.Enabled = u is not null;
        _btnPage.Enabled = u is not null;
        if (u is null)
        {
            if (_updStatus.Text.Length == 0)
            {
                _updStatus.ForeColor = Theme.TextMuted;
                _updStatus.Text = "Pulsa «Buscar actualizaciones» para revisar si hay una versión nueva.";
            }
            _updNotes.Text = "";
        }
        else
        {
            _updStatus.ForeColor = Theme.Accent;
            _updStatus.Text = $"⬆  Hay una versión nueva: {u.Version}" + (u.Version.IsPrerelease ? " (experimental)" : "") +
                              $" · publicada el {u.Published:dd/MM/yyyy}" + (u.ExeSize > 0 ? $" · {u.ExeSize / (1024 * 1024)} MB" : "");
            _updNotes.Text = u.Notes.Length > 0 ? "Novedades:\n" + u.Notes : "Esta versión no incluye notas.";
        }
        string? prev = Updater.PreviousVersion;
        _btnRollback.Visible = prev is not null;
        _btnRollback.Text = $"Volver a la versión anterior ({prev})";
        _btnRollback.Size = _btnRollback.GetPreferredSize(Size.Empty);
    }

    private async Task CheckNow()
    {
        _btnCheck.Enabled = false;
        _updStatus.ForeColor = Theme.TextMuted;
        _updStatus.Text = "Buscando…";
        try
        {
            UpdateInfo? u = await Updater.CheckAsync(raiseEvent: true);
            if (u is null)
            {
                _updStatus.ForeColor = Theme.Success;
                _updStatus.Text = $"✓  Estás al día. No hay versiones nuevas en el canal {(Updater.Channel == UpdateChannel.Experimental ? "experimental" : "estable")}.";
            }
        }
        catch (Exception ex)
        {
            _updStatus.ForeColor = Theme.Danger;
            _updStatus.Text = "No se pudo consultar GitHub. Revisa la conexión a Internet. (" + ex.Message + ")";
        }
        finally
        {
            _btnCheck.Enabled = true;
            RefreshUpdates();
        }
    }

    private async Task Install()
    {
        if (Updater.Available is not { } u) return;
        if (!Updater.IsPortableBuild || !Updater.CanWriteHere())
        {
            if (MessageBox.Show(this,
                    "Esta copia de IGP_Soft no se puede actualizar sola (no es la versión portable o la carpeta no permite escribir).\n\n" +
                    "¿Abrir la página de descarga para actualizar a mano?", "Actualizar",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                Updater.OpenPage(u.PageUrl);
            return;
        }
        if (MessageBox.Show(this,
                $"Se instalará la versión {u.Version}.\n\n" +
                "IGP_Soft se cerrará y volverá a abrirse solo, con las mismas pantallas. Tus enlaces, perfiles, ajustes e imágenes " +
                "se conservan (y antes se guarda una copia de seguridad). ¿Continuar?",
                "Actualizar IGP_Soft", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;

        _btnInstall.Enabled = _btnCheck.Enabled = false;
        _updProgress.Visible = true;
        _updProgress.Value = 0;
        var progress = new Progress<(long done, long total)>(p =>
        {
            int pct = p.total > 0 ? (int)(p.done * 100 / p.total) : 0;
            _updProgress.Value = Math.Clamp(pct, 0, 100);
            _updStatus.ForeColor = Theme.TextMuted;
            _updStatus.Text = $"Descargando… {p.done / (1024 * 1024)} de {Math.Max(1, p.total / (1024 * 1024))} MB";
        });
        try
        {
            await Updater.InstallAsync(u, progress);
            _updStatus.ForeColor = Theme.Success;
            _updStatus.Text = "✓  Listo. Reiniciando IGP_Soft…";
            await Task.Delay(800);
            _restartForUpdate();
        }
        catch (Exception ex)
        {
            _updProgress.Visible = false;
            _updStatus.ForeColor = Theme.Danger;
            _updStatus.Text = "No se pudo actualizar: " + ex.Message;
            _btnInstall.Enabled = _btnCheck.Enabled = true;
        }
    }

    private void Rollback()
    {
        string? prev = Updater.PreviousVersion;
        if (prev is null) return;
        if (MessageBox.Show(this, $"¿Volver a la versión {prev}? IGP_Soft se reiniciará con las mismas pantallas.",
                "Volver a la versión anterior", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;
        try
        {
            Updater.Rollback();
            _restartForUpdate();
        }
        catch (Exception ex)
        {
            _updStatus.ForeColor = Theme.Danger;
            _updStatus.Text = "No se pudo volver atrás: " + ex.Message;
        }
    }

    private Control HotkeyRow(HotkeyAction action)
    {
        var row = new FlowLayoutPanel { WrapContents = false, BackColor = Theme.Surface, Size = new Size(600, 40), Margin = new Padding(0, 0, 0, 2) };
        row.Controls.Add(new Label
        {
            Text = action.Name, AutoSize = false, Size = new Size(320, 34), TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Theme.Text, Margin = new Padding(0),
        });
        var box = new HotkeyBox { Margin = new Padding(0) };
        var status = new Label
        {
            AutoSize = false, Size = new Size(76, 34), TextAlign = ContentAlignment.MiddleLeft, Font = Theme.Small,
            ForeColor = Theme.Danger, Margin = new Padding(8, 0, 0, 0),
        };
        box.Captured += k => Assign(action, k);
        row.Controls.Add(box);
        row.Controls.Add(status);
        _hotkeys.Add((action, box, status));
        return row;
    }

    private void Assign(HotkeyAction action, Keys k)
    {
        HotkeyAction? other = Hotkeys.UsedBy(k, action);
        if (other is not null)
        {
            if (MessageBox.Show(this, $"«{Hotkeys.Format(k)}» ya está asignado a «{other.Name}».\n\n¿Quitárselo y usarlo aquí?",
                    "Atajos de teclado", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                RefreshHotkeys();
                return;
            }
            Hotkeys.Set(other, Keys.None);
        }
        Hotkeys.Set(action, k);
        RefreshHotkeys();
        if (k != Keys.None && Hotkeys.Failed.Contains(action.Key))
            MessageBox.Show(this, $"Windows u otro programa ya usa «{Hotkeys.Format(k)}». Elige otra combinación.",
                "Atajos de teclado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void RefreshHotkeys()
    {
        foreach ((HotkeyAction a, HotkeyBox box, Label status) in _hotkeys)
        {
            box.SetValue(Hotkeys.Get(a));
            bool failed = Hotkeys.Failed.Contains(a.Key);
            box.Unavailable = failed;
            status.Text = failed ? "Ocupado" : "";
        }
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
