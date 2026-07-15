using Ecorex.Agent.Gui.Mvvm;
using Ecorex.Contracts.Agent;

namespace Ecorex.Agent.Gui.ViewModels;

/// <summary>
/// Una celda del panal: una capacidad fija (Config, Gateway, Archivos, Navegador) o un worker
/// EFIMERO que aparece cuando llega una peticion y desaparece al terminar. Solo estado visual.
/// </summary>
public sealed class HiveCellViewModel : ObservableObject
{
    private HiveCellState _state;
    private string? _detail;

    public HiveCellViewModel(SubAgentKind kind, string label, string glyph, bool isEphemeral = false, string? correlationId = null)
    {
        Kind = kind;
        Label = label;
        Glyph = glyph;
        IsEphemeral = isEphemeral;
        CorrelationId = correlationId;
        // La celda de Configuracion es el ancla: nace y permanece LLENA (Active).
        _state = kind == SubAgentKind.Configuration ? HiveCellState.Active : HiveCellState.Idle;
    }

    public SubAgentKind Kind { get; }

    /// <summary>Nombre corto mostrado bajo el hexagono.</summary>
    public string Label { get; }

    /// <summary>Glifo (1-2 chars) dibujado dentro del hexagono; monocromo, sin iconos externos.</summary>
    public string Glyph { get; }

    /// <summary>Worker efimero (una peticion en curso) vs capacidad fija del panal.</summary>
    public bool IsEphemeral { get; }

    /// <summary>Id de correlacion de la peticion que abrio este worker (null en capacidades fijas).</summary>
    public string? CorrelationId { get; }

    /// <summary>La de Config no participa del ciclo encender/apagar por peticiones.</summary>
    public bool IsConfig => Kind == SubAgentKind.Configuration;

    public HiveCellState State
    {
        get => _state;
        set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(IsWorking));
            }
        }
    }

    /// <summary>Texto opcional (p.ej. detalle de la peticion) para tooltip.</summary>
    public string? Detail
    {
        get => _detail;
        set => SetProperty(ref _detail, value);
    }

    /// <summary>Atajo para disparar/parar el Storyboard de pulso desde el control.</summary>
    public bool IsWorking => _state == HiveCellState.Working;
}
