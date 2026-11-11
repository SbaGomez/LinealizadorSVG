using System.Globalization;
using LinealizadorSVG.Core;

namespace LinealizadorSVG.Core.Tests;

public class LinealizadorTests
{
    private static List<(double X, double Y)> Puntos(string d) =>
        [.. System.Text.RegularExpressions.Regex.Matches(d, @"(-?[\d.]+),(-?[\d.]+)")
            .Select(m => (double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)))];

    [Fact]
    public void RectasQuedanIgualesEnAbsoluto()
    {
        var r = Linealizador.Linealizar("M10 10 h20 v20 H10 z", 0.5);
        Assert.Equal("M10,10 L30,10 L30,30 L10,30 Z", r.D);
        Assert.Equal(3, r.Segmentos);
    }

    [Fact]
    public void ParesExtraTrasMoveToSonLineas()
    {
        Assert.Equal("M0,0 L10,0 L10,10", Linealizador.Linealizar("M0 0 10 0 10 10", 1).D);
        Assert.Equal("M5,5 L15,5 L15,15", Linealizador.Linealizar("m5 5 10 0 0 10", 1).D);
    }

    [Fact]
    public void RelativoDespuesDeCerrarArrancaDelInicioDelSubtrazado()
    {
        var r = Linealizador.Linealizar("M10 10 l10 0 z m5 5 l1 0", 1);
        Assert.Equal("M10,10 L20,10 Z M15,15 L16,15", r.D);
    }

    [Fact]
    public void NumerosPegadosSeSeparan()
    {
        // CorelDRAW compacts numbers: "-5-5" and ".5.5" are two numbers each.
        Assert.Equal("M0,0 L-5,-5 L0.5,0.5", Linealizador.Linealizar("M0 0L-5-5L.5.5", 1).D);
    }

    [Fact]
    public void CubicaTerminaEnSuPuntoFinalYLaPrecisionCambiaLosSegmentos()
    {
        const string d = "M0 0 C0 50 100 50 100 0";
        var fino = Linealizador.Linealizar(d, 0.25);
        var grueso = Linealizador.Linealizar(d, 1);
        Assert.EndsWith("L100,0", fino.D);
        Assert.EndsWith("L100,0", grueso.D);
        Assert.True(fino.Segmentos > grueso.Segmentos * 3);
        Assert.DoesNotContain("C", fino.D);
    }

    [Fact]
    public void PuntosDeLaCubicaEstanSobreLaCurva()
    {
        var r = Linealizador.Linealizar("M0 0 C0 100 100 100 100 0", 1);
        var medio = Puntos(r.D)[Puntos(r.D).Count / 2]; // t = 0.5 → (50, 75) for this symmetric curve
        Assert.Equal(50, medio.X, 3);
        Assert.Equal(75, medio.Y, 3);
    }

    [Fact]
    public void CurvasMuyCortasLlevanAlMenosDosSegmentos()
    {
        Assert.Equal(2, Linealizador.Linealizar("M0 0 c0 0 0.01 0 0.01 0", 0.5).Segmentos);
    }

    [Fact]
    public void SuaveReflejaElControlAnterior()
    {
        var conS = Linealizador.Linealizar("M0 0 C0 10 10 10 10 0 S20 -10 20 0", 0.5).D;
        var explicita = Linealizador.Linealizar("M0 0 C0 10 10 10 10 0 C10 -10 20 -10 20 0", 0.5).D;
        Assert.Equal(explicita, conS);
    }

    [Fact]
    public void CuadraticaYTSuave()
    {
        var conT = Linealizador.Linealizar("M0 0 Q5 10 10 0 T20 0", 0.5).D;
        var explicita = Linealizador.Linealizar("M0 0 Q5 10 10 0 Q15 -10 20 0", 0.5).D;
        Assert.Equal(explicita, conT);
    }

    [Fact]
    public void ArcoDeMediaCircunferencia()
    {
        var r = Linealizador.Linealizar("M0 0 A10 10 0 0 1 20 0", 0.5);
        var puntos = Puntos(r.D);
        Assert.Equal((20.0, 0.0), puntos[^1]);
        foreach (var (x, y) in puntos) Assert.Equal(10, Math.Sqrt((x - 10) * (x - 10) + y * y), 3);
        // sweep = 1 goes through negative y (clockwise on screen).
        Assert.Contains(puntos, p => p.Y < -9.9);
    }

    [Fact]
    public void ArcoConRadioCeroEsUnaLinea()
    {
        Assert.Equal("M0,0 L5,5", Linealizador.Linealizar("M0 0 A0 0 0 0 1 5 5", 0.5).D);
    }

    [Fact]
    public void ArcoDemasiadoChicoSeAgranda()
    {
        var puntos = Puntos(Linealizador.Linealizar("M0 0 A1 1 0 0 1 20 0", 0.5).D);
        Assert.Equal((20.0, 0.0), puntos[^1]);
        Assert.All(puntos, p => Assert.InRange(p.Y, -10.001, 0.001));
    }

    [Theory]
    [InlineData("M0 0 L10")]
    [InlineData("10 10 L5 5")]
    [InlineData("M0 0 R10 10")]
    public void TrazadosMalFormadosDanError(string d)
    {
        Assert.Throws<SvgInvalidoException>(() => Linealizador.Linealizar(d, 0.5));
    }

    [Fact]
    public void SinDecimalesDeMasNiNotacionCientifica()
    {
        var d = Linealizador.Linealizar("M0.000001 -0.000001 L1.123456789 0.00001", 1).D;
        Assert.Equal("M0,0 L1.12346,0.00001", d);
    }
}
