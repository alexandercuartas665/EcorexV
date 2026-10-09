using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Preferencias de vista de una tabla/grilla para un usuario concreto (orden de columnas, columnas ocultas,
/// anchos, ordenamiento, agrupaciones y columnas fijas). Viaja con el usuario entre equipos (persistida en BD,
/// no en localStorage). Es puramente de presentacion: no participa de ninguna logica de negocio.
/// Clave logica: (TenantId, UserId, TableKey). El contenido se guarda como JSON para evolucionar sin migraciones.
/// </summary>
public sealed class UserTablePreference : TenantEntity
{
    /// <summary>Usuario (TenantUser) dueño de la preferencia.</summary>
    public Guid UserId { get; set; }

    /// <summary>Identificador estable de la tabla (p. ej. "conciliacion-dian").</summary>
    public string TableKey { get; set; } = string.Empty;

    /// <summary>Preferencias serializadas (orden, ocultas, anchos, sorts, grupos, fijas). JSON.</summary>
    public string PreferencesJson { get; set; } = "{}";
}
