namespace IGP_Soft.Pruebas;

public class GrillasPruebas
{
    [Fact]
    public void El_estado_inicial_es_una_zona()
    {
        GridLayout g = GridLayout.Inicial;
        Assert.Equal(1, g.Cols);
        Assert.Equal(1, g.Rows);
        Assert.Single(g.Zones);
        Assert.Equal(1, g.ActiveCount);
    }

    [Fact]
    public void Volver_a_una_zona_conserva_la_de_arriba_a_la_izquierda()
    {
        GridLayout g = GridLayout.Uniform(3, 2);
        Assert.Equal(2, GridLayout.SeCerrarian(g, new[] { 0, 1, 4 }, GridLayout.Inicial));
        Assert.Equal(2, GridLayout.SeCerrarian(g, new[] { 1, 4 }, GridLayout.Inicial));
        Assert.Equal(0, GridLayout.SeCerrarian(g, new[] { 0 }, GridLayout.Inicial));
        Assert.Equal(0, GridLayout.SeCerrarian(g, Array.Empty<int>(), GridLayout.Inicial));
        Assert.Equal(1, GridLayout.SeCerrarian(g, new[] { 0, 0, 5, 9 }, GridLayout.Inicial));
    }

    [Fact]
    public void Volver_a_una_zona_deshace_uniones_y_zonas_sin_usar()
    {
        GridLayout unida = GridLayout.TryCreate(2, 2, new[] { new ZoneSpec(0, 0, 2, 1), new ZoneSpec(0, 1), new ZoneSpec(1, 1, Off: true) })!;
        Assert.False(unida.SameAs(GridLayout.Inicial));
        Assert.Equal(1, GridLayout.SeCerrarian(unida, new[] { 0, 1 }, GridLayout.Inicial));
    }

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

    [Fact]
    public void Las_divisiones_de_un_clic_son_distintas_y_caben()
    {
        Assert.Equal(GridLayout.Divisiones.Count, GridLayout.Divisiones.Distinct().Count());
        Assert.All(GridLayout.Divisiones, d =>
        {
            GridLayout g = GridLayout.Uniform(d.Cols, d.Rows);
            Assert.Equal(d.Cols, g.Cols);
            Assert.Equal(d.Rows, g.Rows);
        });
        Assert.Contains((3, 2), GridLayout.Divisiones);
    }

    private static Rectangle[] Muro(int cols, int rows, int ancho = 1920, int alto = 1080) =>
        Enumerable.Range(0, cols * rows).Select(i => new Rectangle(i % cols * ancho, i / cols * alto, ancho, alto)).ToArray();

    [Theory]
    [InlineData(2, 1)]
    [InlineData(3, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    [InlineData(1, 3)]
    public void La_cuadricula_de_un_videowall_de_monitores_iguales(int cols, int rows)
    {
        Assert.Equal((cols, rows), GridLayout.CuadriculaDeMiembros(Muro(cols, rows)));
        Assert.Contains(("Una zona por monitor", cols, rows), GridLayout.AtajosDeGrupo(Muro(cols, rows)));
    }

    [Fact]
    public void La_cuadricula_no_depende_del_orden_ni_del_origen()
    {
        Rectangle[] muro = Muro(3, 2).Reverse().Select(r => r with { X = r.X - 3840, Y = r.Y + 200 }).ToArray();
        Assert.Equal((3, 2), GridLayout.CuadriculaDeMiembros(muro));
    }

    [Fact]
    public void Monitores_distintos_o_desalineados_no_tienen_atajos()
    {
        var distintos = new[] { new Rectangle(0, 0, 1920, 1080), new Rectangle(1920, 0, 2560, 1440) };
        var escalon = new[] { new Rectangle(0, 0, 1920, 1080), new Rectangle(1920, 540, 1920, 1080) };
        var en_l = new[] { new Rectangle(0, 0, 1920, 1080), new Rectangle(1920, 0, 1920, 1080), new Rectangle(0, 1080, 1920, 1080) };
        Assert.Null(GridLayout.CuadriculaDeMiembros(distintos));
        Assert.Null(GridLayout.CuadriculaDeMiembros(escalon));
        Assert.Null(GridLayout.CuadriculaDeMiembros(en_l));
        Assert.Empty(GridLayout.AtajosDeGrupo(distintos));
        Assert.Empty(GridLayout.AtajosDeGrupo(Array.Empty<Rectangle>()));
        Assert.Empty(GridLayout.AtajosDeGrupo(Muro(1, 1)));
    }

    [Fact]
    public void Dos_por_monitor_parte_a_lo_ancho_y_si_no_cabe_a_lo_alto()
    {
        Assert.Contains(("2 por monitor", 4, 1), GridLayout.AtajosDeGrupo(Muro(2, 1)));
        Assert.Contains(("2 por monitor", 4, 2), GridLayout.AtajosDeGrupo(Muro(2, 2)));
        Assert.Contains(("2 por monitor", 3, 4), GridLayout.AtajosDeGrupo(Muro(3, 2)));
        Assert.Contains(("2 por monitor", 1, 4), GridLayout.AtajosDeGrupo(Muro(1, 2, 1080, 1920)));
        Assert.Contains(("2 por monitor", 4, 2), GridLayout.AtajosDeGrupo(Muro(4, 1, 1080, 1920)));
    }

    [Fact]
    public void Un_videowall_que_no_cabe_en_la_grilla_maxima_no_ofrece_ese_atajo()
    {
        IReadOnlyList<(string Name, int Cols, int Rows)> atajos = GridLayout.AtajosDeGrupo(Muro(4, 4));
        Assert.Contains(("Una zona por monitor", 4, 4), atajos);
        Assert.DoesNotContain(atajos, a => a.Name == "2 por monitor");
        Assert.Empty(GridLayout.AtajosDeGrupo(Muro(5, 1)));
    }
}
