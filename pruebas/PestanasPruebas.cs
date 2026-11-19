namespace IGP_Soft.Pruebas;

public sealed class PestanasPruebas
{
    [Theory]
    [InlineData("https://www.igp.gob.pe", true)]
    [InlineData("http://ultimosismo.igp.gob.pe/ultimo-sismo", true)]
    [InlineData(@"Assets\video.mp4", false)]
    [InlineData("file:///C:/Windows/win.ini", false)]
    [InlineData("ventana:POWERPNT|clase.pptx", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData(null, false)]
    public void Solo_las_paginas_web_admiten_pestanas(string? url, bool web) =>
        Assert.Equal(web, PestanasWeb.EsWeb(url));

    [Theory]
    [InlineData("atras", true, 1)]
    [InlineData("adelante", true, 2)]
    [InlineData("recargar", true, 3)]
    [InlineData("borrar", false, 0)]
    [InlineData(null, false, 0)]
    public void Atras_adelante_y_recargar_son_las_unicas_acciones(string? accion, bool valida, int comando)
    {
        Assert.Equal(valida, PestanasWeb.EsAccion(accion));
        if (valida) Assert.Equal(comando, PestanasWeb.ComandoDe(accion!));
    }

    [Theory]
    [InlineData(Keys.Left, true, false, false, "atras")]
    [InlineData(Keys.Right, true, false, false, "adelante")]
    [InlineData(Keys.Left, false, false, false, null)]
    [InlineData(Keys.Left, true, true, false, null)]
    [InlineData(Keys.Right, true, false, true, null)]
    [InlineData(Keys.Up, true, false, false, null)]
    public void Alt_con_flechas_navega_atras_y_adelante(Keys tecla, bool alt, bool ctrl, bool mayus, string? accion) =>
        Assert.Equal(accion, PestanasWeb.AccionDeTecla(tecla, alt, ctrl, mayus));

    [Fact]
    public void Fuera_de_los_elementos_de_una_lista_no_hay_indice()
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try
            {
                using var f = new Form { Size = new Size(300, 400) };
                var l = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, DrawMode = DrawMode.OwnerDrawVariable };
                l.MeasureItem += (_, e) => e.ItemHeight = 40;
                f.Controls.Add(l);
                l.Items.Add("a");
                l.Items.Add("b");
                f.Show();
                Assert.Equal(0, l.Indice(new Point(10, 10)));
                Assert.Equal(1, l.Indice(new Point(10, 50)));
                Assert.Equal(-1, l.Indice(new Point(10, 300)));
            }
            catch (Exception e) { error = e; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        Assert.Null(error);
    }

    [Theory]
    [InlineData("www.igp.gob.pe", "https://www.igp.gob.pe", "www.igp.gob.pe")]
    [InlineData("  https://sigrid.igp.gob.pe/mapa  ", "https://sigrid.igp.gob.pe/mapa", "sigrid.igp.gob.pe")]
    [InlineData("", null, null)]
    [InlineData("ftp://servidor/x", null, null)]
    public void La_direccion_escrita_se_completa(string texto, string? url, string? titulo)
    {
        LinkDef? l = PestanasWeb.Desde(texto);
        Assert.Equal(url, l?.Url);
        Assert.Equal(titulo, l?.Titulo);
    }

    private static EstadoZona Zona(int n, string url, params string[] pestanas) =>
        new(n, EstadosZona.EnPantalla, "t", url, false, 0, 0, 100, 100,
            pestanas.Length == 0 ? null : pestanas.Select(p => new LinkDef(p, p)).ToList(), 0);

    [Fact]
    public void Las_pestanas_cuentan_como_abiertas_y_se_guardan_en_el_perfil()
    {
        var mon = new EstadoMonitor("M1", "D1", 1, 0, 0, 100, 100, true, 1, false, 2, 1, new List<ZoneSpec>(), null,
            new List<EstadoZona> { Zona(0, "https://a", "https://a", "https://b"), Zona(1, @"Assets\v.mp4") });
        EstadoPantallas e = EstadoPantallas.Vacio with { Monitores = new List<EstadoMonitor> { mon } };

        Assert.True(e.EstaAbierto("https://B"));
        Assert.Equal(2, mon.Zonas[0].CantidadPestanas);
        Assert.Equal(1, mon.Zonas[1].CantidadPestanas);
        Assert.True(mon.Zonas[0].Web);
        Assert.False(mon.Zonas[1].Web);

        ZoneLayout z = e.ComoPerfil("p").Monitors[0].Links[0];
        Assert.Equal(new[] { "https://a", "https://b" }, z.Pestanas!.Select(p => p.Url));
        Assert.Null(e.ComoPerfil("p").Monitors[0].Links[1].Pestanas);
    }

    [Fact]
    public void Un_perfil_con_pestanas_no_web_no_pasa_la_lista_blanca()
    {
        var lista = new ListaBlanca(() => Path.GetTempPath(), () => Array.Empty<string>());
        LayoutProfile Perfil(params string[] pestanas) => new()
        {
            Name = "p",
            Monitors = new()
            {
                new MonitorLayout
                {
                    Links = new() { new ZoneLayout { Url = "https://a", Pestanas = pestanas.Select(u => new LinkDef(u, u)).ToList() } },
                },
            },
        };
        Assert.True(lista.Permite(Perfil("https://a", "https://b")));
        Assert.False(lista.Permite(Perfil("https://a", "file:///C:/Windows/win.ini")));
        Assert.False(lista.Permite(Perfil("https://a", "ventana:cmd|x")));
    }
}
