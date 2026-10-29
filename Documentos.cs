using System.Runtime.InteropServices;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace IGP_Soft;

internal enum TipoDocumento { Ninguno, Pdf, PowerPoint, Word }

internal static class Documentos
{
    public static TipoDocumento Tipo(string url)
    {
        if (url.Contains("://") || WindowManager.IsWindowSpec(url)) return TipoDocumento.Ninguno;
        string ext;
        try { ext = Path.GetExtension(url).ToLowerInvariant(); }
        catch { return TipoDocumento.Ninguno; }
        return ext switch
        {
            ".pdf" => TipoDocumento.Pdf,
            ".pptx" or ".ppsx" => TipoDocumento.PowerPoint,
            ".docx" => TipoDocumento.Word,
            _ => TipoDocumento.Ninguno,
        };
    }

    public static string? Nombre(string url) => Tipo(url) switch
    {
        TipoDocumento.Pdf => "PDF",
        TipoDocumento.PowerPoint => "PowerPoint",
        TipoDocumento.Word => "Word",
        _ => null,
    };

    public static bool EsOffice(string url) => Tipo(url) is TipoDocumento.PowerPoint or TipoDocumento.Word;

    public static bool Existe(string url)
    {
        try { return File.Exists(AssetStore.Resolve(url)); }
        catch { return false; }
    }

    public static async Task<LaunchedWindow?> MostrarPdf(Control surface, string url, Rectangle area)
    {
        PdfDocument doc;
        try { doc = await CargarPdf(AssetStore.Resolve(url)); }
        catch (Exception ex)
        {
            Diagnostico.Log($"PDF «{url}» no se pudo leer: {ex.Message}");
            return null;
        }
        if (doc.PageCount == 0 || surface.IsDisposed) return null;

        var view = new VisorPdf(doc) { Bounds = area };
        surface.Controls.Add(view);
        view.BringToFront();
        view.CreateControl();
        return new LaunchedWindow
        {
            ProcessId = Environment.ProcessId, Hwnd = view.Handle, ProfileDir = "", ProcessName = "",
            Borderless = false, ParentHwnd = surface.Handle, DesiredArea = area, Native = true, Tag = view,
        };
    }

    public static async Task<PdfDocument> CargarPdf(string path)
    {
        StorageFile file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
        return await PdfDocument.LoadFromFileAsync(file);
    }

    public static (LaunchedWindow? Win, string? Error) AbrirOffice(string url, Rectangle area, IntPtr parent)
    {
        string path = Path.GetFullPath(AssetStore.Resolve(url));
        if (!File.Exists(path)) return (null, "El archivo ya no existe.");
        if (!TiposPermitidos.Archivo(path)) return (null, "El archivo no es un documento válido o contiene macros.");
        bool ppt = Tipo(url) == TipoDocumento.PowerPoint;
        Type? tipo = Type.GetTypeFromProgID(ppt ? "PowerPoint.Application" : "Word.Application");
        if (tipo is null)
            return (null, ppt ? "Este equipo no tiene PowerPoint instalado." : "Este equipo no tiene Word instalado.");

        DocumentoOffice? d = null;
        try
        {
            d = HiloOffice.Ejecutar(() => ppt ? DocumentoOffice.Presentacion(tipo, path) : DocumentoOffice.Texto(tipo, path));
            if (d.Hwnd == IntPtr.Zero)
            {
                d.Cerrar();
                return (null, "La aplicación abrió el archivo, pero no se encontró su ventana.");
            }
            Diagnostico.Log($"{(ppt ? "PowerPoint" : "Word")} abrió «{Path.GetFileName(path)}»");
            (LaunchedWindow? win, string? error) = WindowManager.Attach(d.Hwnd, area, parent, d.Cerrar);
            if (win is null) d.Cerrar();
            return (win, error);
        }
        catch (Exception ex)
        {
            d?.Cerrar();
            Diagnostico.Log($"Office no pudo abrir «{Path.GetFileName(path)}»: {ex.Message}");
            return (null, $"{(ppt ? "PowerPoint" : "Word")} no pudo abrir el archivo:\n{ex.Message}");
        }
    }
}

internal static class HiloOffice
{
    private static readonly object Gate = new();
    private static Control? _control;

    public static T Ejecutar<T>(Func<T> f) => (T)Control().Invoke(f);

    public static void Ejecutar(Action a) => Control().Invoke(a);

    private static Control Control()
    {
        lock (Gate)
        {
            if (_control is { IsDisposed: false }) return _control;
            using var listo = new ManualResetEventSlim();
            var t = new Thread(() =>
            {
                var c = new Control();
                c.CreateControl();
                _control = c;
                listo.Set();
                Application.Run();
            })
            { IsBackground = true, Name = "IGP_Soft Office" };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            listo.Wait();
            return _control!;
        }
    }
}

internal sealed class DocumentoOffice
{
    private readonly dynamic _app;
    private readonly dynamic _doc;
    private object? _vista;
    private readonly bool _ppt;
    private int _cerrado;

    public IntPtr Hwnd { get; private init; }

    private DocumentoOffice(dynamic app, dynamic doc, bool ppt)
    {
        _app = app;
        _doc = doc;
        _ppt = ppt;
    }

    public static DocumentoOffice Presentacion(Type tipo, string path)
    {
        dynamic app = Activator.CreateInstance(tipo)!;
        dynamic pres = app.Presentations.Open(path, -1, 0, 0);
        dynamic ajustes = pres.SlideShowSettings;
        ajustes.ShowType = 1;
        try { ajustes.ShowPresenterView = 0; } catch { }
        HashSet<IntPtr> antes = VentanasPresentacion();
        dynamic ventana = ajustes.Run();
        IntPtr h = IntPtr.Zero;
        for (int i = 0; i < 50 && h == IntPtr.Zero; i++)
        {
            try { h = new IntPtr(Convert.ToInt64(ventana.HWND)); }
            catch { }
            if (h == IntPtr.Zero || !NativeMethods.IsWindow(h))
                h = VentanasPresentacion().FirstOrDefault(x => !antes.Contains(x));
            if (h == IntPtr.Zero) Thread.Sleep(100);
        }
        return new DocumentoOffice(app, pres, true) { Hwnd = h, _vista = ventana };
    }

    private static HashSet<IntPtr> VentanasPresentacion()
    {
        var r = new HashSet<IntPtr>();
        NativeMethods.EnumWindows((h, _) =>
        {
            var cls = new System.Text.StringBuilder(32);
            NativeMethods.GetClassName(h, cls, cls.Capacity);
            if (cls.ToString() == "screenClass" && NativeMethods.IsWindowVisible(h)) r.Add(h);
            return true;
        }, IntPtr.Zero);
        return r;
    }

    public static DocumentoOffice Texto(Type tipo, string path)
    {
        dynamic app = Activator.CreateInstance(tipo)!;
        try { app.DisplayAlerts = 0; } catch { }
        dynamic doc = app.Documents.Open(path, false, true, false);
        app.Visible = true;
        IntPtr h = IntPtr.Zero;
        try { h = new IntPtr((int)doc.ActiveWindow.Hwnd); } catch { }
        return new DocumentoOffice(app, doc, false) { Hwnd = h };
    }

    public void Cerrar()
    {
        if (Interlocked.Exchange(ref _cerrado, 1) == 1) return;
        try { HiloOffice.Ejecutar(CerrarAqui); }
        catch (Exception ex) { Diagnostico.Log($"Office no cerró el documento: {ex.Message}"); }
    }

    private void CerrarAqui()
    {
        try
        {
            if (_ppt)
            {
                try { _doc.SlideShowWindow.View.Exit(); } catch { }
                try { _doc.Close(); } catch { }
                if ((int)_app.Presentations.Count == 0) _app.Quit();
            }
            else
            {
                try { _doc.Close(0); } catch { }
                if ((int)_app.Documents.Count == 0) _app.Quit(0);
            }
        }
        catch (Exception ex) { Diagnostico.Log($"Office no cerró el documento: {ex.Message}"); }
        finally
        {
            try { if (_vista is not null) Marshal.FinalReleaseComObject(_vista); } catch { }
            try { Marshal.FinalReleaseComObject(_doc); } catch { }
            try { Marshal.FinalReleaseComObject(_app); } catch { }
        }
    }
}

internal sealed class VisorPdf : Control
{
    private readonly PdfDocument _doc;
    private readonly System.Windows.Forms.Timer _resize = new() { Interval = 150 };
    private readonly System.Windows.Forms.Timer _ocultar = new() { Interval = 1600 };
    private readonly Dictionary<int, Bitmap> _cache = new();
    private Size _cacheSize;
    private int _pagina;
    private bool _indicador;
    private int _version;

    public VisorPdf(PdfDocument doc)
    {
        _doc = doc;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Black;
        TabStop = true;
        _resize.Tick += (_, _) => { _resize.Stop(); Renderizar(); };
        _ocultar.Tick += (_, _) => { _ocultar.Stop(); _indicador = false; Invalidate(); };
    }

    public int Pagina => _pagina;

    public int Paginas => (int)_doc.PageCount;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Renderizar();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (!IsHandleCreated) return;
        _resize.Stop();
        _resize.Start();
    }

    public void Ir(int pagina)
    {
        pagina = Math.Clamp(pagina, 0, Paginas - 1);
        if (pagina != _pagina)
        {
            _pagina = pagina;
            Renderizar();
        }
        _indicador = true;
        _ocultar.Stop();
        _ocultar.Start();
        Invalidate();
    }

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Space or Keys.Enter ||
        base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode)
        {
            case Keys.Right or Keys.Down or Keys.PageDown or Keys.Space or Keys.Enter or Keys.N: Ir(_pagina + 1); break;
            case Keys.Left or Keys.Up or Keys.PageUp or Keys.Back or Keys.P: Ir(_pagina - 1); break;
            case Keys.Home: Ir(0); break;
            case Keys.End: Ir(Paginas - 1); break;
            default: return;
        }
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        Ir(_pagina + (e.Delta < 0 ? 1 : -1));
    }

    private async void Renderizar()
    {
        if (IsDisposed || Width < 8 || Height < 8) return;
        Size size = ClientSize;
        if (size != _cacheSize)
        {
            LimpiarCache();
            _cacheSize = size;
        }
        int version = ++_version;
        int pagina = _pagina;
        if (!_cache.ContainsKey(pagina))
        {
            Bitmap? bmp = await Dibujar(_doc, pagina, size);
            if (IsDisposed) { bmp?.Dispose(); return; }
            if (bmp is null || size != _cacheSize) { bmp?.Dispose(); return; }
            if (!_cache.TryAdd(pagina, bmp)) bmp.Dispose();
        }
        if (version != _version) return;
        Invalidate();

        foreach (int vecina in new[] { pagina + 1, pagina - 1 })
        {
            if (vecina < 0 || vecina >= Paginas || _cache.ContainsKey(vecina)) continue;
            Bitmap? bmp = await Dibujar(_doc, vecina, size);
            if (IsDisposed || bmp is null || size != _cacheSize || !_cache.TryAdd(vecina, bmp)) bmp?.Dispose();
        }
        foreach (int lejana in _cache.Keys.Where(k => Math.Abs(k - _pagina) > 2).ToList())
        {
            _cache[lejana].Dispose();
            _cache.Remove(lejana);
        }
    }

    public static async Task<Bitmap?> Dibujar(PdfDocument doc, int pagina, Size area)
    {
        try
        {
            using PdfPage page = doc.GetPage((uint)pagina);
            Size fit = Ajustar(page.Size.Width, page.Size.Height, area);
            using var stream = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(stream, new PdfPageRenderOptions
            {
                DestinationWidth = (uint)fit.Width, DestinationHeight = (uint)fit.Height,
                BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255),
            });
            stream.Seek(0);
            using Stream s = stream.AsStreamForRead();
            using var img = Image.FromStream(s);
            return new Bitmap(img);
        }
        catch (Exception ex)
        {
            Diagnostico.Log($"PDF: no se pudo dibujar la página {pagina + 1}: {ex.Message}");
            return null;
        }
    }

    public static Size Ajustar(double ancho, double alto, Size area)
    {
        if (ancho <= 0 || alto <= 0) return area;
        double k = Math.Min(area.Width / ancho, area.Height / alto);
        return new Size(Math.Max(1, (int)Math.Round(ancho * k)), Math.Max(1, (int)Math.Round(alto * k)));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(BackColor);
        if (_cache.TryGetValue(_pagina, out Bitmap? bmp))
            g.DrawImageUnscaled(bmp, (Width - bmp.Width) / 2, (Height - bmp.Height) / 2);
        if (!_indicador) return;

        Theme.Smooth(g);
        string texto = $"{_pagina + 1} / {Paginas}";
        using Font font = Theme.Ui(14f, FontStyle.Bold);
        Size t = TextRenderer.MeasureText(texto, font);
        var caja = new Rectangle(Width - t.Width - 44, Height - t.Height - 34, t.Width + 24, t.Height + 12);
        using (var fondo = new SolidBrush(Color.FromArgb(190, 20, 20, 20)))
        using (var forma = Theme.Round(caja, caja.Height / 2f))
            g.FillPath(fondo, forma);
        TextRenderer.DrawText(g, texto, font, caja, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private void LimpiarCache()
    {
        foreach (Bitmap b in _cache.Values) b.Dispose();
        _cache.Clear();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _resize.Dispose();
            _ocultar.Dispose();
            LimpiarCache();
        }
        base.Dispose(disposing);
    }
}
