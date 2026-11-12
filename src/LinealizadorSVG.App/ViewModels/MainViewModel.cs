using System.Collections.ObjectModel;
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

/// <summary>A fill rule profile as shown in the selector.</summary>
public sealed record OpcionPerfil(PerfilRelleno Valor, string Nombre, string Descripcion);

/// <summary>
/// Loads SVGs (opened, dropped, a whole folder or passed on the command line). One file is converted as soon as
/// it's loaded and again whenever an option changes, with a preview, and saved where the user picks. Several files
/// are a batch: each one is saved straight away as name_BAMBU.svg next to its original.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private static readonly CultureInfo Es = new("es-AR");

    private string? _texto;
    private int _version; // drops results of conversions overtaken by a newer one

    public MainViewModel()
    {
        _precision = Precisiones[1];
        _perfil = Perfiles[0];
    }

    public IReadOnlyList<OpcionPrecision> Precisiones { get; } =
    [
        new(0.25, "0,25 — más detalle"),
        new(0.5, "0,5 — recomendada"),
        new(0.8, "0,8 — archivo liviano"),
        new(1, "1,0 — más liviano"),
    ];

    public IReadOnlyList<OpcionPerfil> Perfiles { get; } =
    [
        new(PerfilRelleno.GymUp, "Perfil GymUP — recomendado",
            "El primer trazado se rellena con nonzero y los demás con evenodd: el perfil validado con el logo de GymUP."),
        new(PerfilRelleno.Original, "Como viene en el archivo",
            "Cada trazado conserva la regla que le puso CorelDRAW (nonzero si no tiene ninguna)."),
        new(PerfilRelleno.TodoNonzero, "Todo nonzero",
            "Rellena según el sentido de cada contorno: un contorno interior dibujado al revés queda hueco."),
        new(PerfilRelleno.TodoEvenodd, "Todo evenodd",
            "Alterna relleno y hueco en cada contorno anidado, sin importar el sentido."),
    ];

    [ObservableProperty] private OpcionPrecision _precision;
    [ObservableProperty] private OpcionPerfil _perfil;

    private OpcionesConversion Opciones => new(Precision.Valor, Perfil.Valor);

    // ---- One file ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NombreArchivo))]
    private string? _ruta;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    [NotifyPropertyChangedFor(nameof(Resumen), nameof(HayResultado), nameof(Avisos))]
    private ResultadoConversion? _resultado;

    /// <summary>The converted paths drawn with their fills and fill rules: what Bambu Studio will see.</summary>
    [ObservableProperty] private DrawingImage? _vista;

    /// <summary>The original file with the paths Bambu Studio is likely to break in red, to compare with <see cref="Vista"/>.</summary>
    [ObservableProperty] private DrawingImage? _vistaSinConvertir;

    /// <summary>What's wrong with the original in Bambu, under <see cref="VistaSinConvertir"/>.</summary>
    [ObservableProperty] private string? _riesgoSinConvertir;

    [ObservableProperty] private bool _convirtiendo;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private string? _estado;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AbrirCarpetaCommand))]
    private string? _guardadoEn;

    public string? NombreArchivo => Ruta is null ? null : Path.GetFileName(Ruta);
    public bool HayResultado => Resultado is not null;
    public IReadOnlyList<string> Avisos => Resultado?.Avisos ?? [];

    public string? Resumen => Resultado is not { } r ? null : Resumir(r);

    private static string Resumir(ResultadoConversion r) => string.Create(Es,
        $"{r.Trazados.Count:N0} {(r.Trazados.Count == 1 ? "trazado" : "trazados")} · {r.Segmentos:N0} segmentos rectos · {Encoding.UTF8.GetByteCount(r.Svg) / 1024.0:N0} KB");

    // ---- Batch ----

    public ObservableCollection<ArchivoLote> Lote { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayResultado))]
    [NotifyCanExecuteChangedFor(nameof(RepetirLoteCommand))]
    private bool _enLote;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RepetirLoteCommand))]
    private bool _procesandoLote;

    [ObservableProperty] private string? _resumenLote;

    /// <summary>The options changed after the batch ran: its files don't reflect them until it's run again.</summary>
    [ObservableProperty] private bool _loteDesactualizado;

    partial void OnPrecisionChanged(OpcionPrecision value) => OpcionesCambiadas();
    partial void OnPerfilChanged(OpcionPerfil value) => OpcionesCambiadas();

    private void OpcionesCambiadas()
    {
        if (EnLote) LoteDesactualizado = Lote.Count > 0;
        else _ = Convertir();
    }

    [RelayCommand]
    private async Task AbrirArchivo()
    {
        var dialogo = new OpenFileDialog
        {
            Filter = "Archivos SVG|*.svg|Todos los archivos|*.*",
            Title = "Elegí el SVG exportado de CorelDRAW (o varios)",
            Multiselect = true,
        };
        if (dialogo.ShowDialog() == true) await CargarArchivos(dialogo.FileNames);
    }

    [RelayCommand]
    private async Task ConvertirCarpeta()
    {
        var dialogo = new OpenFolderDialog { Title = "Elegí la carpeta con los SVG" };
        if (dialogo.ShowDialog() == true) await CargarArchivos([dialogo.FolderName]);
    }

    /// <summary>Files and/or folders: one file opens on its own, anything more becomes a batch.</summary>
    public async Task CargarArchivos(IReadOnlyList<string> rutas)
    {
        if (rutas is [var unica] && File.Exists(unica))
        {
            await CargarArchivo(unica);
            return;
        }

        var archivos = new List<string>();
        foreach (var ruta in rutas)
        {
            if (Directory.Exists(ruta)) archivos.AddRange(BuscarSvgs(ruta));
            else if (Core.Lote.EsConvertible(ruta)) archivos.Add(ruta);
        }
        archivos = [.. archivos.Distinct(StringComparer.OrdinalIgnoreCase)];

        if (archivos.Count == 0)
        {
            SalirDelLote();
            MostrarError("No encontré archivos .svg para convertir (los _BAMBU.svg ya convertidos se saltean).");
            return;
        }
        if (archivos.Count == 1)
        {
            await CargarArchivo(archivos[0]);
            return;
        }
        await ConvertirLote(archivos);
    }

    private static IEnumerable<string> BuscarSvgs(string carpeta)
    {
        try
        {
            return Core.Lote.BuscarSvgs(carpeta);
        }
        catch (Exception)
        {
            return [];
        }
    }

    public async Task CargarArchivo(string ruta)
    {
        SalirDelLote();
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
        if (_texto is not { } texto || EnLote) return;
        var version = ++_version;
        var opciones = Opciones;
        Convirtiendo = true;
        Error = null;
        Estado = null;
        try
        {
            var (resultado, vista, original, sinConvertir) = await Task.Run(() =>
            {
                var r = ConversorSvg.Convertir(texto, opciones);
                var o = ConversorSvg.SimularSinConvertir(texto, opciones.Precision);
                return (r, Dibujar(r), o, Dibujar(o));
            });
            if (version != _version) return;
            Resultado = resultado;
            Vista = vista;
            VistaSinConvertir = sinConvertir;
            RiesgoSinConvertir = DescribirRiesgo(original);
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
        if (GuardadoEn is { } ruta) MostrarEnCarpeta(ruta);
    }

    private bool PuedeAbrirCarpeta() => GuardadoEn is not null;

    // ---- Batch ----

    private async Task ConvertirLote(IReadOnlyList<string> archivos)
    {
        ++_version; // a single-file conversion still running must not show up over the batch
        _texto = null;
        Ruta = null;
        Resultado = null;
        Vista = null;
        VistaSinConvertir = null;
        RiesgoSinConvertir = null;
        Error = null;
        Estado = null;
        GuardadoEn = null;
        Convirtiendo = false;
        EnLote = true;
        Lote.Clear();
        foreach (var a in archivos) Lote.Add(new ArchivoLote(a));
        await ProcesarLote();
    }

    [RelayCommand(CanExecute = nameof(PuedeRepetirLote))]
    private Task RepetirLote() => ProcesarLote();

    private bool PuedeRepetirLote() => EnLote && !ProcesandoLote;

    private async Task ProcesarLote()
    {
        ProcesandoLote = true;
        LoteDesactualizado = false;
        var opciones = Opciones;
        foreach (var item in Lote) item.Reiniciar();
        int bien = 0, conAvisos = 0, mal = 0;
        foreach (var item in Lote)
        {
            ResumenLote = $"Convirtiendo {Lote.IndexOf(item) + 1} de {Lote.Count}…";
            item.Estado = EstadoLote.Convirtiendo;
            var r = await Task.Run(() => Core.Lote.ConvertirArchivo(item.Ruta, opciones));
            item.Terminar(r, r.Resultado is { } res ? Resumir(res) : null);
            if (r.Error is not null) mal++;
            else if (r.Resultado!.Avisos.Count > 0) conAvisos++;
            else bien++;
        }
        var partes = new List<string> { $"{bien + conAvisos} de {Lote.Count} convertidos" };
        if (conAvisos > 0) partes.Add($"{conAvisos} con avisos");
        if (mal > 0) partes.Add(mal == 1 ? "1 con error" : $"{mal} con error");
        ResumenLote = string.Join(" · ", partes) + ". Cada uno quedó como _BAMBU.svg al lado de su original.";
        ProcesandoLote = false;
    }

    [RelayCommand]
    private void MostrarArchivoLote(ArchivoLote item) => MostrarEnCarpeta(item.Salida ?? item.Ruta);

    private void SalirDelLote()
    {
        EnLote = false;
        LoteDesactualizado = false;
        ResumenLote = null;
        Lote.Clear();
    }

    // ---- Helpers ----

    private static void MostrarEnCarpeta(string ruta) => Process.Start("explorer.exe", $"/select,\"{ruta}\"");

    private void MostrarError(string mensaje)
    {
        Resultado = null;
        Vista = null;
        VistaSinConvertir = null;
        RiesgoSinConvertir = null;
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
            if (t.EnRiesgo) pincel = EnRiesgo;
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

    private static readonly SolidColorBrush EnRiesgo = Congelado(Color.FromRgb(0xD6, 0x45, 0x45));

    private static SolidColorBrush Congelado(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static string DescribirRiesgo(ResultadoConversion original)
    {
        var n = original.Trazados.Count(t => t.EnRiesgo);
        return n == 0
            ? "No veo trazados que Bambu suela romper, pero las curvas sin convertir pueden salir con menos detalle."
            : (n == 1 ? "En rojo, un trazado" : $"En rojo, {n} trazados") +
              " con curvas y varias piezas separadas: Bambu arma cada trazado como una sola pieza y las demás como huecos, así que esas partes suelen salir deformadas (por ejemplo, letras convertidas en cuadrados).";
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

public enum EstadoLote { Pendiente, Convirtiendo, Listo, ConAvisos, Error }

/// <summary>One file of a batch and how its conversion went.</summary>
public sealed partial class ArchivoLote(string ruta) : ObservableObject
{
    public string Ruta { get; } = ruta;
    public string Nombre { get; } = Path.GetFileName(ruta);

    [ObservableProperty] private EstadoLote _estado;
    [ObservableProperty] private string? _detalle;
    [ObservableProperty] private string? _salida;

    public void Reiniciar()
    {
        Estado = EstadoLote.Pendiente;
        Detalle = "En espera";
        Salida = null;
    }

    public void Terminar(ResultadoArchivo r, string? resumen)
    {
        Salida = r.Salida;
        if (r.Error is { } error)
        {
            Estado = EstadoLote.Error;
            Detalle = error;
        }
        else if (r.Resultado!.Avisos.Count > 0)
        {
            Estado = EstadoLote.ConAvisos;
            Detalle = resumen + "\n" + string.Join("\n", r.Resultado.Avisos);
        }
        else
        {
            Estado = EstadoLote.Listo;
            Detalle = resumen;
        }
    }
}
