using System.Xml.Linq;
using LinealizadorSVG.Core;

namespace LinealizadorSVG.Core.Tests;

public class PerfilesTests
{
    private const string TresTrazados = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10" style="fill-rule:evenodd">
          <style>.a {fill:red;fill-rule:nonzero}</style>
          <path class="a" d="M0 0 L1 1"/>
          <path d="M0 0 L2 2"/>
          <path fill-rule="nonzero" d="M0 0 L3 3"/>
        </svg>
        """;

    private static string[] Reglas(PerfilRelleno perfil) =>
        [.. ConversorSvg.Convertir(TresTrazados, new OpcionesConversion(0.5, perfil)).Trazados.Select(t => t.ReglaRelleno)];

    [Fact]
    public void GymUpEsElPredeterminado()
    {
        Assert.Equal(["nonzero", "evenodd", "evenodd"], Reglas(PerfilRelleno.GymUp));
        Assert.Equal(PerfilRelleno.GymUp, new OpcionesConversion().Perfil);
    }

    [Fact]
    public void OriginalRespetaClaseAtributoYEstiloDeLaRaiz()
    {
        Assert.Equal(["nonzero", "evenodd", "nonzero"], Reglas(PerfilRelleno.Original));
    }

    [Fact]
    public void TodoIgual()
    {
        Assert.All(Reglas(PerfilRelleno.TodoNonzero), r => Assert.Equal("nonzero", r));
        Assert.All(Reglas(PerfilRelleno.TodoEvenodd), r => Assert.Equal("evenodd", r));
    }
}

public class FormasYAvisosTests
{
    private static ResultadoConversion Convertir(string cuerpo) =>
        ConversorSvg.Convertir($"""<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" viewBox="0 0 100 100">{cuerpo}</svg>""", 0.5);

    [Fact]
    public void RectanguloSeConvierteEnSentidoHorario()
    {
        var t = Convertir("""<rect x="10" y="20" width="30" height="40" fill="#123456"/>""").Trazados.Single();
        Assert.Equal("M10,20 L40,20 L40,60 L10,60 Z", t.D);
        Assert.Equal("#123456", t.Relleno);
    }

    [Fact]
    public void RectanguloRedondeadoUsaArcos()
    {
        var d = Convertir("""<rect width="20" height="10" rx="3"/>""").Trazados.Single().D;
        Assert.StartsWith("M3,0 L17,0 ", d);
        Assert.True(d.Split(' ').Length > 12); // the corners are curves now
    }

    [Fact]
    public void CirculoElipseYPoligonos()
    {
        var r = Convertir("""
            <circle cx="50" cy="50" r="10"/>
            <ellipse cx="20" cy="20" rx="5" ry="3"/>
            <polygon points="0,0 10,0 10,10"/>
            <polyline points="0 0, 5 5, 10 0"/>
            <circle r="0"/>
            """);
        Assert.Equal(4, r.Trazados.Count);
        Assert.StartsWith("M60,50 ", r.Trazados[0].D);
        Assert.EndsWith("L60,50 Z", r.Trazados[0].D);
        Assert.Equal("M0,0 L10,0 L10,10 Z", r.Trazados[2].D);
        Assert.Equal("M0,0 L5,5 L10,0", r.Trazados[3].D);
    }

    [Fact]
    public void DefinicionesYRecortesNoSeImprimen()
    {
        var r = Convertir("""
            <defs><path id="oculto" d="M0 0 L9 9"/></defs>
            <clipPath id="c"><rect width="5" height="5"/></clipPath>
            <g clip-path="url(#c)"><path d="M1 1 L2 2"/><path d="M1 1 L3 3"/></g>
            """);
        Assert.Equal(2, r.Trazados.Count);
        Assert.Contains(r.Avisos, a => a.Contains("1 recorte"));
    }

    [Fact]
    public void UseYSimbolosSeDibujanDondeSeUsan()
    {
        var r = Convertir("""
            <defs><path id="p" fill="red" d="M0 0 L1 0"/></defs>
            <symbol id="s"><path d="M0 0 L0 1"/></symbol>
            <use xlink:href="#p" x="10" y="20"/>
            <use href="#s" transform="translate(5 5)"/>
            <use href="#noexiste"/>
            """);
        Assert.Equal(["M10,20 L11,20", "M5,5 L5,6"], r.Trazados.Select(t => t.D));
        Assert.Equal("red", r.Trazados[0].Relleno);
    }

    [Fact]
    public void UseQueSeReferenciaASiMismoNoCuelga()
    {
        var r = Convertir("""<g id="g"><path d="M0 0 L1 1"/><use href="#g"/></g>""");
        Assert.Single(r.Trazados);
    }

    [Fact]
    public void RellenoComoAtributoDeUnGrupoSeHereda()
    {
        Assert.Equal("#00FF00", Convertir("""<g fill="#00FF00"><path d="M0 0 L1 1"/></g>""").Trazados.Single().Relleno);
    }

    [Fact]
    public void AvisaTextosImagenesYContornos()
    {
        var r = Convertir("""
            <path d="M0 0 L1 1"/>
            <text>Hola</text><text>Chau</text>
            <image href="foto.png" width="10" height="10"/>
            <path fill="none" stroke="black" d="M0 0 L5 5"/>
            <line x1="0" y1="0" x2="5" y2="5" stroke="black"/>
            """);
        Assert.Equal(3, r.Avisos.Count);
        Assert.Contains(r.Avisos, a => a.StartsWith("2 textos sin convertir a curvas quedaron afuera"));
        Assert.Contains(r.Avisos, a => a.StartsWith("1 imagen"));
        Assert.Contains(r.Avisos, a => a.StartsWith("2 formas tienen solo contorno"));
    }

    [Fact]
    public void UnDisenoLimpioNoTieneAvisos()
    {
        Assert.Empty(Convertir("""<path d="M0 0 L1 1"/>""").Avisos);
    }
}

public class LoteTests : IDisposable
{
    private readonly string _carpeta = Directory.CreateTempSubdirectory("linealizador-").FullName;

    public void Dispose() => Directory.Delete(_carpeta, recursive: true);

    private string Crear(string nombre, string contenido)
    {
        var ruta = Path.Combine(_carpeta, nombre);
        File.WriteAllText(ruta, contenido);
        return ruta;
    }

    [Fact]
    public void BuscaSvgsSinLosYaConvertidos()
    {
        Crear("b.svg", "");
        Crear("A.svg", "");
        Crear("a_BAMBU.svg", "");
        Crear("nota.txt", "");
        Assert.Equal(["A.svg", "b.svg"], Lote.BuscarSvgs(_carpeta).Select(Path.GetFileName));
    }

    [Fact]
    public void ConvierteYGuardaAlLado()
    {
        var ruta = Crear("logo.svg", """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 9 9"><path d="M0 0 L1 1"/></svg>""");
        var r = Lote.ConvertirArchivo(ruta, new OpcionesConversion());
        Assert.Null(r.Error);
        Assert.Equal(Path.Combine(_carpeta, "logo_BAMBU.svg"), r.Salida);
        Assert.Equal(r.Resultado!.Svg, File.ReadAllText(r.Salida!));
        Assert.Equal("svg", XDocument.Load(r.Salida!).Root!.Name.LocalName);
    }

    [Fact]
    public void UnArchivoMaloNoCortaElLote()
    {
        var r = Lote.ConvertirArchivo(Crear("roto.svg", "<svg"), new OpcionesConversion());
        Assert.Null(r.Salida);
        Assert.NotNull(r.Error);
        Assert.False(File.Exists(Path.Combine(_carpeta, "roto_BAMBU.svg")));
    }
}
