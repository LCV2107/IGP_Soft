using System.Text.Json;

namespace IGP_Soft.Pruebas;

public class EstadoPruebas
{
    private static PantallaMonitor Monitor(string device, int numero, int x)
    {
        var m = new PantallaMonitor(numero, new MonitorInfo(device, x, 0, 1920, 1080, numero == 1) { HardwareId = $"MON-PRUEBA-{numero}" });
        m.Apply(GridLayout.Uniform(2, 1));
        return m;
    }

    private static EstadoPantallas Estado(params PantallaMonitor[] ms) =>
        EstadoPantallas.Vacio with { Seq = 7, Equipo = "PC-PRUEBA", Navegador = "Edge", Monitores = ms.Select(EstadoPantallas.MonitorDesde).ToList() };

    [Fact]
    public void Refleja_el_estado_de_cada_zona()
    {
        PantallaMonitor a = Monitor(@"\\.\DISPLAY1", 1, 0);
        a.Segments[0].Link = new LinkDef("Último sismo", "https://ultimosismo.igp.gob.pe/ultimo-sismo");
        a.Segments[0].Pending = new object();
        PantallaMonitor b = Monitor(@"\\.\DISPLAY2", 2, 1920);
        b.Apply(b.Grid.With(new[] { b.Grid.Zones[0] with { Off = true }, b.Grid.Zones[1] }));

        EstadoPantallas e = Estado(a, b);

        Assert.Equal(EstadosZona.Abriendo, e.Monitores[0].Zonas[0].Estado);
        Assert.Equal(EstadosZona.Libre, e.Monitores[0].Zonas[1].Estado);
        Assert.Equal(EstadosZona.Desactivada, e.Monitores[1].Zonas[0].Estado);
        Assert.Equal("MON-PRUEBA-2", e.Monitores[1].Id);
        Assert.Equal(@"\\.\DISPLAY2", e.Monitores[1].Dispositivo);
        Assert.Equal(960, e.Monitores[0].Zonas[1].X);
        Assert.True(e.EstaAbierto("https://ultimosismo.igp.gob.pe/ultimo-sismo"));
        Assert.Equal(3, e.ZonasUsables);
        Assert.Equal(1, e.Monitores[1].NumeroVisible(e.Monitores[1].Zonas[1]));
    }

    [Fact]
    public void Se_serializa_en_una_sola_linea_sin_datos_locales()
    {
        PantallaMonitor a = Monitor(@"\\.\DISPLAY1", 1, 0);
        a.Segments[1].Link = new LinkDef("Reportes", "https://ultimosismo.igp.gob.pe/productos/reportes-sismicos");

        string json = JsonSerializer.Serialize(Estado(a), Protocolo.Json);

        Assert.DoesNotContain('\n', json);
        Assert.DoesNotContain("hwnd", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("profileDir", json, StringComparison.OrdinalIgnoreCase);
        EstadoPantallas leido = JsonSerializer.Deserialize<EstadoPantallas>(json, Protocolo.Json)!;
        Assert.Equal("Reportes", leido.Monitores[0].Zonas[1].Titulo);
        Assert.Equal(2, leido.Monitores[0].Grilla.Count);
        Assert.Equal("PC-PRUEBA", leido.Equipo);
    }

    [Fact]
    public void El_estado_se_convierte_en_perfil_con_identificador_de_hardware()
    {
        PantallaMonitor a = Monitor(@"\\.\DISPLAY1", 1, 0);
        a.Segments[1].Link = new LinkDef("Reportes", "https://x.igp.gob.pe/r");
        LayoutProfile p = Estado(a).ComoPerfil("Sala");
        MonitorLayout m = p.Monitors.Single();
        Assert.Equal("MON-PRUEBA-1", m.Hardware);
        Assert.Equal(@"\\.\DISPLAY1", m.Device);
        Assert.Equal(1, m.Links.Single().Zone);
        Assert.Null(m.Layout);
        Assert.Equal("Edge", p.Browser);
    }

    [Fact]
    public void Una_zona_ampliada_se_indica_por_su_numero()
    {
        PantallaMonitor a = Monitor(@"\\.\DISPLAY1", 1, 0);
        a.Maximized = a.Segments[1];
        Assert.Equal(1, Estado(a).Monitores[0].Ampliada);
    }

    [Fact]
    public void Grilla_nueva_reparte_las_zonas_en_pixeles_del_monitor()
    {
        PantallaMonitor a = Monitor(@"\\.\DISPLAY2", 2, 1920);
        a.Apply(GridLayout.Uniform(2, 2));
        Assert.Equal(4, a.Segments.Count);
        Assert.Equal(new Rectangle(1920 + 960, 540, 960, 540), a.Segments[3].Area);
        Assert.Equal(2, a.DisplayNumber(a.Segments[1]));
    }

    [Fact]
    public async Task La_interfaz_rechaza_monitores_y_zonas_que_no_existen()
    {
        using var pantallas = new PantallasLocales(null);
        IPantallas p = pantallas;
        Resultado r = await p.Abrir("no-existe", 0, new LinkDef("x", "https://example.com"));
        Assert.False(r.Ok);
        Assert.Equal("monitor_no_encontrado", r.Codigo);
        Assert.Equal("monitor_no_encontrado", (await p.Cerrar("no-existe", 0)).Codigo);
    }

    [Theory]
    [InlineData(@"\\?\DISPLAY#GSM5B7F#5&2a0d1b1&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}", "MON-GSM5B7F-")]
    [InlineData("", "")]
    [InlineData("sin-formato", "")]
    public void Identificador_de_hardware_desde_la_ruta_del_dispositivo(string ruta, string empieza)
    {
        string id = MonitorEnumerator.HardwareIdFromInterface(ruta);
        Assert.StartsWith(empieza, id);
        if (empieza.Length > 0) Assert.Equal(empieza.Length + 8, id.Length);
    }

    [Fact]
    public void Los_monitores_reales_tienen_identificador_estable_y_distinto()
    {
        List<MonitorInfo> a = MonitorEnumerator.GetOrdered();
        List<MonitorInfo> b = MonitorEnumerator.GetOrdered();
        Assert.Equal(a.Select(m => m.Id), b.Select(m => m.Id));
        Assert.Equal(a.Count, a.Select(m => m.Id).Distinct().Count());
    }
}
