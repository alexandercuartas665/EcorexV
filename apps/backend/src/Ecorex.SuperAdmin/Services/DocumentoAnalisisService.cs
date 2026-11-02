using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Ecorex.Application.Admin;
using Ecorex.Application.Common;
using Ecorex.Application.DocumentosAnalisis;
using Ecorex.Application.Pdf;
using Ecorex.Application.Storage;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.Infrastructure.Persistence;
using Ecorex.SuperAdmin.RealTime;
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
    EcorexDbContext db,
    IFileStorageService files,
    IPdfRasterizer rasterizer,
    IPdfSplitter splitter,
    IHttpClientFactory httpFactory,
    ISecretProtector secretProtector,
    IAiUsageService usage,
    IDocumentoAnalisisNotifier notifier,
    ILogger<DocumentoAnalisisService> logger) : IDocumentoAnalisisService
{
    /// <summary>Clave de modulo con la que se guardan los binarios en el almacenamiento unificado.</summary>
    public const string StorageModule = "AnalizadorDocumentos";

    /// <summary>DPI de rasterizado de cada pagina para la IA de vision (equilibrio nitidez/peso).</summary>
    public const int RasterDpi = 150;

    /// <summary>Tope de paginas a enviar a la IA por documento (defensa ante lotes enormes).</summary>
    public const int MaxPaginasPorDocumento = 60;

    /// <summary>Nombre del HttpClient de vision del Analizador (timeout largo; lo registra el host).</summary>
    public const string VisionHttpClientName = "docanalisis-vision";

    /// <summary>Marcador que el prompt del agente usa para que se inyecte la lista de campos de metadata.</summary>
    private const string CamposMetadataPlaceholder = "{CAMPOS_METADATA}";

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

    // ===================================================================================
    // Configuracion: campos de metadata (self-serve: se inyectan al prompt y son columnas del modal)
    // ===================================================================================

    public async Task<IReadOnlyList<DocumentoAnalisisMetadataCampoDto>> ListarMetadataCamposAsync(CancellationToken ct = default)
    {
        var rows = await db.DocumentosAnalisisMetadataCampos.AsNoTracking()
            .OrderBy(c => c.Orden).ThenBy(c => c.Etiqueta).ToListAsync(ct);
        return rows.Select(MapCampo).ToList();
    }

    public async Task<DocumentoAnalisisMetadataCampoDto> GuardarMetadataCampoAsync(
        Guid? id, string clave, string etiqueta, string tipo, string? descripcion, int orden, bool activo, CancellationToken ct = default)
    {
        clave = (clave ?? "").Trim().ToLowerInvariant();
        etiqueta = (etiqueta ?? "").Trim();
        tipo = NormalizarTipo(tipo);
        if (string.IsNullOrWhiteSpace(clave)) { throw new InvalidOperationException("La clave es obligatoria."); }
        if (string.IsNullOrWhiteSpace(etiqueta)) { throw new InvalidOperationException("La etiqueta es obligatoria."); }

        // Clave unica por tenant (no choca con otra fila).
        var choca = await db.DocumentosAnalisisMetadataCampos.AnyAsync(c => c.Clave == clave && (id == null || c.Id != id), ct);
        if (choca) { throw new InvalidOperationException($"Ya existe un campo con la clave '{clave}'."); }

        DocumentoAnalisisMetadataCampo row;
        if (id is Guid gid)
        {
            row = await db.DocumentosAnalisisMetadataCampos.FirstOrDefaultAsync(c => c.Id == gid, ct)
                  ?? throw new InvalidOperationException("El campo no existe.");
        }
        else
        {
            row = new DocumentoAnalisisMetadataCampo();
            db.DocumentosAnalisisMetadataCampos.Add(row);
        }
        row.Clave = clave;
        row.Etiqueta = etiqueta;
        row.Tipo = tipo;
        row.Descripcion = string.IsNullOrWhiteSpace(descripcion) ? null : descripcion.Trim();
        row.Orden = orden;
        row.Activo = activo;
        await db.SaveChangesAsync(ct);
        return MapCampo(row);
    }

    public async Task<bool> EliminarMetadataCampoAsync(Guid id, CancellationToken ct = default)
    {
        var row = await db.DocumentosAnalisisMetadataCampos.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (row is null) { return false; }
        db.DocumentosAnalisisMetadataCampos.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static string NormalizarTipo(string? tipo)
    {
        var t = (tipo ?? "").Trim().ToLowerInvariant();
        return t is "texto" or "numero" or "fecha" or "lista" ? t : "texto";
    }

    private static DocumentoAnalisisMetadataCampoDto MapCampo(DocumentoAnalisisMetadataCampo c) =>
        new(c.Id, c.Clave, c.Etiqueta, c.Tipo, c.Descripcion, c.Orden, c.Activo);

    // ===================================================================================
    // Procesamiento (separacion por IA de vision, en background)
    // ===================================================================================

    public async Task<int> EncolarAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default)
    {
        if (ids is null || ids.Count == 0) { return 0; }
        if (!await db.DocumentosAnalisisAgentePrompts.AnyAsync(x => x.IsActive, ct))
        {
            throw new InvalidOperationException("Configura y marca un agente ACTIVO antes de procesar.");
        }

        var docs = await db.DocumentosAnalisis
            .Where(d => ids.Contains(d.Id) && d.Estado != DocumentoAnalisisEstado.EnAnalisis)
            .ToListAsync(ct);
        foreach (var d in docs)
        {
            d.Estado = DocumentoAnalisisEstado.EnAnalisis;
            d.MotivoError = null;
        }
        await db.SaveChangesAsync(ct);
        foreach (var d in docs) { notifier.NotificarCambio(d.Id); }
        return docs.Count;
    }

    public async Task<IReadOnlyList<Guid>> ListarEnAnalisisIdsAsync(CancellationToken ct = default) =>
        await db.DocumentosAnalisis.AsNoTracking()
            .Where(d => d.Estado == DocumentoAnalisisEstado.EnAnalisis)
            .OrderBy(d => d.CreatedAt)
            .Select(d => d.Id)
            .ToListAsync(ct);

    public async Task ProcesarAsync(Guid documentoId, CancellationToken ct = default)
    {
        var doc = await db.DocumentosAnalisis.FirstOrDefaultAsync(d => d.Id == documentoId, ct);
        if (doc is null || doc.Estado != DocumentoAnalisisEstado.EnAnalisis) { return; }

        var sw = Stopwatch.StartNew();
        string? rawResponse = null;
        string? promptEnviado = null;
        int? paginasEnviadas = null;
        try
        {
            // 1) Agente activo + su prompt principal (vive en el modulo, no es el system prompt del agente).
            var cfg = await db.DocumentosAnalisisAgentePrompts.AsNoTracking().FirstOrDefaultAsync(x => x.IsActive, ct)
                      ?? throw new InvalidOperationException("No hay un agente activo configurado.");
            var agent = await db.AiAgents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == cfg.AgentId, ct)
                        ?? throw new InvalidOperationException("El agente activo ya no existe.");

            // 2) Cuenta del proveedor (global) + llave descifrada + modelo efectivo.
            var provCfg = await db.AiProviderConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.Provider == agent.Provider, ct);
            if (provCfg is null || !provCfg.IsEnabled || string.IsNullOrWhiteSpace(provCfg.ApiKeyEncrypted))
            {
                throw new InvalidOperationException($"El proveedor {agent.Provider} no esta habilitado o no tiene API key.");
            }
            var apiKey = secretProtector.Unprotect(provCfg.ApiKeyEncrypted);
            var model = !string.IsNullOrWhiteSpace(agent.Model) ? agent.Model!
                        : !string.IsNullOrWhiteSpace(provCfg.Model) ? provCfg.Model!
                        : AiProviderCatalog.For(agent.Provider).DefaultModel;

            // 3) Campos de metadata activos -> bloque inyectado en el prompt.
            var campos = await db.DocumentosAnalisisMetadataCampos.AsNoTracking()
                .Where(c => c.Activo).OrderBy(c => c.Orden).ThenBy(c => c.Etiqueta).ToListAsync(ct);
            var systemPrompt = ConstruirPrompt(cfg.PromptPrincipal, campos);
            promptEnviado = systemPrompt;

            // 4) PDF original -> rasterizar cada pagina a imagen (DeepSeek/otros no aceptan PDF directo).
            var contenido = await files.ReadAsync(doc.StoredFileId, ct)
                            ?? throw new InvalidOperationException("No se pudo leer el PDF del almacenamiento.");
            var paginas = rasterizer.Rasterizar(contenido.Content, RasterDpi, MaxPaginasPorDocumento, ct);
            if (paginas.Count == 0) { throw new InvalidOperationException("El PDF no tiene paginas legibles."); }
            paginasEnviadas = paginas.Count;

            // 5) Prompt multimodal: una instruccion + una imagen por pagina (en orden).
            var partes = new List<AiVisionPart>
            {
                new(Text: $"Te entrego {paginas.Count} paginas en orden (pagina 1 a {paginas.Count}) como imagenes. " +
                          $"total_paginas = {paginas.Count}. Devuelve UNICAMENTE el JSON de separacion.")
            };
            foreach (var p in paginas)
            {
                partes.Add(new AiVisionPart(ImageBase64: Convert.ToBase64String(p.Image), ImageMime: p.Mime));
            }

            // 6) Llamada de vision con cliente de timeout largo (el pipeline corre en background).
            var http = httpFactory.CreateClient(VisionHttpClientName);
            var client = new Ecorex.Infrastructure.Ai.AiProviderClient(http);
            var resp = await client.CompleteVisionAsync(agent.Provider, apiKey, provCfg.BaseUrl, model, systemPrompt, partes, ct);
            // Para la traza: si el modelo no devolvio texto, guarda el detalle del error (incluye el cuerpo crudo).
            rawResponse = string.IsNullOrWhiteSpace(resp.Text) ? resp.Error : resp.Text;

            // 7) Registrar consumo SIEMPRE (haya ido bien o mal).
            await usage.RecordAsync(agent.Id, agent.Provider, model, resp.InputTokens, resp.OutputTokens, "docanalisis", resp.Ok, ct);
            if (!resp.Ok || string.IsNullOrWhiteSpace(resp.Text))
            {
                throw new InvalidOperationException(resp.Error ?? "El agente no devolvio contenido.");
            }

            // 8) Parsear el plan de corte.
            var plan = ParsearPlan(resp.Text!, paginas.Count);
            if (plan.Count == 0) { throw new InvalidOperationException("El agente no devolvio paquetes validos."); }

            // 9) Limpiar paquetes previos (reproceso), separar el PDF y guardar cada nuevo PDF en el blob.
            await LimpiarPaquetesPreviosAsync(doc.Id, ct);
            var grupos = plan.Select(p => (IReadOnlyList<int>)p.Paginas).ToList();
            var pdfs = splitter.Separar(contenido.Content, grupos);

            var nuevos = new List<DocumentoAnalisisPaquete>();
            for (var i = 0; i < plan.Count && i < pdfs.Count; i++)
            {
                var p = plan[i];
                var nombre = SanitizarNombre(p.NombreSugerido, doc.FileName, i + 1);
                var stored = await files.SaveAsync(new FileStorageSaveRequest(
                    StorageModule, doc.LoteId.ToString("N"), doc.Id.ToString("N"), nombre, "application/pdf", pdfs[i]), ct);
                nuevos.Add(new DocumentoAnalisisPaquete
                {
                    DocumentoAnalisisId = doc.Id,
                    Orden = i + 1,
                    NombreSugerido = nombre,
                    StoredFileId = stored.Id,
                    PaginaInicio = p.Paginas.Count > 0 ? p.Paginas.Min() : 0,
                    PaginaFin = p.Paginas.Count > 0 ? p.Paginas.Max() : 0,
                    Paginas = p.Paginas.Count,
                    SizeBytes = stored.SizeBytes,
                    Confianza = p.Confianza,
                    MetadataJson = p.MetadataJson,
                    DocumentosJson = p.DocumentosJson,
                });
            }
            db.DocumentosAnalisisPaquetes.AddRange(nuevos);

            // 10) Cerrar el documento con sus trazas (tiempo, agente, version, motor, tokens).
            sw.Stop();
            doc.Estado = DocumentoAnalisisEstado.Analizado;
            doc.MotivoError = null;
            doc.ProcesadoPorAgentId = agent.Id;
            doc.AgenteVersionAt = LeerVersionAgente(agent.PromptHistoryJson);
            doc.Proveedor = agent.Provider;
            doc.Modelo = model;
            doc.InputTokens = resp.InputTokens;
            doc.OutputTokens = resp.OutputTokens;
            doc.ProcesamientoMs = sw.ElapsedMilliseconds;
            doc.ProcesadoAt = DateTimeOffset.UtcNow;
            doc.PaquetesCount = nuevos.Count;
            doc.ResultadoJson = resp.Text;
            doc.PromptEnviado = promptEnviado;
            doc.PaginasEnviadas = paginasEnviadas;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "Fallo el analisis del documento {DocId}.", documentoId);
            // Contexto posiblemente sucio tras el fallo: se limpia el tracker y se marca el Error en limpio.
            try
            {
                db.ChangeTracker.Clear();
                var d = await db.DocumentosAnalisis.FirstOrDefaultAsync(x => x.Id == documentoId, ct);
                if (d is not null)
                {
                    d.Estado = DocumentoAnalisisEstado.Error;
                    d.MotivoError = Truncar(ex.Message, 1000);
                    d.ProcesamientoMs = sw.ElapsedMilliseconds;
                    d.ProcesadoAt = DateTimeOffset.UtcNow;
                    d.ResultadoJson = rawResponse;
                    d.PromptEnviado = promptEnviado;
                    d.PaginasEnviadas = paginasEnviadas;
                    await db.SaveChangesAsync(ct);
                }
            }
            catch (Exception ex2)
            {
                logger.LogError(ex2, "No se pudo marcar Error el documento {DocId}.", documentoId);
            }
        }
        finally
        {
            notifier.NotificarCambio(documentoId);
        }
    }

    public async Task<IReadOnlyList<DocumentoAnalisisProcesadoDto>> ListarProcesadosAsync(CancellationToken ct = default)
    {
        var docs = await db.DocumentosAnalisis.AsNoTracking()
            .Where(d => d.Estado != DocumentoAnalisisEstado.Cargado)
            .OrderByDescending(d => d.ProcesadoAt ?? d.CreatedAt)
            .ToListAsync(ct);

        var agentIds = docs.Where(d => d.ProcesadoPorAgentId != null).Select(d => d.ProcesadoPorAgentId!.Value).Distinct().ToList();
        var agentes = agentIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.AiAgents.AsNoTracking().Where(a => agentIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Name, ct);

        return docs.Select(d => new DocumentoAnalisisProcesadoDto(
            d.Id, d.FileName, d.Estado, d.PaquetesCount, d.ProcesamientoMs, d.ProcesadoAt,
            d.ProcesadoPorAgentId is Guid aid && agentes.TryGetValue(aid, out var n) ? n : null,
            d.AgenteVersionAt, d.Proveedor, d.Modelo, d.InputTokens, d.OutputTokens, d.MotivoError, d.CreatedAt)).ToList();
    }

    public async Task<IReadOnlyList<DocumentoAnalisisPaqueteDto>> ListarPaquetesAsync(Guid documentoId, CancellationToken ct = default)
    {
        var rows = await db.DocumentosAnalisisPaquetes.AsNoTracking()
            .Where(p => p.DocumentoAnalisisId == documentoId)
            .OrderBy(p => p.Orden).ToListAsync(ct);
        return rows.Select(p => new DocumentoAnalisisPaqueteDto(
            p.Id, p.Orden, p.NombreSugerido, p.StoredFileId, p.PaginaInicio, p.PaginaFin, p.Paginas, p.SizeBytes,
            p.Confianza, p.MetadataJson, p.DocumentosJson, p.RevisionEstado, p.RevisadoAt, p.RevisadoPor)).ToList();
    }

    public async Task<DocumentoAnalisisTrazaDto?> ObtenerTrazaAsync(Guid documentoId, CancellationToken ct = default)
    {
        var d = await db.DocumentosAnalisis.AsNoTracking().FirstOrDefaultAsync(x => x.Id == documentoId, ct);
        if (d is null) { return null; }

        string? agenteNombre = null;
        if (d.ProcesadoPorAgentId is Guid aid)
        {
            agenteNombre = await db.AiAgents.AsNoTracking().Where(a => a.Id == aid).Select(a => a.Name).FirstOrDefaultAsync(ct);
        }

        return new DocumentoAnalisisTrazaDto(
            d.Id, d.FileName, d.Estado, d.MotivoError, agenteNombre, d.AgenteVersionAt, d.Proveedor, d.Modelo,
            d.InputTokens, d.OutputTokens, d.ProcesamientoMs, d.ProcesadoAt, d.PaginasEnviadas, d.PaquetesCount,
            d.PromptEnviado, d.ResultadoJson);
    }

    public async Task MarcarPaqueteAsync(Guid paqueteId, PaqueteRevisionEstado estado, string? revisadoPor, CancellationToken ct = default)
    {
        var p = await db.DocumentosAnalisisPaquetes.FirstOrDefaultAsync(x => x.Id == paqueteId, ct)
                ?? throw new InvalidOperationException("El paquete no existe.");
        p.RevisionEstado = estado;
        if (estado == PaqueteRevisionEstado.Pendiente)
        {
            p.RevisadoAt = null;
            p.RevisadoPor = null;
        }
        else
        {
            p.RevisadoAt = DateTimeOffset.UtcNow;
            p.RevisadoPor = string.IsNullOrWhiteSpace(revisadoPor) ? null : revisadoPor.Trim();
        }
        await db.SaveChangesAsync(ct);
    }

    // ----------------------------------- helpers de proceso -----------------------------------

    /// <summary>Un paquete del plan de corte que devolvio el agente (ya normalizado).</summary>
    private sealed record PlanPaquete(List<int> Paginas, string? NombreSugerido, double? Confianza, string? MetadataJson, string? DocumentosJson);

    /// <summary>Arma el system prompt: toma el prompt del agente y le inyecta el bloque de campos de metadata.</summary>
    private static string ConstruirPrompt(string promptBase, IReadOnlyList<DocumentoAnalisisMetadataCampo> campos)
    {
        var sb = new StringBuilder();
        if (campos.Count == 0)
        {
            sb.Append("(sin campos configurados; incluye al menos: beneficiario, identificacion, concepto, valor, fecha_pago)");
        }
        else
        {
            foreach (var c in campos)
            {
                sb.Append("- ").Append(c.Clave).Append(" (").Append(c.Tipo).Append(')');
                if (!string.IsNullOrWhiteSpace(c.Descripcion)) { sb.Append(" - ").Append(c.Descripcion); }
                sb.Append('\n');
            }
        }
        var bloque = sb.ToString().TrimEnd();
        var baseText = promptBase ?? "";
        if (baseText.Contains(CamposMetadataPlaceholder, StringComparison.Ordinal))
        {
            return baseText.Replace(CamposMetadataPlaceholder, bloque);
        }
        return baseText + "\n\nCAMPOS_METADATA (usa EXACTAMENTE estas claves en metadata{}):\n" + bloque;
    }

    /// <summary>Claves que puede usar el agente para el array de paquetes (tolerante al prompt del usuario).</summary>
    private static readonly string[] ClavesArrayPaquetes = ["paquetes", "documentos", "grupos", "soportes", "paquetes_de_pago", "notas", "items"];

    /// <summary>
    /// Parsea el JSON del agente y devuelve los paquetes con sus paginas (1-based, acotadas a 1..total). Es TOLERANTE:
    /// el array de paquetes puede venir bajo varias claves (paquetes/documentos/grupos/soportes...) o ser el root
    /// mismo si es un array; las paginas se leen de un array "paginas" o de "pagina_inicio"/"pagina_fin" (numeros o
    /// texto). Asi el corte no depende de que el prompt use nombres exactos.
    /// </summary>
    private static List<PlanPaquete> ParsearPlan(string text, int total)
    {
        var json = ExtraerJson(text);
        var plan = new List<PlanPaquete>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Localiza el array de paquetes: el root si ya es array, o la primera clave conocida que sea array.
            JsonElement paquetes = default;
            var hallado = false;
            if (root.ValueKind == JsonValueKind.Array) { paquetes = root; hallado = true; }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var clave in ClavesArrayPaquetes)
                {
                    if (root.TryGetProperty(clave, out var arr) && arr.ValueKind == JsonValueKind.Array) { paquetes = arr; hallado = true; break; }
                }
            }
            if (!hallado) { return plan; }

            foreach (var pk in paquetes.EnumerateArray())
            {
                if (pk.ValueKind != JsonValueKind.Object) { continue; }

                var paginas = new List<int>();
                if (pk.TryGetProperty("paginas", out var pags) && pags.ValueKind == JsonValueKind.Array)
                {
                    foreach (var n in pags.EnumerateArray())
                    {
                        var v = LeerEntero(n);
                        if (v is int p && p >= 1 && p <= total) { paginas.Add(p); }
                    }
                }
                else
                {
                    var a = LeerEnteroProp(pk, "pagina_inicio", "paginaInicio", "inicio", "desde", "from", "start");
                    var b = LeerEnteroProp(pk, "pagina_fin", "paginaFin", "fin", "hasta", "to", "end");
                    if (a is int ai && b is int bi)
                    {
                        for (var v = Math.Max(1, ai); v <= Math.Min(total, bi); v++) { paginas.Add(v); }
                    }
                }
                paginas = paginas.Distinct().OrderBy(x => x).ToList();
                if (paginas.Count == 0) { continue; }

                string? nombre = LeerTextoProp(pk, "nombre_sugerido", "nombre", "titulo", "nombre_archivo");
                double? conf = pk.TryGetProperty("confianza", out var ce) && ce.ValueKind == JsonValueKind.Number && ce.TryGetDouble(out var cd) ? cd : null;
                string? meta = pk.TryGetProperty("metadata", out var me) && me.ValueKind == JsonValueKind.Object ? me.GetRawText() : null;
                // Solo guarda "documentos" por-paquete cuando es un sub-array (no cuando "documentos" fue el array raiz de paquetes).
                string? docs = pk.TryGetProperty("documentos", out var de) && de.ValueKind == JsonValueKind.Array ? de.GetRawText() : null;
                plan.Add(new PlanPaquete(paginas, nombre, conf, meta, docs));
            }
        }
        catch (JsonException)
        {
            return new List<PlanPaquete>();
        }
        return plan;
    }

    /// <summary>Lee un entero de un JsonElement que puede ser numero o cadena numerica. Null si no aplica.</summary>
    private static int? LeerEntero(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var v)) { return v; }
        if (e.ValueKind == JsonValueKind.String && int.TryParse(e.GetString(), out var s)) { return s; }
        return null;
    }

    /// <summary>Primera de las propiedades dadas que exista y sea entero (numero o texto). Null si ninguna.</summary>
    private static int? LeerEnteroProp(JsonElement obj, params string[] nombres)
    {
        foreach (var n in nombres)
        {
            if (obj.TryGetProperty(n, out var e) && LeerEntero(e) is int v) { return v; }
        }
        return null;
    }

    /// <summary>Primera de las propiedades dadas que exista y sea texto no vacio. Null si ninguna.</summary>
    private static string? LeerTextoProp(JsonElement obj, params string[] nombres)
    {
        foreach (var n in nombres)
        {
            if (obj.TryGetProperty(n, out var e) && e.ValueKind == JsonValueKind.String)
            {
                var s = e.GetString();
                if (!string.IsNullOrWhiteSpace(s)) { return s; }
            }
        }
        return null;
    }

    /// <summary>Recorta el texto al primer '{' .. ultimo '}' por si el modelo envolvio el JSON en texto/markdown.</summary>
    private static string ExtraerJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text.Substring(start, end - start + 1) : text;
    }

    /// <summary>Soft-delete de los paquetes previos del documento (reproceso) + borrado best-effort de sus PDFs.</summary>
    private async Task LimpiarPaquetesPreviosAsync(Guid documentoId, CancellationToken ct)
    {
        var previos = await db.DocumentosAnalisisPaquetes.Where(p => p.DocumentoAnalisisId == documentoId).ToListAsync(ct);
        if (previos.Count == 0) { return; }
        foreach (var p in previos)
        {
            p.IsDeleted = true;
            p.DeletedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        foreach (var p in previos) { await files.DeleteAsync(p.StoredFileId, ct); }
    }

    /// <summary>Fecha del snapshot activo del agente (AiAgent.PromptHistoryJson[0].SavedAt) = "version" usada. Null si no hay.</summary>
    private static DateTimeOffset? LeerVersionAgente(string? historyJson)
    {
        if (string.IsNullOrWhiteSpace(historyJson)) { return null; }
        try
        {
            using var doc = JsonDocument.Parse(historyJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0) { return null; }
            var first = doc.RootElement[0];
            foreach (var name in new[] { "SavedAt", "savedAt" })
            {
                if (first.TryGetProperty(name, out var sa) && sa.TryGetDateTimeOffset(out var dto)) { return dto; }
            }
        }
        catch (JsonException) { /* historial ilegible: sin version */ }
        return null;
    }

    /// <summary>Nombre de archivo saneado para el PDF separado (sin caracteres invalidos, con .pdf).</summary>
    private static string SanitizarNombre(string? sugerido, string fallback, int orden)
    {
        var baseName = !string.IsNullOrWhiteSpace(sugerido)
            ? sugerido!
            : $"{Path.GetFileNameWithoutExtension(fallback)}_{orden:00}";
        foreach (var ch in Path.GetInvalidFileNameChars()) { baseName = baseName.Replace(ch, ' '); }
        baseName = string.Join(' ', baseName.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (baseName.Length > 120) { baseName = baseName[..120].Trim(); }
        if (string.IsNullOrWhiteSpace(baseName)) { baseName = $"paquete_{orden:00}"; }
        if (!baseName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) { baseName += ".pdf"; }
        return baseName;
    }

    private static string Truncar(string s, int max) => string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max];
}
