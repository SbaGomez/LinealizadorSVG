using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Navigation;
using LinealizadorSVG.App.ViewModels;

namespace LinealizadorSVG.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // "Open with" / an SVG dropped on the exe arrives as the first argument.
        var argumentos = Environment.GetCommandLineArgs();
        if (argumentos.Length > 1) Loaded += async (_, _) => await Vm.CargarArchivo(argumentos[1]);
    }

    private MainViewModel Vm => (MainViewModel)DataContext;

    private void AlArrastrar(object sender, DragEventArgs e)
    {
        var sirve = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = sirve ? DragDropEffects.Copy : DragDropEffects.None;
        Zona.Stroke = (Brush)FindResource(sirve ? "AccentBrush" : "InputBorderBrush");
        e.Handled = true;
    }

    private void AlSalirArrastre(object sender, DragEventArgs e) => Zona.Stroke = (Brush)FindResource("InputBorderBrush");

    private async void AlSoltar(object sender, DragEventArgs e)
    {
        Zona.Stroke = (Brush)FindResource("InputBorderBrush");
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } archivos) await Vm.CargarArchivo(archivos[0]);
    }

    private void AbrirLink(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // No browser registered: nothing to do.
        }
        e.Handled = true;
    }
}
