using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Historial de una carga masiva de contactos al embudo (modulo 000873, Cargador de
/// contactos). Registra el archivo procesado y los conteos del resultado. Entidad
/// TENANT-SCOPED: el historial de cargas de un tenant es invisible para los demas.
/// </summary>
public class ContactImportBatch : TenantEntity
{
    /// <summary>Nombre del archivo CSV tal como lo subio el usuario.</summary>
    public string FileName { get; set; } = null!;

    /// <summary>Filas de datos parseadas del archivo (sin contar el encabezado).</summary>
    public int TotalRows { get; set; }

    /// <summary>Leads insertados en el embudo.</summary>
    public int Inserted { get; set; }

    /// <summary>Filas saltadas por duplicado (contra leads existentes o dentro del archivo).</summary>
    public int Duplicates { get; set; }

    /// <summary>Filas saltadas por datos invalidos (nombre vacio, email/telefono mal formados).</summary>
    public int Invalid { get; set; }
}
