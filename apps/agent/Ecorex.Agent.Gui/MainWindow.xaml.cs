using System.Windows;
using System.Windows.Input;

namespace Ecorex.Agent.Gui;

/// <summary>
/// Ventana principal de la colmena (Ola A): sin borde, translucida y arrastrable por el fondo,
/// con minimizar/cerrar propios. El panal y el panel de configuracion se agregan en los pasos
/// siguientes de esta ola.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>Arrastre de la ventana sin barra de titulo nativa (solo con boton izquierdo).</summary>
    private void OnDragBackground(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
