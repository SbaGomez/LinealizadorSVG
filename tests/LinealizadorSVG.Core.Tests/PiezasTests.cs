using System.Globalization;
using LinealizadorSVG.Core;

namespace LinealizadorSVG.Core.Tests;

/// <summary>
/// Bambu Studio builds one face per path, with its biggest outline as the piece and every other outline as a hole:
/// separate pieces in the same path have to come out as paths of their own.
/// </summary>
public class PiezasTests
{
    private static string Svg(string d, string extra = "") =>
        $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100" {extra}><path d="{d}"/></svg>""";

    private static List<List<(double X, double Y)>> Contornos(string d) =>
        [.. d.Split('M', StringSplitOptions.RemoveEmptyEntries).Select(parte =>
        {
            var n = System.Text.RegularExpressions.Regex.Matches(parte, @"-?[\d.]+")
                .Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture)).ToArray();
            var p = new List<(double, double)>();
            for (var i = 0; i + 1 < n.Length; i += 2) p.Add((n[i], n[i + 1]));
            return p;
        })];

    private static double Area(List<(double X, double Y)> p)
    {
        double a = 0;
        for (int i = 0, j = p.Count - 1; i < p.Count; j = i++) a += p[j].X * p[i].Y - p[i].X * p[j].Y;
        return a / 2;
    }

    [Fact]
    public void PiezasSeparadasDeUnTrazadoSalenEnTrazadosPropios()
    {
        // A square with a hole, and another square beside it, all in one path.
        var r = ConversorSvg.Convertir(Svg("M0 0 H10 V10 H0 Z M2 2 H8 V8 H2 Z M20 0 H30 V10 H20 Z"), 0.5);
        Assert.Equal(2, r.Trazados.Count);
        Assert.Equal(2, Contornos(r.Trazados[0].D).Count); // the square keeps its hole
        Assert.Single(Contornos(r.Trazados[1].D));
        Assert.Equal(["nonzero", "evenodd"], r.Trazados.Select(t => t.ReglaRelleno));
    }

    [Fact]
    public void HuecoQueGiraIgualQueSuContornoSeDaVuelta()
    {
        // Both outlines clockwise: with nonzero the hole would be filled.
        var r = ConversorSvg.Convertir(Svg("M0 0 H10 V10 H0 Z M2 2 H8 V8 H2 Z"), 0.5);
        var c = Contornos(Assert.Single(r.Trazados).D);
        Assert.True(Math.Sign(Area(c[0])) == -Math.Sign(Area(c[1])));
        Assert.Equal(36, Math.Abs(Area(c[1])), 6);
    }

    [Fact]
    public void HuecoBienOrientadoQuedaIgual()
    {
        const string d = "M0 0 H10 V10 H0 Z M2 2 V8 H8 V2 H2 Z";
        var r = ConversorSvg.Convertir(Svg(d), 0.5);
        Assert.Equal(ConversorSvg.Convertir(Svg(d), 0.5).Svg, r.Svg);
        Assert.Single(r.Trazados);
        Assert.Equal("M0,0 L10,0 L10,10 L0,10 Z M2,2 L2,8 L8,8 L8,2 L2,2 Z", r.Trazados[0].D);
    }

    [Fact]
    public void IslaDentroDeUnHuecoEsOtraPieza()
    {
        // Square, hole, and a small square inside the hole (like the dot of a target).
        var r = ConversorSvg.Convertir(Svg("M0 0 H30 V30 H0 Z M5 5 V25 H25 V5 Z M10 10 H20 V20 H10 Z"), 0.5);
        Assert.Equal(2, r.Trazados.Count);
        Assert.Equal(2, Contornos(r.Trazados[0].D).Count);
        Assert.Single(Contornos(r.Trazados[1].D));
    }

    [Fact]
    public void TrazadoDeUnaSolaPiezaNoCambia()
    {
        const string d = "M0 0 L10 0 L10 10 Z";
        Assert.Equal("M0,0 L10,0 L10,10 Z", Assert.Single(ConversorSvg.Convertir(Svg(d), 0.5).Trazados).D);
    }

    [Fact]
    public void LogoUpConservaElHuecoDeLaP()
    {
        // "UP" exported from CorelDRAW: P, the P's hole, the U (with an arrow) and a thin sliver, all one path and
        // all turning the same way.
        const string d = "M24.44 7.33c-0.08,-0.73 0.23,-1.57 -0.67,-1.64 -0.66,-0.06 -1.63,0 -2.32,0 -0.91,-0 -1.48,-0.18 -1.48,0.83l0 16.3c0,0.44 -0.07,1.94 0.08,2.24 0.28,0.56 1.34,0.36 1.98,0.36 2.89,-0 2.41,0.72 2.41,-4.59 0,-0.63 -0.07,-1.77 0.02,-2.34 0.02,0.01 0.49,0.48 0.58,0.55 0.76,0.63 1.64,1.01 2.68,1.18 2.84,0.45 5.52,-0.71 6.86,-3.14 1.84,-3.35 1.22,-8.76 -2.57,-10.8 -1.81,-0.97 -4.24,-1.07 -6.17,-0.07 -0.28,0.15 -0.53,0.33 -0.77,0.52 -0.19,0.14 -0.15,0.14 -0.32,0.32 -0.11,0.12 -0.21,0.19 -0.3,0.29zm2.68 1.74c-2.01,0.38 -2.83,2.14 -2.68,4.45 0.14,2.08 1.59,3.67 3.89,3.28 2,-0.33 2.83,-2.22 2.64,-4.49 -0.16,-1.94 -1.6,-3.67 -3.86,-3.24zm-27.04 -3.47c0.1,0.06 -0.17,0.06 0.19,0.06 0.45,-0 1.54,-0.08 1.92,0.04l0.03 9.23c-0.07,2.09 1.28,4.24 3.38,5.05 2.04,0.78 5.32,0.75 7.51,0.21 0.55,-0.13 1.1,-0.33 1.5,-0.57 1.53,-0.89 2.24,-2.16 2.24,-4.16 0,-2.83 -0.01,-5.67 -0,-8.5 0,-1.57 0.02,-1.27 -2.93,-1.28 -0.72,-0 -1.47,-0.18 -1.57,0.57l-0.01 6.38c0,1.36 -0.1,2.59 -0.81,3.41 -0.56,0.65 -2.01,1.11 -3.21,0.69 -1.13,-0.39 -1.59,-1.39 -1.67,-2.58 -0.05,-0.82 -0.08,-7.92 0,-8.47l2.07 0c-0.15,-0.33 -0.31,-0.45 -0.53,-0.74l-3.81 -4.96c-0.01,0.01 -0.03,0.01 -0.04,0.03l-1.04 1.35c-0.19,0.24 -0.35,0.46 -0.53,0.69l-2.7 3.53zm2.14 9.34l-0.03 -9.23c-0.39,-0.12 -1.47,-0.05 -1.92,-0.04 -0.36,-0 -0.1,-0 -0.19,-0.06 -0.08,0.08 -0.1,-0.11 -0.05,0.11l2.11 0.01c-0.02,1.07 -0.1,8.88 0.09,9.22z";
        foreach (var perfil in Enum.GetValues<PerfilRelleno>())
        {
            var r = ConversorSvg.Convertir(Svg(d, "style=\"fill-rule:evenodd\""), new OpcionesConversion(0.5, perfil));
            // No path may have an outline outside its biggest one: that's what Bambu breaks.
            Assert.All(r.Trazados, t => Assert.Equal(1, ConversorSvg.PiezasSeparadas(t.D)));
            var p = r.Trazados.Single(t => Contornos(t.D).Any(c => c.Min(q => q.X) > 24 && c.Max(q => q.X) < 31.5));
            var c = Contornos(p.D);
            Assert.Equal(2, c.Count);
            Assert.True(Math.Sign(Area(c[0])) == -Math.Sign(Area(c[1])), $"{perfil}: el hueco de la P gira igual que la P");
        }
    }
}
