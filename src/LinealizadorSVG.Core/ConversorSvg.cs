using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace LinealizadorSVG.Core;

/// <summary>One path of the clean SVG, ready for Bambu Studio.</summary>
/// <param name="D">Absolute M/L/Z commands with the element and group transforms already applied.</param>
/// <param name="Relleno">Fill as written in the source (a color, or "none").</param>
/// <param name="ReglaRelleno">"nonzero" or "evenodd", as chosen by the <see cref="PerfilRelleno"/>.</param>
/// <param name="EnRiesgo">
/// Only in <see cref="ConversorSvg.SimularSinConvertir"/>: the original path has curves and several separate pieces,
/// the combination Bambu Studio's importer tends to break when it isn't converted.
/// </param>
public sealed record TrazadoBambu(string D, string Relleno, string ReglaRelleno, string? Opacidad, bool EnRiesgo = false);

/// <param name="Svg">The file to save, UTF-8 text with XML declaration.</param>
/// <param name="VistaX">viewBox of the result (min-x, min-y, width, height), for drawing a preview.</param>
/// <param name="Avisos">Things of the original that don't make it to the print (texts, images, clips…), for the user.</param>
public sealed record ResultadoConversion(
    string Svg,
    IReadOnlyList<TrazadoBambu> Trazados,
    int Segmentos,
    double VistaX, double VistaY, double VistaAncho, double VistaAlto,
    IReadOnlyList<string> Avisos);

/// <summary>
/// Rebuilds an SVG (typically exported from CorelDRAW) as a flat list of straight-segment paths: no groups, CSS
/// classes or inherited styles, which is what Bambu Studio imports reliably. With the default options it behaves
/// like the HTML tool V5 (first path nonzero, the rest evenodd, validated with the GymUP logo); on top of that it
/// converts basic shapes (rect, circle…), follows &lt;use&gt; references, and leaves out definitions and clip
/// paths instead of printing them as shapes.
/// </summary>
public static partial class ConversorSvg
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";
    private static readonly XNamespace XLink = "http://www.w3.org/1999/xlink";
    private static readonly string[] AtributosRaiz = ["width", "height", "viewBox", "preserveAspectRatio", "version"];

    /// <summary>Elements that define things used elsewhere, or that aren't drawn: never printed as shapes.</summary>
    private static readonly HashSet<string> NoSeDibujan =
    [
        "defs", "clipPath", "mask", "pattern", "symbol", "marker", "metadata", "title", "desc", "style", "script",
        "linearGradient", "radialGradient", "filter", "foreignObject",
    ];

    /// <summary>Presentation attributes a group passes down to its shapes.</summary>
    private static readonly string[] Heredables = ["fill", "fill-rule"];

    [GeneratedRegex(@"\.([-\w]+)\s*\{([^}]*)\}")]
    private static partial Regex ReglaCss();

    [GeneratedRegex("[CcSsQqTtAa]")]
    private static partial Regex Curvas();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Espacios();

    /// <summary>Name for the converted file: "logo.svg" → "logo_BAMBU.svg".</summary>
    public static string NombreSalida(string nombreOriginal) =>
        Path.GetFileNameWithoutExtension(nombreOriginal) + "_BAMBU.svg";

    public static ResultadoConversion Convertir(string svg, double precision) => Convertir(svg, new OpcionesConversion(precision));

    public static ResultadoConversion Convertir(string svg, OpcionesConversion opciones) => Convertir(svg, opciones, comoBambu: false);

    /// <summary>
    /// The original file, for comparing with the conversion: each path with its own fill rule, flagged
    /// <see cref="TrazadoBambu.EnRiesgo"/> when Bambu Studio is likely to break it. Bambu's importer ignores the fill
    /// rule, takes the biggest outline of each path as the piece and every other outline as a hole, and builds the
    /// face from the curves' sampled points: a path with curves and pieces outside its biggest one comes out
    /// deformed (letters turned into boxes). &lt;use&gt; references are left out, as Bambu's SVG reader doesn't
    /// follow them. Never throws for an SVG without shapes: the result just has no paths.
    /// </summary>
    public static ResultadoConversion SimularSinConvertir(string svg, double precision = 0.5) =>
        Convertir(svg, new OpcionesConversion(precision, PerfilRelleno.Original), comoBambu: true);

    private static ResultadoConversion Convertir(string svg, OpcionesConversion opciones, bool comoBambu)
    {
        if (!(opciones.Precision > 0)) throw new ArgumentOutOfRangeException(nameof(opciones), "La precisión tiene que ser mayor que cero.");
        var raiz = Cargar(svg).Root!;
        if (raiz.Name.LocalName != "svg") throw new SvgInvalidoException("El documento no contiene una raíz SVG.");

        // CorelDRAW writes the fills as classes in a <style> block: ".fil0 {fill:#373435}".
        var clases = new Dictionary<string, Dictionary<string, string>>();
        foreach (var estilo in raiz.DescendantsAndSelf().Where(e => e.Name.LocalName == "style"))
        foreach (Match m in ReglaCss().Matches(estilo.Value))
        {
            if (!clases.TryGetValue(m.Groups[1].Value, out var reglas)) clases[m.Groups[1].Value] = reglas = [];
            foreach (var (k, v) in LeerEstilo(m.Groups[2].Value)) reglas[k] = v;
        }

        var porId = new Dictionary<string, XElement>();
        foreach (var e in raiz.DescendantsAndSelf())
            if (e.Attribute("id")?.Value is { Length: > 0 } id) porId.TryAdd(id, e);

        var atributos = new List<XAttribute>();
        foreach (var nombre in AtributosRaiz)
            if (raiz.Attribute(nombre) is { } a) atributos.Add(new XAttribute(nombre, a.Value));
        if (raiz.Attribute("viewBox") is null)
        {
            var w = Positivo(Numeros.LeerPrefijo(raiz.Attribute("width")?.Value)) ?? 100;
            var h = Positivo(Numeros.LeerPrefijo(raiz.Attribute("height")?.Value)) ?? 100;
            atributos.Add(new XAttribute("viewBox", $"0 0 {Numeros.Formatear(w)} {Numeros.Formatear(h)}"));
        }

        var trazados = new List<TrazadoBambu>();
        var segmentos = 0;
        int textos = 0, imagenes = 0, soloContorno = 0, recortes = 0;
        var enUso = new HashSet<XElement>(); // <use> chain being followed, to stop reference loops

        Dictionary<string, string> Estilo(XElement el, IReadOnlyDictionary<string, string> heredado)
        {
            var propios = new Dictionary<string, string>(heredado);
            foreach (var k in Heredables)
                if (NoVacio(el.Attribute(k)?.Value) is { } v) propios[k] = v;
            foreach (var clase in Espacios().Split(el.Attribute("class")?.Value ?? "").Where(c => c.Length > 0))
                if (clases.TryGetValue(clase, out var reglas))
                    foreach (var (k, v) in reglas) propios[k] = v;
            foreach (var (k, v) in LeerEstilo(el.Attribute("style")?.Value)) propios[k] = v;
            return propios;
        }

        void Recorrer(XElement el, Matriz padre, IReadOnlyDictionary<string, string> heredado)
        {
            var nombre = el.Name.LocalName;
            if (NoSeDibujan.Contains(nombre)) return;
            if (nombre == "text") { textos++; return; }
            if (nombre == "image") { imagenes++; return; }
            if (nombre == "line") { soloContorno++; return; } // a line has no area to fill

            var propios = Estilo(el, heredado);
            // Counted on the element that clips; removed so its children don't count it again.
            var enEstilo = propios.Remove("clip-path") | propios.Remove("mask");
            if (enEstilo || el.Attribute("clip-path") is not null || el.Attribute("mask") is not null) recortes++;
            var m = padre * Matriz.Leer(el.Attribute("transform")?.Value);

            if (nombre == "use")
            {
                if (comoBambu) return; // nanosvg, Bambu's SVG reader, doesn't follow references
                var href = (el.Attribute(XLink + "href") ?? el.Attribute("href"))?.Value;
                // A reference to itself or to a group containing it is circular: browsers draw nothing for it.
                if (href is not { Length: > 1 } || href[0] != '#' || !porId.TryGetValue(href[1..], out var destino) ||
                    el.AncestorsAndSelf().Contains(destino) || !enUso.Add(destino)) return;
                var desplazada = m * new Matriz(1, 0, 0, 1, Numeros.LeerPrefijo(el.Attribute("x")?.Value) ?? 0, Numeros.LeerPrefijo(el.Attribute("y")?.Value) ?? 0);
                if (destino.Name.LocalName == "symbol")
                {
                    var delSimbolo = Estilo(destino, propios);
                    foreach (var hijo in destino.Elements()) Recorrer(hijo, desplazada, delSimbolo);
                }
                else Recorrer(destino, desplazada, propios);
                enUso.Remove(destino);
                return;
            }

            string? d;
            if (nombre == "path") d = el.Attribute("d")?.Value;
            else if (Formas.Nombres.Contains(nombre)) d = Formas.ComoTrazado(el);
            else
            {
                foreach (var hijo in el.Elements()) Recorrer(hijo, m, propios);
                return;
            }
            if (string.IsNullOrEmpty(d)) return;

            var lineal = Linealizador.Linealizar(d, opciones.Precision);
            var relleno = NoVacio(el.Attribute("fill")?.Value) ?? NoVacio(propios.GetValueOrDefault("fill")) ?? "#000000";
            var opacidad = NoVacio(el.Attribute("opacity")?.Value) ?? NoVacio(propios.GetValueOrDefault("opacity"));
            if (relleno.Equals("none", StringComparison.OrdinalIgnoreCase) &&
                NoVacio(el.Attribute("stroke")?.Value ?? propios.GetValueOrDefault("stroke")) is { } trazo &&
                !trazo.Equals("none", StringComparison.OrdinalIgnoreCase))
                soloContorno++;

            var regla = opciones.Perfil switch
            {
                PerfilRelleno.GymUp => trazados.Count == 0 ? "nonzero" : "evenodd",
                PerfilRelleno.TodoNonzero => "nonzero",
                PerfilRelleno.TodoEvenodd => "evenodd",
                _ => (NoVacio(el.Attribute("fill-rule")?.Value) ?? propios.GetValueOrDefault("fill-rule")) == "evenodd" ? "evenodd" : "nonzero",
            };
            var final = m.AplicarATrazado(lineal.D);
            var enRiesgo = comoBambu && Curvas().IsMatch(d) && PiezasSeparadas(final) > 1;
            trazados.Add(new TrazadoBambu(final, relleno, regla, opacidad, enRiesgo));
            segmentos += lineal.Segmentos;
        }

        // The root's own style (CorelDRAW puts "fill-rule:evenodd" there) is inherited like a group's.
        var deLaRaiz = Estilo(raiz, new Dictionary<string, string>());
        foreach (var hijo in raiz.Elements()) Recorrer(hijo, Matriz.Identidad, deLaRaiz);

        if (trazados.Count == 0 && !comoBambu)
            throw new SvgInvalidoException(textos > 0
                ? "El SVG solo tiene textos. Convertí los textos a curvas en CorelDRAW (Ctrl+Q) y exportá el SVG de nuevo."
                : "No encontré formas para convertir. Convertí los textos a curvas en CorelDRAW (Ctrl+Q) y exportá el SVG de nuevo.");

        var avisos = new List<string>();
        if (textos > 0)
            avisos.Add($"{Cantidad(textos, "texto sin convertir a curvas quedó afuera", "textos sin convertir a curvas quedaron afuera")}. En CorelDRAW seleccioná todo y usá Ctrl+Q antes de exportar.");
        if (imagenes > 0)
            avisos.Add($"{Cantidad(imagenes, "imagen (bitmap) quedó afuera", "imágenes (bitmaps) quedaron afuera")}: no se pueden convertir en formas. Vectorizalas en CorelDRAW.");
        if (soloContorno > 0)
            avisos.Add($"{Cantidad(soloContorno, "forma tiene", "formas tienen")} solo contorno, sin relleno: en Bambu no van a tener volumen. En CorelDRAW convertí el contorno en objeto (Ctrl+Mayús+Q).");
        if (recortes > 0)
            avisos.Add($"El diseño usa {Cantidad(recortes, "recorte", "recortes")} (PowerClip o máscara): se ignoran, así que lo recortado aparece completo. Revisá el resultado.");

        var nuevo = new XElement(Svg + "svg", atributos);
        foreach (var t in trazados)
        {
            var p = new XElement(Svg + "path",
                new XAttribute("d", t.D), new XAttribute("fill", t.Relleno), new XAttribute("fill-rule", t.ReglaRelleno));
            if (t.Opacidad is { } o) p.Add(new XAttribute("opacity", o));
            nuevo.Add(p);
        }

        var vista = LeerVista(nuevo.Attribute("viewBox")!.Value);
        var texto = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + nuevo.ToString().Replace("\r\n", "\n") + "\n";
        return new ResultadoConversion(texto, trazados, segmentos, vista[0], vista[1], vista[2], vista[3], avisos);
    }

    /// <summary>
    /// How many outlines of a straight-segment path lie outside its biggest one (plus that one): the pieces Bambu
    /// Studio would have to build as a single face with "holes" that aren't inside it.
    /// </summary>
    internal static int PiezasSeparadas(string dLineal)
    {
        var contornos = new List<List<(double X, double Y)>>();
        foreach (var parte in dLineal.Split('M', StringSplitOptions.RemoveEmptyEntries))
        {
            var n = Numeros.Numero().Matches(parte).Select(x => Numeros.Leer(x.Value)).ToArray();
            var puntos = new List<(double, double)>();
            for (var i = 0; i + 1 < n.Length; i += 2) puntos.Add((n[i], n[i + 1]));
            if (puntos.Count >= 3) contornos.Add(puntos);
        }
        if (contornos.Count < 2) return contornos.Count;
        var mayor = contornos.MaxBy(c => Math.Abs(Area(c)))!;
        return 1 + contornos.Count(c => c != mayor && !Adentro(c[0], mayor));
    }

    private static double Area(List<(double X, double Y)> p)
    {
        double a = 0;
        for (int i = 0, j = p.Count - 1; i < p.Count; j = i++) a += p[j].X * p[i].Y - p[i].X * p[j].Y;
        return a / 2;
    }

    private static bool Adentro((double X, double Y) q, List<(double X, double Y)> p)
    {
        var dentro = false;
        for (int i = 0, j = p.Count - 1; i < p.Count; j = i++)
            if ((p[i].Y > q.Y) != (p[j].Y > q.Y) && q.X < (p[j].X - p[i].X) * (q.Y - p[i].Y) / (p[j].Y - p[i].Y) + p[i].X)
                dentro = !dentro;
        return dentro;
    }

    private static string Cantidad(int n, string singular, string plural) => n == 1 ? "1 " + singular : $"{n} {plural}";

    private static XDocument Cargar(string svg)
    {
        // CorelDRAW and Illustrator files carry a DOCTYPE (and Illustrator, entities): parse it, but never fetch
        // anything from the internet and cap entity expansion.
        var opciones = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Parse,
            XmlResolver = null,
            MaxCharactersFromEntities = 10_000_000,
        };
        try
        {
            using var lector = XmlReader.Create(new StringReader(svg), opciones);
            return XDocument.Load(lector);
        }
        catch (XmlException)
        {
            throw new SvgInvalidoException("El archivo no es un SVG válido.");
        }
    }

    private static Dictionary<string, string> LeerEstilo(string? texto)
    {
        var o = new Dictionary<string, string>();
        foreach (var parte in (texto ?? "").Split(';'))
        {
            var k = parte.IndexOf(':');
            if (k > 0) o[parte[..k].Trim()] = parte[(k + 1)..].Trim();
        }
        return o;
    }

    private static double[] LeerVista(string viewBox)
    {
        var v = Numeros.Numero().Matches(viewBox).Select(m => Numeros.Leer(m.Value)).ToArray();
        return v.Length >= 4 && v[2] > 0 && v[3] > 0 ? v[..4] : [0, 0, 100, 100];
    }

    private static string? NoVacio(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static double? Positivo(double? v) => v > 0 ? v : null;
}
