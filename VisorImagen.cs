using System.Globalization;
using System.Text.RegularExpressions;

namespace IGP_Soft;

internal sealed record VistaImagen(double Zoom, double X, double Y)
{
    public const double ZoomMinimo = 1.0;
    public const double ZoomMaximo = 10.0;

    public static VistaImagen Encajada { get; } = new(1, 0.5, 0.5);

    [System.Text.Json.Serialization.JsonIgnore] public bool EsEncajada => Math.Abs(Zoom - 1) < 0.001 && Math.Abs(X - 0.5) < 0.0005 && Math.Abs(Y - 0.5) < 0.0005;

    [System.Text.Json.Serialization.JsonIgnore] public int Porcentaje => (int)Math.Round(Zoom * 100);

    public static VistaImagen? ParaGuardar(VistaImagen? v) => v is null || v.EsEncajada ? null : v;
}

internal static class Vistas
{
    public static VistaImagen? De(LaunchedWindow? w) => w?.Tag is VisorImagen v ? v.Vista : w?.VistaPagina;

    public static VistaImagen? De(Segment s) => s.Launched is not null ? De(s.Launched) : s.VistaPedida;
}

internal static class Encuadre
{
    public const double PasoRueda = 1.1;
    public const double PasoBoton = 1.25;
    public const double PasoMover = 0.2;

    public static readonly string[] Acciones = { "acercar", "alejar", "encajar", "izquierda", "derecha", "arriba", "abajo" };

    public static bool EsAccion(string? a) => a is not null && Acciones.Contains(a);

    private static double Escala(SizeF img, SizeF caja, double zoom) =>
        img.Width <= 0 || img.Height <= 0 ? 1 : Math.Min(caja.Width / img.Width, caja.Height / img.Height) * zoom;

    public static VistaImagen Ajustar(VistaImagen v, SizeF img, SizeF caja)
    {
        double zoom = double.IsFinite(v.Zoom) ? Math.Clamp(v.Zoom, VistaImagen.ZoomMinimo, VistaImagen.ZoomMaximo) : 1;
        double s = Escala(img, caja, zoom);
        double Eje(double c, double lado, double visible)
        {
            double mostrado = lado * s;
            if (mostrado <= visible + 0.5 || mostrado <= 0) return 0.5;
            double medio = visible / (2 * mostrado);
            return Math.Clamp(double.IsFinite(c) ? c : 0.5, medio, 1 - medio);
        }
        return new VistaImagen(zoom, Eje(v.X, img.Width, caja.Width), Eje(v.Y, img.Height, caja.Height));
    }

    public static RectangleF Destino(VistaImagen v, SizeF img, SizeF caja)
    {
        v = Ajustar(v, img, caja);
        double s = Escala(img, caja, v.Zoom);
        float w = (float)(img.Width * s), h = (float)(img.Height * s);
        return new RectangleF((float)(caja.Width / 2 - v.X * w), (float)(caja.Height / 2 - v.Y * h), w, h);
    }

    public static VistaImagen ZoomHacia(VistaImagen v, double factor, PointF punto, SizeF img, SizeF caja)
    {
        RectangleF antes = Destino(v, img, caja);
        if (antes.Width <= 0 || antes.Height <= 0) return v;
        double ux = (punto.X - antes.X) / antes.Width, uy = (punto.Y - antes.Y) / antes.Height;
        double zoom = Math.Clamp(Ajustar(v, img, caja).Zoom * factor, VistaImagen.ZoomMinimo, VistaImagen.ZoomMaximo);
        double s = Escala(img, caja, zoom);
        double w = img.Width * s, h = img.Height * s;
        double izquierda = punto.X - ux * w, arriba = punto.Y - uy * h;
        return Ajustar(new VistaImagen(zoom, (caja.Width / 2 - izquierda) / w, (caja.Height / 2 - arriba) / h), img, caja);
    }

    public static VistaImagen Mover(VistaImagen v, float dx, float dy, SizeF img, SizeF caja)
    {
        RectangleF r = Destino(v, img, caja);
        if (r.Width <= 0 || r.Height <= 0) return v;
        VistaImagen a = Ajustar(v, img, caja);
        return Ajustar(a with { X = a.X - dx / r.Width, Y = a.Y - dy / r.Height }, img, caja);
    }

    public static VistaImagen Aplicar(VistaImagen v, string accion, SizeF img, SizeF caja)
    {
        var centro = new PointF(caja.Width / 2, caja.Height / 2);
        float px = (float)(caja.Width * PasoMover), py = (float)(caja.Height * PasoMover);
        return accion switch
        {
            "acercar" => ZoomHacia(v, PasoBoton, centro, img, caja),
            "alejar" => ZoomHacia(v, 1 / PasoBoton, centro, img, caja),
            "encajar" => VistaImagen.Encajada,
            "izquierda" => Mover(v, px, 0, img, caja),
            "derecha" => Mover(v, -px, 0, img, caja),
            "arriba" => Mover(v, 0, py, img, caja),
            "abajo" => Mover(v, 0, -py, img, caja),
            _ => v,
        };
    }

    public static VistaImagen AplicarSinMedidas(VistaImagen v, string accion) => accion switch
    {
        "acercar" => v with { Zoom = Math.Min(VistaImagen.ZoomMaximo, v.Zoom * PasoBoton) },
        "alejar" => v.Zoom / PasoBoton <= 1.001 ? VistaImagen.Encajada : v with { Zoom = v.Zoom / PasoBoton },
        "encajar" => VistaImagen.Encajada,
        "izquierda" => v with { X = Math.Max(0, v.X - PasoMover / v.Zoom) },
        "derecha" => v with { X = Math.Min(1, v.X + PasoMover / v.Zoom) },
        "arriba" => v with { Y = Math.Max(0, v.Y - PasoMover / v.Zoom) },
        "abajo" => v with { Y = Math.Min(1, v.Y + PasoMover / v.Zoom) },
        _ => v,
    };

    private static readonly Regex TituloVista = new(@"IGP_Soft · vista ([0-9.]+) ([0-9.]+) ([0-9.]+)", RegexOptions.CultureInvariant);

    public static VistaImagen? LeerTitulo(string? titulo)
    {
        if (titulo is null || TituloVista.Match(titulo) is not { Success: true } m) return null;
        static double N(string s) => double.Parse(s, CultureInfo.InvariantCulture);
        return new VistaImagen(Math.Clamp(N(m.Groups[1].Value), VistaImagen.ZoomMinimo, VistaImagen.ZoomMaximo),
            Math.Clamp(N(m.Groups[2].Value), 0, 1), Math.Clamp(N(m.Groups[3].Value), 0, 1));
    }

    public static string Js(VistaImagen? v)
    {
        v ??= VistaImagen.Encajada;
        return string.Create(CultureInfo.InvariantCulture, $"{{z:{v.Zoom:0.####},x:{v.X:0.####},y:{v.Y:0.####}}}");
    }
}

internal sealed class VisorImagen : Control
{
    private readonly Image _imagen;
    private readonly bool _animada;
    private VistaImagen _vista;
    private Point? _arrastre;
    private bool _encima;
    private int _boton = -1;
    private readonly System.Windows.Forms.Timer _ocultar = new() { Interval = 1800 };

    private static readonly (string Accion, string Texto)[] Barra = { ("alejar", "−"), ("", ""), ("acercar", "+"), ("encajar", "Encajar") };

    public event Action? VistaCambiada;

    public VisorImagen(Image imagen, VistaImagen? vista)
    {
        _imagen = imagen;
        _vista = vista ?? VistaImagen.Encajada;
        BackColor = FondoZonas.Color;
        Font = Theme.BodyBold;
        TabStop = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        _animada = ImageAnimator.CanAnimate(imagen);
        if (_animada) ImageAnimator.Animate(imagen, Animar);
        _ocultar.Tick += (_, _) =>
        {
            _ocultar.Stop();
            if (_arrastre is not null || ClientRectangle.Contains(PointToClient(Cursor.Position)) && _boton >= 0) return;
            _encima = false;
            Invalidate();
        };
    }

    public Image Imagen => _imagen;

    private SizeF Img => _imagen.Size;

    private SizeF Caja => ClientSize;

    public VistaImagen Vista => Encuadre.Ajustar(_vista, Img, Caja);

    private void Animar(object? o, EventArgs e)
    {
        if (IsHandleCreated && !IsDisposed) BeginInvoke(Invalidate);
    }

    public void Fijar(VistaImagen v, bool avisar = true)
    {
        VistaImagen nueva = Encuadre.Ajustar(v, Img, Caja);
        if (nueva == _vista) return;
        _vista = nueva;
        Invalidate();
        if (avisar) VistaCambiada?.Invoke();
    }

    public void Aplicar(string accion) => Fijar(Encuadre.Aplicar(Vista, accion, Img, Caja));

    private Rectangle BotonDe(int i)
    {
        int ancho = 220, alto = 36;
        var barra = new Rectangle(ClientSize.Width - ancho - 12, ClientSize.Height - alto - 12, ancho, alto);
        int[] anchos = { 36, 64, 36, 84 };
        int x = barra.X;
        for (int k = 0; k < i; k++) x += anchos[k];
        return new Rectangle(x, barra.Y, anchos[i], alto);
    }

    private Rectangle BarraRect => Rectangle.Union(BotonDe(0), BotonDe(3));

    private bool BarraVisible => _encima && ClientSize.Width > 260 && ClientSize.Height > 80;

    private void Mostrar()
    {
        if (!_encima) { _encima = true; Invalidate(); }
        _ocultar.Stop();
        _ocultar.Start();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Mostrar();
        if (_arrastre is { } desde && e.Button == MouseButtons.Left)
        {
            Fijar(Encuadre.Mover(Vista, e.X - desde.X, e.Y - desde.Y, Img, Caja));
            _arrastre = e.Location;
            return;
        }
        int b = -1;
        if (BarraVisible)
            for (int i = 0; i < Barra.Length; i++)
                if (Barra[i].Accion.Length > 0 && BotonDe(i).Contains(e.Location)) b = i;
        if (b != _boton) { _boton = b; Invalidate(BarraRect); }
        Cursor = b >= 0 ? Cursors.Hand : Vista.Zoom > 1.001 ? Cursors.SizeAll : Cursors.Default;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _boton = -1;
        _ocultar.Stop();
        _ocultar.Start();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button != MouseButtons.Left) return;
        if (BarraVisible && BarraRect.Contains(e.Location))
        {
            for (int i = 0; i < Barra.Length; i++)
                if (Barra[i].Accion.Length > 0 && BotonDe(i).Contains(e.Location)) Aplicar(Barra[i].Accion);
            return;
        }
        _arrastre = e.Location;
        Capture = true;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _arrastre = null;
        Capture = false;
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (BarraVisible && BarraRect.Contains(e.Location)) return;
        Fijar(VistaImagen.Encajada);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        Mostrar();
        double factor = Math.Pow(Encuadre.PasoRueda, e.Delta / 120.0);
        Fijar(Encuadre.ZoomHacia(Vista, factor, e.Location, Img, Caja));
    }

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Alt || e.Control) return;
        string? accion = e.KeyCode switch
        {
            Keys.Add or Keys.Oemplus => "acercar",
            Keys.Subtract or Keys.OemMinus => "alejar",
            Keys.D0 or Keys.NumPad0 => "encajar",
            Keys.Left => "izquierda",
            Keys.Right => "derecha",
            Keys.Up => "arriba",
            Keys.Down => "abajo",
            _ => null,
        };
        if (accion is null) return;
        Aplicar(accion);
        e.Handled = true;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        _vista = Encuadre.Ajustar(_vista, Img, Caja);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(BackColor);
        if (_animada) ImageAnimator.UpdateFrames(_imagen);
        RectangleF destino = Encuadre.Destino(_vista, Img, Caja);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        g.DrawImage(_imagen, destino);
        if (!BarraVisible) return;
        Theme.Smooth(g);
        Rectangle barra = BarraRect;
        Theme.FillRound(g, Color.FromArgb(215, 15, 23, 42), barra, 10);
        for (int i = 0; i < Barra.Length; i++)
        {
            Rectangle r = BotonDe(i);
            string texto = i == 1 ? $"{Vista.Porcentaje} %" : Barra[i].Texto;
            if (i == _boton) Theme.FillRound(g, Theme.Accent, Rectangle.Inflate(r, -3, -4), 8);
            TextRenderer.DrawText(g, texto, i == 1 ? Theme.Body : Theme.BodyBold, r, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_animada) ImageAnimator.StopAnimate(_imagen, Animar);
            _ocultar.Dispose();
        }
        base.Dispose(disposing);
    }
}
