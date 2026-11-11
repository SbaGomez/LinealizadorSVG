namespace LinealizadorSVG.Core;

/// <summary>Which fill rule each path of the result gets.</summary>
public enum PerfilRelleno
{
    /// <summary>First path nonzero, the rest evenodd: the profile validated with the GymUP logo (HTML tool V5).</summary>
    GymUp,

    /// <summary>The rule each path already had (attribute, CSS class or inherited style); nonzero when none.</summary>
    Original,

    TodoNonzero,
    TodoEvenodd,
}

/// <param name="Precision">Target length of the straight segments, in SVG units.</param>
public sealed record OpcionesConversion(double Precision = 0.5, PerfilRelleno Perfil = PerfilRelleno.GymUp);
