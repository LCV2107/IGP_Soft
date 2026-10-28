using System.Drawing.Imaging;
using System.Threading;

namespace IGP_Soft;

internal sealed class PantallasLocales : IPantallas, IDisposable
{
    private const string OrderKey = "ordenMonitores";

    private readonly Icon? _icon;
    private readonly SynchronizationContext _ui;
    private readonly List<PantallaMonitor> _pantallas = new();
    private readonly HashSet<string> _openedUrls = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _excludedDevices = new();
    private readonly Dictionary<string, DisplaySurface> _surfaces = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Form> _identifyForms = new();
    private readonly List<Task<LaunchedWindow?>> _pendingLaunches = new();
    private readonly Dictionary<string, List<Task>> _windowTasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Windows.Forms.Timer _guard = new() { Interval = 1500 };
    private readonly System.Windows.Forms.Timer _maintenance = new() { Interval = 5000 };
    private List<MonitorInfo> _monitors = new();
    private List<string> _physicalOrder = new();
    private bool _skipPrimary;
    private bool _skipPrimaryTouched;
    private bool _applyingProfile;
    private bool _disposed;
    private DateTime _lastReload = DateTime.Now;
    private long _seq;

    public PantallasLocales(Icon? icon)
    {
        _icon = icon;
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        Browsers = BrowserLocator.InstalledAll();
        string? want = Config.Get("browser") ?? BrowserLocator.DefaultName();
        Browser = Browsers.FirstOrDefault(b => b.Name.Equals(want, StringComparison.OrdinalIgnoreCase))
                  ?? Browsers.FirstOrDefault();
    }

    public event Action? SurfaceShown;
    public event Action? ZoneOpened;
    public event Action<Resultado>? Aviso;
    public event Action<EstadoPantallas>? Cambio;

    public List<Browser> Browsers { get; }
    public Browser? Browser { get; private set; }
    public IReadOnlyList<PantallaMonitor> Monitores => _pantallas;
    public bool SkipPrimary => _skipPrimary;
    public bool SessionReady { get; set; }
    public bool IsIdentifying => _identifyForms.Any(f => !f.IsDisposed);
    public int OpenCount => _pantallas.Sum(t => t.Segments.Count(s => s.Launched is not null));
    public int ZoneCount => _pantallas.Where(t => !t.Excluded).Sum(t => t.Usable.Count());
    public bool AnyBusy => _pantallas.Any(t => t.Segments.Any(s => s.Launched is not null || s.Pending is not null));
    public bool IsOpen(string url) => _openedUrls.Contains(url);

    public EstadoPantallas Estado => new(
        _seq, Environment.MachineName, Browser?.Name, Browsers.Select(b => b.Name).ToList(), _skipPrimary, HasCustomNumbering(),
        _pantallas.Select(EstadoPantallas.MonitorDesde).ToList(), LinkStore.Links.ToList(), ProfileStore.Items.ToList());

    public void Refresh() => Notify();

    public void Start()
    {
        Profiles.CleanupOld();
        ReloadMonitors();
        _guard.Tick += (_, _) =>
        {
            CheckClosedWindows();
            EnforceWindows();
        };
        _guard.Start();
        _maintenance.Tick += (_, _) =>
        {
            int hours = Profiles.ReloadHours;
            if (hours > 0 && DateTime.Now - _lastReload >= TimeSpan.FromHours(hours)) ReloadAll();
        };
        _maintenance.Start();
        WindowWatcher.Start(OnWindowMoved);
        AssetStore.Moved += OnAssetsMoved;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _guard.Stop();
        _maintenance.Stop();
        WindowWatcher.Stop();
        AssetStore.Moved -= OnAssetsMoved;
        CloseIdentifyForms();
        CloseAllSurfaces();
        CloseInFlightLaunches();
        _disposed = true;
        _guard.Dispose();
        _maintenance.Dispose();
    }

    private void Notify()
    {
        if (_disposed) return;
        _seq++;
        foreach (PantallaMonitor t in _pantallas)
            if (_surfaces.TryGetValue(t.Device, out DisplaySurface? surface) && !surface.IsDisposed)
                surface.SetContents(t.Usable.Where(s => s.Link is not null).Select(s => s.Link!.Titulo.TrimStart('▣', ' ')));
        Cambio?.Invoke(Estado);
    }

    private void Avisar(Resultado r)
    {
        if (!_disposed && r.Mensaje is not null) Aviso?.Invoke(r);
    }

    public void SetBrowser(string name)
    {
        Browser? b = Browsers.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (b is null || ReferenceEquals(b, Browser)) return;
        Browser = b;
        Config.Set("browser", b.Name);
        Notify();
    }

    public void ReloadSettings()
    {
        Config.Reload();
        LinkStore.Load();
        ProfileStore.Load();
        if (Config.Get("browser") is { } want) SetBrowser(want);
        Notify();
    }

    public void SetSkipPrimary(bool value)
    {
        _skipPrimaryTouched = true;
        if (_skipPrimary == value) return;
        _skipPrimary = value;
        ApplySkipPrimary();
        SaveLastSession();
    }

    public void ReloadMonitors()
    {
        CloseIdentifyForms();

        List<MonitorInfo> current = MonitorGroups.Apply(MonitorEnumerator.GetOrdered());
        var currentDevices = current.Select(m => m.DeviceName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (PantallaMonitor missing in _pantallas.Where(t => !currentDevices.Contains(t.Device)).ToList())
            CloseTileWindows(missing);

        var prev = new Dictionary<string, (GridLayout layout, List<(LinkDef? Link, LaunchedWindow? Win, object? Pending)> items)>();
        foreach (PantallaMonitor t in _pantallas)
            prev[t.Device] = (t.Grid, t.Segments.Select(s => (s.Link, s.Launched, s.Pending)).ToList());
        _pantallas.Clear();

        _monitors = SavedOrder(current);
        _physicalOrder = MonitorEnumerator.GetOrdered().Select(m => m.DeviceName).ToList();

        if (!_skipPrimaryTouched)
        {
            bool auto = _monitors.Count > LinkStore.Links.Count;
            if (auto != _skipPrimary)
            {
                _skipPrimary = auto;
                _skipPrimaryTouched = true;
            }
        }

        for (int i = 0; i < _monitors.Count; i++)
        {
            MonitorInfo m = _monitors[i];
            var tile = new PantallaMonitor(i + 1, m) { Excluded = _excludedDevices.Contains(m.DeviceName) };

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
            _pantallas.Add(tile);

            if (_surfaces.TryGetValue(m.DeviceName, out DisplaySurface? surface) && !surface.IsDisposed)
            {
                surface.Place(m);
                foreach (Segment s in tile.Segments)
                    if (s.Launched is not null)
                        WindowManager.Reposition(s.Launched, AreaOf(tile, s));
            }
        }

        ApplySkipPrimary();
    }

    private static List<MonitorInfo> SavedOrder(List<MonitorInfo> current)
    {
        string[] saved = (Config.Get(OrderKey) ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries);
        var ordered = saved.Select(d => current.FirstOrDefault(m => m.DeviceName.Equals(d, StringComparison.OrdinalIgnoreCase)))
                           .OfType<MonitorInfo>().ToList();
        ordered.AddRange(current.Where(m => !ordered.Contains(m)));
        return ordered;
    }

    public void SwapNumbers(PantallaMonitor a, PantallaMonitor b)
    {
        (a.Number, b.Number) = (b.Number, a.Number);
        ApplyNumbering();
    }

    private void ApplyNumbering()
    {
        _pantallas.Sort((a, b) => a.Number.CompareTo(b.Number));
        _monitors = _pantallas.Select(t => t.Monitor).ToList();
        Config.Set(OrderKey, string.Join("|", _pantallas.Select(t => t.Device)));
        foreach (PantallaMonitor t in _pantallas)
            if (_surfaces.TryGetValue(t.Device, out DisplaySurface? surface) && !surface.IsDisposed)
                surface.Number = t.Number;
        Notify();
        SaveLastSession();
    }

    private static int NaturalIndex(MonitorInfo m, List<string> physical)
    {
        var members = m.Members ?? new[] { m };
        return members.Select(x => physical.FindIndex(d => d.Equals(x.DeviceName, StringComparison.OrdinalIgnoreCase)))
                      .Where(i => i >= 0).DefaultIfEmpty(99).Min();
    }

    private List<PantallaMonitor> NaturalOrder() =>
        _pantallas.OrderBy(t => NaturalIndex(t.Monitor, _physicalOrder)).ToList();

    public void ResetNumbering()
    {
        List<PantallaMonitor> natural = NaturalOrder();
        for (int i = 0; i < natural.Count; i++) natural[i].Number = i + 1;
        ApplyNumbering();
        Config.Set(OrderKey, "");
    }

    public bool HasCustomNumbering() => !string.IsNullOrEmpty(Config.Get(OrderKey)) &&
        !_pantallas.SequenceEqual(NaturalOrder());

    public List<MonitorInfo> PhysicalMonitors() => MonitorEnumerator.GetOrdered();

    public List<OpenWindow> ListOpenWindows() => WindowManager.ListOpenWindows();

    public static string SpecOf(OpenWindow w) => WindowManager.SpecOf(w);

    public void SeparateMonitors(PantallaMonitor tile)
    {
        if (!tile.Monitor.IsCombined) return;
        var members = MonitorGroups.MembersOf(tile.Device);
        SetGroups(MonitorGroups.Saved().Where(g => !g.SequenceEqual(members, StringComparer.OrdinalIgnoreCase)).ToList());
    }

    public void SetGroups(List<List<string>> groups)
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

    public void CloseMonitor(PantallaMonitor tile)
    {
        if (!tile.Segments.Any(s => s.Busy)) return;
        CloseTileWindows(tile);
        SaveLastSession();
    }

    private void ApplySkipPrimary()
    {
        foreach (PantallaMonitor t in _pantallas)
        {
            if (!t.Monitor.IsPrimary) continue;
            if (_skipPrimary)
            {
                CloseTileWindows(t);
                t.Excluded = true;
                _excludedDevices.Add(t.Device);
            }
            else
            {
                t.Excluded = false;
                _excludedDevices.Remove(t.Device);
            }
        }
        Notify();
    }

    private void CheckClosedWindows()
    {
        bool cambio = false;
        foreach (PantallaMonitor t in _pantallas.ToList())
        {
            foreach (Segment s in t.Segments.ToList())
            {
                if (s.Launched is null || NativeMethods.IsWindow(s.Launched.Hwnd)) continue;

                LaunchedWindow w = s.Launched;
                s.Launched = null;
                if (s.Link is not null) { _openedUrls.Remove(s.Link.Url); s.Link = null; }
                Task.Run(() => WindowManager.Close(w));
                cambio = true;
            }
            CloseSurfaceIfEmpty(t);
        }
        if (cambio) Notify();
    }

    private void OnWindowMoved(IntPtr hwnd)
    {
        if (IsIdentifying) return;
        foreach (PantallaMonitor t in _pantallas)
            foreach (Segment s in t.Segments)
                if (s.Launched is { } w && w.Hwnd == hwnd)
                {
                    WindowManager.Reposition(w, AreaOf(t, s));
                    return;
                }
    }

    private void EnforceWindows()
    {
        if (IsIdentifying) return;
        foreach (PantallaMonitor t in _pantallas) EnforceTile(t);
    }

    private static void EnforceTile(PantallaMonitor t)
    {
        foreach (Segment s in t.Segments)
        {
            if (s.Launched is null) continue;
            s.Launched.Hidden = t.Maximized is not null && s != t.Maximized;
            WindowManager.Reposition(s.Launched, AreaOf(t, s));
        }
    }

    public bool ReloadAll()
    {
        _lastReload = DateTime.Now;
        bool any = _pantallas.Any(t => t.Segments.Any(s => s.Launched is { Attached: false, Native: false }));
        if (!any) return false;
        _ = ReloadAllAsync();
        return true;
    }

    private async Task ReloadAllAsync()
    {
        LayoutProfile snapshot = Snapshot("recarga");
        CloseAllWindows();
        await Task.Delay(3500);
        if (!_disposed) ApplyProfile(snapshot);
    }

    public static Bitmap? CaptureMonitor(MonitorInfo monitor, int width)
    {
        try
        {
            int tw = Math.Clamp(width, 80, 1920);
            int th = Math.Max(1, (int)(tw * (double)monitor.Height / monitor.Width));
            using var full = new Bitmap(monitor.Width, monitor.Height);
            using (Graphics gf = Graphics.FromImage(full))
                gf.CopyFromScreen(monitor.X, monitor.Y, 0, 0, new Size(monitor.Width, monitor.Height));
            var thumb = new Bitmap(tw, th);
            using (Graphics gt = Graphics.FromImage(thumb))
            {
                gt.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
                gt.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                gt.DrawImage(full, new Rectangle(0, 0, tw, th));
            }
            return thumb;
        }
        catch { return null; }
    }

    public async Task<Resultado> OpenAsync(PantallaMonitor tile, Segment seg, LinkDef link, bool quiet = false)
    {
        bool isWindow = WindowManager.IsWindowSpec(link.Url);
        if (tile.Excluded || seg.Disabled || seg.Pending is not null || (Browser is null && !isWindow))
            return Resultado.Falla("no_disponible");

        if (_openedUrls.Contains(link.Url) && seg.Link?.Url != link.Url)
        {
            var r = new Resultado(false, "enlace_en_uso", "Enlace en uso", $"El enlace «{link.Titulo}» ya está abierto en otra zona.");
            Avisar(r);
            return r;
        }

        if (seg.Launched is not null || seg.Link is not null) CloseSeg(tile, seg);

        DisplaySurface surface;
        try { surface = EnsureSurface(tile); }
        catch (Exception ex)
        {
            CloseSurfaceIfEmpty(tile);
            var r = new Resultado(false, "pantalla", "Error", $"No se pudo preparar la pantalla:\n{ex.Message}", Nivel.Error);
            Avisar(r);
            return r;
        }

        if (!isWindow && NativeImage.Supports(link.Url) &&
            NativeImage.Show(surface, link.Url, AreaOf(tile, seg)) is { } image)
        {
            seg.Link = link;
            seg.Launched = image;
            _openedUrls.Add(link.Url);
            Notify();
            SaveLastSession();
            EnforceWindows();
            ZoneOpened?.Invoke();
            return Resultado.Bien;
        }

        return isWindow ? await AttachAsync(tile, seg, link, surface, quiet) : await LaunchAsync(tile, seg, link, surface, quiet);
    }

    private async Task<Resultado> AttachAsync(PantallaMonitor tile, Segment seg, LinkDef link, DisplaySurface surface, bool quiet)
    {
        var windowToken = new object();
        seg.Pending = windowToken;
        seg.Link = link;
        _openedUrls.Add(link.Url);
        Notify();

        string device = tile.Device;
        Rectangle windowArea = AreaOf(tile, seg);
        IntPtr windowParent = surface.Handle;
        Task<(LaunchedWindow? Win, string? Error)> attaching =
            Task.Run(() => WindowManager.Attach(link.Url, windowArea, windowParent));
        TrackWindowTask(device, attaching);
        (LaunchedWindow? Win, string? Error) result;
        try { result = await attaching; }
        catch (Exception ex) { result = (null, ex.Message); }

        (PantallaMonitor? wt, Segment? ws) = FindPending(windowToken);
        if (_disposed || wt is null || ws is null)
        {
            if (result.Win is { } orphan) TrackWindowTask(device, WindowManager.ReleaseAsync(orphan));
            return Resultado.Falla("cancelada");
        }

        ws.Pending = null;
        if (result.Win is null)
        {
            _openedUrls.Remove(link.Url);
            ws.Link = null;
            Notify();
            CloseSurfaceIfEmpty(wt);
            var r = new Resultado(false, "adjuntar", "No se pudo adjuntar",
                quiet ? null : $"No se pudo adjuntar «{link.Titulo.TrimStart('▣', ' ')}».\n\n{result.Error}", Nivel.Aviso);
            Avisar(r);
            return r;
        }
        ws.Launched = result.Win;
        Notify();
        SaveLastSession();
        EnforceWindows();
        ZoneOpened?.Invoke();
        return Resultado.Bien;
    }

    private async Task<Resultado> LaunchAsync(PantallaMonitor tile, Segment seg, LinkDef link, DisplaySurface surface, bool quiet)
    {
        var token = new object();
        seg.Pending = token;
        seg.Link = link;
        _openedUrls.Add(link.Url);
        Notify();
        SaveLastSession();

        Browser browser = Browser!;
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

        (PantallaMonitor? t, Segment? s) = FindPending(token);
        if (_disposed || t is null || s is null)
        {
            if (win is not null) _ = Task.Run(() => WindowManager.Close(win));
            return Resultado.Falla("cancelada");
        }

        s.Pending = null;
        if (win is null)
        {
            _openedUrls.Remove(link.Url);
            s.Link = null;
            Notify();
            CloseSurfaceIfEmpty(t);
            var r = new Resultado(false, "navegador", "No se pudo abrir",
                quiet ? null
                : error is not null
                    ? $"No se pudo abrir el navegador:\n{error}"
                    : $"El navegador se inició, pero no se pudo integrar «{link.Titulo}» en la pantalla. " +
                      "La zona quedó libre para intentarlo nuevamente.", Nivel.Aviso);
            Avisar(r);
            return r;
        }

        s.Launched = win;
        Notify();
        EnforceWindows();
        ZoneOpened?.Invoke();
        return Resultado.Bien;
    }

    private (PantallaMonitor? Tile, Segment? Seg) FindPending(object token)
    {
        foreach (PantallaMonitor t in _pantallas)
            foreach (Segment s in t.Segments)
                if (ReferenceEquals(s.Pending, token)) return (t, s);
        return (null, null);
    }

    public Resultado AutoAssignInOrder()
    {
        var targets = new List<(PantallaMonitor T, Segment S)>();
        foreach (PantallaMonitor t in _pantallas)
            if (!t.Excluded)
                foreach (Segment s in t.Usable)
                    targets.Add((t, s));

        if (targets.Count == 0)
        {
            var r = new Resultado(false, "sin_zonas", "Abrir en orden", "No hay zonas disponibles (todos los monitores están omitidos).");
            Avisar(r);
            return r;
        }

        int n = Math.Min(targets.Count, LinkStore.Links.Count);
        for (int i = 0; i < n; i++)
        {
            (PantallaMonitor t, Segment s) = targets[i];
            LinkDef link = LinkStore.Links[i];
            if (s.Link?.Url == link.Url || s.Pending is not null) continue;
            if (_openedUrls.Contains(link.Url)) continue;
            _ = OpenAsync(t, s, link);
        }
        return Resultado.Bien;
    }

    public void SetGrid(PantallaMonitor tile, int cols, int rows)
    {
        SetLayout(tile, GridLayout.Uniform(cols, rows));
        SaveLastSession();
    }

    public void SetGridAll(int cols, int rows)
    {
        foreach (PantallaMonitor t in _pantallas.Where(t => !t.Excluded)) SetLayout(t, GridLayout.Uniform(cols, rows));
        SaveLastSession();
    }

    public void ChangeLayout(PantallaMonitor tile, GridLayout layout)
    {
        SetLayout(tile, layout);
        SaveLastSession();
    }

    public void ToggleZone(PantallaMonitor tile, Segment seg)
    {
        var zones = tile.Grid.Zones.Select((z, i) => i == seg.Index ? z with { Off = !z.Off } : z).ToList();
        GridLayout next = tile.Grid.With(zones);
        if (next.ActiveCount == 0) return;
        SetLayout(tile, next);
        SaveLastSession();
    }

    private void SetLayout(PantallaMonitor tile, GridLayout layout)
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
        Notify();
    }

    public void CloseZone(PantallaMonitor tile, Segment seg)
    {
        CloseSeg(tile, seg);
        SaveLastSession();
    }

    private void CloseSeg(PantallaMonitor tile, Segment seg)
    {
        bool wasMax = tile.Maximized == seg;
        if (wasMax) tile.Maximized = null;
        if (seg.Launched is not null)
        {
            LaunchedWindow w = seg.Launched;
            if (w.Attached) TrackWindowTask(tile.Device, WindowManager.ReleaseAsync(w));
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
        Notify();
        CloseSurfaceIfEmpty(tile);
    }

    private void CloseTileWindows(PantallaMonitor tile)
    {
        foreach (Segment s in tile.Segments.ToList())
            if (s.Launched is not null || s.Link is not null || s.Pending is not null)
                CloseSeg(tile, s);
    }

    public void CloseAllWindows()
    {
        foreach (PantallaMonitor t in _pantallas.ToList()) CloseTileWindows(t);
        CloseAllSurfaces();
    }

    private DisplaySurface EnsureSurface(PantallaMonitor tile)
    {
        if (!_surfaces.TryGetValue(tile.Device, out DisplaySurface? surface) || surface.IsDisposed)
        {
            surface = new DisplaySurface(tile.Monitor, tile.Number, _icon);
            _surfaces[tile.Device] = surface;
            surface.Show();
        }
        surface.Place(tile.Monitor);
        SurfaceShown?.Invoke();
        return surface;
    }

    private static Rectangle AreaOf(PantallaMonitor tile, Segment seg) =>
        tile.Maximized == seg ? new Rectangle(0, 0, tile.Monitor.Width, tile.Monitor.Height) : ToSurfaceArea(tile, seg.Area);

    private static Rectangle ToSurfaceArea(PantallaMonitor tile, Rectangle screenArea) =>
        new(screenArea.X - tile.Monitor.X, screenArea.Y - tile.Monitor.Y,
            screenArea.Width, screenArea.Height);

    private void CloseSurfaceIfEmpty(PantallaMonitor tile)
    {
        if (tile.Segments.Any(s => s.Launched is not null || s.Pending is not null)) return;
        if (WindowTasksPending(tile.Device)) return;
        if (!_surfaces.Remove(tile.Device, out DisplaySurface? surface)) return;
        if (!surface.IsDisposed) surface.Close();
    }

    private void TrackWindowTask(string device, Task task)
    {
        if (!_windowTasks.TryGetValue(device, out List<Task>? list))
            _windowTasks[device] = list = new List<Task>();
        list.Add(task);
        task.ContinueWith(_ => _ui.Post(_ =>
        {
            if (_disposed) return;
            if (_windowTasks.TryGetValue(device, out List<Task>? l)) l.Remove(task);
            if (_pantallas.FirstOrDefault(t => t.Device.Equals(device, StringComparison.OrdinalIgnoreCase)) is { } tile)
                CloseSurfaceIfEmpty(tile);
        }, null), TaskScheduler.Default);
    }

    private bool WindowTasksPending(string device) =>
        _windowTasks.TryGetValue(device, out List<Task>? list) && list.Any(t => !t.IsCompleted);

    private void CloseAllSurfaces()
    {
        var releases = _pantallas.SelectMany(t => t.Segments)
                                 .Where(s => s.Launched is { Attached: true })
                                 .Select(s => WindowManager.ReleaseAsync(s.Launched!))
                                 .Concat(_windowTasks.Values.SelectMany(l => l))
                                 .ToList();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (releases.Any(t => !t.IsCompleted) && sw.ElapsedMilliseconds < 5000)
        {
            Application.DoEvents();
            Thread.Sleep(20);
        }
        if (releases.Any(t => !t.IsCompleted))
            Diagnostico.Log("Al cerrar: alguna ventana adjunta no respondió a tiempo para volver al escritorio");
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

    public void CloseEverything()
    {
        var windows = _pantallas.SelectMany(t => t.Segments)
                                .Where(s => s.Launched is not null)
                                .Select(s => s.Launched!)
                                .ToList();
        CloseAllSurfaces();
        foreach (LaunchedWindow window in windows) WindowManager.Close(window);
    }

    public bool CloseAll()
    {
        if (_pantallas.All(t => t.Segments.All(s => s.Launched is null && s.Pending is null))) return false;
        CloseAllWindows();
        SaveLastSession();
        return true;
    }

    public void ToggleExcluded(PantallaMonitor tile)
    {
        if (!tile.Excluded) CloseTileWindows(tile);
        tile.Excluded = !tile.Excluded;
        if (tile.Excluded) _excludedDevices.Add(tile.Device);
        else _excludedDevices.Remove(tile.Device);
        if (tile.Monitor.IsPrimary)
        {
            _skipPrimaryTouched = true;
            if (_skipPrimary != tile.Excluded)
            {
                _skipPrimary = tile.Excluded;
                ApplySkipPrimary();
            }
        }
        Notify();
        SaveLastSession();
    }

    public LayoutProfile Snapshot(string name) => new()
    {
        Name = name,
        Browser = Browser?.Name,
        Saved = DateTime.Now,
        Monitors = _pantallas.Select(t => new MonitorLayout
        {
            Number = t.Number,
            Device = t.Device,
            Hardware = t.Id,
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

    public void SaveLastSession()
    {
        if (!SessionReady || _applyingProfile) return;
        ProfileStore.Put(Snapshot(ProfileStore.LastSession));
    }

    public void ApplyProfile(LayoutProfile p)
    {
        if (p.Browser is not null && !p.Browser.Equals(Browser?.Name, StringComparison.OrdinalIgnoreCase))
            SetBrowser(p.Browser);
        if (Browser is null) return;
        SyncGroupsWith(p);

        var plan = new List<(PantallaMonitor Tile, MonitorLayout Layout)>();
        var used = new HashSet<PantallaMonitor>();
        foreach (MonitorLayout ml in p.Monitors.Where(ml => !string.IsNullOrEmpty(ml.Hardware)))
            if (_pantallas.FirstOrDefault(t => !used.Contains(t) &&
                    t.Id.Equals(ml.Hardware, StringComparison.OrdinalIgnoreCase)) is { } t)
            { used.Add(t); plan.Add((t, ml)); }
        foreach (MonitorLayout ml in p.Monitors.Where(ml => plan.All(x => x.Layout != ml)))
            if (_pantallas.FirstOrDefault(t => !used.Contains(t) &&
                    t.Device.Equals(ml.Device, StringComparison.OrdinalIgnoreCase)) is { } t)
            { used.Add(t); plan.Add((t, ml)); }
        foreach (MonitorLayout ml in p.Monitors.Where(ml => plan.All(x => x.Layout != ml)))
            if (_pantallas.FirstOrDefault(t => !used.Contains(t) && t.Number == ml.Number) is { } t)
            { used.Add(t); plan.Add((t, ml)); }

        _applyingProfile = true;
        try
        {
            foreach (PantallaMonitor t in _pantallas.Where(t => !used.Contains(t))) CloseTileWindows(t);

            var wanted = new List<(PantallaMonitor Tile, Segment Seg, LinkDef Link)>();
            foreach ((PantallaMonitor t, MonitorLayout ml) in plan)
            {
                if (t.Excluded != ml.Excluded) ToggleExcluded(t);
                if (t.Excluded) continue;

                (int cols, int rows) = ml.Cols > 0 && ml.Rows > 0 ? (ml.Cols, ml.Rows) : PantallaMonitor.LegacyGrid(ml.Zones);
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

            foreach ((PantallaMonitor t, Segment s, LinkDef link) in wanted)
                if (!_openedUrls.Contains(link.Url))
                    _ = OpenAsync(t, s, link, quiet: true);
        }
        finally { _applyingProfile = false; }

        Notify();
        SaveLastSession();
    }

    public void IdentifyMonitors()
    {
        CloseIdentifyForms();

        foreach ((PantallaMonitor tile, MonitorInfo m) in _pantallas.SelectMany(t => (t.Monitor.Members ?? new[] { t.Monitor }).Select(p => (t, p))))
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

    public PantallaMonitor? MonitorAt(Point screen) =>
        _pantallas.FirstOrDefault(t => !t.Excluded &&
            new Rectangle(t.Monitor.X, t.Monitor.Y, t.Monitor.Width, t.Monitor.Height).Contains(screen));

    public static Segment? SegmentAt(PantallaMonitor tile, Point screen) => tile.Usable.FirstOrDefault(s => s.Area.Contains(screen));

    public void Maximize(PantallaMonitor tile, Segment seg)
    {
        if (seg.Disabled || !seg.Busy || tile.Segments.Count <= 1 || tile.Maximized == seg) return;
        tile.Maximized = seg;
        EnforceTile(tile);
        if (seg.Launched is not null)
            NativeMethods.SetWindowPos(seg.Launched.Hwnd, NativeMethods.HWND_TOP, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE |
                (seg.Launched.Attached ? NativeMethods.SWP_ASYNCWINDOWPOS : 0));
        Notify();
    }

    public void RestoreGrid(PantallaMonitor tile)
    {
        if (tile.Maximized is null) return;
        tile.Maximized = null;
        EnforceTile(tile);
        Notify();
    }

    public void RestoreAllGrids()
    {
        foreach (PantallaMonitor t in _pantallas.Where(t => t.Maximized is not null).ToList()) RestoreGrid(t);
    }

    public void StepMaximize(PantallaMonitor tile, int dir)
    {
        var candidates = tile.Usable.Where(s => s.Launched is not null).ToList();
        if (candidates.Count == 0) candidates = tile.Usable.ToList();
        if (candidates.Count == 0) return;
        int i = tile.Maximized is null ? (dir > 0 ? 0 : candidates.Count - 1)
              : (candidates.IndexOf(tile.Maximized) + dir + candidates.Count) % candidates.Count;
        Maximize(tile, candidates[Math.Max(0, i)]);
    }

    private void OnAssetsMoved(IReadOnlyDictionary<string, string> map)
    {
        foreach (PantallaMonitor t in _pantallas)
            foreach (Segment s in t.Segments)
            {
                if (s.Link is null || !map.TryGetValue(s.Link.Url, out string? to)) continue;
                _openedUrls.Remove(s.Link.Url);
                s.Link = s.Link with { Url = to };
                _openedUrls.Add(to);
            }
        Notify();
        SaveLastSession();
    }

    private PantallaMonitor? FindMonitor(string id) =>
        _pantallas.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
        ?? _pantallas.FirstOrDefault(p => p.Device.Equals(id, StringComparison.OrdinalIgnoreCase));

    private static Task<Resultado> Ok() => Task.FromResult(Resultado.Bien);

    private static Task<Resultado> NoMonitor() => Task.FromResult(Resultado.Falla("monitor_no_encontrado"));

    private static Task<Resultado> NoZona() => Task.FromResult(Resultado.Falla("zona_no_encontrada"));

    public Task<Resultado> Abrir(string monitor, int zona, LinkDef enlace)
    {
        if (FindMonitor(monitor) is not { } t) return NoMonitor();
        if (t.Segments.FirstOrDefault(s => s.Index == zona) is not { } seg) return NoZona();
        Task<Resultado> open = OpenAsync(t, seg, enlace);
        return Task.FromResult(open.IsCompleted ? open.Result : Resultado.Bien);
    }

    public Task<Resultado> Cerrar(string monitor, int zona)
    {
        if (FindMonitor(monitor) is not { } t) return NoMonitor();
        if (t.Segments.FirstOrDefault(s => s.Index == zona) is not { } seg) return NoZona();
        CloseZone(t, seg);
        return Ok();
    }

    public Task<Resultado> CambiarGrilla(string monitor, GridLayout grilla)
    {
        if (FindMonitor(monitor) is not { } t) return NoMonitor();
        if (grilla.ActiveCount == 0) return Task.FromResult(Resultado.Falla("grilla_no_valida"));
        ChangeLayout(t, grilla);
        return Ok();
    }

    public Task<Resultado> DividirTodos(int columnas, int filas)
    {
        SetGridAll(columnas, filas);
        return Ok();
    }

    public Task<Resultado> AlternarOmitir(string monitor)
    {
        if (FindMonitor(monitor) is not { } t) return NoMonitor();
        ToggleExcluded(t);
        return Ok();
    }

    public Task<Resultado> OmitirPrincipal(bool omitir)
    {
        SetSkipPrimary(omitir);
        return Ok();
    }

    public Task<Resultado> CerrarMonitor(string monitor)
    {
        if (FindMonitor(monitor) is not { } t) return NoMonitor();
        CloseMonitor(t);
        return Ok();
    }

    public Task<Resultado> IntercambiarNumeros(string a, string b)
    {
        if (FindMonitor(a) is not { } ta || FindMonitor(b) is not { } tb) return NoMonitor();
        if (ta != tb) SwapNumbers(ta, tb);
        return Ok();
    }

    public Task<Resultado> NumeracionOriginal()
    {
        ResetNumbering();
        return Ok();
    }

    public Task<Resultado> Combinar(List<List<string>> grupos)
    {
        List<MonitorInfo> physical = MonitorEnumerator.GetOrdered();
        string? Device(string id) =>
            physical.FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.DeviceName
            ?? physical.FirstOrDefault(m => m.DeviceName.Equals(id, StringComparison.OrdinalIgnoreCase))?.DeviceName;
        var groups = new List<List<string>>();
        foreach (List<string> g in grupos)
        {
            var devices = g.Select(Device).ToList();
            if (devices.Count < 2 || devices.Any(d => d is null)) return Task.FromResult(Resultado.Falla("monitor_no_encontrado"));
            groups.Add(devices!);
        }
        SetGroups(groups);
        return Ok();
    }

    public Task<Resultado> Separar(string monitor)
    {
        if (FindMonitor(monitor) is not { } t) return NoMonitor();
        SeparateMonitors(t);
        return Ok();
    }

    public Task<Resultado> Ampliar(string monitor, int zona)
    {
        if (FindMonitor(monitor) is not { } t) return NoMonitor();
        if (t.Segments.FirstOrDefault(s => s.Index == zona) is not { } seg) return NoZona();
        Maximize(t, seg);
        return Ok();
    }

    public Task<Resultado> Restaurar(string? monitor)
    {
        if (monitor is null) RestoreAllGrids();
        else if (FindMonitor(monitor) is { } t) RestoreGrid(t);
        else return NoMonitor();
        return Ok();
    }

    public Task<Resultado> AmpliarSiguiente(string monitor, int paso)
    {
        if (FindMonitor(monitor) is not { } t) return NoMonitor();
        StepMaximize(t, Math.Sign(paso) == 0 ? 1 : Math.Sign(paso));
        return Ok();
    }

    public Task<Resultado> AplicarPerfil(LayoutProfile perfil)
    {
        ApplyProfile(perfil);
        return Ok();
    }

    public Task<Resultado> AbrirEnOrden() => Task.FromResult(AutoAssignInOrder());

    public Task<Resultado> CerrarTodo() =>
        Task.FromResult(CloseAll() ? Resultado.Bien : Resultado.Falla("nada_abierto"));

    public Task<Resultado> Identificar()
    {
        IdentifyMonitors();
        return Ok();
    }

    public Task<Resultado> RecargarMonitores()
    {
        ReloadMonitors();
        return Ok();
    }

    public Task<Resultado> RecargarTodo() =>
        Task.FromResult(ReloadAll() ? Resultado.Bien : Resultado.Falla("nada_que_recargar"));

    public Task<Resultado> ElegirNavegador(string nombre)
    {
        if (!Browsers.Any(b => b.Name.Equals(nombre, StringComparison.OrdinalIgnoreCase)))
            return Task.FromResult(Resultado.Falla("navegador_no_instalado"));
        SetBrowser(nombre);
        return Ok();
    }

    public Task<Resultado> GuardarEnlaces(List<LinkDef> enlaces)
    {
        if (enlaces.Count == 0) return Task.FromResult(Resultado.Falla("lista_vacia"));
        LinkStore.Replace(enlaces);
        Notify();
        return Ok();
    }

    public Task<Resultado> GuardarPerfiles(List<LayoutProfile> perfiles)
    {
        var keep = ProfileStore.Get(ProfileStore.LastSession);
        var list = perfiles.Where(x => !ProfileStore.IsLastSession(x.Name)).ToList();
        if (keep is not null) list.Add(keep);
        ProfileStore.ReplaceAll(list);
        Notify();
        return Ok();
    }

    public Task<List<VentanaAbierta>> VentanasAbiertas() =>
        Task.FromResult(WindowManager.ListOpenWindows().Select(w => new VentanaAbierta(w.ProcessName, w.Title, SpecOf(w))).ToList());

    public Task<List<MonitorFisico>> MonitoresFisicos() =>
        Task.FromResult(MonitorEnumerator.GetOrdered().Select(m => new MonitorFisico(m.Id, m.X, m.Y, m.Width, m.Height, m.IsPrimary)).ToList());

    public Task<byte[]?> VistaPrevia(string monitor, int ancho)
    {
        if (FindMonitor(monitor) is not { } t) return Task.FromResult<byte[]?>(null);
        MonitorInfo m = t.Monitor;
        return Task.Run<byte[]?>(() =>
        {
            using Bitmap? bmp = CaptureMonitor(m, ancho);
            if (bmp is null) return null;
            using var ms = new MemoryStream();
            var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
            using var prms = new EncoderParameters(1);
            prms.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 70L);
            bmp.Save(ms, codec, prms);
            return ms.ToArray();
        });
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
}
