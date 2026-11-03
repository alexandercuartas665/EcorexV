using Microsoft.AspNetCore.Components;
using Ecorex.Application.Directorio;

namespace Ecorex.SuperAdmin.Components.Shared;

/// <summary>Que se busca en el <see cref="TerceroPickerModal"/>.</summary>
public enum TerceroPickerMode
{
    /// <summary>Elegir una empresa (para asociarle una persona).</summary>
    Empresas,
    /// <summary>Elegir una persona/contacto existente (para colgarla de una empresa).</summary>
    Personas
}

/// <summary>
/// Selector modal reutilizable para VINCULAR terceros existentes entre si (empresa &lt;-&gt; persona),
/// uno a la vez. Busca en servidor con debounce reusando <see cref="ITerceroService.ListAsync"/>;
/// no fabrica registros (el alta sigue en el modal del tercero). El padre lo abre con
/// <see cref="OpenAsync"/> via @ref, pasando el modo y los textos como ARGUMENTOS (no parametros),
/// para que la recarga use el modo correcto sin depender del ciclo de render de Blazor. La fila
/// elegida vuelve por <see cref="OnPick"/>.
/// </summary>
public partial class TerceroPickerModal : ComponentBase, IDisposable
{
    [Inject] private ITerceroService TerceroSvc { get; set; } = default!;

    /// <summary>Se dispara con la fila elegida (ya confirmada si era un movimiento).</summary>
    [Parameter] public EventCallback<TerceroListItemDto> OnPick { get; set; }

    // Configuracion de la apertura actual (fijada en OpenAsync, no via [Parameter]).
    private TerceroPickerMode _mode = TerceroPickerMode.Empresas;
    private string? _title;
    private string? _subtitle;
    private Guid? _excludeId;
    private Guid? _currentEmpresaId;
    // Cuando false, elegir una persona que ya pertenece a una empresa NO pide confirmacion de "mover"
    // (el picker solo DEVUELVE la fila; no reasigna empresa). Lo usan contextos como asignar el contacto
    // de una tarea, donde no hay empresa destino.
    private bool _confirmMove = true;

    private bool _open;
    private bool _loading;
    private bool _busy;
    private string _term = "";
    private Guid? _confirmId;
    private List<TerceroListItemDto> _rows = new();
    private CancellationTokenSource? _cts;

    private string DefaultTitle => _mode == TerceroPickerMode.Empresas ? "Buscar empresa" : "Buscar persona";

    private string SearchPlaceholder => _mode == TerceroPickerMode.Empresas
        ? "Buscar empresa por nombre, NIT..."
        : "Buscar persona por nombre, cedula, correo...";

    private string EmptyNote => string.IsNullOrWhiteSpace(_term)
        ? (_mode == TerceroPickerMode.Empresas ? "Escribe para buscar una empresa." : "Escribe para buscar una persona.")
        : "Sin resultados.";

    /// <summary>Abre el selector y carga la primera busqueda (vacia).</summary>
    /// <param name="mode">Que lista buscar (empresas o personas).</param>
    /// <param name="title">Titulo de la cabecera; vacio = titulo por defecto segun el modo.</param>
    /// <param name="subtitle">Subtitulo opcional de ayuda.</param>
    /// <param name="excludeId">Id a excluir de los resultados (p.ej. el propio tercero en edicion).</param>
    /// <param name="currentEmpresaId">En modo Personas: oculta las que YA estan en esta empresa.</param>
    /// <param name="confirmMove">En modo Personas: si true (por defecto) pide confirmar cuando la persona
    /// elegida ya pertenece a otra empresa; si false, elige directo sin confirmar (solo devuelve la fila).</param>
    public async Task OpenAsync(
        TerceroPickerMode mode,
        string? title = null,
        string? subtitle = null,
        Guid? excludeId = null,
        Guid? currentEmpresaId = null,
        bool confirmMove = true)
    {
        _mode = mode;
        _title = title;
        _subtitle = subtitle;
        _excludeId = excludeId;
        _currentEmpresaId = currentEmpresaId;
        _confirmMove = confirmMove;
        _term = "";
        _confirmId = null;
        _rows = new();
        _open = true;
        StateHasChanged();
        await ReloadAsync();
        StateHasChanged();
    }

    private void Close()
    {
        _open = false;
        _confirmId = null;
        _cts?.Cancel();
    }

    private void OnInput(ChangeEventArgs e)
    {
        _term = e.Value?.ToString() ?? "";
        _confirmId = null;
        _cts?.Cancel();
        _cts?.Dispose();
        var cts = new CancellationTokenSource();
        _cts = cts;
        // No await: el handler no debe bloquear la escritura (si no, "se traga las letras").
        _ = DebouncedAsync(cts.Token);
    }

    private async Task DebouncedAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(300, token);
            await ReloadAsync();
            if (!token.IsCancellationRequested)
            {
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    private async Task ReloadAsync()
    {
        _loading = true;
        try
        {
            var filter = _mode == TerceroPickerMode.Empresas
                ? new TerceroListFilter(Naturaleza: TerceroTabNaturaleza.Empresas, Busqueda: _term)
                // IncludeSubContacts: tambien trae personas que ya estan en una empresa (para poder moverlas).
                : new TerceroListFilter(Naturaleza: TerceroTabNaturaleza.Contactos, Busqueda: _term, IncludeSubContacts: true);
            var all = await TerceroSvc.ListAsync(filter);
            IEnumerable<TerceroListItemDto> q = all;
            if (_excludeId is Guid ex)
            {
                q = q.Where(r => r.Id != ex);
            }
            if (_mode == TerceroPickerMode.Personas && _currentEmpresaId is Guid cur)
            {
                q = q.Where(r => r.EmpresaId != cur);
            }
            _rows = q.Take(60).ToList();
        }
        catch
        {
            _rows = new();
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task Choose(TerceroListItemDto row)
    {
        // Mover una persona que YA pertenece a otra empresa: pide confirmacion antes de reasignar.
        // Se omite cuando el host abrio con confirmMove=false (solo quiere la fila, sin mover de empresa).
        if (_confirmMove && _mode == TerceroPickerMode.Personas && row.EmpresaId is not null
            && !string.IsNullOrWhiteSpace(row.EmpresaNombre))
        {
            _confirmId = row.Id;
            StateHasChanged();
            return;
        }
        await Commit(row);
    }

    private async Task Commit(TerceroListItemDto row)
    {
        _open = false;
        _confirmId = null;
        await OnPick.InvokeAsync(row);
    }

    private static string RowSub(TerceroListItemDto r)
        => !string.IsNullOrWhiteSpace(r.Sub) ? r.Sub!
         : !string.IsNullOrWhiteSpace(r.Identificacion) ? r.Identificacion
         : "-";

    private static readonly string[] AvatarPalette =
        { "--t-violet", "--t-blue", "--t-amber", "--t-green", "--t-rose", "--t-slate" };

    private static string AvatarColor(string name)
    {
        var hash = 0;
        foreach (var ch in name) { hash = (hash * 31 + ch) & 0x7fffffff; }
        return $"var({AvatarPalette[hash % AvatarPalette.Length]})";
    }

    private static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) { return "?"; }
        return parts.Length == 1
            ? parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant()
            : (parts[0][..1] + parts[1][..1]).ToUpperInvariant();
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        GC.SuppressFinalize(this);
    }
}
