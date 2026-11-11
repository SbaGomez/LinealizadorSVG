using System.Text;
using System.Text.RegularExpressions;

namespace LinealizadorSVG.Core;

/// <summary>2D affine transform in SVG order: [a b c d e f] maps (x, y) to (a·x + c·y + e, b·x + d·y + f).</summary>
public readonly partial record struct Matriz(double A, double B, double C, double D, double E, double F)
{
    public static Matriz Identidad { get; } = new(1, 0, 0, 1, 0, 0);

    public bool EsIdentidad => this == Identidad;

    public static Matriz operator *(Matriz m, Matriz n) => new(
        m.A * n.A + m.C * n.B,
        m.B * n.A + m.D * n.B,
        m.A * n.C + m.C * n.D,
        m.B * n.C + m.D * n.D,
        m.A * n.E + m.C * n.F + m.E,
        m.B * n.E + m.D * n.F + m.F);

    public (double X, double Y) Aplicar(double x, double y) => (A * x + C * y + E, B * x + D * y + F);

    [GeneratedRegex(@"([a-zA-Z]+)\s*\(([^)]*)\)")]
    private static partial Regex Funcion();

    /// <summary>Parses a <c>transform</c> attribute ("translate(10 5) rotate(30)"); unknown functions are skipped.</summary>
    public static Matriz Leer(string? transform)
    {
        var m = Identidad;
        foreach (Match f in Funcion().Matches(transform ?? ""))
        {
            var v = Numeros.Numero().Matches(f.Groups[2].Value).Select(x => Numeros.Leer(x.Value)).ToArray();
            if (v.Length == 0) continue;
            Matriz? n = f.Groups[1].Value switch
            {
                "matrix" when v.Length >= 6 => new Matriz(v[0], v[1], v[2], v[3], v[4], v[5]),
                "translate" => new Matriz(1, 0, 0, 1, v[0], v.Length > 1 ? v[1] : 0),
                "scale" => new Matriz(v[0], 0, 0, v.Length > 1 ? v[1] : v[0], 0, 0),
                "rotate" => Rotacion(v),
                "skewX" => new Matriz(1, 0, Math.Tan(v[0] * Math.PI / 180), 1, 0, 0),
                "skewY" => new Matriz(1, Math.Tan(v[0] * Math.PI / 180), 0, 1, 0, 0),
                _ => null,
            };
            if (n is { } nn) m *= nn;
        }
        return m;
    }

    private static Matriz Rotacion(double[] v)
    {
        double a = v[0] * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
        var r = new Matriz(c, s, -s, c, 0, 0);
        return v.Length > 2 ? new Matriz(1, 0, 0, 1, v[1], v[2]) * r * new Matriz(1, 0, 0, 1, -v[1], -v[2]) : r;
    }

    /// <summary>
    /// Transforms path data that is already absolute and made of M/L/Z only (what <see cref="Linealizador"/> returns).
    /// </summary>
    public string AplicarATrazado(string d)
    {
        if (EsIdentidad) return d;
        var t = Numeros.Token().Matches(d).Select(x => x.Value).ToArray();
        var sb = new StringBuilder();
        var trasComando = false; // the coordinates go right after their letter: "M1,2 L3,4"
        for (var i = 0; i < t.Length;)
        {
            if (char.IsAsciiLetter(t[i][0]))
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(t[i++]);
                trasComando = true;
                continue;
            }
            if (i + 1 >= t.Length) break;
            var (x, y) = Aplicar(Numeros.Leer(t[i]), Numeros.Leer(t[i + 1]));
            i += 2;
            if (!trasComando && sb.Length > 0) sb.Append(' ');
            sb.Append(Numeros.Formatear(x)).Append(',').Append(Numeros.Formatear(y));
            trasComando = false;
        }
        return sb.ToString();
    }
}
