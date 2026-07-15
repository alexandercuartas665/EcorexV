using UserControl = System.Windows.Controls.UserControl;

namespace Ecorex.Agent.Gui.Controls;

/// <summary>
/// Celda hexagonal del panal. Su DataContext es un HiveCellViewModel; los estados (Vacio/Lleno/
/// Atendiendo/Error) y el pulso se resuelven por DataTriggers en el ControlTemplate (XAML).
/// </summary>
public partial class HexTile : UserControl
{
    public HexTile()
    {
        InitializeComponent();
    }
}
