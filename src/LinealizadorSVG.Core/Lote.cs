using System.Text;

namespace LinealizadorSVG.Core;

/// <summary>Outcome of converting one file of a batch.</summary>
/// <param name="Salida">Where the _BAMBU.svg was written; null when it failed.</param>
public sealed record ResultadoArchivo(string Origen, string? Salida, ResultadoConversion? Resultado, string? Error);

/// <summary>Converts several SVGs at once, each saved as name_BAMBU.svg next to its original.</summary>
public static class Lote
{
    /// <summary>The SVGs to convert in a folder (not its subfolders), leaving out the _BAMBU.svg already made.</summary>
    public static IReadOnlyList<string> BuscarSvgs(string carpeta) =>
        [.. Directory.EnumerateFiles(carpeta, "*.svg")
            .Where(EsConvertible)
            .Order(StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>An .svg that isn't a result of this app.</summary>
    public static bool EsConvertible(string ruta) =>
        ruta.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) &&
        !Path.GetFileNameWithoutExtension(ruta).EndsWith("_BAMBU", StringComparison.OrdinalIgnoreCase);

    /// <summary>Converts and saves one file; never throws, the error goes in the result.</summary>
    public static ResultadoArchivo ConvertirArchivo(string ruta, OpcionesConversion opciones)
    {
        try
        {
            var resultado = ConversorSvg.Convertir(File.ReadAllText(ruta), opciones);
            var salida = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(ruta))!, ConversorSvg.NombreSalida(ruta));
            File.WriteAllText(salida, resultado.Svg, new UTF8Encoding(false));
            return new ResultadoArchivo(ruta, salida, resultado, null);
        }
        catch (SvgInvalidoException e)
        {
            return new ResultadoArchivo(ruta, null, null, e.Message);
        }
        catch (IOException e)
        {
            return new ResultadoArchivo(ruta, null, null, "No se pudo leer o guardar: " + e.Message);
        }
        catch (UnauthorizedAccessException)
        {
            return new ResultadoArchivo(ruta, null, null, "Windows no deja escribir en esa carpeta.");
        }
    }
}
