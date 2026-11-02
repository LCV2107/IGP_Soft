namespace IGP_Soft;

internal static class PestanasWeb
{
    public const int Maximo = 6;

    public static bool EsWeb(string? url) =>
        url is not null && Uri.TryCreate(url, UriKind.Absolute, out Uri? u) && u.Scheme is "http" or "https" && u.Host.Length > 0;

    public static LinkDef? Desde(string texto)
    {
        texto = texto.Trim();
        if (texto.Length == 0) return null;
        if (!texto.Contains("://", StringComparison.Ordinal)) texto = "https://" + texto;
        if (!EsWeb(texto)) return null;
        return new LinkDef(new Uri(texto).Host, texto);
    }
}

internal sealed record PestanaVista(string Titulo, bool Cargando);

internal sealed class BarraPestanas : Control
{
    public const int Franja = 6;
    private const int Ancho = 260;
    private const int Cabecera = 46;
    private const int Fila = 40;

    private readonly System.Windows.Forms.Timer _cerrar = new() { Interval = 350 };
    private Rectangle _zona;
    private IReadOnlyList<PestanaVista> _pestanas = Array.Empty<PestanaVista>();
    private int _activa;
    private bool _abierta;
    private int _hover = -1;
    private bool _hoverCerrar;

    public BarraPestanas(int zona)
    {
        Zona = zona;
        Cursor = Cursors.Hand;
        Font = Theme.Body;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        _cerrar.Tick += (_, _) =>
        {
            if (IsDisposed || RectangleToScreen(ClientRectangle).Contains(Cursor.Position)) return;
            _cerrar.Stop();
            _abierta = false;
            _hover = -1;
            Ubicar();
        };
    }

    public int Zona { get; }

    public event Action<BarraPestanas, int>? Activar;
    public event Action<BarraPestanas, int>? Cerrar;
    public event Action<BarraPestanas>? Nueva;

    public void Actualizar(Rectangle zona, IReadOnlyList<PestanaVista> pestanas, int activa)
    {
        _zona = zona;
        _pestanas = pestanas;
        _activa = activa;
        Ubicar();
        Invalidate();
    }

    private void Ubicar()
    {
        Bounds = new Rectangle(_zona.X, _zona.Y, _abierta ? Math.Min(Ancho, _zona.Width) : Franja, _zona.Height);
        AlFrente();
    }

    public void AlFrente()
    {
        if (IsHandleCreated)
            NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOP, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    private bool PuedeAgregar => _pestanas.Count < PestanasWeb.Maximo;

    private Rectangle FilaDe(int i) => new(8, Cabecera + i * Fila, Width - 16, Fila - 4);

    private Rectangle Agregar => new(8, Cabecera + _pestanas.Count * Fila + 6, Width - 16, Fila - 4);

    private Rectangle CerrarDe(int i)
    {
        Rectangle r = FilaDe(i);
        return new Rectangle(r.Right - 30, r.Y + (r.Height - 24) / 2, 24, 24);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        if (_abierta) return;
        _abierta = true;
        Ubicar();
        Invalidate();
        _cerrar.Start();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_abierta) return;
        int hover = -1;
        for (int i = 0; i < _pestanas.Count; i++)
            if (FilaDe(i).Contains(e.Location)) hover = i;
        if (hover < 0 && Agregar.Contains(e.Location)) hover = _pestanas.Count;
        bool cerrar = hover >= 0 && hover < _pestanas.Count && CerrarDe(hover).Contains(e.Location);
        if (hover == _hover && cerrar == _hoverCerrar) return;
        _hover = hover;
        _hoverCerrar = cerrar;
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (!_abierta || e.Button != MouseButtons.Left) return;
        for (int i = 0; i < _pestanas.Count; i++)
        {
            if (CerrarDe(i).Contains(e.Location)) { Cerrar?.Invoke(this, i); return; }
            if (FilaDe(i).Contains(e.Location)) { Activar?.Invoke(this, i); return; }
        }
        if (PuedeAgregar && Agregar.Contains(e.Location)) Nueva?.Invoke(this);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Theme.Sidebar);
        if (!_abierta)
        {
            using var b = new SolidBrush(Theme.Accent);
            g.FillRectangle(b, 1, Height / 2 - 24, Width - 2, 48);
            return;
        }
        Theme.Smooth(g);
        TextRenderer.DrawText(g, "Pestañas", Theme.BodyBold, new Rectangle(16, 0, Width - 70, Cabecera), Color.White,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, $"{_pestanas.Count}/{PestanasWeb.Maximo}", Theme.Small, new Rectangle(Width - 60, 0, 44, Cabecera), Theme.SidebarMuted,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        for (int i = 0; i < _pestanas.Count; i++)
        {
            Rectangle r = FilaDe(i);
            if (r.Bottom > Height) break;
            bool activa = i == _activa;
            if (activa) Theme.FillRound(g, Theme.Accent, r, 8);
            else if (i == _hover) Theme.FillRound(g, Theme.SidebarHover, r, 8);
            string titulo = _pestanas[i].Titulo + (_pestanas[i].Cargando ? "  · cargando…" : "");
            TextRenderer.DrawText(g, titulo, Theme.Body, new Rectangle(r.X + 12, r.Y, r.Width - 48, r.Height),
                activa ? Color.White : Theme.SidebarText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (i == _hover || activa)
            {
                Rectangle c = CerrarDe(i);
                if (i == _hover && _hoverCerrar) Theme.FillRound(g, Theme.Danger, c, 6);
                TextRenderer.DrawText(g, "✕", Theme.Small, c, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
        Rectangle a = Agregar;
        if (a.Bottom <= Height)
        {
            if (PuedeAgregar && _hover == _pestanas.Count) Theme.FillRound(g, Theme.SidebarHover, a, 8);
            TextRenderer.DrawText(g, PuedeAgregar ? "＋  Nueva pestaña" : $"Máximo {PestanasWeb.Maximo} pestañas", Theme.BodyBold,
                new Rectangle(a.X + 12, a.Y, a.Width - 12, a.Height), PuedeAgregar ? Color.White : Theme.SidebarMuted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _cerrar.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class NuevaPestanaForm : Form
{
    private readonly ComboBox _direccion = new();
    private readonly List<LinkDef> _enlaces;

    public LinkDef? Resultado { get; private set; }

    public NuevaPestanaForm(IEnumerable<LinkDef> enlaces)
    {
        _enlaces = enlaces.Where(l => PestanasWeb.EsWeb(l.Url)).ToList();
        Theme.StyleWindow(this, darkCaption: false);
        Text = "Nueva pestaña";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        ClientSize = new Size(524, 196);

        var card = new Card { Location = new Point(14, 14), Size = new Size(496, 112), Padding = new Padding(0) };
        var title = new Label { Text = "Página web para la nueva pestaña", Font = Theme.Section, ForeColor = Theme.Text, AutoSize = true, Location = new Point(20, 16), BackColor = Theme.Surface };
        var hint = new Label { Text = "Escribe una dirección o elige un enlace de la lista", ForeColor = Theme.TextMuted, AutoSize = true, Location = new Point(20, 44), BackColor = Theme.Surface };
        _direccion.Location = new Point(20, 68);
        _direccion.Width = 456;
        _direccion.Font = Theme.Body;
        _direccion.DropDownStyle = ComboBoxStyle.DropDown;
        _direccion.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        _direccion.AutoCompleteSource = AutoCompleteSource.ListItems;
        foreach (LinkDef l in _enlaces) _direccion.Items.Add(l.Titulo);
        card.Controls.AddRange(new Control[] { title, hint, _direccion });

        var ok = new ModernButton("Abrir", ButtonKind.Primary, "＋") { DialogResult = DialogResult.OK, Location = new Point(300, 144), Size = new Size(110, 38) };
        var cancel = new ModernButton("Cancelar") { DialogResult = DialogResult.Cancel, Location = new Point(418, 144), Size = new Size(92, 38) };
        AcceptButton = ok;
        CancelButton = cancel;
        Controls.AddRange(new Control[] { card, ok, cancel });

        FormClosing += (_, e) =>
        {
            if (DialogResult != DialogResult.OK) return;
            string texto = _direccion.Text.Trim();
            Resultado = _enlaces.FirstOrDefault(l => l.Titulo.Equals(texto, StringComparison.CurrentCultureIgnoreCase)) ?? PestanasWeb.Desde(texto);
            if (Resultado is not null) return;
            MessageBox.Show(this, "Escribe una dirección web válida (por ejemplo: www.igp.gob.pe) o elige un enlace de la lista.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            e.Cancel = true;
        };
    }
}
