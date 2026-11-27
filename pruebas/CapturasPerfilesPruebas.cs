namespace IGP_Soft.Pruebas;

[Collection("Perfiles")]
public sealed class CapturasPerfilesPruebas : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "igp_capturas_" + Guid.NewGuid().ToString("N"));
    private readonly string _antes = CapturasPerfiles.Carpeta;

    public CapturasPerfilesPruebas() => CapturasPerfiles.Carpeta = _dir;

    public void Dispose()
    {
        CapturasPerfiles.Carpeta = _antes;
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static byte[] Jpeg(int w, int h)
    {
        using var bmp = new Bitmap(w, h);
        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
        return ms.ToArray();
    }

    [Fact]
    public void La_captura_se_guarda_se_lee_y_se_borra_por_perfil()
    {
        Assert.False(CapturasPerfiles.Tiene("Sala: noche/1"));
        CapturasPerfiles.Guardar("Sala: noche/1", new[] { (1, Jpeg(64, 36)), (3, Jpeg(128, 36)) });
        IReadOnlyDictionary<int, Image> c = CapturasPerfiles.De("sala: NOCHE/1");
        Assert.Equal(new[] { 1, 3 }, c.Keys.OrderBy(k => k));
        Assert.Equal(128, c[3].Width);
        Assert.False(CapturasPerfiles.Tiene("Sala: noche/2"));

        CapturasPerfiles.Guardar("Sala: noche/1", new[] { (2, Jpeg(64, 36)) });
        Assert.Equal(new[] { 2 }, CapturasPerfiles.De("Sala: noche/1").Keys);

        CapturasPerfiles.Borrar("Sala: noche/1");
        Assert.False(CapturasPerfiles.Tiene("Sala: noche/1"));
    }

    [Fact]
    public void Un_monitor_sin_enlaces_se_muestra_vacio()
    {
        Assert.True(DibujoPerfil.Vacio(new MonitorLayout()));
        Assert.True(DibujoPerfil.Vacio(new MonitorLayout { Links = { new ZoneLayout { Zone = 0, Url = "" } } }));
        Assert.False(DibujoPerfil.Vacio(new MonitorLayout { Links = { new ZoneLayout { Zone = 0, Url = "https://a.com" } } }));
    }

    [Fact]
    public void Las_imagenes_y_videos_usan_el_fondo_de_las_zonas()
    {
        Assert.Contains("background:" + FondoZonas.Css + ";", MediaWrapper.ImagenHtml("a.png", null));
        Assert.Contains("background:" + FondoZonas.Css + ";", MediaWrapper.VideoHtml("a.mp4"));
    }

    [Fact]
    public void La_captura_de_perfil_solo_toma_las_zonas_y_rellena_con_el_fondo()
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try
            {
                using var f = new Form
                {
                    FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual,
                    Bounds = new Rectangle(40, 40, 400, 200), BackColor = Color.Lime, ShowInTaskbar = false, TopMost = true,
                };
                var zona = new Panel { Bounds = new Rectangle(0, 0, 200, 200), BackColor = Color.Red };
                f.Controls.Add(zona);
                f.Show();
                for (int i = 0; i < 10; i++)
                {
                    f.Refresh();
                    Application.DoEvents();
                    Thread.Sleep(30);
                }
                NativeMethods.GetWindowRect(zona.Handle, out NativeMethods.RECT r);
                Rectangle ventana = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
                var monitor = new MonitorInfo("M", ventana.X, ventana.Y, 400, 200, true);
                using Bitmap? bmp = PantallasLocales.CapturarZonas(monitor,
                    new[] { new PantallasLocales.VentanaZona(zona.Handle, ventana, ventana) }, Color.Black, 400);
                Assert.NotNull(bmp);
                Assert.Equal(new Size(400, 200), bmp!.Size);
                Color dentro = bmp.GetPixel(100, 100), fuera = bmp.GetPixel(300, 100);
                Assert.True(dentro.R > 200 && dentro.G < 60 && dentro.B < 60, dentro.ToString());
                Assert.True(fuera.R < 20 && fuera.G < 20 && fuera.B < 20, fuera.ToString());
            }
            catch (Exception e) { error = e; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        Assert.Null(error);
    }

    [Fact]
    public void Una_captura_negra_o_de_un_color_se_detecta()
    {
        using var bmp = new Bitmap(200, 100);
        using (Graphics g = Graphics.FromImage(bmp)) g.Clear(Color.Black);
        Assert.True(PantallasLocales.Uniforme(bmp, new Rectangle(0, 0, 200, 100)));
        using (Graphics g = Graphics.FromImage(bmp)) g.FillRectangle(Brushes.Orange, 120, 0, 80, 100);
        Assert.False(PantallasLocales.Uniforme(bmp, new Rectangle(0, 0, 200, 100)));
        Assert.True(PantallasLocales.Uniforme(bmp, new Rectangle(0, 0, 100, 100)));
    }

    [Fact]
    public void La_lista_de_perfiles_dibuja_el_texto_en_todas_las_tarjetas()
    {
        var colores = new List<Color>();
        Exception? error = null;
        var t = new Thread(() =>
        {
            try
            {
                using var f = new Form { Size = new Size(300, 300), StartPosition = FormStartPosition.Manual, Location = new Point(60, 60), ShowInTaskbar = false };
                var l = new ListaSinParpadeo { Dock = DockStyle.Fill, IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 60 };
                using var fuente = new Font("Segoe UI", 20);
                l.DrawItem += (_, e) =>
                {
                    e.Graphics.FillRectangle(Brushes.White, e.Bounds);
                    TextRenderer.DrawText(e.Graphics, "██████", fuente, new Point(e.Bounds.X + 5, e.Bounds.Y + 10), Color.Red);
                };
                f.Controls.Add(l);
                l.Items.Add("a");
                l.Items.Add("b");
                f.Show();
                Application.DoEvents();
                using var bmp = new Bitmap(l.Width, l.Height);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    IntPtr hdc = g.GetHdc();
                    NativeMethods.PrintWindow(l.Handle, hdc, NativeMethods.PW_RENDERFULLCONTENT);
                    g.ReleaseHdc(hdc);
                }
                colores.Add(bmp.GetPixel(20, 30));
                colores.Add(bmp.GetPixel(20, 90));
            }
            catch (Exception e) { error = e; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        Assert.Null(error);
        Assert.All(colores, c => Assert.True(c.R > 200 && c.G < 60 && c.B < 60, c.ToString()));
    }
}
