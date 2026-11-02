namespace IGP_Soft.Pruebas;

public sealed class RepartoPruebas
{
    [Theory]
    [InlineData(1, "1×1")]
    [InlineData(2, "2×1")]
    [InlineData(3, "3×1")]
    [InlineData(4, "2×2")]
    [InlineData(5, "3×2")]
    [InlineData(7, "4×2")]
    [InlineData(9, "3×3")]
    [InlineData(12, "4×3")]
    [InlineData(20, "4×4")]
    public void Recomienda_la_grilla_mas_chica_donde_entran_todos(int elementos, string grilla) =>
        Assert.Equal(grilla, Repartos.Recomendada(GridLayout.Uniform(1, 1), Array.Empty<int>(), elementos).Describe());

    [Fact]
    public void Si_la_grilla_actual_alcanza_se_queda_la_actual()
    {
        GridLayout actual = GridLayout.Uniform(3, 2);
        Assert.Same(actual, Repartos.Recomendada(actual, new[] { 0, 1 }, 4));
    }

    [Fact]
    public void Lo_que_ya_se_muestra_cuenta_al_recomendar()
    {
        GridLayout r = Repartos.Recomendada(GridLayout.Uniform(2, 1), new[] { 0, 1 }, 2);
        Assert.Equal("2×2", r.Describe());
        HashSet<int> usadas = Repartos.Ocupadas(GridLayout.Uniform(2, 1), new[] { 0, 1 }, r);
        Assert.Equal(new HashSet<int> { 0, 1 }, usadas);
    }

    [Fact]
    public void Reparte_en_orden_en_las_zonas_libres_y_lo_demas_queda_sin_asignar()
    {
        int?[] zonas = Repartos.Repartir(GridLayout.Uniform(2, 2), new HashSet<int> { 1 }, 5);
        Assert.Equal(new int?[] { 0, null, 1, 2 }, zonas);
    }

    [Fact]
    public void Las_zonas_sin_usar_no_reciben_elementos()
    {
        GridLayout g = GridLayout.Uniform(2, 1);
        g = g.With(new[] { g.Zones[0] with { Off = true }, g.Zones[1] });
        Assert.Equal(new int?[] { null, 0 }, Repartos.Repartir(g, new HashSet<int>(), 2));
    }

    [Fact]
    public void El_nombre_del_archivo_es_el_titulo()
    {
        LinkDef l = Repartos.Elemento(@"C:\Fotos\Mapa de peligros.png");
        Assert.Equal("Mapa de peligros", l.Titulo);
        Assert.Equal(@"C:\Fotos\Mapa de peligros.png", l.Url);
    }
}
