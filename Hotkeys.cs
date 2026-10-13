using System.Runtime.InteropServices;

namespace IGP_Soft;

internal sealed record HotkeyAction(int Id, string Key, string Name, Keys Default);

internal static class Hotkeys
{
    public const int WM_HOTKEY = 0x0312;

    public static readonly IReadOnlyList<HotkeyAction> Actions = new List<HotkeyAction>
    {
        new(1, "ampliar", "Ampliar la zona bajo el mouse a todo el monitor", Keys.Control | Keys.Alt | Keys.Return),
        new(2, "restaurar", "Volver a la grilla", Keys.Control | Keys.Alt | Keys.Back),
        new(3, "siguiente", "Ampliar la zona siguiente", Keys.Control | Keys.Alt | Keys.PageDown),
        new(4, "anterior", "Ampliar la zona anterior", Keys.Control | Keys.Alt | Keys.PageUp),
        new(5, "panel", "Mostrar u ocultar el panel", Keys.Control | Keys.Alt | Keys.P),
        new(6, "abrirorden", "Abrir en orden", Keys.None),
        new(7, "cerrartodas", "Cerrar todas las ventanas", Keys.None),
        new(8, "cerrarmonitor", "Cerrar todo en el monitor bajo el mouse", Keys.None),
    };

    public static HashSet<string> Failed { get; } = new();

    public static event Action? Changed;

    public static Keys Get(HotkeyAction a)
    {
        string? v = Config.Get("atajo." + a.Key);
        if (v is null) return a.Default;
        if (v == "ninguno") return Keys.None;
        return Enum.TryParse(v, out Keys k) && IsValid(k) ? k : a.Default;
    }

    public static void Set(HotkeyAction a, Keys k)
    {
        Config.Set("atajo." + a.Key, k == Keys.None ? "ninguno" : k.ToString());
        Changed?.Invoke();
    }

    public static void ResetAll()
    {
        foreach (HotkeyAction a in Actions) Config.Set("atajo." + a.Key, a.Default == Keys.None ? "ninguno" : a.Default.ToString());
        Changed?.Invoke();
    }

    public static HotkeyAction? UsedBy(Keys k, HotkeyAction except) =>
        k == Keys.None ? null : Actions.FirstOrDefault(a => a != except && Get(a) == k);

    public static bool IsValid(Keys k)
    {
        Keys key = k & Keys.KeyCode;
        bool mod = (k & (Keys.Control | Keys.Alt)) != 0;
        return mod && key is not (Keys.None or Keys.ControlKey or Keys.Menu or Keys.ShiftKey or Keys.LWin or Keys.RWin);
    }

    public static string Format(Keys k)
    {
        if (k == Keys.None) return "Sin asignar";
        var parts = new List<string>();
        if ((k & Keys.Control) != 0) parts.Add("Ctrl");
        if ((k & Keys.Alt) != 0) parts.Add("Alt");
        if ((k & Keys.Shift) != 0) parts.Add("Mayús");
        Keys key = k & Keys.KeyCode;
        parts.Add(key switch
        {
            Keys.Up => "↑",
            Keys.Down => "↓",
            Keys.Left => "←",
            Keys.Right => "→",
            Keys.Return => "Enter",
            Keys.Escape => "Esc",
            Keys.Space => "Espacio",
            Keys.Back => "Retroceso",
            Keys.PageUp => "RePág",
            Keys.PageDown => "AvPág",
            >= Keys.D0 and <= Keys.D9 => ((int)(key - Keys.D0)).ToString(),
            >= Keys.NumPad0 and <= Keys.NumPad9 => "Num " + (int)(key - Keys.NumPad0),
            _ => key.ToString(),
        });
        return string.Join(" + ", parts);
    }

    public static void Register(IntPtr hwnd)
    {
        Unregister(hwnd);
        Failed.Clear();
        foreach (HotkeyAction a in Actions)
        {
            Keys k = Get(a);
            if (k == Keys.None) continue;
            uint mods = MOD_NOREPEAT;
            if ((k & Keys.Control) != 0) mods |= MOD_CONTROL;
            if ((k & Keys.Alt) != 0) mods |= MOD_ALT;
            if ((k & Keys.Shift) != 0) mods |= MOD_SHIFT;
            if (!RegisterHotKey(hwnd, a.Id, mods, (uint)(k & Keys.KeyCode)))
                Failed.Add(a.Key);
        }
    }

    public static void Unregister(IntPtr hwnd)
    {
        foreach (HotkeyAction a in Actions) UnregisterHotKey(hwnd, a.Id);
    }

    private const uint MOD_ALT = 0x1;
    private const uint MOD_CONTROL = 0x2;
    private const uint MOD_SHIFT = 0x4;
    private const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}

internal sealed class HotkeyBox : Control
{
    private bool _capturing;
    private bool _hover;

    public Keys Value { get; private set; }
    public bool Unavailable { get; set; }

    public event Action<Keys>? Captured;

    public HotkeyBox()
    {
        Size = new Size(200, 34);
        Font = Theme.BodyBold;
        Cursor = Cursors.Hand;
        TabStop = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
    }

    public void SetValue(Keys k)
    {
        Value = k;
        Invalidate();
    }

    protected override bool IsInputKey(Keys keyData) => true;

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        Focus();
        _capturing = true;
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        _capturing = false;
        Invalidate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        e.SuppressKeyPress = true;
        if (!_capturing)
        {
            if (e.KeyCode is Keys.Enter or Keys.Space) { _capturing = true; Invalidate(); }
            return;
        }
        if (e.KeyCode == Keys.Escape && e.Modifiers == Keys.None)
        {
            _capturing = false;
            Invalidate();
            return;
        }
        if (e.KeyCode is Keys.Back or Keys.Delete && e.Modifiers == Keys.None)
        {
            _capturing = false;
            Captured?.Invoke(Keys.None);
            return;
        }
        Keys k = e.KeyData;
        if (!Hotkeys.IsValid(k)) return;
        _capturing = false;
        Captured?.Invoke(k);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        Theme.Smooth(g);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Color fill = _capturing ? Theme.AccentSoft : Value == Keys.None ? Theme.SurfaceAlt : Color.FromArgb(241, 245, 249);
        Theme.FillRound(g, fill, r, 8);
        Theme.DrawRound(g, _capturing ? Theme.Accent : Unavailable ? Theme.Danger : _hover ? Theme.BorderStrong : Theme.Border, r, 8,
            _capturing ? 1.6f : 1f);
        string text = _capturing ? "Pulsa la combinación…" : Hotkeys.Format(Value);
        Color fore = _capturing ? Theme.Accent : Value == Keys.None ? Theme.TextSubtle : Unavailable ? Theme.Danger : Theme.Text;
        TextRenderer.DrawText(g, text, _capturing || Value == Keys.None ? Theme.Body : Font, Rectangle.Round(r), fore,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}
