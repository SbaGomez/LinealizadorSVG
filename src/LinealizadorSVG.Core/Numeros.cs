using System.Globalization;
using System.Text.RegularExpressions;

namespace LinealizadorSVG.Core;

/// <summary>Number parsing and formatting shared by the path and transform code.</summary>
internal static partial class Numeros
{
    /// <summary>Path data tokens: single-letter commands and numbers (".5", "-1e3", "1.5.5" → "1.5", ".5").</summary>
    [GeneratedRegex(@"[a-zA-Z]|[-+]?(?:\d*\.)?\d+(?:[eE][-+]?\d+)?")]
    public static partial Regex Token();

    [GeneratedRegex(@"[-+]?(?:\d*\.)?\d+(?:e[-+]?\d+)?", RegexOptions.IgnoreCase)]
    public static partial Regex Numero();

    [GeneratedRegex(@"^\s*[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?")]
    private static partial Regex Prefijo();

    public static double Leer(string s) => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary>Like JavaScript's parseFloat: the leading number of "210mm" is 210; null when there's none.</summary>
    public static double? LeerPrefijo(string? s)
    {
        var m = Prefijo().Match(s ?? "");
        return m.Success ? Leer(m.Value) : null;
    }

    /// <summary>
    /// Up to 5 decimals, no trailing zeros, never exponent notation nor "-0". Rounds like JavaScript's toFixed(5):
    /// on the exact binary value, ties away from zero, so the output matches the HTML tool digit for digit.
    /// </summary>
    public static string Formatear(double n)
    {
        if (Math.Abs(n) < 1e-8) return "0";
        string s;
        try
        {
            // "F20" prints the double's exact digits; digits past the 20th can't turn a tie into a non-tie.
            var exacto = decimal.Parse(n.ToString("F20", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);
            s = Math.Round(exacto, 5, MidpointRounding.AwayFromZero).ToString("0.#####", CultureInfo.InvariantCulture);
        }
        catch (OverflowException)
        {
            s = n.ToString("0.#####", CultureInfo.InvariantCulture);
        }
        return s is "-0" or "0" or "" ? "0" : s;
    }
}
