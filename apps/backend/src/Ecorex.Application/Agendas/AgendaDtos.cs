using Ecorex.Domain.Enums;

namespace Ecorex.Application.Agendas;

/// <summary>Resultado de una operacion de escritura del modulo de Agendas (ADR-0165): ok + error legible + id.</summary>
public sealed record AgendaOpResult(bool Ok, string? Error = null, Guid? Id = null)
{
    /// <summary>Advertencia de una operacion exitosa (p.ej. turnos que quedan dentro de una novedad nueva).</summary>
    public string? Aviso { get; init; }

    public static AgendaOpResult Bien(Guid? id = null) => new(true, null, id);
    public static AgendaOpResult Mal(string error) => new(false, error);
}

/// <summary>Franja semanal (dia + tramo + bloque + cupo). Misma forma para agendas base y agendas del recurso.</summary>
public sealed record AgendaFranjaDto(DayOfWeek Dia, TimeOnly Inicio, TimeOnly Fin, int IntervaloMinutos, int Cupo);

public sealed record AgendaTipoDto(
    Guid Id, string Nombre, string Icono, string Color, AgendaVinculo Vinculo, int Orden, bool Activo, int Recursos);

public sealed record SaveAgendaTipoRequest(
    Guid? Id, string Nombre, string Icono, string Color, AgendaVinculo Vinculo, bool Activo = true);

public sealed record AgendaPlantillaDto(
    Guid Id, string Nombre, string? Descripcion, bool Activa, long Version,
    IReadOnlyList<AgendaFranjaDto> Franjas, int CuposSemana, int Recursos);

public sealed record SaveAgendaPlantillaRequest(
    Guid? Id, string Nombre, string? Descripcion, bool Activa, IReadOnlyList<AgendaFranjaDto> Franjas, long? Version = null);

public sealed record AgendaRecursoDto(
    Guid Id, string Nombre, string? Descripcion,
    Guid TipoRecursoId, string TipoNombre, string TipoIcono, string TipoColor, AgendaVinculo Vinculo,
    Guid? SedeId, string? SedeNombre, Guid? TenantUserId, string? UsuarioNombre,
    bool AgenteHabilitado, bool Activo, long Version,
    string? AgendaVigente, int CuposSemana, DateOnly? AgendaDesde = null, bool AgendaEsProxima = false);

public sealed record SaveAgendaRecursoRequest(
    Guid? Id, string Nombre, string? Descripcion, Guid TipoRecursoId, Guid? SedeId, Guid? TenantUserId,
    bool AgenteHabilitado, bool Activo = true, long? Version = null);

public sealed record AgendaVigenciaDto(
    Guid Id, Guid RecursoId, string Nombre, DateOnly Desde, DateOnly? Hasta,
    Guid? PlantillaOrigenId, string? PlantillaOrigenNombre, bool DifiereDeOrigen,
    IReadOnlyList<AgendaFranjaDto> Franjas, int CuposSemana);

public sealed record ImportarPlantillaRequest(Guid RecursoId, Guid PlantillaId, DateOnly Desde, DateOnly? Hasta, string? Nombre);

public sealed record SaveVigenciaRequest(Guid Id, string Nombre, DateOnly Desde, DateOnly? Hasta, IReadOnlyList<AgendaFranjaDto> Franjas);

public sealed record AgendaNovedadDto(
    Guid Id, Guid RecursoId, AgendaNovedadTipo Tipo, DateOnly Desde, DateOnly Hasta,
    TimeOnly? HoraDesde, TimeOnly? HoraHasta, string? Nota);

public sealed record SaveAgendaNovedadRequest(
    Guid? Id, Guid RecursoId, AgendaNovedadTipo Tipo, DateOnly Desde, DateOnly Hasta,
    TimeOnly? HoraDesde, TimeOnly? HoraHasta, string? Nota);

/// <summary>Disponibilidad de un recurso en un dia: franjas de la agenda que aplica, cupos y lo que los cierra.</summary>
public sealed record AgendaDiaDto(
    DateOnly Fecha, string? VigenciaNombre, IReadOnlyList<AgendaFranjaDto> Franjas,
    int CuposTotales, int CuposBloqueados, string? Cierre, IReadOnlyList<string> Novedades, int CuposOcupados = 0)
{
    /// <summary>Cupos que aun se pueden tomar: total - cerrados por novedad/calendario - ocupados por turnos.</summary>
    public int CuposDisponibles => Math.Max(0, CuposTotales - CuposBloqueados - CuposOcupados);
}

/// <summary>Opcion simple (id + nombre) para selectores de sede o usuario.</summary>
public sealed record AgendaOpcionDto(Guid Id, string Nombre);
