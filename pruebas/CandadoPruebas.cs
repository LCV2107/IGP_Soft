namespace IGP_Soft.Pruebas;

public sealed class CandadoPruebas
{
    [Fact]
    public void Al_desbloquear_reutiliza_lo_igual_abre_lo_nuevo_y_cierra_lo_que_sobra()
    {
        var borrador = new List<(int, string)> { (0, "https://a"), (1, "https://b"), (2, "https://c") };
        var congeladas = new List<string> { "https://B", "https://x", "https://a" };
        var plan = PantallasLocales.PlanDesbloqueo(borrador, congeladas);
        Assert.Equal(new List<(int, int)> { (0, 2), (1, 0) }, plan.Reusar);
        Assert.Equal(new List<int> { 2 }, plan.Abrir);
        Assert.Equal(new List<int> { 1 }, plan.Cerrar);
    }

    [Fact]
    public void Un_mismo_enlace_congelado_solo_se_reutiliza_una_vez()
    {
        var plan = PantallasLocales.PlanDesbloqueo(new List<(int, string)> { (0, "u"), (3, "u") }, new List<string> { "u" });
        Assert.Equal(new List<(int, int)> { (0, 0) }, plan.Reusar);
        Assert.Equal(new List<int> { 3 }, plan.Abrir);
        Assert.Empty(plan.Cerrar);
    }

    [Fact]
    public void Borrador_vacio_cierra_todo_lo_proyectado()
    {
        var plan = PantallasLocales.PlanDesbloqueo(new List<(int, string)>(), new List<string> { "a", "b" });
        Assert.Empty(plan.Reusar);
        Assert.Empty(plan.Abrir);
        Assert.Equal(new List<int> { 0, 1 }, plan.Cerrar);
    }

    [Fact]
    public void Un_monitor_bloqueado_muestra_el_borrador_y_su_candado()
    {
        var p = new PantallaMonitor(1, new MonitorInfo("M1", 0, 0, 1920, 1080, true));
        p.Apply(GridLayout.Uniform(2, 1));
        p.Segments[0].Link = new LinkDef("Mapa", "https://igp.gob.pe");
        Assert.Equal(EstadosZona.Libre, EstadoPantallas.MonitorDesde(p).Zonas[0].Estado);

        p.Bloqueado = true;
        EstadoMonitor m = EstadoPantallas.MonitorDesde(p);
        Assert.True(m.Bloqueado);
        Assert.Equal(EstadosZona.Borrador, m.Zonas[0].Estado);
        Assert.Equal(EstadosZona.Libre, m.Zonas[1].Estado);
        Assert.True(m.Zonas[0].Ocupada);
    }

    [Fact]
    public void Todos_bloqueados_ignora_los_omitidos()
    {
        EstadoMonitor Mon(string id, bool omitido, bool bloqueado) =>
            new(id, id, 1, 0, 0, 10, 10, false, 1, omitido, 1, 1, new List<ZoneSpec>(), null, new List<EstadoZona>(), bloqueado);
        EstadoPantallas e = EstadoPantallas.Vacio with { Monitores = new List<EstadoMonitor> { Mon("a", false, true), Mon("b", true, false) } };
        Assert.True(e.TodosBloqueados);
        Assert.True(e.HayBloqueados);
        e = e with { Monitores = new List<EstadoMonitor> { Mon("a", false, true), Mon("c", false, false) } };
        Assert.False(e.TodosBloqueados);
    }
}
