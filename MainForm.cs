using System.Threading;

namespace IGP_Soft;

internal sealed class MainForm : Form
{
    private readonly PantallasLocales _pantallas;
    private readonly List<MonitorTile> _tiles = new();
    private bool _syncing;
    private bool _activatePanelOnOpen;

    private readonly Label _info = new();
    private readonly Panel _canvas = new();
    private readonly ComboBox _cboBrowser = new();
    private readonly ToggleSwitch _chkPreview = new("");
    private readonly ToggleSwitch _chkSkipPrimary = new("");
    private readonly System.Windows.Forms.Timer _previewTimer = new() { Interval = 1500 };
    private volatile bool _capturing;
    private NotifyIcon _tray = null!;
    private Icon? _trayIcon;
    private bool _reallyExit;
    private bool _trayHintShown;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showReg;
    private readonly Panel _content = new();
    private readonly List<(ModernButton Nav, Page Page)> _nav = new();
    private readonly Panel _sideStatus = new();
    private readonly Chip _chipBrowser = new();
    private readonly Chip _chipMonitors = new();
    private readonly Chip _chipZones = new();
    private readonly Chip _chipOpen = new();
    private readonly Chip _chipLinks = new();
    private readonly Chip _chipMemory = new();
    private readonly System.Windows.Forms.Timer _maintenanceTimer = new() { Interval = 5000 };
    private long _browserMemory;
    private volatile bool _sampling;
    private Page _screensPage = null!;
    private LinksPage _linksPage = null!;
    private GalleryPage _galleryPage = null!;
    private ProfilesPage _profilesPage = null!;
    private ExportPage _exportPage = null!;
    private SettingsPage _settingsPage = null!;
    private ToolStripMenuItem _trayProfiles = null!;

    public MainForm(string startupProfile)
    {
        Text = "IGP_Soft — Pantallas de monitoreo IGP";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(980, 620);
        Size = new Size(1240, 760);
        TopMost = false;

        _trayIcon = BuildIcon();
        _pantallas = new PantallasLocales(_trayIcon);
        _pantallas.Changed += OnStateChanged;
        _pantallas.MonitorsChanged += RebuildTiles;
        _pantallas.SurfaceShown += KeepPanelAboveSurfaces;
        _pantallas.ZoneOpened += () => KeepPanelInFront(_activatePanelOnOpen);
        _pantallas.Aviso += ShowAviso;

        BuildLayout();
        BuildTray();
        _pantallas.Start();

        if (_pantallas.Browser is null)
        {
            _info.Visible = true;
            _info.Text = "⚠  No hay un navegador compatible (Edge, Chrome, Brave, Firefox u Opera). Instala uno para mostrar pantallas.";
        }

        _previewTimer.Tick += (_, _) =>
        {
            if (_chkPreview.Checked) CapturePreviews();
        };
        _previewTimer.Start();
        _maintenanceTimer.Tick += (_, _) => Maintenance();
        _maintenanceTimer.Start();

        Shown += (_, _) => BeginInvoke(() => RunStartupProfile(startupProfile));
        Updater.Found += OnUpdateFound;
        Shown += async (_, _) =>
        {
            await Task.Delay(15000);
            await CheckForUpdatesQuietly();
        };

        FormClosing += (_, e) =>
        {
            if (_reallyExit) return;
            if (e.CloseReason is CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing) return;
            e.Cancel = true;
            HideToTray();
        };

        FormClosed += (_, _) =>
        {
            _previewTimer.Stop();
            _maintenanceTimer.Stop();
            Updater.Found -= OnUpdateFound;
            Hotkeys.Changed -= ReRegisterHotkeys;
            Hotkeys.Unregister(Handle);
            _showReg?.Unregister(null);
            _showEvent?.Dispose();
            _pantallas.Dispose();
            ClearPreviews();
            _tray.Visible = false;
            _tray.Dispose();
            _trayIcon?.Dispose();
        };
    }

    private void BuildLayout()
    {
        Theme.StyleWindow(this, darkCaption: true);

        _info.Dock = DockStyle.Top;
        _info.Height = 34;
        _info.TextAlign = ContentAlignment.MiddleLeft;
        _info.ForeColor = Theme.Danger;
        _info.Font = Theme.BodyBold;
        _info.Visible = false;

        _cboBrowser.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboBrowser.FlatStyle = FlatStyle.Flat;
        _cboBrowser.Font = Theme.Body;
        _cboBrowser.Width = 220;
        foreach (Browser b in _pantallas.Browsers) _cboBrowser.Items.Add(b.Name);
        if (_pantallas.Browser is not null) _cboBrowser.SelectedItem = _pantallas.Browser.Name;
        _cboBrowser.Enabled = _pantallas.Browsers.Count > 0;
        _cboBrowser.SelectedIndexChanged += (_, _) =>
        {
            if (_syncing) return;
            if (_cboBrowser.SelectedItem is string name) _pantallas.SetBrowser(name);
        };

        _chkPreview.Text = "Vista previa en vivo de cada monitor en el panel";
        _chkPreview.Checked = true;
        _chkPreview.CheckedChanged += (_, _) => { if (!_chkPreview.Checked) ClearPreviews(); };

        _chkSkipPrimary.Text = "No usar la pantalla principal (la laptop desde la que manejas el programa)";
        _chkSkipPrimary.CheckedChanged += (_, _) =>
        {
            if (_syncing) return;
            _pantallas.SetSkipPrimary(_chkSkipPrimary.Checked);
        };

        _screensPage = new Page("Pantallas", "Clic en una zona para elegir qué mostrar  ·  ⊞ para dividir  ·  arrastra un monitor sobre otro para cambiar su número.");
        ModernButton gridAll = Page.ToolButton("▦  Dividir todos", () => { });
        gridAll.Click += (_, _) => ShowGridPickerForAll(gridAll);
        FlowLayoutPanel actions = Page.ToolBar(
            Page.ToolButton("▶  Abrir en orden", AutoAssignInOrder, primary: true),
            gridAll,
            Page.ToolButton("◎  Identificar", () => _pantallas.IdentifyMonitors()),
            Page.ToolButton("⧉  Combinar monitores", ShowCombine),
            Page.ToolButton("⟳  Actualizar", () => _pantallas.ReloadMonitors()),
            Page.ToolButton("✕  Cerrar todas", CloseAll));

        var chips = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, WrapContents = false, BackColor = Theme.Background };
        foreach (Chip c in new[] { _chipBrowser, _chipMonitors, _chipZones, _chipOpen, _chipMemory, _chipLinks }) chips.Controls.Add(c);

        _canvas.Dock = DockStyle.Fill;
        _canvas.BackColor = Theme.Background;
        _canvas.Padding = new Padding(8, 12, 8, 8);
        _canvas.Resize += (_, _) => RelayoutTiles();
        BuildUpdateBanner();
        _screensPage.Body.Controls.Add(_canvas);
        _screensPage.Body.Controls.Add(chips);
        _screensPage.Body.Controls.Add(_updateBanner);
        _screensPage.Body.Controls.Add(_info);
        _screensPage.Body.Controls.Add(actions);

        _linksPage = new LinksPage();
        _linksPage.Changed += () =>
        {
            foreach (MonitorTile t in _tiles) t.Invalidate();
            UpdateInfo();
        };
        _galleryPage = new GalleryPage(GalleryTargets);
        _profilesPage = new ProfilesPage(_pantallas.Snapshot, p =>
        {
            ApplyProfile(p);
            ShowPage(_screensPage);
        });
        _exportPage = new ExportPage(() => { _pantallas.CloseAllWindows(); return true; }, AfterRestore);
        _settingsPage = new SettingsPage(_cboBrowser, _chkPreview, _chkSkipPrimary, () => ReloadAll(manual: true), RestartForUpdate);

        var side = new Panel { Dock = DockStyle.Left, Width = 236, BackColor = Theme.Sidebar };
        var brand = new Panel { Dock = DockStyle.Top, Height = 92, BackColor = Theme.Sidebar };
        brand.Paint += (_, e) =>
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            var logo = new Rectangle(22, 26, 42, 42);
            using (var grad = new System.Drawing.Drawing2D.LinearGradientBrush(logo, Color.FromArgb(59, 130, 246), Color.FromArgb(37, 99, 235), 45f))
            using (var path = Theme.Round(logo, 11))
                g.FillPath(grad, path);
            TextRenderer.DrawText(g, "IGP", Theme.SmallBold, logo, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            using var title = Theme.Ui(13f, FontStyle.Bold);
            TextRenderer.DrawText(g, "IGP_Soft", title, new Point(74, 26), Color.White);
            TextRenderer.DrawText(g, "Pantallas de monitoreo", Theme.Small, new Point(75, 50), Theme.SidebarMuted);
        };

        var navList = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            BackColor = Theme.Sidebar, Padding = new Padding(14, 8, 0, 0),
        };
        navList.Controls.Add(new Label
        {
            Text = "MENÚ", Font = Theme.SmallBold, ForeColor = Theme.SidebarMuted, AutoSize = false,
            Size = new Size(200, 24), Margin = new Padding(8, 0, 0, 4), TextAlign = ContentAlignment.MiddleLeft,
        });
        foreach ((string glyph, string text, Page page) in new (string, string, Page)[]
                 {
                     ("▦", "Pantallas", _screensPage), ("☰", "Enlaces", _linksPage), ("▤", "Galería", _galleryPage),
                     ("★", "Perfiles", _profilesPage), ("⇪", "Exportar y copias", _exportPage), ("⚙", "Ajustes", _settingsPage),
                 })
        {
            var b = new ModernButton(text, ButtonKind.Nav, glyph)
            {
                Size = new Size(208, 42), Margin = new Padding(0, 0, 0, 4), Radius = 10, BackColor = Theme.Sidebar,
            };
            Page target = page;
            b.Click += (_, _) => ShowPage(target);
            navList.Controls.Add(b);
            _nav.Add((b, page));
            page.Visible = false;
            _content.Controls.Add(page);
        }

        _sideStatus.Dock = DockStyle.Bottom;
        _sideStatus.Height = 78;
        _sideStatus.BackColor = Theme.Sidebar;
        _sideStatus.Paint += PaintSideStatus;

        side.Controls.Add(navList);
        side.Controls.Add(_sideStatus);
        side.Controls.Add(brand);

        _content.Dock = DockStyle.Fill;
        _content.BackColor = Theme.Background;
        Controls.Add(_content);
        Controls.Add(side);
        ShowPage(_screensPage);
    }

    private void PaintSideStatus(object? sender, PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        Theme.Smooth(g);
        var card = new RectangleF(14, 8, _sideStatus.Width - 28, _sideStatus.Height - 20);
        Theme.FillRound(g, Theme.SidebarHover, card, 10);
        Browser? browser = _pantallas.Browser;
        int open = _pantallas.OpenCount;
        Color dot = browser is null ? Theme.Danger : open > 0 ? Color.FromArgb(74, 222, 128) : Theme.SidebarMuted;
        using (var b = new SolidBrush(dot)) g.FillEllipse(b, card.X + 14, card.Y + 17, 9, 9);
        string state = browser is null ? "Sin navegador" : open > 0 ? $"{open} pantalla(s) en vivo" : "Sin pantallas abiertas";
        TextRenderer.DrawText(g, state, Theme.SmallBold, new Point((int)card.X + 30, (int)card.Y + 12), Color.White);
        string second = _browserMemory > 0
            ? $"{ResourceMonitor.Format(_browserMemory)} · {browser?.Name ?? "—"}"
            : $"{_pantallas.Monitores.Count} monitor(es) · {browser?.Name ?? "—"}";
        TextRenderer.DrawText(g, second, Theme.Small,
            new Point((int)card.X + 30, (int)card.Y + 32), Theme.SidebarMuted);
    }

    private void ShowPage(Page page)
    {
        foreach ((ModernButton nav, Page p) in _nav) nav.Selected = p == page;
        page.OnPageShown();
        page.Visible = true;
        page.BringToFront();
        foreach ((_, Page p) in _nav)
            if (p != page) p.Visible = false;
        if (page == _screensPage)
        {
            foreach (MonitorTile t in _tiles) t.Invalidate();
            UpdateInfo();
        }
    }

    private IReadOnlyList<GalleryTarget> GalleryTargets()
    {
        var targets = new List<GalleryTarget>();
        foreach (PantallaMonitor t in _pantallas.Monitores.Where(t => !t.Excluded))
            foreach (Segment s in t.Usable)
            {
                PantallaMonitor tile = t;
                Segment seg = s;
                string label = t.Usable.Count() > 1 ? $"Monitor {t.Number} · zona {t.DisplayNumber(s)}" : $"Monitor {t.Number}";
                targets.Add(new GalleryTarget(label, s.Link?.Titulo, link => Open(tile, seg, link)));
            }
        return targets;
    }

    private void AfterRestore()
    {
        _pantallas.ReloadSettings();
        foreach (MonitorTile t in _tiles) t.Invalidate();
        UpdateInfo();
    }

    private void OnStateChanged()
    {
        _syncing = true;
        try
        {
            if (_pantallas.Browser is { } b && !string.Equals(_cboBrowser.SelectedItem as string, b.Name, StringComparison.Ordinal))
                _cboBrowser.SelectedItem = b.Name;
            if (_chkSkipPrimary.Checked != _pantallas.SkipPrimary) _chkSkipPrimary.Checked = _pantallas.SkipPrimary;
        }
        finally { _syncing = false; }
        _tiles.Sort((a, b) => a.Number.CompareTo(b.Number));
        foreach (MonitorTile t in _tiles) t.Invalidate();
        UpdateInfo();
    }

    private void ShowAviso(Resultado r)
    {
        MessageBoxIcon icon = r.Nivel switch
        {
            Nivel.Error => MessageBoxIcon.Error,
            Nivel.Aviso => MessageBoxIcon.Warning,
            _ => MessageBoxIcon.Information,
        };
        MessageBox.Show(this, r.Mensaje, r.Titulo ?? "IGP_Soft", MessageBoxButtons.OK, icon);
    }

    private void KeepPanelAboveSurfaces()
    {
        if (Visible && !IsDisposed)
            NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOP, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    private void RememberPanelFocus() => _activatePanelOnOpen = ActiveForm == this || (Visible && ContainsFocus);

    private void Open(PantallaMonitor tile, Segment seg, LinkDef link)
    {
        RememberPanelFocus();
        _ = _pantallas.OpenAsync(tile, seg, link);
    }

    private void AutoAssignInOrder()
    {
        RememberPanelFocus();
        _pantallas.AutoAssignInOrder();
    }

    private void ApplyProfile(LayoutProfile p)
    {
        RememberPanelFocus();
        _pantallas.ApplyProfile(p);
    }

    private void BuildTray()
    {
        Icon = _trayIcon;

        var menu = Theme.Menu();
        menu.Items.Add(new ToolStripMenuItem("Abrir panel", null, (_, _) => ShowFromTray()) { Font = new Font(Font, FontStyle.Bold) });
        menu.Items.Add(new ToolStripSeparator());
        _trayProfiles = new ToolStripMenuItem("Abrir perfil");
        menu.Items.Add(_trayProfiles);
        menu.Opening += (_, _) => FillProfileMenu(_trayProfiles);
        menu.Items.Add(new ToolStripMenuItem("Cerrar todas las ventanas", null, (_, _) => CloseAll()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Salir", null, (_, _) => ExitApp()));

        _tray = new NotifyIcon
        {
            Icon = _trayIcon,
            Text = "IGP_Soft — pantallas de monitoreo",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => ShowFromTray();

        try
        {
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowEventName);
            _showReg = ThreadPool.RegisterWaitForSingleObject(_showEvent,
                (_, _) => { try { if (!IsDisposed) BeginInvoke(ShowFromTray); } catch { } },
                null, Timeout.Infinite, false);
        }
        catch { }
    }

    private static Icon BuildIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var b = new SolidBrush(Color.FromArgb(0, 120, 215));
            g.FillEllipse(b, 0, 0, 31, 31);
            using var f = new Font("Segoe UI", 9f, FontStyle.Bold);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("IGP", f, Brushes.White, new RectangleF(0, 0, 32, 32), sf);
        }
        IntPtr h = bmp.GetHicon();
        var ico = (Icon)Icon.FromHandle(h).Clone();
        NativeMethods.DestroyIcon(h);
        return ico;
    }

    private void HideToTray()
    {
        Hide();
        ShowInTaskbar = false;
        if (!_trayHintShown)
        {
            _trayHintShown = true;
            _tray.ShowBalloonTip(3000, "IGP_Soft",
                "El panel sigue activo aquí. Doble clic para volver a abrirlo.", ToolTipIcon.Info);
        }
    }

    private void ShowFromTray()
    {
        Show();
        ShowInTaskbar = true;
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        BringToFront();
        Activate();
    }

    private void ExitApp()
    {
        if (!Visible) ShowFromTray();
        if (_pantallas.AnyBusy)
        {
            DialogResult r = MessageBox.Show(this,
                "Al salir se cerrarán las pantallas de monitoreo y su contenido " +
                "(las ventanas adjuntas vuelven al escritorio, no se cierran). ¿Desea continuar?",
                "Salir de IGP_Soft", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (r != DialogResult.OK) return;
            _pantallas.CloseEverything();
        }
        _reallyExit = true;
        Close();
    }

    private void RebuildTiles()
    {
        foreach (MonitorTile t in _tiles) { _canvas.Controls.Remove(t); t.Dispose(); }
        _tiles.Clear();

        foreach (PantallaMonitor m in _pantallas.Monitores)
        {
            var tile = new MonitorTile(m);
            tile.SegmentActivated += ShowSegmentMenu;
            tile.MonitorMenuRequested += ShowMonitorMenu;
            tile.GridRequested += ShowGridPicker;
            tile.DragMoved += OnTileDragMoved;
            tile.DragDropped += OnTileDropped;
            _tiles.Add(tile);
            _canvas.Controls.Add(tile);
        }

        RelayoutTiles();
        UpdateInfo();
    }

    private MonitorTile? TileAtScreen(Point screen, MonitorTile? except)
    {
        Point p = _canvas.PointToClient(screen);
        return _tiles.FirstOrDefault(t => t != except && t.Bounds.Contains(p));
    }

    private void OnTileDragMoved(MonitorTile source, Point screen)
    {
        MonitorTile? target = TileAtScreen(screen, source);
        foreach (MonitorTile t in _tiles)
        {
            bool on = t == target;
            if (t.DropTarget == on) continue;
            t.DropTarget = on;
            t.Invalidate();
        }
    }

    private void OnTileDropped(MonitorTile source, Point screen)
    {
        MonitorTile? target = TileAtScreen(screen, source);
        foreach (MonitorTile t in _tiles.Where(t => t.DropTarget)) { t.DropTarget = false; t.Invalidate(); }
        if (target is null) return;
        _pantallas.SwapNumbers(source.Model, target.Model);
    }

    private void ShowCombine()
    {
        using var f = new CombineForm(_pantallas.PhysicalMonitors());
        if (f.ShowDialog(this) != DialogResult.OK) return;
        _pantallas.SetGroups(f.Result);
    }

    private void SeparateMonitors(PantallaMonitor tile)
    {
        if (!tile.Monitor.IsCombined) return;
        if (tile.Segments.Any(s => s.Busy) &&
            MessageBox.Show(this, "Se cerrará lo que está abierto en esta pantalla combinada. ¿Separar los monitores?", "Separar monitores",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;
        _pantallas.SeparateMonitors(tile);
    }

    private void AddMonitorItems(ContextMenuStrip menu, PantallaMonitor tile)
    {
        var close = new ToolStripMenuItem("✕  Cerrar todo en este monitor", null, (_, _) => _pantallas.CloseMonitor(tile))
        {
            Enabled = tile.Segments.Any(s => s.Busy),
        };
        Keys k = Hotkeys.Get(Hotkeys.Actions.First(a => a.Key == "cerrarmonitor"));
        if (k != Keys.None) close.ShortcutKeyDisplayString = Hotkeys.Format(k);
        menu.Items.Add(close);
        if (tile.Monitor.IsCombined)
            menu.Items.Add(new ToolStripMenuItem("⊟  Separar monitores combinados", null, (_, _) => SeparateMonitors(tile)));
        if (_pantallas.HasCustomNumbering())
            menu.Items.Add(new ToolStripMenuItem("↺  Restablecer numeración de monitores", null, (_, _) => _pantallas.ResetNumbering()));
    }

    private void UpdateInfo()
    {
        _chipBrowser.Set("Navegador", _pantallas.Browser?.Name ?? "ninguno");
        _chipMonitors.Set("Monitores", _pantallas.Monitores.Count.ToString());
        _chipZones.Set("Zonas", _pantallas.ZoneCount.ToString());
        int abiertas = _pantallas.OpenCount;
        _chipOpen.Set("En vivo", abiertas.ToString());
        _chipOpen.Fill = abiertas > 0 ? Theme.SuccessSoft : Theme.Surface;
        _chipLinks.Set("Enlaces", LinkStore.Links.Count.ToString());
        UpdateMemoryChip();
        _sideStatus.Invalidate();
    }

    private void RelayoutTiles()
    {
        if (_tiles.Count == 0) return;

        List<MonitorInfo> monitors = _pantallas.Monitores.Select(p => p.Monitor).ToList();
        int minX = monitors.Min(m => m.X);
        int minY = monitors.Min(m => m.Y);
        int maxX = monitors.Max(m => m.X + m.Width);
        int maxY = monitors.Max(m => m.Y + m.Height);

        var area = new Rectangle(
            _canvas.Padding.Left, _canvas.Padding.Top,
            Math.Max(50, _canvas.ClientSize.Width - _canvas.Padding.Horizontal),
            Math.Max(50, _canvas.ClientSize.Height - _canvas.Padding.Vertical));

        double vw = Math.Max(1, maxX - minX);
        double vh = Math.Max(1, maxY - minY);
        double scale = Math.Min(area.Width / vw, area.Height / vh);

        int usedW = (int)(vw * scale);
        int usedH = (int)(vh * scale);
        int offX = area.Left + (area.Width - usedW) / 2;
        int offY = area.Top + (area.Height - usedH) / 2;

        const int gap = 8;
        foreach (MonitorTile t in _tiles)
        {
            MonitorInfo m = t.Monitor;
            t.Bounds = new Rectangle(
                offX + (int)((m.X - minX) * scale) + gap,
                offY + (int)((m.Y - minY) * scale) + gap,
                Math.Max(120, (int)(m.Width * scale) - gap * 2),
                Math.Max(80, (int)(m.Height * scale) - gap * 2));
        }
    }

    private void Maintenance()
    {
        if (!_sampling)
        {
            _sampling = true;
            Task.Run(() =>
            {
                long mem = 0;
                try { mem = ResourceMonitor.BrowserMemory(); }
                catch { }
                try
                {
                    BeginInvoke(() =>
                    {
                        _browserMemory = mem;
                        _sampling = false;
                        UpdateMemoryChip();
                        _sideStatus.Invalidate();
                    });
                }
                catch { _sampling = false; }
            });
        }

        if (DateTime.Now - _lastUpdateCheck >= TimeSpan.FromHours(6)) _ = CheckForUpdatesQuietly();
    }

    private readonly Card _updateBanner = new() { Dock = DockStyle.Top, Height = 64, Visible = false, Elevated = false, Padding = new Padding(16, 12, 12, 12) };
    private readonly Label _updateText = new();
    private DateTime _lastUpdateCheck = DateTime.Now;
    private string? _balloonShownFor;

    private void BuildUpdateBanner()
    {
        _updateBanner.Fill = Theme.AccentSoft;
        _updateText.Dock = DockStyle.Fill;
        _updateText.TextAlign = ContentAlignment.MiddleLeft;
        _updateText.Font = Theme.BodyBold;
        _updateText.ForeColor = Theme.AccentPressed;
        _updateText.BackColor = Theme.AccentSoft;
        var actions = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 300, WrapContents = false, BackColor = Theme.AccentSoft, FlowDirection = FlowDirection.RightToLeft };
        var dismiss = new ModernButton("", ButtonKind.Ghost, "✕") { Size = new Size(38, 38), Margin = new Padding(0) };
        dismiss.Click += (_, _) =>
        {
            if (Updater.Available is { } u) Updater.Dismissed = u.Version.ToString();
            _updateBanner.Visible = false;
        };
        var open = new ModernButton("Ver novedades y actualizar", ButtonKind.Primary, "⬆") { Margin = new Padding(0, 0, 8, 0) };
        open.Size = open.GetPreferredSize(Size.Empty);
        open.Click += (_, _) => ShowUpdates();
        actions.Controls.Add(dismiss);
        actions.Controls.Add(open);
        _updateBanner.Controls.Add(_updateText);
        _updateBanner.Controls.Add(actions);
    }

    private void ShowUpdates()
    {
        if (!Visible) ShowFromTray();
        ShowPage(_settingsPage);
        if (_settingsPage.UpdatesCard is { } card && card.Parent is ScrollableControl sc) sc.ScrollControlIntoView(card);
    }

    private async Task CheckForUpdatesQuietly()
    {
        _lastUpdateCheck = DateTime.Now;
        if (!Updater.Notify) return;
        try { await Updater.CheckAsync(raiseEvent: true); }
        catch { }
    }

    private void OnUpdateFound(UpdateInfo u)
    {
        if (IsDisposed) return;
        bool dismissed = Updater.Dismissed == u.Version.ToString();
        _updateText.Text = $"⬆   Hay una versión nueva de IGP_Soft: {u.Version}" + (u.Version.IsPrerelease ? " (experimental)" : "") +
                           $"  ·  tienes la {Updater.Current}";
        _updateBanner.Visible = !dismissed;
        if (!dismissed && Updater.Notify && _balloonShownFor != u.Version.ToString())
        {
            _balloonShownFor = u.Version.ToString();
            _tray.BalloonTipClicked -= OnBalloonClicked;
            _tray.BalloonTipClicked += OnBalloonClicked;
            _tray.ShowBalloonTip(6000, "IGP_Soft — actualización disponible",
                $"La versión {u.Version} está lista. Haz clic aquí para ver las novedades.", ToolTipIcon.Info);
        }
    }

    private void OnBalloonClicked(object? sender, EventArgs e) => ShowUpdates();

    private void RestartForUpdate()
    {
        _pantallas.CloseEverything();
        Updater.Restart($"--perfil=\"{ProfileStore.LastSession}\"");
        _reallyExit = true;
        Close();
    }

    private void UpdateMemoryChip()
    {
        long total = ResourceMonitor.PhysicalMemory();
        bool high = total > 0 && _browserMemory > total / 2;
        _chipMemory.Set("Memoria navegador", _browserMemory > 0 ? ResourceMonitor.Format(_browserMemory) : "—");
        _chipMemory.Fill = high ? Color.FromArgb(254, 243, 199) : Theme.Surface;
        _chipMemory.Invalidate();
    }

    private void ReloadAll(bool manual)
    {
        if (!_pantallas.ReloadAll() && manual)
            MessageBox.Show(this, "No hay páginas abiertas para recargar.", "Recargar", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ClearPreviews()
    {
        foreach (MonitorTile t in _tiles)
        {
            Bitmap? p = t.Preview;
            t.Preview = null;
            p?.Dispose();
            t.Invalidate();
        }
    }

    private void CapturePreviews()
    {
        if (_capturing || !Visible || WindowState == FormWindowState.Minimized || _tiles.Count == 0) return;
        _capturing = true;

        var jobs = _tiles.Where(t => !t.Excluded && t.Width > 8 && t.Height > 8)
                         .Select(t => (Tile: t, t.Monitor, t.Width)).ToList();

        Task.Run(() =>
        {
            var done = new List<(MonitorTile Tile, Bitmap Bmp)>();
            foreach (var j in jobs)
                if (PantallasLocales.CaptureMonitor(j.Monitor, Math.Clamp(j.Width * 2, 160, 720)) is { } thumb)
                    done.Add((j.Tile, thumb));

            void Apply()
            {
                foreach (var (tile, bmp) in done)
                {
                    if (!IsDisposed && _tiles.Contains(tile) && !tile.Excluded)
                    {
                        Bitmap? old = tile.Preview;
                        tile.Preview = bmp;
                        old?.Dispose();
                        tile.Invalidate();
                    }
                    else bmp.Dispose();
                }
                _capturing = false;
            }

            try
            {
                if (IsDisposed) { foreach (var d in done) d.Bmp.Dispose(); _capturing = false; }
                else BeginInvoke(Apply);
            }
            catch { foreach (var d in done) d.Bmp.Dispose(); _capturing = false; }
        });
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_DISPLAYCHANGE = 0x007E;
        if (m.Msg == Hotkeys.WM_HOTKEY)
        {
            int id = (int)m.WParam;
            BeginInvoke(() => HandleHotkey(id));
            return;
        }
        base.WndProc(ref m);
        if (m.Msg == WM_DISPLAYCHANGE)
            BeginInvoke(() => _pantallas.ReloadMonitors());
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Hotkeys.Register(Handle);
        Hotkeys.Changed += ReRegisterHotkeys;
    }

    private void ReRegisterHotkeys()
    {
        if (IsHandleCreated && !IsDisposed) Hotkeys.Register(Handle);
    }

    private void HandleHotkey(int id)
    {
        PantallaMonitor? under = _pantallas.MonitorAt(Cursor.Position);
        switch (Hotkeys.Actions.FirstOrDefault(a => a.Id == id)?.Key)
        {
            case "ampliar":
                if (under is not null && PantallasLocales.SegmentAt(under, Cursor.Position) is { } s) _pantallas.Maximize(under, s);
                break;
            case "restaurar":
                if (under is not null) _pantallas.RestoreGrid(under);
                else _pantallas.RestoreAllGrids();
                break;
            case "siguiente":
                if (under is not null) _pantallas.StepMaximize(under, +1);
                break;
            case "anterior":
                if (under is not null) _pantallas.StepMaximize(under, -1);
                break;
            case "panel":
                if (Visible && WindowState != FormWindowState.Minimized && ActiveForm == this) HideToTray();
                else ShowFromTray();
                break;
            case "abrirorden":
                AutoAssignInOrder();
                break;
            case "cerrartodas":
                CloseAll();
                break;
            case "cerrarmonitor":
                if (under is not null) _pantallas.CloseMonitor(under);
                break;
        }
    }

    private void ShowMonitorMenu(MonitorTile view)
    {
        PantallaMonitor tile = view.Model;
        var menu = Theme.Menu();
        menu.Items.Add(new ToolStripLabel($"Monitor {tile.Number}{(tile.Monitor.IsPrimary ? " (principal)" : "")}") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        var incl = new ToolStripMenuItem(tile.Excluded ? "Incluir esta pantalla" : "Omitir esta pantalla") { Checked = tile.Excluded };
        incl.Click += (_, _) => _pantallas.ToggleExcluded(tile);
        menu.Items.Add(incl);
        if (tile.Monitor.IsCombined)
            menu.Items.Add(new ToolStripMenuItem("⊟  Separar monitores combinados", null, (_, _) => SeparateMonitors(tile)));
        if (_pantallas.HasCustomNumbering())
            menu.Items.Add(new ToolStripMenuItem("↺  Restablecer numeración de monitores", null, (_, _) => _pantallas.ResetNumbering()));
        menu.Show(Cursor.Position);
    }

    private void ShowSegmentMenu(MonitorTile view, Segment seg)
    {
        PantallaMonitor tile = view.Model;
        if (_pantallas.Browser is null)
        {
            MessageBox.Show(this, "No hay navegador compatible instalado.", "IGP_Soft",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var menu = Theme.Menu();
        int usable = tile.Usable.Count();
        string zona = seg.Disabled ? " · zona sin usar" : usable > 1 ? $" · zona {tile.DisplayNumber(seg)}/{usable}" : "";
        menu.Items.Add(new ToolStripLabel($"Monitor {tile.Number}{zona}") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());

        if (!seg.Disabled && tile.Usable.Count() > 1)
        {
            ToolStripMenuItem ampliar = tile.Maximized == seg
                ? new ToolStripMenuItem("⤡  Volver a la grilla", null, (_, _) => _pantallas.RestoreGrid(tile))
                : new ToolStripMenuItem("⤢  Ampliar a todo el monitor", null, (_, _) => _pantallas.Maximize(tile, seg)) { Enabled = seg.Busy };
            HotkeyAction act = Hotkeys.Actions[tile.Maximized == seg ? 1 : 0];
            if (Hotkeys.Get(act) != Keys.None) ampliar.ShortcutKeyDisplayString = Hotkeys.Format(Hotkeys.Get(act));
            menu.Items.Add(ampliar);
        }

        var disenar = new ToolStripMenuItem("▦  Diseñar grilla de este monitor…");
        disenar.Click += (_, _) => EditLayout(tile);
        menu.Items.Add(disenar);

        if (seg.Disabled)
        {
            var usar = new ToolStripMenuItem("✓  Usar esta zona");
            usar.Click += (_, _) => _pantallas.ToggleZone(tile, seg);
            menu.Items.Add(usar);
            menu.Show(Cursor.Position);
            return;
        }

        if (tile.Segments.Count > 1)
        {
            var noUsar = new ToolStripMenuItem("⊘  No usar esta zona") { Enabled = usable > 1 };
            noUsar.Click += (_, _) => _pantallas.ToggleZone(tile, seg);
            menu.Items.Add(noUsar);
        }

        var omitir = new ToolStripMenuItem("Omitir esta pantalla");
        omitir.Click += (_, _) => _pantallas.ToggleExcluded(tile);
        menu.Items.Add(omitir);
        AddMonitorItems(menu, tile);
        menu.Items.Add(new ToolStripSeparator());

        if (seg.Launched is not null || seg.Pending is not null)
        {
            var cerrar = new ToolStripMenuItem(
                seg.Pending is not null ? "✖  Cancelar apertura"
                : seg.Launched?.Attached == true ? "↩  Soltar ventana (devolverla al escritorio)"
                : "✖  Cerrar esta ventana");
            cerrar.Click += (_, _) => _pantallas.CloseZone(tile, seg);
            menu.Items.Add(cerrar);
            menu.Items.Add(new ToolStripSeparator());
        }

        foreach (LinkDef link in LinkStore.Links)
        {
            bool aqui = seg.Link?.Url == link.Url;
            bool enOtro = _pantallas.IsOpen(link.Url) && !aqui;
            var item = new ToolStripMenuItem((aqui ? "" : "Abrir: ") + link.Titulo)
            {
                Checked = aqui,
                Enabled = !enOtro && !aqui && seg.Pending is null,
            };
            if (enOtro) item.Text += "   (ya abierto)";
            LinkDef captured = link;
            item.Click += (_, _) => Open(tile, seg, captured);
            menu.Items.Add(item);
        }

        menu.Items.Add(new ToolStripSeparator());
        var custom = new ToolStripMenuItem("➕  Elemento personalizado (imagen / video / URL)…") { Enabled = seg.Pending is null };
        custom.Click += (_, _) => AddCustomElement(tile, seg);
        menu.Items.Add(custom);
        menu.Items.Add(BuildAttachMenu(tile, seg));

        menu.Show(Cursor.Position);
    }

    private ToolStripMenuItem BuildAttachMenu(PantallaMonitor tile, Segment seg)
    {
        var attach = new ToolStripMenuItem("▣  Adjuntar ventana abierta") { Enabled = seg.Pending is null };
        foreach (OpenWindow w in _pantallas.ListOpenWindows())
        {
            string spec = PantallasLocales.SpecOf(w);
            string title = w.Title.Length > 70 ? w.Title[..67] + "…" : w.Title;
            var item = new ToolStripMenuItem($"{title}   — {w.ProcessName}".Replace("&", "&&"))
            {
                Enabled = !_pantallas.IsOpen(spec),
            };
            OpenWindow captured = w;
            item.Click += (_, _) => Open(tile, seg, new LinkDef("▣ " + captured.Title, spec));
            attach.DropDownItems.Add(item);
        }
        if (attach.DropDownItems.Count == 0)
            attach.DropDownItems.Add(new ToolStripMenuItem("(no hay ventanas disponibles)") { Enabled = false });
        return attach;
    }

    private async void AddCustomElement(PantallaMonitor tile, Segment seg)
    {
        if (tile.Excluded) return;
        string titulo, target;
        bool addToList;
        using (var d = new CustomElementForm(tile.Number))
        {
            if (d.ShowDialog(this) != DialogResult.OK) return;
            (titulo, target, addToList) = (d.Titulo, d.Target, d.AddToList);
        }

        if (AssetStore.IsExternalFile(target))
        {
            UseWaitCursor = true;
            try { target = await Task.Run(() => AssetStore.Import(target)); }
            finally { UseWaitCursor = false; }
        }

        var link = new LinkDef(titulo, target);
        link = addToList ? LinkStore.Add(link) : link;
        Open(tile, seg, link);
    }

    private void KeepPanelInFront(bool wasActive)
    {
        if (!wasActive || !Visible || IsDisposed || WindowState == FormWindowState.Minimized) return;
        Activate();
    }

    private void ShowGridPicker(MonitorTile view, Point screen)
    {
        PantallaMonitor tile = view.Model;
        GridPicker.Show(this, screen, tile.Cols, tile.Rows, (c, r) => _pantallas.SetGrid(tile, c, r), () => EditLayout(tile));
    }

    private void EditLayout(PantallaMonitor tile)
    {
        var contents = tile.Segments.Where(s => s.Link is not null).ToDictionary(s => s.Index, s => s.Link!.Titulo);
        using var f = new LayoutEditorForm(tile.Number, new Size(tile.Monitor.Width, tile.Monitor.Height), tile.Grid, contents);
        if (f.ShowDialog(this) != DialogResult.OK) return;
        _pantallas.ChangeLayout(tile, f.Result);
    }

    private void ShowGridPickerForAll(Control anchor)
    {
        PantallaMonitor? first = _pantallas.Monitores.FirstOrDefault(t => !t.Excluded);
        if (first is null) return;
        GridPicker.Show(this, anchor.PointToScreen(new Point(anchor.Width, 0)), first.Cols, first.Rows, (c, r) => _pantallas.SetGridAll(c, r));
    }

    private void CloseAll()
    {
        if (!_pantallas.CloseAll())
            MessageBox.Show(this, "No hay ventanas abiertas por este programa.", "Cerrar todas",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void FillProfileMenu(ToolStripMenuItem parent)
    {
        parent.DropDownItems.Clear();
        foreach (LayoutProfile p in ProfileStore.Get(ProfileStore.LastSession) is { } last
                     ? ProfileStore.Named.Prepend(last) : ProfileStore.Named)
        {
            LayoutProfile captured = p;
            parent.DropDownItems.Add(new ToolStripMenuItem(p.Name, null, (_, _) => ApplyProfile(captured)));
        }
        if (parent.DropDownItems.Count == 0)
            parent.DropDownItems.Add(new ToolStripMenuItem("(no hay perfiles guardados)") { Enabled = false });
    }

    private void RunStartupProfile(string name)
    {
        LayoutProfile? p = ProfileStore.Get(name);
        if (p is null && !string.IsNullOrWhiteSpace(name) && !ProfileStore.IsLastSession(name))
            _tray.ShowBalloonTip(4000, "IGP_Soft", $"No existe el perfil «{name}».", ToolTipIcon.Warning);

        if (p is not null && p.LinkCount > 0 && _pantallas.Browser is not null)
        {
            ApplyProfile(p);
            HideToTray();
        }
        _pantallas.SessionReady = true;
        if (p is not null) _pantallas.SaveLastSession();
    }

    private sealed class MonitorTile : Panel
    {
        private const TextFormatFlags C = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;

        public PantallaMonitor Model { get; }
        public int Number => Model.Number;
        public MonitorInfo Monitor => Model.Monitor;
        public bool Excluded => Model.Excluded;
        public Segment? Maximized => Model.Maximized;
        public GridLayout Grid => Model.Grid;
        public List<Segment> Segments => Model.Segments;
        public bool DropTarget { get; set; }
        public Bitmap? Preview { get; set; }

        public event Action<MonitorTile, Segment>? SegmentActivated;
        public event Action<MonitorTile>? MonitorMenuRequested;
        public event Action<MonitorTile, Point>? GridRequested;
        public event Action<MonitorTile, Point>? DragMoved;
        public event Action<MonitorTile, Point>? DragDropped;

        private Point _downAt;
        private bool _pressed;
        private bool _dragging;
        private bool _suppressClick;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || _ctrlRect.Contains(e.Location)) return;
            _downAt = e.Location;
            _pressed = true;
            _dragging = false;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_dragging)
            {
                _dragging = false;
                _suppressClick = true;
                Cursor = Cursors.Hand;
                Capture = false;
                DragDropped?.Invoke(this, PointToScreen(e.Location));
                Invalidate();
            }
            _pressed = false;
        }

        private bool _hover;
        private bool _hoverCtrl;
        private Segment? _hoverSeg;
        private Rectangle _ctrlRect;

        public MonitorTile(PantallaMonitor model)
        {
            Model = model;
            DoubleBuffered = true;
            Cursor = Cursors.Hand;
            BackColor = Theme.Background;
            MouseEnter += (_, _) => { _hover = true; Invalidate(); };
            MouseLeave += (_, _) => { _hover = false; _hoverSeg = null; _hoverCtrl = false; Invalidate(); };
        }

        private RectangleF RectOf(Segment s) => Grid.AreaOf(s.Spec, new SizeF(Width, Height));

        protected override void Dispose(bool disposing)
        {
            if (disposing) { Preview?.Dispose(); Preview = null; }
            base.Dispose(disposing);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (_suppressClick) { _suppressClick = false; return; }

            if (!Excluded && _ctrlRect.Contains(e.Location))
            {
                GridRequested?.Invoke(this, PointToScreen(new Point(_ctrlRect.Left, _ctrlRect.Bottom + 2)));
                return;
            }

            if (Excluded) { MonitorMenuRequested?.Invoke(this); return; }

            Segment? seg = Segments.FirstOrDefault(s => RectOf(s).Contains(e.Location));
            if (seg is not null) SegmentActivated?.Invoke(this, seg);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_pressed && !_dragging && (Math.Abs(e.X - _downAt.X) > 10 || Math.Abs(e.Y - _downAt.Y) > 10))
            {
                _dragging = true;
                Capture = true;
                Cursor = Cursors.SizeAll;
                _hoverSeg = null;
                Invalidate();
            }
            if (_dragging)
            {
                DragMoved?.Invoke(this, PointToScreen(e.Location));
                return;
            }
            Segment? over = Excluded || _ctrlRect.Contains(e.Location) ? null : Segments.FirstOrDefault(s => RectOf(s).Contains(e.Location));
            bool onCtrl = !Excluded && _ctrlRect.Contains(e.Location);
            if (over == _hoverSeg && onCtrl == _hoverCtrl) return;
            _hoverSeg = over;
            _hoverCtrl = onCtrl;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Background);
            Theme.Smooth(g);

            bool preview = Preview is not null && !Excluded;
            bool live = Segments.Any(s => s.Launched is not null);
            var card = new RectangleF(3, 2, Width - 7, Height - 8);
            Theme.Shadow(g, card, 14, _hover ? 6 : 4);
            using var clip = Theme.Round(card, 14);

            Theme.FillRound(g, Excluded ? Color.FromArgb(226, 232, 240) : Color.FromArgb(30, 41, 59), card, 14);
            g.SetClip(clip);
            if (preview)
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                g.DrawImage(Preview!, card);
            }

            if (!Excluded)
            {
                bool multi = Segments.Count > 1;
                foreach (Segment s in Segments)
                {
                    RectangleF zr = RectOf(s);
                    zr.Intersect(card);
                    zr.Inflate(multi ? -3f : -6f, multi ? -3f : -6f);
                    if (zr.Width < 4 || zr.Height < 4) continue;
                    bool hover = s == _hoverSeg;

                    if (s.Disabled)
                    {
                        using var hatch = new System.Drawing.Drawing2D.HatchBrush(System.Drawing.Drawing2D.HatchStyle.BackwardDiagonal,
                            Color.FromArgb(70, 148, 163, 184), Color.FromArgb(preview ? 170 : 90, 15, 23, 42));
                        using (var hp = Theme.Round(zr, 8)) g.FillPath(hatch, hp);
                        TextRenderer.DrawText(g, "sin usar", Theme.Small, Rectangle.Round(zr), Color.FromArgb(203, 213, 225), C);
                        continue;
                    }

                    if (Maximized is not null && s != Maximized)
                    {
                        Theme.FillRound(g, Color.FromArgb(150, 15, 23, 42), zr, 8);
                        TextRenderer.DrawText(g, "detrás", Theme.Small, Rectangle.Round(zr), Color.FromArgb(148, 163, 184), C);
                        continue;
                    }
                    if (s == Maximized)
                    {
                        Theme.DrawRound(g, Color.FromArgb(250, 204, 21), zr, 8, 2.5f);
                        var badge = new RectangleF(zr.X + 8, zr.Y + 40, 150, 22);
                        if (zr.Width > 170 && zr.Height > 90)
                        {
                            Theme.FillRound(g, Color.FromArgb(250, 204, 21), badge, 11);
                            TextRenderer.DrawText(g, "⤢  Ampliada al monitor", Theme.SmallBold, Rectangle.Round(badge), Color.FromArgb(66, 32, 6), C);
                        }
                    }

                    Color fill = hover ? Color.FromArgb(preview ? 90 : 120, 37, 99, 235)
                               : s.Link is not null ? Color.FromArgb(preview ? 0 : 60, 37, 99, 235)
                               : Color.FromArgb(preview ? 40 : 18, 255, 255, 255);
                    if (fill.A > 0) Theme.FillRound(g, fill, zr, 8);
                    if (multi || hover)
                    {
                        using var zp = Theme.Round(zr, 8);
                        using var pen = new Pen(hover ? Color.FromArgb(147, 197, 253) : Color.FromArgb(preview ? 150 : 60, 255, 255, 255), hover ? 2f : 1f);
                        if (s.Link is null && !hover) pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                        g.DrawPath(pen, zp);
                    }

                    if (s.Link is null && s.Pending is null)
                    {
                        string hint = hover ? "＋  Elegir contenido" : "＋";
                        TextRenderer.DrawText(g, hint, hover ? Theme.BodyBold : Theme.Section, Rectangle.Round(zr),
                            hover ? Color.White : Color.FromArgb(preview ? 200 : 110, 255, 255, 255), C);
                        continue;
                    }

                    string title = s.Link?.Titulo ?? "";
                    Color dot = s.Pending is not null ? Color.FromArgb(250, 204, 21) : Color.FromArgb(74, 222, 128);
                    string txt = s.Pending is not null ? "Abriendo " + title : title;
                    int maxW = (int)zr.Width - 16;
                    int tw = Math.Min(maxW - 22, TextRenderer.MeasureText(txt, Theme.Small).Width);
                    if (maxW < 40) continue;
                    var pill = new RectangleF(zr.X + 8, zr.Bottom - 30, tw + 28, 22);
                    Theme.FillRound(g, Color.FromArgb(215, 15, 23, 42), pill, 11);
                    using (var db = new SolidBrush(dot)) g.FillEllipse(db, pill.X + 9, pill.Y + 7.5f, 7, 7);
                    TextRenderer.DrawText(g, txt, Theme.Small, new Rectangle((int)pill.X + 20, (int)pill.Y, tw + 4, (int)pill.Height),
                        Color.White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                }
            }
            g.ResetClip();

            if (_dragging || DropTarget)
            {
                g.SetClip(clip);
                Theme.FillRound(g, _dragging ? Color.FromArgb(150, 255, 255, 255) : Color.FromArgb(150, 37, 99, 235), card, 14);
                g.ResetClip();
                string msg = _dragging ? "Suelta sobre otro monitor" : $"⇄  Intercambiar con el monitor {Number}";
                TextRenderer.DrawText(g, msg, Theme.Section, Rectangle.Round(card), _dragging ? Theme.Text : Color.White, C);
            }

            Color border = DropTarget ? Color.FromArgb(147, 197, 253) : Excluded ? Theme.BorderStrong : _hover ? Theme.Accent : live ? Theme.Success : Color.FromArgb(51, 65, 85);
            using (var bp = new Pen(border, DropTarget ? 3f : _hover || live ? 2f : 1f))
            {
                if (Excluded) bp.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                g.DrawPath(bp, clip);
            }

            string name = $"Monitor {Number}" + (Monitor.IsCombined ? $" · Combinado ({Monitor.Members!.Count})" : Monitor.IsPrimary ? " · Principal" : "");
            int nw = TextRenderer.MeasureText(name, Theme.SmallBold).Width + 20;
            var tag = new RectangleF(card.X + 10, card.Y + 10, nw, 24);
            Theme.FillRound(g, Excluded ? Color.FromArgb(203, 213, 225) : Color.FromArgb(215, 15, 23, 42), tag, 12);
            TextRenderer.DrawText(g, name, Theme.SmallBold, Rectangle.Round(tag), Excluded ? Theme.TextMuted : Color.White, C);

            if (!Excluded)
            {
                string label = $"⊞  {Grid.Describe()}  ▾";
                int w = TextRenderer.MeasureText(label, Theme.SmallBold).Width + 22;
                _ctrlRect = new Rectangle((int)(card.Right - w - 10), (int)card.Y + 10, w, 24);
                Theme.FillRound(g, _hoverCtrl ? Theme.Accent : Color.FromArgb(215, 15, 23, 42), _ctrlRect, 12);
                TextRenderer.DrawText(g, label, Theme.SmallBold, _ctrlRect, Color.White, C);
            }
            else
            {
                _ctrlRect = Rectangle.Empty;
                var center = Rectangle.Round(card);
                TextRenderer.DrawText(g, "Pantalla omitida", Theme.Section, new Rectangle(center.X, center.Y + center.Height / 2 - 24, center.Width, 26),
                    Theme.TextMuted, C);
                TextRenderer.DrawText(g, "Clic para volver a incluirla", Theme.Small, new Rectangle(center.X, center.Y + center.Height / 2 + 4, center.Width, 20),
                    Theme.TextSubtle, C);
            }
        }
    }
}
