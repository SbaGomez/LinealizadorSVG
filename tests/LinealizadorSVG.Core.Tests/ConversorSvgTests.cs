using System.Xml.Linq;
using LinealizadorSVG.Core;

namespace LinealizadorSVG.Core.Tests;

public class ConversorSvgTests
{
    private static readonly XNamespace Ns = "http://www.w3.org/2000/svg";

    // Shape of a CorelDRAW export: DOCTYPE, CDATA CSS classes, nested groups.
    private const string Corel = """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE svg PUBLIC "-//W3C//DTD SVG 1.1//EN" "http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd">
        <svg xmlns="http://www.w3.org/2000/svg" xml:space="preserve" width="50mm" height="20mm" version="1.1"
             style="shape-rendering:geometricPrecision" viewBox="0 0 5000 2000" xmlns:xlink="http://www.w3.org/1999/xlink">
         <defs>
          <style type="text/css">
           <![CDATA[
            .fil1 {fill:#FEFEFE}
            .fil0 {fill:#373435;fill-rule:nonzero}
           ]]>
          </style>
         </defs>
         <g id="Capa_x0020_1">
          <metadata id="CorelCorpID_0Corel-Layer"/>
          <path class="fil0" d="M0 0 L5000 0 L5000 2000 L0 2000 Z"/>
          <g transform="translate(100 200)">
            <path class="fil1" d="M0 0 C0 100 100 100 100 0 Z"/>
            <path class="fil1" style="fill:#FF0000" d="M10 10 h10 v10 z"/>
          </g>
         </g>
        </svg>
        """;

    private static List<XElement> Paths(ResultadoConversion r) => [.. XDocument.Parse(r.Svg).Root!.Elements()];

    [Fact]
    public void ReconstruyeUnSvgPlanoSinGruposNiCss()
    {
        var r = ConversorSvg.Convertir(Corel, 0.5);
        var raiz = XDocument.Parse(r.Svg).Root!;
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-8\"?>", r.Svg);
        Assert.Equal(Ns + "svg", raiz.Name);
        Assert.Equal("50mm", (string?)raiz.Attribute("width"));
        Assert.Equal("20mm", (string?)raiz.Attribute("height"));
        Assert.Equal("0 0 5000 2000", (string?)raiz.Attribute("viewBox"));
        Assert.Null(raiz.Attribute("style"));
        Assert.All(raiz.Elements(), e => Assert.Equal(Ns + "path", e.Name));
        Assert.Equal(3, raiz.Elements().Count());
        Assert.DoesNotContain("class", r.Svg);
        Assert.DoesNotContain("<g", r.Svg);
    }

    [Fact]
    public void RellenosSalenDeClasesYEstilosYElAtributoGana()
    {
        var p = Paths(ConversorSvg.Convertir(Corel, 0.5));
        Assert.Equal(["#373435", "#FEFEFE", "#FF0000"], p.Select(e => (string)e.Attribute("fill")!));

        const string conAtributo = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><path fill="blue" style="fill:red" d="M0 0 L1 1"/></svg>""";
        Assert.Equal("blue", (string?)Paths(ConversorSvg.Convertir(conAtributo, 0.5))[0].Attribute("fill"));
    }

    [Fact]
    public void PrimerTrazadoNonzeroYLosDemasEvenodd()
    {
        var p = Paths(ConversorSvg.Convertir(Corel, 0.5));
        Assert.Equal(["nonzero", "evenodd", "evenodd"], p.Select(e => (string)e.Attribute("fill-rule")!));
    }

    [Fact]
    public void AplicaLasTransformacionesDeLosGrupos()
    {
        var p = Paths(ConversorSvg.Convertir(Corel, 0.5));
        Assert.Equal("M110,210 L120,210 L120,220 Z", (string?)p[2].Attribute("d"));
        Assert.StartsWith("M100,200 ", (string)p[1].Attribute("d")!);
        Assert.DoesNotContain("C", (string)p[1].Attribute("d")!);
    }

    [Fact]
    public void TransformacionesAnidadasSeComponen()
    {
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100">
              <g transform="translate(10,0)"><g transform="scale(2)"><path transform="translate(1 1)" d="M0 0 L5 0"/></g></g>
            </svg>
            """;
        Assert.Equal("M12,2 L22,2", (string?)Paths(ConversorSvg.Convertir(svg, 0.5))[0].Attribute("d"));
    }

    [Fact]
    public void ConservaOpacidad()
    {
        const string svg = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><g style="opacity:0.5"><path d="M0 0 L1 1"/></g></svg>""";
        var p = Paths(ConversorSvg.Convertir(svg, 0.5))[0];
        Assert.Equal("0.5", (string?)p.Attribute("opacity"));
        Assert.Equal("#000000", (string?)p.Attribute("fill"));
    }

    [Fact]
    public void SinViewBoxLoArmaConElTamano()
    {
        const string svg = """<svg xmlns="http://www.w3.org/2000/svg" width="210mm" height="99.5"><path d="M0 0 L1 1"/></svg>""";
        var r = ConversorSvg.Convertir(svg, 0.5);
        Assert.Equal("0 0 210 99.5", (string?)XDocument.Parse(r.Svg).Root!.Attribute("viewBox"));
        Assert.Equal((0.0, 0.0, 210.0, 99.5), (r.VistaX, r.VistaY, r.VistaAncho, r.VistaAlto));
    }

    [Fact]
    public void CuentaTrazadosYSegmentos()
    {
        var r = ConversorSvg.Convertir(Corel, 1);
        Assert.Equal(3, r.Trazados.Count);
        Assert.True(r.Segmentos > 4 + 3 + 3);
    }

    [Fact]
    public void SinPathsPideConvertirTextosACurvas()
    {
        const string svg = """<svg xmlns="http://www.w3.org/2000/svg"><text>Hola</text></svg>""";
        var e = Assert.Throws<SvgInvalidoException>(() => ConversorSvg.Convertir(svg, 0.5));
        Assert.Contains("Ctrl+Q", e.Message);
    }

    [Theory]
    [InlineData("esto no es xml")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><path d=\"M0 0\"></svg>")]
    [InlineData("<html><path d=\"M0 0 L1 1\"/></html>")]
    public void ArchivosInvalidosDanError(string texto)
    {
        Assert.Throws<SvgInvalidoException>(() => ConversorSvg.Convertir(texto, 0.5));
    }

    [Fact]
    public void NoDescargaDtdsNiExpandeEntidadesExternas()
    {
        const string svg = """
            <?xml version="1.0"?>
            <!DOCTYPE svg [<!ENTITY ext SYSTEM "http://127.0.0.1:1/x">]>
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><desc>&ext;</desc><path d="M0 0 L1 1"/></svg>
            """;
        Assert.Single(ConversorSvg.Convertir(svg, 0.5).Trazados);
    }

    [Fact]
    public void NombreDeSalida()
    {
        Assert.Equal("logo GymUP_BAMBU.svg", ConversorSvg.NombreSalida("logo GymUP.svg"));
        Assert.Equal("logo_BAMBU.svg", ConversorSvg.NombreSalida(@"C:\disenos\logo.SVG"));
    }
}

public class MatrizTests
{
    [Fact]
    public void RotacionAlrededorDeUnPunto()
    {
        var (x, y) = Matriz.Leer("rotate(90 10 10)").Aplicar(20, 10);
        Assert.Equal(10, x, 9);
        Assert.Equal(20, y, 9);
    }

    [Fact]
    public void MatrizYFuncionesDesconocidas()
    {
        Assert.Equal(new Matriz(1, 2, 3, 4, 5, 6), Matriz.Leer("matrix(1 2 3 4 5 6) foo(3)"));
        Assert.True(Matriz.Leer(null).EsIdentidad);
        Assert.Equal("M1,2 L3,4 Z", Matriz.Identidad.AplicarATrazado("M1,2 L3,4 Z"));
        Assert.Equal("M3,4 L5,6 Z", Matriz.Leer("translate(2,2)").AplicarATrazado("M1,2 L3,4 Z"));
    }
}
