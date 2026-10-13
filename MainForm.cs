using System.Threading;

namespace IGP_Soft;

internal sealed class MainForm : Form
{
    private Browser? _browser;
    private List<Browser> _browsers = new();
    private List<MonitorInfo> _monitors = new();
    private readonly HashSet<string> _openedUrls = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _excludedDevices = new();
    private readonly List<MonitorTile> _tiles = new();
    private readonly Dictionary<string, DisplaySurface> _surfaces = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Form> _identifyForms = new();
    private readonly List<Task<LaunchedWindow?>> _pendingLaunches = new();
    private bool _skipPrimaryTouched;

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
    private DateTime _lastReload = DateTime.Now;
    private Page _screensPage = null!;
    private LinksPage _linksPage = null!;
    private GalleryPage _galleryPage = null!;
    private ProfilesPage _profilesPage = null!;
    private ExportPage _exportPage = null!;
    private SettingsPage _settingsPage = null!;
    private ToolStripMenuItem _trayProfiles = null!;
    private bool _sessionReady;
    private bool _applyingProfile;

    public MainForm(string startupProfile)
    {
        Text = "IGP_Soft — Pantallas de monitoreo IGP";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(980, 620);
        Size = new Size(1240, 760);
        TopMost = false;

        _browsers = BrowserLocator.InstalledAll();
        string? want = Config.Get("browser") ?? BrowserLocator.DefaultName();
        _browser = _browsers.FirstOrDefault(b => b.Name.Equals(want, StringComparison.OrdinalIgnoreCase))
                   ?? _browsers.FirstOrDefault();

        BuildLayout();
        BuildTray();
        Profiles.CleanupOld();
        ReloadMonitors();

        if (_browser is null)
        {
            _info.Visible = true;
            _info.Text = "⚠  No hay un navegador compatible (Edge, Chrome, Brave, Firefox u Opera). Instala uno para mostrar pantallas.";
        }

        _previewTimer.Tick += (_, _) =>
        {
            CheckClosedWindows();
            EnforceWindows();
            if (_chkPreview.Checked) CapturePreviews();
        };
        _previewTimer.Start();
        _maintenanceTimer.Tick += (_, _) => Maintenance();
        _maintenanceTimer.Start();
        WindowWatcher.Start(OnWindowMoved);
        AssetStore.Moved += OnAssetsMoved;

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
            WindowWatcher.Stop();
            AssetStore.Moved -= OnAssetsMoved;
            _showReg?.Unregister(null);
            _showEvent?.Dispose();
            CloseIdentifyForms();
            CloseAllSurfaces();
            CloseInFlightLaunches();
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
        foreach (Browser b in _browsers) _cboBrowser.Items.Add(b.Name);
        if (_browser is not null) _cboBrowser.SelectedItem = _browser.Name;
        _cboBrowser.Enabled = _browsers.Count > 0;
        _cboBrowser.SelectedIndexChanged += (_, _) =>
        {
            Browser? b = _browsers.FirstOrDefault(x => x.Name == (_cboBrowser.SelectedItem as string));
            if (b is not null) { _browser = b; Config.Set("browser", b.Name); UpdateInfo(); }
        };

        _chkPreview.Text = "Vista previa en vivo de cada monitor en el panel";
        _chkPreview.Checked = true;
        _chkPreview.CheckedChanged += (_, _) => { if (!_chkPreview.Checked) ClearPreviews(); };

        _chkSkipPrimary.Text = "No usar la pantalla principal (la laptop desde la que manejas el programa)";
        _chkSkipPrimary.CheckedChanged += (_, _) => { _skipPrimaryTouched = true; ApplySkipPrimary(); SaveLastSession(); };

        _screensPage = new Page("Pantallas", "Clic en una zona para elegir qué mostrar  ·  ⊞ para dividir  ·  arrastra un monitor sobre otro para cambiar su número.");
        ModernButton gridAll = Page.ToolButton("▦  Dividir todos", () => { });
        gridAll.Click += (_, _) => ShowGridPickerForAll(gridAll);
        FlowLayoutPanel actions = Page.ToolBar(
            Page.ToolButton("▶  Abrir en orden", AutoAssignInOrder, primary: true),
            gridAll,
            Page.ToolButton("◎  Identificar", IdentifyMonitors),
            Page.ToolButton("⧉  Combinar monitores", ShowCombine),
            Page.ToolButton("⟳  Actualizar", ReloadMonitors),
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
        _profilesPage = new ProfilesPage(Snapshot, p =>
        {
            ApplyProfile(p);
            ShowPage(_screensPage);
        });
        _exportPage = new ExportPage(() => { CloseAllWindows(); return true; }, AfterRestore);
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
        int open = _tiles.Sum(t => t.Segments.Count(s => s.Launched is not null));
        Color dot = _browser is null ? Theme.Danger : open > 0 ? Color.FromArgb(74, 222, 128) : Theme.SidebarMuted;
        using (var b = new SolidBrush(dot)) g.FillEllipse(b, card.X + 14, card.Y + 17, 9, 9);
        string state = _browser is null ? "Sin navegador" : open > 0 ? $"{open} pantalla(s) en vivo" : "Sin pantallas abiertas";
        TextRenderer.DrawText(g, state, Theme.SmallBold, new Point((int)card.X + 30, (int)card.Y + 12), Color.White);
        string second = _browserMemory > 0
            ? $"{ResourceMonitor.Format(_browserMemory)} · {_browser?.Name ?? "—"}"
            : $"{_monitors.Count} monitor(es) · {_browser?.Name ?? "—"}";
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
        foreach (MonitorTile t in _tiles.Where(t => !t.Excluded))
            foreach (Segment s in t.Usable)
            {
                MonitorTile tile = t;
                Segment seg = s;
                string label = t.Usable.Count() > 1 ? $"Monitor {t.Number} · zona {t.DisplayNumber(s)}" : $"Monitor {t.Number}";
                targets.Add(new GalleryTarget(label, s.Link?.Titulo, link => AssignAndOpen(tile, seg, link)));
            }
        return targets;
    }

    private void AfterRestore()
    {
        Config.Reload();
        LinkStore.Load();
        ProfileStore.Load();
        string? want = Config.Get("browser");
        if (want is not null && _browsers.Any(b => b.Name.Equals(want, StringComparison.OrdinalIgnoreCase)))
            _cboBrowser.SelectedItem = _browsers.First(b => b.Name.Equals(want, StringComparison.OrdinalIgnoreCase)).Name;
        foreach (MonitorTile t in _tiles) t.Invalidate();
        UpdateInfo();
    }

    private void BuildTray()
    {
        _trayIcon = BuildIcon();
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
        if (_tiles.Any(t => t.Segments.Any(s => s.Launched is not null || s.Pending is not null)))
        {
            DialogResult r = MessageBox.Show(this,
                "Al salir se cerrarán las pantallas de monitoreo y su contenido " +
                "(las ventanas adjuntas vuelven al escritorio, no se cierran). ¿Desea continuar?",
                "Salir de IGP_Soft", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (r != DialogResult.OK) return;

            var windows = _tiles.SelectMany(t => t.Segments)
                                .Where(s => s.Launched is not null)
                                .Select(s => s.Launched!)
                                .ToList();
            CloseAllSurfaces();
            foreach (LaunchedWindow window in windows) WindowManager.Close(window);
        }
        _reallyExit = true;
        Close();
    }

    private void OnAssetsMoved(IReadOnlyDictionary<string, string> map)
    {
        foreach (MonitorTile t in _tiles)
        {
            foreach (Segment s in t.Segments)
            {
                if (s.Link is null || !map.TryGetValue(s.Link.Url, out string? to)) continue;
                _openedUrls.Remove(s.Link.Url);
                s.Link = s.Link with { Url = to };
                _openedUrls.Add(to);
            }
            t.Invalidate();
        }
        SaveLastSession();
    }

    private void ReloadMonitors()
    {
        CloseIdentifyForms();

        List<MonitorInfo> current = MonitorGroups.Apply(MonitorEnumerator.GetOrdered());
        var currentDevices = current.Select(m => m.DeviceName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (MonitorTile missing in _tiles.Where(t => !currentDevices.Contains(t.Monitor.DeviceName)).ToList())
            CloseTileWindows(missing);

        var prev = new Dictionary<string, (GridLayout layout, List<(LinkDef? Link, LaunchedWindow? Win, object? Pending)> items)>();
        foreach (MonitorTile t in _tiles)
            prev[t.Monitor.DeviceName] = (t.Grid, t.Segments.Select(s => (s.Link, s.Launched, s.Pending)).ToList());

        foreach (MonitorTile t in _tiles) { _canvas.Controls.Remove(t); t.Dispose(); }
        _tiles.Clear();

        _monitors = SavedOrder(current);

        if (!_skipPrimaryTouched)
            _chkSkipPrimary.Checked = _monitors.Count > LinkStore.Links.Count;

        for (int i = 0; i < _monitors.Count; i++)
        {
            MonitorInfo m = _monitors[i];
            var tile = new MonitorTile(i + 1, m) { Excluded = _excludedDevices.Contains(m.DeviceName) };

            if (prev.TryGetValue(m.DeviceName, out var pv))
            {
                tile.Apply(pv.layout);
                for (int k = 0; k < tile.Segments.Count && k < pv.items.Count; k++)
                {
                    tile.Segments[k].Link = pv.items[k].Link;
                    tile.Segments[k].Launched = pv.items[k].Win;
                    tile.Segments[k].Pending = pv.items[k].Pending;
                }
            }

            tile.SegmentActivated += ShowSegmentMenu;
            tile.MonitorMenuRequested += ShowMonitorMenu;
            tile.GridRequested += ShowGridPicker;
            tile.DragMoved += OnTileDragMoved;
            tile.DragDropped += OnTileDropped;
            _tiles.Add(tile);
            _canvas.Controls.Add(tile);

            if (_surfaces.TryGetValue(m.DeviceName, out DisplaySurface? surface) && !surface.IsDisposed)
            {
                surface.Place(m);
                foreach (Segment s in tile.Segments)
                    if (s.Launched is not null)
                        WindowManager.Reposition(s.Launched, AreaOf(tile, s));
            }
        }

        ApplySkipPrimary();
        RelayoutTiles();
        UpdateInfo();
    }

    private const string OrderKey = "ordenMonitores";

    private static List<MonitorInfo> SavedOrder(List<MonitorInfo> current)
    {
        string[] saved = (Config.Get(OrderKey) ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries);
        var ordered = saved.Select(d => current.FirstOrDefault(m => m.DeviceName.Equals(d, StringComparison.OrdinalIgnoreCase)))
                           .OfType<MonitorInfo>().ToList();
        ordered.AddRange(current.Where(m => !ordered.Contains(m)));
        return ordered;
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
        (source.Number, target.Number) = (target.Number, source.Number);
        ApplyNumbering();
    }

    private void ApplyNumbering()
    {
        _tiles.Sort((a, b) => a.Number.CompareTo(b.Number));
        _monitors = _tiles.Select(t => t.Monitor).ToList();
        Config.Set(OrderKey, string.Join("|", _tiles.Select(t => t.Monitor.DeviceName)));
        foreach (MonitorTile t in _tiles)
        {
            if (_surfaces.TryGetValue(t.Monitor.DeviceName, out DisplaySurface? surface) && !surface.IsDisposed)
                surface.Number = t.Number;
            t.Invalidate();
        }
        UpdateInfo();
        SaveLastSession();
    }

    private static int NaturalIndex(MonitorInfo m, List<string> physical)
    {
        var members = m.Members ?? new[] { m };
        return members.Select(x => physical.FindIndex(d => d.Equals(x.DeviceName, StringComparison.OrdinalIgnoreCase)))
                      .Where(i => i >= 0).DefaultIfEmpty(99).Min();
    }

    private List<MonitorTile> NaturalOrder()
    {
        var physical = MonitorEnumerator.GetOrdered().Select(m => m.DeviceName).ToList();
        return _tiles.OrderBy(t => NaturalIndex(t.Monitor, physical)).ToList();
    }

    private void ResetNumbering()
    {
        List<MonitorTile> natural = NaturalOrder();
        for (int i = 0; i < natural.Count; i++) natural[i].Number = i + 1;
        ApplyNumbering();
        Config.Set(OrderKey, "");
    }

    private bool HasCustomNumbering() => !string.IsNullOrEmpty(Config.Get(OrderKey)) &&
        !_tiles.SequenceEqual(NaturalOrder());

    private void ShowCombine()
    {
        List<MonitorInfo> physical = MonitorEnumerator.GetOrdered();
        using var f = new CombineForm(physical);
        if (f.ShowDialog(this) != DialogResult.OK) return;
        SetGroups(f.Result);
    }

    private void SeparateMonitors(MonitorTile tile)
    {
        if (!tile.Monitor.IsCombined) return;
        if (tile.Segments.Any(s => s.Busy) &&
            MessageBox.Show(this, "Se cerrará lo que está abierto en esta pantalla combinada. ¿Separar los monitores?", "Separar monitores",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;
        var members = MonitorGroups.MembersOf(tile.Monitor.DeviceName);
        SetGroups(MonitorGroups.Saved().Where(g => !g.SequenceEqual(members, StringComparer.OrdinalIgnoreCase)).ToList());
    }

    private void SetGroups(List<List<string>> groups)
    {
        MonitorGroups.Save(groups);
        ReloadMonitors();
        SaveLastSession();
    }

    private void SyncGroupsWith(LayoutProfile p)
    {
        var physical = MonitorEnumerator.GetOrdered().Select(m => m.DeviceName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var wanted = p.Monitors.Where(m => MonitorGroups.IsGroupName(m.Device))
                               .Select(m => MonitorGroups.MembersOf(m.Device).ToList())
                               .Where(g => g.All(physical.Contains))
                               .ToList();
        var current = MonitorGroups.Saved();
        string Key(IEnumerable<List<string>> gs) => string.Join(";", gs.Select(g => string.Join("+", g)).OrderBy(x => x));
        if (Key(wanted) == Key(current)) return;
        MonitorGroups.Save(wanted);
        ReloadMonitors();
    }

    private void CloseMonitor(MonitorTile tile)
    {
        if (!tile.Segments.Any(s => s.Busy)) return;
        CloseTileWindows(tile);
        SaveLastSession();
    }

    private void AddMonitorItems(ContextMenuStrip menu, MonitorTile tile)
    {
        var close = new ToolStripMenuItem("✕  Cerrar todo en este monitor", null, (_, _) => CloseMonitor(tile))
        {
            Enabled = tile.Segments.Any(s => s.Busy),
        };
        Keys k = Hotkeys.Get(Hotkeys.Actions.First(a => a.Key == "cerrarmonitor"));
        if (k != Keys.None) close.ShortcutKeyDisplayString = Hotkeys.Format(k);
        menu.Items.Add(close);
        if (tile.Monitor.IsCombined)
            menu.Items.Add(new ToolStripMenuItem("⊟  Separar monitores combinados", null, (_, _) => SeparateMonitors(tile)));
        if (HasCustomNumbering())
            menu.Items.Add(new ToolStripMenuItem("↺  Restablecer numeración de monitores", null, (_, _) => ResetNumbering()));
    }

    private void ApplySkipPrimary()
    {
        foreach (MonitorTile t in _tiles)
        {
            if (!t.Monitor.IsPrimary) continue;
            if (_chkSkipPrimary.Checked)
            {
                CloseTileWindows(t);
                t.Excluded = true;
                _excludedDevices.Add(t.Monitor.DeviceName);
            }
            else
            {
                t.Excluded = false;
                _excludedDevices.Remove(t.Monitor.DeviceName);
            }
            t.Invalidate();
        }
        UpdateInfo();
    }

    private void UpdateInfo()
    {
        int abiertas = _tiles.Sum(t => t.Segments.Count(s => s.Launched is not null));
        int zonas = _tiles.Where(t => !t.Excluded).Sum(t => t.Usable.Count());
        _chipBrowser.Set("Navegador", _browser?.Name ?? "ninguno");
        _chipMonitors.Set("Monitores", _monitors.Count.ToString());
        _chipZones.Set("Zonas", zonas.ToString());
        _chipOpen.Set("En vivo", abiertas.ToString());
        _chipOpen.Fill = abiertas > 0 ? Theme.SuccessSoft : Theme.Surface;
        _chipLinks.Set("Enlaces", LinkStore.Links.Count.ToString());
        UpdateMemoryChip();
        _sideStatus.Invalidate();
        foreach (MonitorTile t in _tiles)
            if (_surfaces.TryGetValue(t.Monitor.DeviceName, out DisplaySurface? surface) && !surface.IsDisposed)
                surface.SetContents(t.Usable.Where(s => s.Link is not null).Select(s => s.Link!.Titulo.TrimStart('▣', ' ')));
    }

    private void RelayoutTiles()
    {
        if (_tiles.Count == 0) return;

        int minX = _monitors.Min(m => m.X);
        int minY = _monitors.Min(m => m.Y);
        int maxX = _monitors.Max(m => m.X + m.Width);
        int maxY = _monitors.Max(m => m.Y + m.Height);

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

    private void CheckClosedWindows()
    {
        bool cambio = false;
        foreach (MonitorTile t in _tiles.ToList())
        {
            foreach (Segment s in t.Segments.ToList())
            {
                if (s.Launched is null || NativeMethods.IsWindow(s.Launched.Hwnd)) continue;

                LaunchedWindow w = s.Launched;
                s.Launched = null;
                if (s.Link is not null) { _openedUrls.Remove(s.Link.Url); s.Link = null; }
                t.Invalidate();
                Task.Run(() => WindowManager.Close(w));
                cambio = true;
            }
            CloseSurfaceIfEmpty(t);
        }
        if (cambio) UpdateInfo();
    }

    private void OnWindowMoved(IntPtr hwnd)
    {
        if (_identifyForms.Any(f => !f.IsDisposed)) return;
        foreach (MonitorTile t in _tiles)
            foreach (Segment s in t.Segments)
                if (s.Launched is { } w && w.Hwnd == hwnd)
                {
                    WindowManager.Reposition(w, AreaOf(t, s));
                    return;
                }
    }

    private void EnforceWindows()
    {
        if (_identifyForms.Any(f => !f.IsDisposed)) return;
        foreach (MonitorTile t in _tiles) EnforceTile(t);
    }

    private static void EnforceTile(MonitorTile t)
    {
        foreach (Segment s in t.Segments)
        {
            if (s.Launched is null) continue;
            s.Launched.Hidden = t.Maximized is not null && s != t.Maximized;
            WindowManager.Reposition(s.Launched, AreaOf(t, s));
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

        int hours = Profiles.ReloadHours;
        if (hours > 0 && DateTime.Now - _lastReload >= TimeSpan.FromHours(hours)) ReloadAll(manual: false);
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
        var windows = _tiles.SelectMany(t => t.Segments).Where(s => s.Launched is not null).Select(s => s.Launched!).ToList();
        CloseAllSurfaces();
        foreach (LaunchedWindow window in windows) WindowManager.Close(window);
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

    private async void ReloadAll(bool manual)
    {
        _lastReload = DateTime.Now;
        bool any = _tiles.Any(t => t.Segments.Any(s => s.Launched is { Attached: false, Native: false }));
        if (!any)
        {
            if (manual)
                MessageBox.Show(this, "No hay páginas abiertas para recargar.", "Recargar", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        LayoutProfile snapshot = Snapshot("recarga");
        CloseAllWindows();
        await Task.Delay(3500);
        if (!IsDisposed) ApplyProfile(snapshot);
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
            {
                try
                {
                    int tw = Math.Clamp(j.Width * 2, 160, 720);
                    int th = Math.Max(1, (int)(tw * (double)j.Monitor.Height / j.Monitor.Width));
                    using var full = new Bitmap(j.Monitor.Width, j.Monitor.Height);
                    using (Graphics gf = Graphics.FromImage(full))
                        gf.CopyFromScreen(j.Monitor.X, j.Monitor.Y, 0, 0, new Size(j.Monitor.Width, j.Monitor.Height));
                    var thumb = new Bitmap(tw, th);
                    using (Graphics gt = Graphics.FromImage(thumb))
                    {
                        gt.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
                        gt.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                        gt.DrawImage(full, new Rectangle(0, 0, tw, th));
                    }
                    done.Add((j.Tile, thumb));
                }
                catch { }
            }

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
            BeginInvoke(ReloadMonitors);
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
        switch (Hotkeys.Actions.FirstOrDefault(a => a.Id == id)?.Key)
        {
            case "ampliar":
                if (TileUnderCursor() is { } t && SegmentUnderCursor(t) is { } s) Maximize(t, s);
                break;
            case "restaurar":
                if (TileUnderCursor() is { } tr) RestoreGrid(tr);
                else foreach (MonitorTile x in _tiles.Where(x => x.Maximized is not null).ToList()) RestoreGrid(x);
                break;
            case "siguiente":
                if (TileUnderCursor() is { } tn) StepMaximize(tn, +1);
                break;
            case "anterior":
                if (TileUnderCursor() is { } tp) StepMaximize(tp, -1);
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
                if (TileUnderCursor() is { } tc) CloseMonitor(tc);
                break;
        }
    }

    private MonitorTile? TileUnderCursor()
    {
        Point p = Cursor.Position;
        return _tiles.FirstOrDefault(t => !t.Excluded &&
            new Rectangle(t.Monitor.X, t.Monitor.Y, t.Monitor.Width, t.Monitor.Height).Contains(p));
    }

    private static Segment? SegmentUnderCursor(MonitorTile tile)
    {
        Point p = Cursor.Position;
        return tile.Usable.FirstOrDefault(s => s.Area.Contains(p));
    }

    private void Maximize(MonitorTile tile, Segment seg)
    {
        if (seg.Disabled || !seg.Busy || tile.Segments.Count <= 1 || tile.Maximized == seg) return;
        tile.Maximized = seg;
        EnforceTile(tile);
        if (seg.Launched is not null)
            NativeMethods.SetWindowPos(seg.Launched.Hwnd, NativeMethods.HWND_TOP, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        tile.Invalidate();
    }

    private void RestoreGrid(MonitorTile tile)
    {
        if (tile.Maximized is null) return;
        tile.Maximized = null;
        EnforceTile(tile);
        tile.Invalidate();
    }

    private void StepMaximize(MonitorTile tile, int dir)
    {
        var candidates = tile.Usable.Where(s => s.Launched is not null).ToList();
        if (candidates.Count == 0) candidates = tile.Usable.ToList();
        if (candidates.Count == 0) return;
        int i = tile.Maximized is null ? (dir > 0 ? 0 : candidates.Count - 1)
              : (candidates.IndexOf(tile.Maximized) + dir + candidates.Count) % candidates.Count;
        Maximize(tile, candidates[Math.Max(0, i)]);
    }

    private void ShowMonitorMenu(MonitorTile tile)
    {
        var menu = Theme.Menu();
        menu.Items.Add(new ToolStripLabel($"Monitor {tile.Number}{(tile.Monitor.IsPrimary ? " (principal)" : "")}") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        var incl = new ToolStripMenuItem(tile.Excluded ? "Incluir esta pantalla" : "Omitir esta pantalla") { Checked = tile.Excluded };
        incl.Click += (_, _) => ToggleExcluded(tile);
        menu.Items.Add(incl);
        if (tile.Monitor.IsCombined)
            menu.Items.Add(new ToolStripMenuItem("⊟  Separar monitores combinados", null, (_, _) => SeparateMonitors(tile)));
        if (HasCustomNumbering())
            menu.Items.Add(new ToolStripMenuItem("↺  Restablecer numeración de monitores", null, (_, _) => ResetNumbering()));
        menu.Show(Cursor.Position);
    }

    private void ShowSegmentMenu(MonitorTile tile, Segment seg)
    {
        if (_browser is null)
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
                ? new ToolStripMenuItem("⤡  Volver a la grilla", null, (_, _) => RestoreGrid(tile))
                : new ToolStripMenuItem("⤢  Ampliar a todo el monitor", null, (_, _) => Maximize(tile, seg)) { Enabled = seg.Busy };
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
            usar.Click += (_, _) => ToggleZone(tile, seg);
            menu.Items.Add(usar);
            menu.Show(Cursor.Position);
            return;
        }

        if (tile.Segments.Count > 1)
        {
            var noUsar = new ToolStripMenuItem("⊘  No usar esta zona") { Enabled = usable > 1 };
            noUsar.Click += (_, _) => ToggleZone(tile, seg);
            menu.Items.Add(noUsar);
        }

        var omitir = new ToolStripMenuItem("Omitir esta pantalla");
        omitir.Click += (_, _) => ToggleExcluded(tile);
        menu.Items.Add(omitir);
        AddMonitorItems(menu, tile);
        menu.Items.Add(new ToolStripSeparator());

        if (seg.Launched is not null || seg.Pending is not null)
        {
            var cerrar = new ToolStripMenuItem(
                seg.Pending is not null ? "✖  Cancelar apertura"
                : seg.Launched?.Attached == true ? "↩  Soltar ventana (devolverla al escritorio)"
                : "✖  Cerrar esta ventana");
            cerrar.Click += (_, _) => { CloseSeg(tile, seg); SaveLastSession(); };
            menu.Items.Add(cerrar);
            menu.Items.Add(new ToolStripSeparator());
        }

        foreach (LinkDef link in LinkStore.Links)
        {
            bool aqui = seg.Link?.Url == link.Url;
            bool enOtro = _openedUrls.Contains(link.Url) && !aqui;
            var item = new ToolStripMenuItem((aqui ? "" : "Abrir: ") + link.Titulo)
            {
                Checked = aqui,
                Enabled = !enOtro && !aqui && seg.Pending is null,
            };
            if (enOtro) item.Text += "   (ya abierto)";
            LinkDef captured = link;
            item.Click += (_, _) => AssignAndOpen(tile, seg, captured);
            menu.Items.Add(item);
        }

        menu.Items.Add(new ToolStripSeparator());
        var custom = new ToolStripMenuItem("➕  Elemento personalizado (imagen / video / URL)…") { Enabled = seg.Pending is null };
        custom.Click += (_, _) => AddCustomElement(tile, seg);
        menu.Items.Add(custom);
        menu.Items.Add(BuildAttachMenu(tile, seg));

        menu.Show(Cursor.Position);
    }

    private ToolStripMenuItem BuildAttachMenu(MonitorTile tile, Segment seg)
    {
        var attach = new ToolStripMenuItem("▣  Adjuntar ventana abierta") { Enabled = seg.Pending is null };
        foreach (OpenWindow w in WindowManager.ListOpenWindows())
        {
            string spec = WindowManager.SpecOf(w);
            string title = w.Title.Length > 70 ? w.Title[..67] + "…" : w.Title;
            var item = new ToolStripMenuItem($"{title}   — {w.ProcessName}".Replace("&", "&&"))
            {
                Enabled = !_openedUrls.Contains(spec),
            };
            OpenWindow captured = w;
            item.Click += (_, _) => AssignAndOpen(tile, seg, new LinkDef("▣ " + captured.Title, spec));
            attach.DropDownItems.Add(item);
        }
        if (attach.DropDownItems.Count == 0)
            attach.DropDownItems.Add(new ToolStripMenuItem("(no hay ventanas disponibles)") { Enabled = false });
        return attach;
    }

    private async void AddCustomElement(MonitorTile tile, Segment seg)
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
        AssignAndOpen(tile, seg, link);
    }

    private async void AssignAndOpen(MonitorTile tile, Segment seg, LinkDef link, bool quiet = false)
    {
        bool isWindow = WindowManager.IsWindowSpec(link.Url);
        if (tile.Excluded || seg.Disabled || seg.Pending is not null || (_browser is null && !isWindow)) return;
        bool panelActive = ActiveForm == this || (Visible && ContainsFocus);

        if (_openedUrls.Contains(link.Url) && seg.Link?.Url != link.Url)
        {
            MessageBox.Show(this, $"El enlace «{link.Titulo}» ya está abierto en otra zona.",
                "Enlace en uso", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (seg.Launched is not null || seg.Link is not null) CloseSeg(tile, seg);

        DisplaySurface surface;
        try { surface = EnsureSurface(tile); }
        catch (Exception ex)
        {
            CloseSurfaceIfEmpty(tile);
            MessageBox.Show(this, $"No se pudo preparar la pantalla:\n{ex.Message}",
                "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (!isWindow && NativeImage.Supports(link.Url) &&
            NativeImage.Show(surface, link.Url, AreaOf(tile, seg)) is { } image)
        {
            seg.Link = link;
            seg.Launched = image;
            _openedUrls.Add(link.Url);
            tile.Invalidate();
            UpdateInfo();
            SaveLastSession();
            EnforceWindows();
            KeepPanelInFront(panelActive);
            return;
        }

        if (isWindow)
        {
            LaunchedWindow? attached = WindowManager.Attach(link.Url, AreaOf(tile, seg), surface.Handle);
            if (attached is null)
            {
                CloseSurfaceIfEmpty(tile);
                if (!quiet)
                    MessageBox.Show(this,
                        $"No se pudo adjuntar «{link.Titulo.TrimStart('▣', ' ')}».\n\n" +
                        "Puede que la ventana se haya cerrado, o que esa aplicación se ejecute como administrador " +
                        "(en ese caso, abra IGP_Soft también como administrador).",
                        "No se pudo adjuntar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            seg.Link = link;
            seg.Launched = attached;
            _openedUrls.Add(link.Url);
            tile.Invalidate();
            UpdateInfo();
            SaveLastSession();
            EnforceWindows();
            KeepPanelInFront(panelActive);
            return;
        }

        var token = new object();
        seg.Pending = token;
        seg.Link = link;
        _openedUrls.Add(link.Url);
        tile.Invalidate();
        UpdateInfo();
        SaveLastSession();

        Browser browser = _browser!;
        bool keepData = Profiles.KeepData;
        Rectangle area = AreaOf(tile, seg);
        string tag = $"{tile.Monitor.ShortName}-{seg.Index}";
        IntPtr parent = surface.Handle;

        Task<LaunchedWindow?> launch = Task.Run(() => WindowManager.LaunchAndPlace(browser, link.Url, area, tag, parent, keepData));
        _pendingLaunches.Add(launch);
        LaunchedWindow? win = null;
        string? error = null;
        try { win = await launch; }
        catch (Exception ex) { error = ex.Message; }
        finally { _pendingLaunches.Remove(launch); }

        (MonitorTile? t, Segment? s) = FindPending(token);
        if (IsDisposed || t is null || s is null)
        {
            if (win is not null) _ = Task.Run(() => WindowManager.Close(win));
            return;
        }

        s.Pending = null;
        if (win is null)
        {
            _openedUrls.Remove(link.Url);
            s.Link = null;
            t.Invalidate();
            UpdateInfo();
            CloseSurfaceIfEmpty(t);
            if (quiet) return;
            MessageBox.Show(this,
                error is not null
                    ? $"No se pudo abrir el navegador:\n{error}"
                    : $"El navegador se inició, pero no se pudo integrar «{link.Titulo}» en la pantalla. " +
                      "La zona quedó libre para intentarlo nuevamente.",
                "No se pudo abrir", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        s.Launched = win;
        t.Invalidate();
        UpdateInfo();
        EnforceWindows();
        KeepPanelInFront(panelActive);
    }

    private void KeepPanelInFront(bool wasActive)
    {
        if (!wasActive || !Visible || IsDisposed || WindowState == FormWindowState.Minimized) return;
        Activate();
    }

    private (MonitorTile? Tile, Segment? Seg) FindPending(object token)
    {
        foreach (MonitorTile t in _tiles)
            foreach (Segment s in t.Segments)
                if (ReferenceEquals(s.Pending, token)) return (t, s);
        return (null, null);
    }

    private void AutoAssignInOrder()
    {
        var targets = new List<(MonitorTile T, Segment S)>();
        foreach (MonitorTile t in _tiles)
            if (!t.Excluded)
                foreach (Segment s in t.Usable)
                    targets.Add((t, s));

        if (targets.Count == 0)
        {
            MessageBox.Show(this, "No hay zonas disponibles (todos los monitores están omitidos).",
                "Abrir en orden", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        int n = Math.Min(targets.Count, LinkStore.Links.Count);
        for (int i = 0; i < n; i++)
        {
            (MonitorTile t, Segment s) = targets[i];
            LinkDef link = LinkStore.Links[i];
            if (s.Link?.Url == link.Url || s.Pending is not null) continue;
            if (_openedUrls.Contains(link.Url)) continue;
            AssignAndOpen(t, s, link);
        }
    }

    private void ShowGridPicker(MonitorTile tile, Point screen)
    {
        GridPicker.Show(this, screen, tile.Cols, tile.Rows, (c, r) =>
        {
            SetGrid(tile, c, r);
            SaveLastSession();
        }, () => EditLayout(tile));
    }

    private void EditLayout(MonitorTile tile)
    {
        var contents = tile.Segments.Where(s => s.Link is not null).ToDictionary(s => s.Index, s => s.Link!.Titulo);
        using var f = new LayoutEditorForm(tile.Number, new Size(tile.Monitor.Width, tile.Monitor.Height), tile.Grid, contents);
        if (f.ShowDialog(this) != DialogResult.OK) return;
        SetLayout(tile, f.Result);
        SaveLastSession();
    }

    private void ToggleZone(MonitorTile tile, Segment seg)
    {
        var zones = tile.Grid.Zones.Select((z, i) => i == seg.Index ? z with { Off = !z.Off } : z).ToList();
        GridLayout next = tile.Grid.With(zones);
        if (next.ActiveCount == 0) return;
        SetLayout(tile, next);
        SaveLastSession();
    }

    private void ShowGridPickerForAll(Control anchor)
    {
        MonitorTile? first = _tiles.FirstOrDefault(t => !t.Excluded);
        if (first is null) return;
        GridPicker.Show(this, anchor.PointToScreen(new Point(anchor.Width, 0)), first.Cols, first.Rows, (c, r) =>
        {
            foreach (MonitorTile t in _tiles.Where(t => !t.Excluded)) SetGrid(t, c, r);
            SaveLastSession();
        });
    }

    private void SetGrid(MonitorTile tile, int cols, int rows) => SetLayout(tile, GridLayout.Uniform(cols, rows));

    private void SetLayout(MonitorTile tile, GridLayout layout)
    {
        if (layout.SameAs(tile.Grid)) return;

        List<Segment> old = tile.Segments.ToList();
        Dictionary<int, int> carry = GridLayout.Carry(tile.Grid, old.Where(s => s.Busy).Select(s => s.Index), layout);
        foreach (Segment s in old.Where(s => s.Busy && !carry.ContainsKey(s.Index)))
            CloseSeg(tile, s);

        var moving = carry.Select(kv => (Target: kv.Value, old[kv.Key].Link, old[kv.Key].Launched, old[kv.Key].Pending)).ToList();
        tile.Apply(layout);
        foreach (var m in moving)
        {
            Segment seg = tile.Segments[m.Target];
            seg.Link = m.Link;
            seg.Launched = m.Launched;
            seg.Pending = m.Pending;
            if (seg.Launched is not null) seg.Launched.Hidden = false;
            if (seg.Launched is not null)
                WindowManager.Reposition(seg.Launched, AreaOf(tile, seg));
        }

        CloseSurfaceIfEmpty(tile);
        UpdateInfo();
    }

    private void CloseSeg(MonitorTile tile, Segment seg)
    {
        bool wasMax = tile.Maximized == seg;
        if (wasMax) tile.Maximized = null;
        if (seg.Launched is not null)
        {
            LaunchedWindow w = seg.Launched;
            if (w.Attached) WindowManager.Release(w);
            else if (w.Native) NativeImage.Dispose(w);
            else Task.Run(() => WindowManager.Close(w));
            seg.Launched = null;
        }
        if (wasMax) EnforceTile(tile);
        seg.Pending = null;
        if (seg.Link is not null)
        {
            _openedUrls.Remove(seg.Link.Url);
            seg.Link = null;
        }
        tile.Invalidate();
        UpdateInfo();
        CloseSurfaceIfEmpty(tile);
    }

    private void CloseTileWindows(MonitorTile tile)
    {
        foreach (Segment s in tile.Segments.ToList())
            if (s.Launched is not null || s.Link is not null || s.Pending is not null)
                CloseSeg(tile, s);
    }

    private void CloseAllWindows()
    {
        foreach (MonitorTile t in _tiles.ToList()) CloseTileWindows(t);
        CloseAllSurfaces();
    }

    private DisplaySurface EnsureSurface(MonitorTile tile)
    {
        if (!_surfaces.TryGetValue(tile.Monitor.DeviceName, out DisplaySurface? surface) || surface.IsDisposed)
        {
            surface = new DisplaySurface(tile.Monitor, tile.Number, Icon);
            _surfaces[tile.Monitor.DeviceName] = surface;
            surface.Show();
        }
        surface.Place(tile.Monitor);
        if (Visible && !IsDisposed)
            NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOP, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        return surface;
    }

    private static Rectangle AreaOf(MonitorTile tile, Segment seg) =>
        tile.Maximized == seg ? new Rectangle(0, 0, tile.Monitor.Width, tile.Monitor.Height) : ToSurfaceArea(tile, seg.Area);

    private static Rectangle ToSurfaceArea(MonitorTile tile, Rectangle screenArea) =>
        new(screenArea.X - tile.Monitor.X, screenArea.Y - tile.Monitor.Y,
            screenArea.Width, screenArea.Height);

    private void CloseSurfaceIfEmpty(MonitorTile tile)
    {
        if (tile.Segments.Any(s => s.Launched is not null || s.Pending is not null)) return;
        if (!_surfaces.Remove(tile.Monitor.DeviceName, out DisplaySurface? surface)) return;
        if (!surface.IsDisposed) surface.Close();
    }

    private void CloseAllSurfaces()
    {
        foreach (Segment s in _tiles.SelectMany(t => t.Segments))
            if (s.Launched is { Attached: true } w)
                WindowManager.Release(w);
        foreach (DisplaySurface surface in _surfaces.Values.ToList())
            if (!surface.IsDisposed) surface.Close();
        _surfaces.Clear();
    }

    private void CloseInFlightLaunches()
    {
        foreach (Task<LaunchedWindow?> launch in _pendingLaunches.ToList())
        {
            try
            {
                if (launch.Wait(TimeSpan.FromSeconds(20)) && launch.Result is { } w)
                    WindowManager.Close(w);
            }
            catch { }
        }
        _pendingLaunches.Clear();
    }

    private void CloseAll()
    {
        if (_tiles.All(t => t.Segments.All(s => s.Launched is null && s.Pending is null)))
        {
            MessageBox.Show(this, "No hay ventanas abiertas por este programa.", "Cerrar todas",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        CloseAllWindows();
        SaveLastSession();
    }

    private void ToggleExcluded(MonitorTile tile)
    {
        if (!tile.Excluded) CloseTileWindows(tile);
        tile.Excluded = !tile.Excluded;
        if (tile.Excluded) _excludedDevices.Add(tile.Monitor.DeviceName);
        else _excludedDevices.Remove(tile.Monitor.DeviceName);
        if (tile.Monitor.IsPrimary)
        {
            _skipPrimaryTouched = true;
            _chkSkipPrimary.Checked = tile.Excluded;
        }
        tile.Invalidate();
        UpdateInfo();
        SaveLastSession();
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

    private LayoutProfile Snapshot(string name) => new()
    {
        Name = name,
        Browser = _browser?.Name,
        Saved = DateTime.Now,
        Monitors = _tiles.Select(t => new MonitorLayout
        {
            Number = t.Number,
            Device = t.Monitor.DeviceName,
            Primary = t.Monitor.IsPrimary,
            Excluded = t.Excluded,
            Zones = t.Segments.Count,
            Cols = t.Cols,
            Rows = t.Rows,
            Layout = t.Grid.IsUniform ? null : t.Grid.Zones.ToList(),
            Links = t.Segments.Where(s => s.Link is not null)
                              .Select(s => new ZoneLayout { Zone = s.Index, Titulo = s.Link!.Titulo, Url = s.Link.Url })
                              .ToList(),
        }).ToList(),
    };

    private void SaveLastSession()
    {
        if (!_sessionReady || _applyingProfile) return;
        ProfileStore.Put(Snapshot(ProfileStore.LastSession));
    }

    private void RunStartupProfile(string name)
    {
        LayoutProfile? p = ProfileStore.Get(name);
        if (p is null && !string.IsNullOrWhiteSpace(name) && !ProfileStore.IsLastSession(name))
            _tray.ShowBalloonTip(4000, "IGP_Soft", $"No existe el perfil «{name}».", ToolTipIcon.Warning);

        if (p is not null && p.LinkCount > 0 && _browser is not null)
        {
            ApplyProfile(p);
            HideToTray();
        }
        _sessionReady = true;
        if (p is not null) SaveLastSession();
    }

    private void ApplyProfile(LayoutProfile p)
    {
        if (p.Browser is not null && !p.Browser.Equals(_browser?.Name, StringComparison.OrdinalIgnoreCase) &&
            _browsers.Any(b => b.Name.Equals(p.Browser, StringComparison.OrdinalIgnoreCase)))
            _cboBrowser.SelectedItem = _browsers.First(b => b.Name.Equals(p.Browser, StringComparison.OrdinalIgnoreCase)).Name;
        if (_browser is null) return;
        SyncGroupsWith(p);

        var plan = new List<(MonitorTile Tile, MonitorLayout Layout)>();
        var used = new HashSet<MonitorTile>();
        foreach (MonitorLayout ml in p.Monitors)
            if (_tiles.FirstOrDefault(t => !used.Contains(t) &&
                    t.Monitor.DeviceName.Equals(ml.Device, StringComparison.OrdinalIgnoreCase)) is { } t)
            { used.Add(t); plan.Add((t, ml)); }
        foreach (MonitorLayout ml in p.Monitors.Where(ml => plan.All(x => x.Layout != ml)))
            if (_tiles.FirstOrDefault(t => !used.Contains(t) && t.Number == ml.Number) is { } t)
            { used.Add(t); plan.Add((t, ml)); }

        _applyingProfile = true;
        try
        {
            foreach (MonitorTile t in _tiles.Where(t => !used.Contains(t))) CloseTileWindows(t);

            var wanted = new List<(MonitorTile Tile, Segment Seg, LinkDef Link)>();
            foreach ((MonitorTile t, MonitorLayout ml) in plan)
            {
                if (t.Excluded != ml.Excluded) ToggleExcluded(t);
                if (t.Excluded) continue;

                (int cols, int rows) = ml.Cols > 0 && ml.Rows > 0 ? (ml.Cols, ml.Rows) : MonitorTile.LegacyGrid(ml.Zones);
                SetLayout(t, GridLayout.FromSaved(cols, rows, ml.Layout));

                foreach (Segment s in t.Segments)
                {
                    if (s.Disabled) continue;
                    ZoneLayout? z = ml.Links.FirstOrDefault(l => l.Zone == s.Index);
                    LinkDef? link = z is null ? null
                        : LinkStore.Links.FirstOrDefault(l => l.Url.Equals(z.Url, StringComparison.OrdinalIgnoreCase))
                          ?? new LinkDef(z.Titulo, z.Url);
                    if (s.Link?.Url.Equals(link?.Url, StringComparison.OrdinalIgnoreCase) == true) continue;

                    if (s.Link is not null || s.Launched is not null || s.Pending is not null) CloseSeg(t, s);
                    if (link is not null) wanted.Add((t, s, link));
                }
            }

            foreach ((MonitorTile t, Segment s, LinkDef link) in wanted)
                if (!_openedUrls.Contains(link.Url))
                    AssignAndOpen(t, s, link, quiet: true);
        }
        finally { _applyingProfile = false; }

        UpdateInfo();
        SaveLastSession();
    }

    private void IdentifyMonitors()
    {
        CloseIdentifyForms();

        foreach ((MonitorTile tile, MonitorInfo m) in _tiles.SelectMany(t => (t.Monitor.Members ?? new[] { t.Monitor }).Select(p => (t, p))))
        {
            bool excl = tile.Excluded;
            const int w = 320, h = 240;
            var f = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                ShowInTaskbar = false,
                TopMost = true,
                BackColor = excl ? Color.FromArgb(71, 85, 105) : Theme.Accent,
                ControlBox = false,
                Size = new Size(w, h),
            };
            using (var shape = Theme.Round(new RectangleF(0, 0, w, h), 28)) f.Region = new Region(shape);
            int number = tile.Number;
            string caption = excl ? "Omitida" : tile.Monitor.IsCombined ? "Pantalla combinada" : m.IsPrimary ? "Monitor principal" : "Monitor";
            f.Paint += (_, e) =>
            {
                Theme.Smooth(e.Graphics);
                using var big = Theme.Ui(96f, FontStyle.Bold);
                TextRenderer.DrawText(e.Graphics, number.ToString(), big, new Rectangle(0, 10, w, 170), Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(e.Graphics, caption, Theme.Section, new Rectangle(0, 172, w, 40), Color.FromArgb(219, 234, 254),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };

            f.Show();
            NativeMethods.SetWindowPos(f.Handle, IntPtr.Zero,
                m.X + (m.Width - w) / 2, m.Y + (m.Height - h) / 2, w, h,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_SHOWWINDOW);

            var timer = new System.Windows.Forms.Timer { Interval = 3000 };
            Form fc = f;
            timer.Tick += (_, _) => { timer.Stop(); timer.Dispose(); if (!fc.IsDisposed) fc.Close(); };
            timer.Start();
            _identifyForms.Add(f);
        }
    }

    private void CloseIdentifyForms()
    {
        foreach (Form f in _identifyForms)
            if (!f.IsDisposed) f.Close();
        _identifyForms.Clear();
    }

    private sealed class DisplaySurface : Form
    {
        public int Number { get; set; }

        public DisplaySurface(MonitorInfo monitor, int number, Icon? icon)
        {
            Number = number;
            Text = $"IGP_Soft — Monitor {number}";
            if (icon is not null) Icon = icon;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = true;
            BackColor = Color.White;
            Place(monitor);
        }

        public void Place(MonitorInfo monitor)
        {
            Bounds = new Rectangle(monitor.X, monitor.Y, monitor.Width, monitor.Height);
            if (IsHandleCreated)
                NativeMethods.SetWindowPos(Handle, IntPtr.Zero,
                    monitor.X, monitor.Y, monitor.Width, monitor.Height,
                    NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_SHOWWINDOW);
        }

        public void SetContents(IEnumerable<string> titles)
        {
            string list = string.Join(", ", titles);
            string text = $"IGP_Soft — Monitor {Number}" + (list.Length > 0 ? " · " + list : "");
            if (Text != text) Text = text;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style |= 0x02000000 | 0x04000000;
                return cp;
            }
        }
    }

    internal sealed class Segment
    {
        public int Index;
        public ZoneSpec Spec = new(0, 0);
        public Rectangle Area;
        public RectangleF TileRect;
        public LinkDef? Link;
        public LaunchedWindow? Launched;
        public object? Pending;
        public bool Disabled => Spec.Off;
        public bool Busy => Link is not null || Launched is not null || Pending is not null;
    }

    private sealed class MonitorTile : Panel
    {
        private const TextFormatFlags C = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;

        public int Number { get; set; }
        public MonitorInfo Monitor { get; }
        public bool DropTarget { get; set; }
        public bool Excluded { get; set; }
        public Bitmap? Preview { get; set; }
        public Segment? Maximized { get; set; }
        public GridLayout Grid { get; private set; } = GridLayout.Uniform(1, 1);
        public int Cols => Grid.Cols;
        public int Rows => Grid.Rows;
        public List<Segment> Segments { get; } = new();
        public IEnumerable<Segment> Usable => Segments.Where(s => !s.Disabled);

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

        public MonitorTile(int number, MonitorInfo monitor)
        {
            Number = number;
            Monitor = monitor;
            DoubleBuffered = true;
            Cursor = Cursors.Hand;
            BackColor = Theme.Background;
            MouseEnter += (_, _) => { _hover = true; Invalidate(); };
            MouseLeave += (_, _) => { _hover = false; _hoverSeg = null; _hoverCtrl = false; Invalidate(); };
            Apply(GridLayout.Uniform(1, 1));
        }

        public static (int cols, int rows) LegacyGrid(int n) => n switch
        {
            2 => (2, 1),
            3 => (3, 1),
            4 => (2, 2),
            6 => (3, 2),
            8 => (4, 2),
            9 => (3, 3),
            _ => (1, 1),
        };

        public void Apply(GridLayout layout)
        {
            Maximized = null;
            Grid = layout;
            Segments.Clear();
            var bounds = new Rectangle(Monitor.X, Monitor.Y, Monitor.Width, Monitor.Height);
            for (int i = 0; i < layout.Zones.Count; i++)
                Segments.Add(new Segment { Index = i, Spec = layout.Zones[i], Area = layout.AreaOf(layout.Zones[i], bounds) });
            LayoutSegments();
            Invalidate();
        }

        public int DisplayNumber(Segment seg) => Usable.TakeWhile(s => s != seg).Count() + 1;

        private void LayoutSegments()
        {
            foreach (Segment s in Segments)
                s.TileRect = Grid.AreaOf(s.Spec, new SizeF(Width, Height));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutSegments();
        }

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

            Segment? seg = Segments.FirstOrDefault(s => s.TileRect.Contains(e.Location));
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
            Segment? over = Excluded || _ctrlRect.Contains(e.Location) ? null : Segments.FirstOrDefault(s => s.TileRect.Contains(e.Location));
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
                    RectangleF zr = s.TileRect;
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
