using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Ecorex.Agent.Gui.Mvvm;

/// <summary>Base MVVM ligera: notifica cambios de propiedad sin dependencias externas.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) { return false; }
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
