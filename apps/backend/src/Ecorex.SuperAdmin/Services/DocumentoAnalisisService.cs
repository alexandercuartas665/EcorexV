using Ecorex.Application.DocumentosAnalisis;
using Ecorex.Application.Storage;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.SuperAdmin.Services;

/// <summary>
/// Implementacion del Analizador de documentos (fase 1: carga). Se apoya en el almacenamiento
/// UNIFICADO (<see cref="IFileStorageService"/>, Azure Blob o disco) para el binario y deja una fila
/// <see cref="DocumentoAnalisis"/> por documento. Vive en presentacion (como FileStorageService)
/// porque usa EcorexDbContext y el guard de subida del propio host. Tenant-scoped por el filtro
/// global (no se filtra por TenantId a mano); el TenantId lo sella el interceptor en el insert.
/// </summary>
public sealed class DocumentoAnalisisService(
    EcorexDbContext db, IFileStorageService files) : IDocumentoAnalisisService
{
    /// <summary>Clave de modulo con la que se guardan los binarios en el almacenamiento unificado.</summary>
    public const string StorageModule = "AnalizadorDocumentos";

    /// <summary>
    /// Tope de tamano por PDF en ESTE modulo (500 MB). Es propio del Analizador (los PDF pueden ser muy
    /// grandes) y NO reusa el limite de 25 MB del DocumentUploadGuard, que gobierna otros modulos.
    /// NOTA (mejora futura): a este tamano conviene subir en streaming directo al blob en vez de leer el
    /// archivo completo a memoria (byte[]) como hoy; se deja anotado en PROGRESO.md.
    /// </summary>
    public const long MaxUploadBytes = 500L * 1024 * 1024;

    public async Task<DocumentoAnalisisDto> SubirAsync(Guid loteId, DocumentoAnalisisUploadItem item, CancellationToken ct = default)
    {
        // 1) Validacion server-side (defensa, no se confia en el navegador): solo PDF, no vacio,
        //    dentro del limite de tamano y con la FIRMA de bytes magicos correcta (%PDF).
        var ext = DocumentUploadGuard.ResolveExtension(item.FileName);
        if (ext != ".pdf")
        {
            throw new InvalidOperationException("Solo se admiten archivos PDF.");
        }
        if (item.Content is null || item.Content.Length == 0)
        {
            throw new InvalidOperationException("El archivo esta vacio.");
        }
        if (item.Content.LongLength > MaxUploadBytes)
        {
            throw new InvalidOperationException($"Supera el limite de {MaxUploadBytes / (1024 * 1024)} MB.");
        }
        if (!DocumentUploadGuard.MatchesSignature(item.Content, ext))
        {
            throw new InvalidOperationException("El contenido no corresponde a un PDF.");
        }

        // 2) Guardar el binario en el almacenamiento unificado. Ref1 = lote, para agrupar/recuperar.
        var stored = await files.SaveAsync(
            new FileStorageSaveRequest(StorageModule, loteId.ToString("N"), null, item.FileName, "application/pdf", item.Content), ct);

        // 3) Registrar el documento. SaveAsync ya confirmo el StoredFile por su cuenta; si el insert de
        //    la fila del Analizador falla, se compensa borrando el binario para no dejar huerfanos.
        try
        {
            var entity = new DocumentoAnalisis
            {
                LoteId = loteId,
                StoredFileId = stored.Id,
                FileName = stored.FileName,
                Extension = ext,
                ContentType = "application/pdf",
                SizeBytes = stored.SizeBytes,
                Estado = DocumentoAnalisisEstado.Cargado,
            };
            db.DocumentosAnalisis.Add(entity);
            await db.SaveChangesAsync(ct);
            return Map(entity);
        }
        catch
        {
            await files.DeleteAsync(stored.Id, ct);
            throw;
        }
    }

    public async Task<IReadOnlyList<DocumentoAnalisisDto>> ListarAsync(Guid? loteId = null, CancellationToken ct = default)
    {
        var q = db.DocumentosAnalisis.AsNoTracking();
        if (loteId is Guid lote) { q = q.Where(d => d.LoteId == lote); }
        return (await q.OrderByDescending(d => d.CreatedAt).ToListAsync(ct)).Select(Map).ToList();
    }

    public async Task<bool> EliminarAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await db.DocumentosAnalisis.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (entity is null) { return false; }

        // Soft-delete de la fila (regla 5) + borrado best-effort del binario.
        entity.IsDeleted = true;
        entity.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await files.DeleteAsync(entity.StoredFileId, ct);
        return true;
    }

    private static DocumentoAnalisisDto Map(DocumentoAnalisis d) =>
        new(d.Id, d.LoteId, d.StoredFileId, d.FileName, d.Extension, d.ContentType, d.SizeBytes, d.Estado, d.MotivoError, d.CreatedAt);

    // ===================================================================================
    // Configuracion: agentes de IA del modulo (solo SELECCION de agentes existentes + prompt)
    // ===================================================================================

    public async Task<IReadOnlyList<DocumentoAnalisisAgenteOpcionDto>> ListarAgentesDisponiblesAsync(CancellationToken ct = default)
    {
        var usados = await db.DocumentosAnalisisAgentePrompts.AsNoTracking().Select(x => x.AgentId).ToListAsync(ct);
        return await db.AiAgents.AsNoTracking()
            .Where(a => !usados.Contains(a.Id))
            .OrderBy(a => a.SortOrder).ThenBy(a => a.Name)
            .Select(a => new DocumentoAnalisisAgenteOpcionDto(a.Id, a.Name, a.Role))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<DocumentoAnalisisAgenteDto>> ListarAgentesConfigAsync(CancellationToken ct = default)
    {
        var rows = await db.DocumentosAnalisisAgentePrompts.AsNoTracking().ToListAsync(ct);
        if (rows.Count == 0) { return []; }
        var ids = rows.Select(r => r.AgentId).ToList();
        var agentes = await db.AiAgents.AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => new { a.Name, a.Role }, ct);
        return rows
            .Select(r =>
            {
                agentes.TryGetValue(r.AgentId, out var a);
                return new DocumentoAnalisisAgenteDto(
                    r.AgentId, a?.Name ?? "(agente no encontrado)", a?.Role, r.PromptPrincipal, r.IsActive);
            })
            .OrderByDescending(x => x.IsActive).ThenBy(x => x.AgentName)
            .ToList();
    }

    public async Task AgregarAgenteAsync(Guid agentId, CancellationToken ct = default)
    {
        // Solo se referencia un agente EXISTENTE del tenant (filtro global); el Analizador no crea agentes.
        if (!await db.AiAgents.AnyAsync(a => a.Id == agentId, ct))
        {
            throw new InvalidOperationException("El agente no existe o no es de este tenant.");
        }
        if (await db.DocumentosAnalisisAgentePrompts.AnyAsync(x => x.AgentId == agentId, ct)) { return; }

        // El primer agente agregado queda activo por defecto.
        var hayActivo = await db.DocumentosAnalisisAgentePrompts.AnyAsync(x => x.IsActive, ct);
        db.DocumentosAnalisisAgentePrompts.Add(new DocumentoAnalisisAgentePrompt
        {
            AgentId = agentId,
            PromptPrincipal = "",
            IsActive = !hayActivo,
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task GuardarPromptAgenteAsync(Guid agentId, string prompt, CancellationToken ct = default)
    {
        var row = await db.DocumentosAnalisisAgentePrompts.FirstOrDefaultAsync(x => x.AgentId == agentId, ct);
        if (row is null) { throw new InvalidOperationException("El agente no esta en la configuracion."); }
        row.PromptPrincipal = prompt ?? "";
        await db.SaveChangesAsync(ct);
    }

    public async Task MarcarAgenteActivoAsync(Guid agentId, CancellationToken ct = default)
    {
        var rows = await db.DocumentosAnalisisAgentePrompts.ToListAsync(ct);
        if (rows.All(r => r.AgentId != agentId))
        {
            throw new InvalidOperationException("El agente no esta en la configuracion.");
        }
        // Un unico agente activo por tenant: se marca el elegido y se desmarcan los demas, en transaccion.
        foreach (var r in rows) { r.IsActive = r.AgentId == agentId; }
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> QuitarAgenteAsync(Guid agentId, CancellationToken ct = default)
    {
        var row = await db.DocumentosAnalisisAgentePrompts.FirstOrDefaultAsync(x => x.AgentId == agentId, ct);
        if (row is null) { return false; }
        var eraActivo = row.IsActive;
        db.DocumentosAnalisisAgentePrompts.Remove(row);
        await db.SaveChangesAsync(ct);

        // Si se quito el activo y quedan agentes, se promueve el mas antiguo para no quedar sin activo.
        if (eraActivo)
        {
            var siguiente = await db.DocumentosAnalisisAgentePrompts.OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(ct);
            if (siguiente is not null) { siguiente.IsActive = true; await db.SaveChangesAsync(ct); }
        }
        return true;
    }
}
