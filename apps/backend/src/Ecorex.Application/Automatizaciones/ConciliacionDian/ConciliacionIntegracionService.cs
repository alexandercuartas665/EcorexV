using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.Storage;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Automatizaciones.ConciliacionDian;

/// <summary>Resultado de ejecutar (o simular) el envio de documentos al sistema contable.</summary>
public sealed record IntegracionRunResult(
    int Renglones, int Elegibles, int Enviados, int YaEnviados, int SinDatos, int SinArchivos, int Errores,
    bool Simulado, IReadOnlyList<IntegracionRenglonOutcome> Detalle, string? Mensaje = null);

/// <summary>Resultado por renglon. <see cref="Estado"/>: enviado | ya_enviado | sin_datos | sin_archivos | error.
/// <see cref="Lote"/> = numero(s) LOT que devolvio el contable (o el LOT ya guardado si "ya_enviado").</summary>
public sealed record IntegracionRenglonOutcome(
    Guid RenglonId, string NumFacturaProveedor, string Proveedor, string Estado, string? Lote, string? Detalle);

/// <summary>Progreso en vivo del envio (para la barra + lista de la UI). <see cref="Last"/> es el renglon recien
/// procesado.</summary>
public sealed record IntegracionProgress(int Done, int Total, IntegracionRenglonOutcome Last);

/// <summary>
/// Ejecuta la "Integracion contable" del conciliador DIAN: por cada factura PENDIENTE que tiene los datos que
/// alimentan la integracion (el parametro "documento" mapeado resuelve a un valor) y al menos uno de los archivos
/// marcados, arma el multipart (concepto+usuario fijos + parametros mapeados de la parrilla + los documentos ya
/// capturados por CUFE) y lo postea a la API del core (sweb_cargar_documentos.ashx) de cada integracion habilitada.
/// Los archivos salen del almacen unificado (modulo "ConciliacionDian", Ref1 = CUFE), el mismo que alimenta el
/// visor de autorizacion. Se dispara MANUALMENTE (boton junto a "Aplicar reglas"); no depende del flujo. El envio
/// es idempotente: un renglon enviado con exito se marca y no se reenvia (ver IAutorizacionComprasConfigService).
/// </summary>
public interface IConciliacionIntegracionService
{
    /// <summary>Simula o ejecuta el envio de los renglones del documento. Con <paramref name="simular"/>=true no
    /// hace POST ni marca nada (para el conteo de confirmacion); con false envia y marca CADA factura apenas sube
    /// con exito (durable: si se interrumpe, lo ya enviado queda marcado y no se reenvia). <paramref name="onProgress"/>
    /// se invoca (y se espera) factura a factura, para que la UI (Blazor Server) pinte el avance de forma fiable.</summary>
    Task<IntegracionRunResult> EjecutarAsync(Guid documentoId, bool simular,
        Func<IntegracionProgress, Task>? onProgress = null, CancellationToken ct = default);
}

public sealed class ConciliacionIntegracionService : IConciliacionIntegracionService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAutorizacionComprasConfigService _cfg;
    private readonly IFileStorageService _files;
    private readonly IHttpClientFactory _http;

    // Modulo del almacen unificado donde viven los documentos de la factura (Ref1 = CUFE). Igual que el visor.
    private const string FilesModule = "ConciliacionDian";

    // Nombres de los campos multipart para los 4 documentos. El handler del core (sweb_cargar_documentos.ashx)
    // debe leer estos nombres; se mantienen explicitos y estables para poder alinear el handler.
    private const string FieldPdfProveedor = "pdf_proveedor";
    private const string FieldPdfDian = "pdf_dian";
    private const string FieldXmlProveedor = "xml_proveedor";
    private const string FieldXmlDian = "xml_dian";

    // Parametros de la API que se toman de la parrilla (concepto y usuario son fijos y van aparte).
    private const string ApiDocumento = "documento";
    private static readonly string[] ApiFieldsMapeados =
        { "documento", "tipo", "observacion", "empresa", "cedula", "contrato" };

    public ConciliacionIntegracionService(IApplicationDbContext db, ITenantContext tenant,
        IAutorizacionComprasConfigService cfg, IFileStorageService files, IHttpClientFactory http)
    {
        _db = db; _tenant = tenant; _cfg = cfg; _files = files; _http = http;
    }

    public async Task<IntegracionRunResult> EjecutarAsync(Guid documentoId, bool simular,
        Func<IntegracionProgress, Task>? onProgress = null, CancellationToken ct = default)
    {
        var vacio = Array.Empty<IntegracionRenglonOutcome>();
        if (_tenant.TenantId is null)
        {
            return new IntegracionRunResult(0, 0, 0, 0, 0, 0, 0, simular, vacio, "Sin tenant.");
        }

        var cfg = await _cfg.GetIntegracionAsync(ct);
        var integs = cfg.Integraciones
            .Where(i => i.Enabled && !string.IsNullOrWhiteSpace(i.ApiUrl))
            .ToList();
        if (integs.Count == 0)
        {
            return new IntegracionRunResult(0, 0, 0, 0, 0, 0, 0, simular, vacio, "No hay integraciones habilitadas.");
        }

        var renglones = await _db.ConciliacionDianRenglones.AsNoTracking()
            .Where(r => r.DocumentoId == documentoId
                && (r.Estado == EstadoConciliacionDian.Conciliar || r.Estado == EstadoConciliacionDian.Conciliado))
            .ToListAsync(ct);
        if (renglones.Count == 0)
        {
            return new IntegracionRunResult(0, 0, 0, 0, 0, 0, 0, simular, vacio, "No hay renglones para enviar.");
        }

        // Campos dinamicos (EAV) del tenant + valores por renglon, para resolver columnas "dyn:*" mapeadas.
        var ids = renglones.Select(r => r.Id).ToList();
        var dynDefs = await _db.ConciliacionFieldDefinitions.AsNoTracking().OrderBy(d => d.SortOrder).ToListAsync(ct);
        var dynVals = new Dictionary<(Guid Renglon, Guid Def), string>();
        if (dynDefs.Count > 0)
        {
            var vs = await _db.ConciliacionFieldValues.AsNoTracking()
                .Where(v => ids.Contains(v.RenglonId)).ToListAsync(ct);
            foreach (var v in vs) { dynVals[(v.RenglonId, v.FieldDefinitionId)] = v.Value ?? ""; }
        }

        var enviados = await _cfg.GetEnviadosContableAsync(ct);

        var detalle = new List<IntegracionRenglonOutcome>(renglones.Count);
        int cEleg = 0, cEnv = 0, cYa = 0, cSinDatos = 0, cSinArch = 0, cErr = 0;
        var done = 0;
        var total = renglones.Count;

        HttpClient? client = null;
        if (!simular)
        {
            client = _http.CreateClient();
            // Techo por POST. Si el contable no responde en este tiempo, el renglon se marca como error
            // (fila visible con "sin respuesta"), no se cuelga el envio ni se pierde la trazabilidad.
            client.Timeout = TimeSpan.FromSeconds(30);
        }

        foreach (var r in renglones)
        {
            ct.ThrowIfCancellationRequested();
            if (enviados.TryGetValue(r.Id, out var lotePrevio))
            {
                cYa++;
                var ya = new IntegracionRenglonOutcome(r.Id, r.NumFacturaProveedor, r.NombreProveedor, "ya_enviado",
                    string.IsNullOrWhiteSpace(lotePrevio) ? null : lotePrevio, null);
                detalle.Add(ya);
                if (onProgress is not null) { await onProgress(new IntegracionProgress(++done, total, ya)); }
                continue;
            }

            var vals = BuildValues(r, dynDefs, dynVals);

            // Archivos ya capturados de la factura (por CUFE), clasificados en los 4 buckets. Una sola lectura
            // por renglon; cada integracion filtra con sus flags.
            List<StoredFileDto> files;
            try
            {
                files = string.IsNullOrWhiteSpace(r.Cufe)
                    ? new List<StoredFileDto>()
                    : (await _files.ListAsync(FilesModule, r.Cufe, null, ct)).ToList();
            }
            catch { files = new List<StoredFileDto>(); }

            // Clasificacion por Ref2 canonico (lo fija el bot DIAN / la ingesta NEWTON): DIAN_PDF/DIAN_XML/
            // NEWTON_PDF/NEWTON_XML. Si un archivo viejo no trae Ref2, cae a la heuristica extension+origen.
            var pdfProv = PickByRef2(files, "NEWTON_PDF", f => EsPdf(f) && EsProveedor(f));
            var pdfDian = PickByRef2(files, "DIAN_PDF", f => EsPdf(f) && !EsProveedor(f));
            var xmlProv = PickByRef2(files, "NEWTON_XML", f => EsXml(f) && EsProveedor(f));
            var xmlDian = PickByRef2(files, "DIAN_XML", f => EsXml(f) && !EsProveedor(f));

            var huboError = false;
            var huboEnvio = false;
            var huboSinArch = false;
            var huboSinDatos = false;
            var notas = new List<string>();
            var lotes = new List<string>();

            foreach (var ig in integs)
            {
                var documento = ResolveParam(ig.Mappings, ApiDocumento, vals);
                if (string.IsNullOrWhiteSpace(documento))
                {
                    huboSinDatos = true;
                    notas.Add($"{ig.Nombre}: sin 'documento'");
                    continue;
                }

                // Archivos a enviar para ESTA integracion segun sus flags.
                var toSend = new List<(string Field, StoredFileDto File)>();
                if (ig.SendPdfProveedor && pdfProv is not null) { toSend.Add((FieldPdfProveedor, pdfProv)); }
                if (ig.SendPdfDian && pdfDian is not null) { toSend.Add((FieldPdfDian, pdfDian)); }
                if (ig.SendXmlProveedor && xmlProv is not null) { toSend.Add((FieldXmlProveedor, xmlProv)); }
                if (ig.SendXmlDian && xmlDian is not null) { toSend.Add((FieldXmlDian, xmlDian)); }
                if (toSend.Count == 0)
                {
                    huboSinArch = true;
                    notas.Add($"{ig.Nombre}: sin archivos");
                    continue;
                }

                if (simular)
                {
                    huboEnvio = true;
                    continue;
                }

                try
                {
                    using var form = new MultipartFormDataContent();
                    form.Add(new StringContent(ig.Concepto ?? string.Empty), "concepto");
                    form.Add(new StringContent(string.IsNullOrWhiteSpace(ig.Usuario) ? "ECOREX" : ig.Usuario), "usuario");
                    foreach (var af in ApiFieldsMapeados)
                    {
                        var val = ResolveParam(ig.Mappings, af, vals);
                        if (!string.IsNullOrWhiteSpace(val)) { form.Add(new StringContent(val), af); }
                    }
                    foreach (var (field, f) in toSend)
                    {
                        var content = await _files.ReadAsync(f.Id, ct);
                        if (content is null) { continue; }
                        var bytes = new ByteArrayContent(content.Content);
                        if (!string.IsNullOrWhiteSpace(content.ContentType))
                        {
                            bytes.Headers.ContentType = new MediaTypeHeaderValue(content.ContentType);
                        }
                        form.Add(bytes, field, content.FileName);
                    }

                    using var resp = await client!.PostAsync(ig.ApiUrl, form, ct);
                    var body = await resp.Content.ReadAsStringAsync(ct);
                    // IMPORTANTE: el handler responde 200 con {ok:false,error:...} ante fallos logicos (p. ej.
                    // "no se recibio ningun archivo"). No basta el status: hay que mirar el cuerpo.
                    var (ok, lote, err) = ParseRespuesta(resp.IsSuccessStatusCode, (int)resp.StatusCode, body);
                    if (ok)
                    {
                        huboEnvio = true;
                        if (!string.IsNullOrWhiteSpace(lote)) { lotes.Add(lote!); }
                    }
                    else
                    {
                        huboError = true;
                        notas.Add($"{ig.Nombre}: {err}");
                    }
                }
                // Solo la cancelacion REAL del usuario (ct) aborta el envio; el timeout propio del HttpClient
                // tambien llega como OperationCanceledException pero NO es cancelacion: es el contable sin
                // responder, y debe quedar como un error legible del renglon (fila visible), no como "cancelado".
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (OperationCanceledException)
                {
                    huboError = true;
                    notas.Add($"{ig.Nombre}: sin respuesta (timeout)");
                }
                catch (Exception ex)
                {
                    huboError = true;
                    notas.Add($"{ig.Nombre}: {ex.Message}");
                }
            }

            // Resultado del renglon (prioridad: error > enviado > sin_archivos > sin_datos).
            string estado;
            var loteStr = lotes.Count > 0 ? string.Join(", ", lotes.Distinct()) : null;
            if (huboError)
            {
                estado = "error"; cErr++;
            }
            else if (huboEnvio)
            {
                estado = "enviado"; cEnv++; cEleg++;
                // Marcado DURABLE e incremental: apenas sube con exito, se persiste con su LOT. Asi una
                // interrupcion no obliga a reenviar (evita LOTs duplicados en el contable).
                if (!simular) { await _cfg.MarkEnviadoContableAsync(r.Id, loteStr ?? string.Empty, ct); }
            }
            else if (huboSinArch)
            {
                estado = "sin_archivos"; cSinArch++;
            }
            else
            {
                estado = "sin_datos"; cSinDatos++;
                _ = huboSinDatos; // informativo
            }

            var nota = notas.Count > 0 ? string.Join("; ", notas) : null;
            var outcome = new IntegracionRenglonOutcome(r.Id, r.NumFacturaProveedor, r.NombreProveedor, estado, loteStr, nota);
            detalle.Add(outcome);
            if (onProgress is not null) { await onProgress(new IntegracionProgress(++done, total, outcome)); }
        }

        return new IntegracionRunResult(
            renglones.Count, cEleg, cEnv, cYa, cSinDatos, cSinArch, cErr, simular, detalle);
    }

    /// <summary>Interpreta la respuesta del handler contable. Devuelve (ok, lote, error). El cuerpo esperado es
    /// <c>{ok:true,documentos:["LOT..."],numero,cantidad}</c> o <c>{ok:false,error:"..."}</c>. Tolerante: si no
    /// es JSON pero el HTTP fue 2xx, lo da por bueno sin lote.</summary>
    private static (bool Ok, string? Lote, string? Error) ParseRespuesta(bool httpOk, int status, string body)
    {
        if (!httpOk) { return (false, null, $"HTTP {status}"); }
        if (string.IsNullOrWhiteSpace(body)) { return (true, null, null); }
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { return (true, null, null); }
            var ok = !root.TryGetProperty("ok", out var okEl)
                || okEl.ValueKind == JsonValueKind.True
                || (okEl.ValueKind == JsonValueKind.String && bool.TryParse(okEl.GetString(), out var b) && b);
            if (!ok)
            {
                var error = root.TryGetProperty("error", out var eEl) ? eEl.ToString() : "rechazado";
                return (false, null, error);
            }
            string? lote = null;
            if (root.TryGetProperty("documentos", out var docsEl) && docsEl.ValueKind == JsonValueKind.Array)
            {
                lote = string.Join(", ", docsEl.EnumerateArray().Select(x => x.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)));
            }
            if (string.IsNullOrWhiteSpace(lote) && root.TryGetProperty("numero", out var numEl))
            {
                lote = numEl.ToString();
            }
            return (true, string.IsNullOrWhiteSpace(lote) ? null : lote, null);
        }
        catch { return (true, null, null); } // 2xx pero cuerpo no-JSON: enviado sin lote legible
    }

    // ---- Resolucion de parametros ----

    /// <summary>Resuelve el valor de un parametro de la API para un renglon: "text" -> literal; "column" -> valor
    /// de la columna de la parrilla (estatica o dyn:*). Si no hay mapeo, string vacio.</summary>
    private static string ResolveParam(IReadOnlyList<IntegracionFieldMapDto> mappings, string apiField, IReadOnlyDictionary<string, string> vals)
    {
        var m = mappings.FirstOrDefault(x => string.Equals(x.ApiField, apiField, StringComparison.OrdinalIgnoreCase));
        if (m is null) { return string.Empty; }
        if (string.Equals(m.Source, "text", StringComparison.OrdinalIgnoreCase)) { return m.Value ?? string.Empty; }
        return vals.TryGetValue(m.Value ?? string.Empty, out var v) ? v : string.Empty;
    }

    /// <summary>Dict campo-de-parrilla -> valor para un renglon. Mismas claves que las columnas del conciliador
    /// (estaticas) + "dyn:{key}" (EAV). Es el espejo de ConciliacionRuleService.BuildContext mas rut/rechazado.</summary>
    private static Dictionary<string, string> BuildValues(ConciliacionDianRenglon r,
        IReadOnlyList<ConciliacionFieldDefinition> dynDefs, IReadOnlyDictionary<(Guid, Guid), string> dynVals)
    {
        string D(decimal d) => d.ToString(CultureInfo.InvariantCulture);
        var dict = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["numprov"] = r.NumFacturaProveedor,
            ["numsol"] = r.NumFacturaSoldarco,
            ["oc"] = r.OrdenCompraSoldarco,
            ["nit"] = r.NitProveedor,
            ["proveedor"] = r.NombreProveedor,
            ["tipodoc"] = r.TipoDocDian,
            ["tipopago"] = r.TipoPago,
            ["cufe"] = r.Cufe,
            ["fecha"] = r.FechaEmision.ToString("yyyy-MM-dd"),
            ["sub_bruto"] = D(r.SubtotalBruto),
            ["descuento"] = D(r.DescuentoComercial),
            ["sub_neto"] = D(r.SubtotalNeto),
            ["iva"] = D(r.IvaDescontable),
            ["total_antes_ret"] = D(r.TotalAntesRetenciones),
            ["retefuente"] = D(r.RetRetefuente),
            ["ica"] = D(r.RetIca),
            ["total"] = D(r.TotalFactura),
            ["plataforma"] = r.PlataformaProveedor ? "1" : "0",
            ["aprobada"] = r.FacturaAprobada ? "1" : "0",
            ["aprobada_manual"] = r.AprobadaManual ? "1" : "0",
            ["rut"] = r.RutEscaneado ?? string.Empty,
            ["rechazado"] = r.Rechazado ? "1" : "0",
            ["ev30"] = r.Evento30 ? "1" : "0",
            ["ev31"] = r.Evento31 ? "1" : "0",
            ["ev32"] = r.Evento32 ? "1" : "0",
            ["ev33"] = r.Evento33 ? "1" : "0",
            ["ev34"] = r.Evento34 ? "1" : "0",
            ["estado"] = r.Estado.ToString(),
        };
        foreach (var d in dynDefs)
        {
            dict["dyn:" + d.FieldKey] = dynVals.TryGetValue((r.Id, d.Id), out var dv) ? dv : string.Empty;
        }
        return dict;
    }

    // ---- Clasificacion de archivos ----
    /// <summary>Elige el archivo por su Ref2 canonico (p. ej. "DIAN_PDF"); si ninguno lo trae, cae al
    /// predicado heuristico (extension + origen) para archivos viejos sin Ref2.</summary>
    private static StoredFileDto? PickByRef2(IReadOnlyList<StoredFileDto> files, string ref2, Func<StoredFileDto, bool> fallback)
    {
        var exact = files.FirstOrDefault(f => string.Equals(f.Ref2, ref2, StringComparison.OrdinalIgnoreCase));
        return exact ?? files.FirstOrDefault(fallback);
    }

    private static bool EsPdf(StoredFileDto f)
        => (f.ContentType ?? "").Contains("pdf", StringComparison.OrdinalIgnoreCase)
        || (f.FileName ?? "").EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
    private static bool EsXml(StoredFileDto f)
        => (f.ContentType ?? "").Contains("xml", StringComparison.OrdinalIgnoreCase)
        || (f.FileName ?? "").EndsWith(".xml", StringComparison.OrdinalIgnoreCase);
    // Proveedor de facturacion = representacion de NEWTON (antes "Newton"); si no, es el documento de la DIAN.
    private static bool EsProveedor(StoredFileDto f)
        => ((f.Ref2 ?? "") + (f.FileName ?? "")).Contains("newton", StringComparison.OrdinalIgnoreCase);
}
