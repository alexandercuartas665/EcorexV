namespace Ecorex.Application.Tenancy;

/// <summary>
/// Cargador masivo de contactos al embudo comercial (modulo 000873). Tenant-scoped.
/// Flujo: el CSV ya parseado (CsvTableParser) + un mapeo de columnas se validan fila a
/// fila (nombre obligatorio, email/telefono bien formados, duplicados contra los leads
/// existentes del tenant y dentro del propio archivo) y las filas validas se insertan
/// como Lead en la primera etapa del pipeline, en UNA transaccion, dejando el resumen
/// en ContactImportBatch (historial de cargas).
/// </summary>
public interface IContactLoaderService
{
    /// <summary>
    /// Valida la tabla contra el mapeo SIN persistir nada: veredicto por fila + conteos.
    /// Es la previsualizacion de la pagina. Lanza nada: si no hay tenant activo devuelve
    /// todo invalido con mensaje claro.
    /// </summary>
    Task<ContactImportPreview> ValidateAsync(CsvTable table, ContactColumnMapping mapping, CancellationToken cancellationToken = default);

    /// <summary>
    /// Carga transaccional: re-valida, inserta las filas validas como Lead (primera etapa
    /// del pipeline, asignadas al importador) + LeadActivity "lead.imported" + la fila de
    /// historial ContactImportBatch. Rollback total si algo falla. Devuelve null si no hay
    /// tenant activo, si el mapeo no define la columna de nombre o si el tenant no tiene
    /// etapas de pipeline.
    /// </summary>
    Task<ContactImportResult?> ImportAsync(string fileName, CsvTable table, ContactColumnMapping mapping, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Historial de cargas del tenant (mas recientes primero).</summary>
    Task<IReadOnlyList<ContactImportBatchDto>> ListBatchesAsync(int take = 20, CancellationToken cancellationToken = default);
}
