using System.Text;
using System.Xml.Linq;

namespace LinealizadorSVG.Core;

/// <summary>
/// SVG basic shapes (rect, circle, ellipse, polygon, polyline) as equivalent path data, drawn in the direction the
/// SVG spec defines, so nonzero holes behave the same as in the original.
/// </summary>
internal static class Formas
{
    public static readonly HashSet<string> Nombres = ["rect", "circle", "ellipse", "polygon", "polyline"];

    /// <summary>Path data for the shape, or null when it has no area (zero size, fewer than two points).</summary>
    public static string? ComoTrazado(XElement el) => el.Name.LocalName switch
    {
        "rect" => Rectangulo(el),
        "circle" => Elipse(Num(el, "cx"), Num(el, "cy"), Num(el, "r"), Num(el, "r")),
        "ellipse" => Elipse(Num(el, "cx"), Num(el, "cy"), Num(el, "rx"), Num(el, "ry")),
        "polygon" => Poligono(el, cerrar: true),
        "polyline" => Poligono(el, cerrar: false),
        _ => null,
    };

    private static double Num(XElement el, string atributo) => Numeros.LeerPrefijo(el.Attribute(atributo)?.Value) ?? 0;

    private static double? NumOpcional(XElement el, string atributo) => Numeros.LeerPrefijo(el.Attribute(atributo)?.Value);

    private static string F(double v) => Numeros.Formatear(v);

    private static string? Rectangulo(XElement el)
    {
        double x = Num(el, "x"), y = Num(el, "y"), w = Num(el, "width"), h = Num(el, "height");
        if (w <= 0 || h <= 0) return null;
        // A missing rx takes ry's value and vice versa (SVG 1.1 §9.2).
        var rx = NumOpcional(el, "rx");
        var ry = NumOpcional(el, "ry");
        double rrx = Math.Clamp(rx ?? ry ?? 0, 0, w / 2), rry = Math.Clamp(ry ?? rx ?? 0, 0, h / 2);
        if (rrx == 0 || rry == 0) return $"M{F(x)},{F(y)} H{F(x + w)} V{F(y + h)} H{F(x)} Z";
        var a = $"A{F(rrx)},{F(rry)} 0 0 1 ";
        return $"M{F(x + rrx)},{F(y)} H{F(x + w - rrx)} {a}{F(x + w)},{F(y + rry)} V{F(y + h - rry)} {a}{F(x + w - rrx)},{F(y + h)} " +
               $"H{F(x + rrx)} {a}{F(x)},{F(y + h - rry)} V{F(y + rry)} {a}{F(x + rrx)},{F(y)} Z";
    }

    private static string? Elipse(double cx, double cy, double rx, double ry)
    {
        if (rx <= 0 || ry <= 0) return null;
        var a = $"A{F(rx)},{F(ry)} 0 0 1 ";
        return $"M{F(cx + rx)},{F(cy)} {a}{F(cx)},{F(cy + ry)} {a}{F(cx - rx)},{F(cy)} {a}{F(cx)},{F(cy - ry)} {a}{F(cx + rx)},{F(cy)} Z";
    }

    private static string? Poligono(XElement el, bool cerrar)
    {
        var v = Numeros.Numero().Matches(el.Attribute("points")?.Value ?? "").Select(m => Numeros.Leer(m.Value)).ToArray();
        if (v.Length < 4) return null;
        var sb = new StringBuilder($"M{F(v[0])},{F(v[1])}");
        for (var i = 2; i + 1 < v.Length; i += 2) sb.Append($" L{F(v[i])},{F(v[i + 1])}");
        if (cerrar) sb.Append(" Z");
        return sb.ToString();
    }
}
