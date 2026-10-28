using System.Text.Json;

namespace IGP_Soft.Pruebas;

public class EstadoPruebas
{
    private static PantallaMonitor Monitor(string id, int numero, int x)
    {
        var m = new PantallaMonitor(numero, new MonitorInfo(id, x, 0, 1920, 1080, numero == 1));
        m.Apply(GridLayout.Uniform(2, 1));
        return m;
    }

    [Fact]
    public void Refleja_el_estado_de_cada_zona()
    {
        PantallaMonitor a = Monitor(@"\\.\DISPLAY1", 1, 0);
        a.Segments[0].Link = new LinkDef("Último sismo", "https://ultimosismo.igp.gob.pe/ultimo-sismo");
        a.Segments[0].Pending = new object();
        PantallaMonitor b = Monitor(@"\\.\DISPLAY2", 2, 1920);
        b.Apply(b.Grid.With(new[] { b.Grid.Zones[0] with { Off = true }, b.Grid.Zones[1] }));

        EstadoPantallas e = EstadoPantallas.Desde(7, "Edge", false, new[] { a, b });

        Assert.Equal(7, e.Seq);
        Assert.Equal(EstadosZona.Abriendo, e.Monitores[0].Zonas[0].Estado);
        Assert.Equal(EstadosZona.Libre, e.Monitores[0].Zonas[1].Estado);
        Assert.Equal(EstadosZona.Desactivada, e.Monitores[1].Zonas[0].Estado);
        Assert.Equal(1920, e.Monitores[1].X);
        Assert.Equal(960, e.Monitores[0].Zonas[1].X);
    }

    [Fact]
    public void Se_serializa_en_una_sola_linea_sin_datos_locales()
    {
        PantallaMonitor a = Monitor(@"\\.\DISPLAY1", 1, 0);
        a.Segments[1].Link = new LinkDef("Reportes", "https://ultimosismo.igp.gob.pe/productos/reportes-sismicos");

        string json = JsonSerializer.Serialize(EstadoPantallas.Desde(1, "Edge", true, new[] { a }));

        Assert.DoesNotContain('\n', json);
        Assert.DoesNotContain("Hwnd", json);
        Assert.DoesNotContain("ProfileDir", json);
        EstadoPantallas leido = JsonSerializer.Deserialize<EstadoPantallas>(json)!;
        Assert.Equal("Reportes", leido.Monitores[0].Zonas[1].Titulo);
        Assert.True(leido.OmitirPrincipal);
        Assert.Equal(2, leido.Monitores[0].Grilla.Count);
    }

    [Fact]
    public void Una_zona_ampliada_se_indica_por_su_numero()
    {
        PantallaMonitor a = Monitor(@"\\.\DISPLAY1", 1, 0);
        a.Maximized = a.Segments[1];
        Assert.Equal(1, EstadoPantallas.Desde(1, null, false, new[] { a }).Monitores[0].Ampliada);
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
}
