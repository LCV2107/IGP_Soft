namespace IGP_Soft.Pruebas;

public class GrillasPruebas
{
    [Fact]
    public void Uniforme_crea_una_zona_por_celda_en_orden()
    {
        GridLayout g = GridLayout.Uniform(3, 2);
        Assert.Equal(6, g.Zones.Count);
        Assert.Equal(new ZoneSpec(0, 0), g.Zones[0]);
        Assert.Equal(new ZoneSpec(2, 1), g.Zones[5]);
        Assert.True(g.IsUniform);
        Assert.Equal("3×2", g.Describe());
    }

    [Fact]
    public void Uniforme_limita_columnas_y_filas()
    {
        GridLayout g = GridLayout.Uniform(9, 0);
        Assert.Equal(GridLayout.Max, g.Cols);
        Assert.Equal(1, g.Rows);
    }

    [Fact]
    public void TryCreate_rechaza_zonas_que_se_pisan_o_dejan_huecos()
    {
        Assert.Null(GridLayout.TryCreate(2, 1, new[] { new ZoneSpec(0, 0, 2, 1), new ZoneSpec(1, 0) }));
        Assert.Null(GridLayout.TryCreate(2, 1, new[] { new ZoneSpec(0, 0) }));
        Assert.Null(GridLayout.TryCreate(2, 1, new[] { new ZoneSpec(0, 0), new ZoneSpec(2, 0) }));
        Assert.NotNull(GridLayout.TryCreate(2, 1, new[] { new ZoneSpec(0, 0), new ZoneSpec(1, 0) }));
    }

    [Fact]
    public void FromSaved_usa_la_uniforme_si_lo_guardado_no_es_valido()
    {
        GridLayout g = GridLayout.FromSaved(2, 2, new[] { new ZoneSpec(0, 0, 3, 3) });
        Assert.True(g.SameAs(GridLayout.Uniform(2, 2)));
    }

    [Fact]
    public void AreaOf_reparte_el_monitor_sin_perder_pixeles()
    {
        GridLayout g = GridLayout.Uniform(3, 1);
        var bounds = new Rectangle(1920, 0, 1920, 1080);
        Rectangle[] areas = g.Zones.Select(z => g.AreaOf(z, bounds)).ToArray();
        Assert.Equal(1920, areas[0].X);
        Assert.Equal(1920 * 2, areas[2].Right);
        Assert.Equal(1920, areas.Sum(a => a.Width));
        Assert.All(areas, a => Assert.Equal(1080, a.Height));
    }

    [Fact]
    public void Zona_unida_ocupa_varias_celdas()
    {
        GridLayout g = GridLayout.TryCreate(2, 2, new[] { new ZoneSpec(0, 0, 1, 2), new ZoneSpec(1, 0), new ZoneSpec(1, 1) })!;
        Rectangle a = g.AreaOf(g.Zones[0], new Rectangle(0, 0, 1000, 800));
        Assert.Equal(new Rectangle(0, 0, 500, 800), a);
        Assert.False(g.IsUniform);
        Assert.Equal("3 zonas", g.Describe());
    }

    [Fact]
    public void Desactivar_zona_cuenta_solo_las_activas()
    {
        GridLayout g = GridLayout.Uniform(2, 1);
        GridLayout off = g.With(new[] { g.Zones[0] with { Off = true }, g.Zones[1] });
        Assert.Equal(1, off.ActiveCount);
        Assert.False(off.SameAs(g));
    }

    [Fact]
    public void Carry_lleva_cada_zona_ocupada_a_la_misma_esquina()
    {
        GridLayout from = GridLayout.Uniform(2, 2);
        GridLayout to = GridLayout.Uniform(3, 3);
        Dictionary<int, int> map = GridLayout.Carry(from, new[] { 0, 3 }, to);
        Assert.Equal(0, map[0]);
        Assert.Equal(to.ZoneAt(1, 1), map[3]);
    }

    [Fact]
    public void Carry_descarta_lo_que_queda_fuera_de_la_grilla_nueva()
    {
        GridLayout from = GridLayout.Uniform(2, 1);
        Dictionary<int, int> map = GridLayout.Carry(from, new[] { 0, 1 }, GridLayout.Uniform(1, 1));
        Assert.Single(map);
        Assert.Equal(0, map[0]);
    }

    [Fact]
    public void Las_grillas_predefinidas_son_validas()
    {
        Assert.All(GridLayout.Presets, p => Assert.NotNull(p.Layout));
    }
}
