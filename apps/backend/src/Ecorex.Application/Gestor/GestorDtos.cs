using Ecorex.Domain.Enums;

namespace Ecorex.Application.Gestor;

/// <summary>
/// KPIs de cabecera del Gestor de Clientes (000740): embudo del pipeline y valor abierto.
/// </summary>
public sealed record GestorKpisDto(
    int ProspectosFiltrados,
    int Contactados,
    int CalificadosLead,
    int OportunidadesAbiertas,
    decimal ValorPipeline);

/// <summary>Prospecto capturado por scraping (pestana "Prospectos scrapeados").</summary>
public sealed record ProspectoDto(
    Guid Id,
    string Fuente,
    string NombreCompleto,
    string? Cargo,
    string? Empresa,
    string? Ciudad,
    string? Metrica,
    string? Badge,
    string? Telefono,
    string? Correo,
    Guid? TerceroId,
    bool Promovido,
    DateTimeOffset? FechaCaptura);

/// <summary>Columna/estado configurable de la Bolsa de contactos (kanban de terceros).</summary>
public sealed record BolsaColumnaDto(
    Guid Id,
    string Nombre,
    string Color,
    int SortOrder,
    bool EsCliente,
    int TerceroCount);

/// <summary>Tarjeta de un tercero dentro de la Bolsa (kanban por columna).</summary>
public sealed record BolsaTerceroDto(
    Guid TerceroId,
    Guid ColumnaId,
    string Nombre,
    string? Sub,
    string? Vendedor,
    int OportunidadesAbiertas,
    decimal Valor);

/// <summary>Oportunidad de negocio (kanban por etapa y panel de la ficha del cliente).
/// La etapa CONFIGURABLE (EstadoId/EstadoNombre/EstadoColor/EstadoTipo) es opcional durante la
/// transicion; cuando es null la UI cae al enum heredado <see cref="OportunidadEtapa"/> Etapa.</summary>
public sealed record OportunidadDto(
    Guid Id,
    Guid TerceroId,
    string TerceroNombre,
    string Nombre,
    OportunidadEtapa Etapa,
    decimal Valor,
    string? Responsable,
    int Probabilidad,
    DateTimeOffset? FechaCierre,
    string? Fuente,
    string? Descripcion,
    Guid? EstadoId = null,
    string? EstadoNombre = null,
    string? EstadoColor = null,
    OportunidadEstadoTipo? EstadoTipo = null);

/// <summary>Alta/edicion de una oportunidad.</summary>
public sealed record SaveOportunidadRequest(
    string Nombre,
    OportunidadEtapa Etapa = OportunidadEtapa.Nueva,
    decimal Valor = 0m,
    string? Responsable = null,
    int Probabilidad = 0,
    DateTimeOffset? FechaCierre = null,
    string? Fuente = null,
    string? Descripcion = null);

/// <summary>Cita / evento de la Agenda.</summary>
public sealed record CitaDto(
    Guid Id,
    Guid? TerceroId,
    string? TerceroNombre,
    Guid? OportunidadId,
    string Titulo,
    CitaTipo Tipo,
    DateTimeOffset Inicio,
    int DuracionMinutos,
    string? Nota,
    bool Completada);

/// <summary>Alta/edicion de una cita.</summary>
public sealed record SaveCitaRequest(
    Guid? TerceroId,
    Guid? OportunidadId,
    string Titulo,
    CitaTipo Tipo = CitaTipo.Reunion,
    DateTimeOffset Inicio = default,
    int DuracionMinutos = 0,
    string? Nota = null);

/// <summary>Criterio de un filtro dinamico (campo / operador / valor).</summary>
public sealed record FiltroCriterio(string Campo, string Operador, string Valor);

/// <summary>
/// Filtro dinamico guardado: segmento con su conteo en vivo y el % de crecimiento
/// frente al snapshot del periodo anterior.
/// </summary>
public sealed record FiltroDto(
    Guid Id,
    string Nombre,
    string? Descripcion,
    string? Fuente,
    int Conteo,
    int ConteoAnterior,
    int Crecimiento,
    IReadOnlyList<FiltroCriterio> Criterios);

/// <summary>Alta/edicion de un filtro dinamico.</summary>
public sealed record SaveFiltroRequest(
    string Nombre,
    string? Descripcion,
    string? Fuente,
    IReadOnlyList<FiltroCriterio> Criterios);
