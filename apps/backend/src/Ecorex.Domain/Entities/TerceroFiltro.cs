using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Filtro dinamico guardado del Gestor de Clientes (000740): un segmento sobre los campos del
/// contacto (dinamicos o no) que queda en el sidebar como INDICADOR DE CRECIMIENTO de esa bolsa.
/// El conteo se calcula en vivo a partir de <see cref="CriteriosJson"/>; el % de crecimiento sale
/// de comparar el conteo actual contra <see cref="ConteoAnterior"/> (snapshot del periodo previo).
/// TENANT-SCOPED.
/// </summary>
public class TerceroFiltro : TenantEntity
{
    public string Nombre { get; set; } = null!;

    public string? Descripcion { get; set; }

    /// <summary>Fuente sobre la que aplica (LinkedIn, Maps, Web, Todos...).</summary>
    public string? Fuente { get; set; }

    /// <summary>Lista de criterios (campo/operador/valor) serializada. jsonb en PG.</summary>
    public string? CriteriosJson { get; set; }

    /// <summary>Conteo del periodo anterior (snapshot) para calcular el % de crecimiento.</summary>
    public int ConteoAnterior { get; set; }

    /// <summary>Fecha del snapshot de <see cref="ConteoAnterior"/>.</summary>
    public DateTimeOffset? FechaSnapshot { get; set; }

    public int SortOrder { get; set; }
}
