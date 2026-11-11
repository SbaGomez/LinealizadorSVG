namespace LinealizadorSVG.Core;

/// <summary>The SVG can't be converted; the message is ready to show to the user.</summary>
public sealed class SvgInvalidoException(string mensaje) : Exception(mensaje);
