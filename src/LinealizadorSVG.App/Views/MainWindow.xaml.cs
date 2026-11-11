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
        // "Open with" / SVGs or folders dropped on the exe arrive as arguments.
        var argumentos = Environment.GetCommandLineArgs()[1..];
        if (argumentos.Length > 0) Loaded += async (_, _) => await Vm.CargarArchivos(argumentos);
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
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } archivos) await Vm.CargarArchivos(archivos);
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
