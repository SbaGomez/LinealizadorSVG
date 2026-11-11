using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinealizadorSVG.Core;
using Microsoft.Win32;

namespace LinealizadorSVG.App.ViewModels;

/// <summary>How long the straight segments are, in SVG units (the same choices as the HTML tool).</summary>
public sealed record OpcionPrecision(double Valor, string Nombre);

/// <summary>
/// Loads an SVG (opened, dropped or passed on the command line), converts it as soon as it's loaded and every time
/// the precision changes, shows a preview, and saves the result next to the original.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private string? _texto;
    private int _version; // drops results of conversions overtaken by a newer one

    public MainViewModel() => _precision = Precisiones[1];

    public IReadOnlyList<OpcionPrecision> Precisiones { get; } =
    [
        new(0.25, "0,25 — más detalle"),
        new(0.5, "0,5 — recomendada"),
        new(0.8, "0,8 — archivo liviano"),
        new(1, "1,0 — más liviano"),
    ];

    [ObservableProperty] private OpcionPrecision _precision;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NombreArchivo))]
    private string? _ruta;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    [NotifyPropertyChangedFor(nameof(Resumen), nameof(HayResultado))]
    private ResultadoConversion? _resultado;

    /// <summary>The converted paths drawn with their fills and fill rules: what Bambu Studio will see.</summary>
    [ObservableProperty] private DrawingImage? _vista;

    [ObservableProperty] private bool _convirtiendo;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private string? _estado;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AbrirCarpetaCommand))]
    private string? _guardadoEn;

    public string? NombreArchivo => Ruta is null ? null : Path.GetFileName(Ruta);
    public bool HayResultado => Resultado is not null;

    public string? Resumen => Resultado is not { } r ? null : string.Create(new CultureInfo("es-AR"),
        $"{r.Trazados.Count:N0} trazados · {r.Segmentos:N0} segmentos rectos · {Encoding.UTF8.GetByteCount(r.Svg) / 1024.0:N0} KB");

    partial void OnPrecisionChanged(OpcionPrecision value) => _ = Convertir();

    [RelayCommand]
    private async Task AbrirArchivo()
    {
        var dialogo = new OpenFileDialog
        {
            Filter = "Archivos SVG|*.svg|Todos los archivos|*.*",
            Title = "Elegí el SVG exportado de CorelDRAW",
        };
        if (dialogo.ShowDialog() == true) await CargarArchivo(dialogo.FileName);
    }

    public async Task CargarArchivo(string ruta)
    {
        if (!ruta.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            MostrarError("Elegí un archivo .svg.");
            return;
        }
        try
        {
            _texto = await File.ReadAllTextAsync(ruta);
        }
        catch (Exception)
        {
            MostrarError("No se pudo leer el archivo.");
            return;
        }
        Ruta = ruta;
        GuardadoEn = null;
        await Convertir();
    }

    private async Task Convertir()
    {
        if (_texto is not { } texto) return;
        var version = ++_version;
        var precision = Precision.Valor;
        Convirtiendo = true;
        Error = null;
        Estado = null;
        try
        {
            var (resultado, vista) = await Task.Run(() =>
            {
                var r = ConversorSvg.Convertir(texto, precision);
                return (r, Dibujar(r));
            });
            if (version != _version) return;
            Resultado = resultado;
            Vista = vista;
        }
        catch (SvgInvalidoException e)
        {
            if (version == _version) MostrarError(e.Message + "\nNo se generó ningún archivo.");
        }
        catch (Exception e)
        {
            if (version == _version) MostrarError("No se pudo convertir el SVG: " + e.Message);
        }
        finally
        {
            if (version == _version) Convirtiendo = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HayResultado))]
    private void Guardar()
    {
        if (Resultado is not { } r || Ruta is null) return;
        var dialogo = new SaveFileDialog
        {
            Filter = "Archivo SVG|*.svg",
            Title = "Guardar el SVG para Bambu Studio",
            FileName = ConversorSvg.NombreSalida(Ruta),
            InitialDirectory = Path.GetDirectoryName(Ruta),
        };
        if (dialogo.ShowDialog() != true) return;
        try
        {
            File.WriteAllText(dialogo.FileName, r.Svg, new UTF8Encoding(false));
        }
        catch (Exception e)
        {
            Error = "No se pudo guardar el archivo: " + e.Message;
            return;
        }
        GuardadoEn = dialogo.FileName;
        Error = null;
        Estado = $"Guardado: {Path.GetFileName(dialogo.FileName)}. Importalo en Bambu Studio.";
    }

    [RelayCommand(CanExecute = nameof(PuedeAbrirCarpeta))]
    private void AbrirCarpeta()
    {
        if (GuardadoEn is { } ruta) Process.Start("explorer.exe", $"/select,\"{ruta}\"");
    }

    private bool PuedeAbrirCarpeta() => GuardadoEn is not null;

    private void MostrarError(string mensaje)
    {
        Resultado = null;
        Vista = null;
        Estado = null;
        Error = mensaje;
    }

    private static DrawingImage Dibujar(ResultadoConversion r)
    {
        var grupo = new DrawingGroup();
        // Invisible frame the size of the viewBox, so the preview keeps the document's margins and proportions.
        grupo.Children.Add(new GeometryDrawing(Brushes.Transparent, null,
            new RectangleGeometry(new Rect(r.VistaX, r.VistaY, r.VistaAncho, r.VistaAlto))));
        foreach (var t in r.Trazados)
        {
            if (Pincel(t.Relleno) is not { } pincel) continue;
            // "F1" = nonzero, "F0" = evenodd in WPF's path mini-language.
            var geometria = Geometry.Parse((t.ReglaRelleno == "nonzero" ? "F1 " : "F0 ") + t.D);
            var dibujo = new GeometryDrawing(pincel, null, geometria);
            if (double.TryParse(t.Opacidad, NumberStyles.Float, CultureInfo.InvariantCulture, out var opacidad))
            {
                var capa = new DrawingGroup { Opacity = Math.Clamp(opacidad, 0, 1) };
                capa.Children.Add(dibujo);
                grupo.Children.Add(capa);
            }
            else grupo.Children.Add(dibujo);
        }
        grupo.ClipGeometry = new RectangleGeometry(new Rect(r.VistaX, r.VistaY, r.VistaAncho, r.VistaAlto));
        var imagen = new DrawingImage(grupo);
        imagen.Freeze();
        return imagen;
    }

    private static Brush? Pincel(string relleno)
    {
        if (relleno.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            if (new BrushConverter().ConvertFromInvariantString(relleno) is Brush b)
            {
                b.Freeze();
                return b;
            }
        }
        catch (Exception)
        {
            // Gradients (url(#…)) and other paints: draw them black, like the converted file's default.
        }
        return Brushes.Black;
    }
}
