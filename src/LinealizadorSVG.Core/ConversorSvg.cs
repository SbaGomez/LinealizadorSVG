using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace LinealizadorSVG.Core;

/// <summary>One path of the clean SVG, ready for Bambu Studio.</summary>
/// <param name="D">Absolute M/L/Z commands with the element and group transforms already applied.</param>
/// <param name="Relleno">Fill as written in the source (a color, or "none").</param>
/// <param name="ReglaRelleno">"nonzero" for the first path, "evenodd" for the rest.</param>
public sealed record TrazadoBambu(string D, string Relleno, string ReglaRelleno, string? Opacidad);

/// <param name="Svg">The file to save, UTF-8 text with XML declaration.</param>
/// <param name="VistaX">viewBox of the result (min-x, min-y, width, height), for drawing a preview.</param>
public sealed record ResultadoConversion(
    string Svg,
    IReadOnlyList<TrazadoBambu> Trazados,
    int Segmentos,
    double VistaX, double VistaY, double VistaAncho, double VistaAlto);

/// <summary>
/// Rebuilds an SVG (typically exported from CorelDRAW) as a flat list of straight-segment paths: no groups, CSS
/// classes or inherited styles, which is what Bambu Studio imports reliably. Same profile as the HTML tool V5:
/// the first path is filled nonzero and the following ones evenodd (validated with the GymUP logo); sub-paths
/// and the document size are kept.
/// </summary>
public static partial class ConversorSvg
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";
    private static readonly string[] AtributosRaiz = ["width", "height", "viewBox", "preserveAspectRatio", "version"];

    [GeneratedRegex(@"\.([-\w]+)\s*\{([^}]*)\}")]
    private static partial Regex ReglaCss();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Espacios();

    /// <summary>Name for the converted file: "logo.svg" → "logo_BAMBU.svg".</summary>
    public static string NombreSalida(string nombreOriginal) =>
        Path.GetFileNameWithoutExtension(nombreOriginal) + "_BAMBU.svg";

    public static ResultadoConversion Convertir(string svg, double precision)
    {
        var raiz = Cargar(svg).Root!;
        if (raiz.Name.LocalName != "svg") throw new SvgInvalidoException("El documento no contiene una raíz SVG.");
        if (!raiz.Descendants(Svg + "path").Any())
            throw new SvgInvalidoException("No encontré elementos path. Convertí los textos a curvas en CorelDRAW (Ctrl+Q) y exportá el SVG de nuevo.");

        // CorelDRAW writes the fills as classes in a <style> block: ".fil0 {fill:#373435}".
        var clases = new Dictionary<string, Dictionary<string, string>>();
        foreach (var estilo in raiz.DescendantsAndSelf().Where(e => e.Name.LocalName == "style"))
        foreach (Match m in ReglaCss().Matches(estilo.Value))
        {
            if (!clases.TryGetValue(m.Groups[1].Value, out var reglas)) clases[m.Groups[1].Value] = reglas = [];
            foreach (var (k, v) in LeerEstilo(m.Groups[2].Value)) reglas[k] = v;
        }

        var atributos = new List<XAttribute>();
        foreach (var nombre in AtributosRaiz)
            if (raiz.Attribute(nombre) is { } a) atributos.Add(new XAttribute(nombre, a.Value));
        if (raiz.Attribute("viewBox") is null)
        {
            var w = Positivo(Numeros.LeerPrefijo(raiz.Attribute("width")?.Value)) ?? 100;
            var h = Positivo(Numeros.LeerPrefijo(raiz.Attribute("height")?.Value)) ?? 100;
            atributos.Insert(Math.Min(2, atributos.Count), new XAttribute("viewBox", $"0 0 {Numeros.Formatear(w)} {Numeros.Formatear(h)}"));
        }

        var trazados = new List<TrazadoBambu>();
        var segmentos = 0;

        void Recorrer(XElement el, Matriz padre, IReadOnlyDictionary<string, string> heredado)
        {
            var propios = new Dictionary<string, string>(heredado);
            foreach (var clase in Espacios().Split(el.Attribute("class")?.Value ?? "").Where(c => c.Length > 0))
                if (clases.TryGetValue(clase, out var reglas))
                    foreach (var (k, v) in reglas) propios[k] = v;
            foreach (var (k, v) in LeerEstilo(el.Attribute("style")?.Value)) propios[k] = v;
            var m = padre * Matriz.Leer(el.Attribute("transform")?.Value);

            if (el.Name.LocalName != "path")
            {
                foreach (var hijo in el.Elements()) Recorrer(hijo, m, propios);
                return;
            }

            var d = el.Attribute("d")?.Value;
            if (string.IsNullOrEmpty(d)) return;
            var lineal = Linealizador.Linealizar(d, precision);
            var relleno = NoVacio(el.Attribute("fill")?.Value) ?? NoVacio(propios.GetValueOrDefault("fill")) ?? "#000000";
            var opacidad = NoVacio(el.Attribute("opacity")?.Value) ?? NoVacio(propios.GetValueOrDefault("opacity"));
            // GymUP profile: first path nonzero, the rest evenodd.
            var regla = trazados.Count == 0 ? "nonzero" : "evenodd";
            trazados.Add(new TrazadoBambu(m.AplicarATrazado(lineal.D), relleno, regla, opacidad));
            segmentos += lineal.Segmentos;
        }

        foreach (var hijo in raiz.Elements()) Recorrer(hijo, Matriz.Identidad, new Dictionary<string, string>());
        if (trazados.Count == 0) throw new SvgInvalidoException("No se pudieron procesar los trazados del SVG.");

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
        return new ResultadoConversion(texto, trazados, segmentos, vista[0], vista[1], vista[2], vista[3]);
    }

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
