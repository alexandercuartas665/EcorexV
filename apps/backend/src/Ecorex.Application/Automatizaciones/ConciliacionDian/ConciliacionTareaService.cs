using System.Globalization;
using System.Text;
using Ecorex.Application.Forms;
using Ecorex.Application.Storage;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Automatizaciones.ConciliacionDian;

/// <summary>
/// Crea la tarea (actividad) de una factura del conciliador DIAN con una SALIDA al flujo (concepto 000270 que trae
/// su flujo + formulario). Es la UNICA forma de crearla: la usan el boton "+ Tarea" y el analisis con IA del
/// conciliador, y el boton "Crear tarea" del modulo de autorizacion, asi las tres producen el MISMO tipo de tarea:
/// mismo titulo y resumen, formulario prellenado con los datos de la factura (incluido el mapeo grilla->form de
/// "Direccionar compra"), los PDF de la factura como documentos de la tarea y el vinculo factura->tarea que pintan
/// ambos modulos. No asigna la factura: quien llama la deja asignada antes (columna del conciliador).
/// </summary>
public interface IConciliacionTareaService
{
    /// <returns>(true, numero de tarea) o (false, mensaje de error).</returns>
    Task<(bool Ok, string Result)> CrearTareaAsync(ConciliacionDianRenglonDto renglon, SalidaFlujoDto salida,
        Guid assigneeTenantUserId, Guid actorUserId, string actorName, CancellationToken ct = default);
}

public sealed class ConciliacionTareaService : IConciliacionTareaService
{
    private readonly ITaskItemService _tasks;
    private readonly IFormResponseService _forms;
    private readonly IAutorizacionComprasConfigService _cfg;
    private readonly IConciliacionFieldService _fields;
    private readonly IFileStorageService _files;

    public ConciliacionTareaService(ITaskItemService tasks, IFormResponseService forms,
        IAutorizacionComprasConfigService cfg, IConciliacionFieldService fields, IFileStorageService files)
    {
        _tasks = tasks; _forms = forms; _cfg = cfg; _fields = fields; _files = files;
    }

    public async Task<(bool Ok, string Result)> CrearTareaAsync(ConciliacionDianRenglonDto r, SalidaFlujoDto salida,
        Guid assignee, Guid actorUserId, string actorName, CancellationToken ct = default)
    {
        // Candado: una fila fantasma (solo en NEWTON, no en la DIAN) nunca produce tarea.
        if (r.Origen == OrigenRenglonConciliacionDian.Newton) { return (false, "Documento solo en NEWTON (no esta en la DIAN): no puede generar tarea."); }
        if (salida.SubcategoriaId is not Guid subId) { return (false, "La salida no tiene concepto configurado."); }
        try
        {
            var titulo = $"Compra {r.NumFacturaProveedor} - {r.NombreProveedor}".Trim();
            if (titulo.Length > 120) { titulo = titulo[..120]; }
            var req = new CreateTaskItemRequest(
                Title: titulo,
                ActivityTypeId: null,
                Description: BuildResumen(r),
                AssigneeTenantUserId: assignee,
                RequesterName: r.NombreProveedor,
                RequesterDocument: r.NitProveedor,
                SubcategoriaId: subId);
            var result = await _tasks.CreateAsync(req, actorUserId, actorName, ct);
            if (!result.IsOk || result.Value is null) { return (false, result.Error ?? "No se pudo crear la tarea."); }
            var num = result.Value.Item.Number;

            // El formulario configurado "cae" en la tarea (via ActividadSubcategoria.FormDefinitionId). Se prellena con
            // los datos de la factura en un borrador keyed por el NUMERO de tarea. Solo los campos que SIGUEN siendo
            // texto/numero/fecha PLANOS: si un codigo se repurposo (selector de usuario, lookup) no se pisa con el dato crudo.
            if (salida.FormDefinitionId is Guid fid)
            {
                try
                {
                    var td = await _forms.GetOrCreateDraftAsync(fid, num, ct);
                    if (td.IsOk && td.Value is { } tdr)
                    {
                        var candidatos = new Dictionary<string, FormFieldValue>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["proveedor"] = new FormFieldValue(r.NombreProveedor ?? string.Empty, "Text"),
                            ["nit"] = new FormFieldValue(r.NitProveedor ?? string.Empty, "Text"),
                            ["rut"] = new FormFieldValue(r.RutEscaneado ?? string.Empty, "Text"),
                            ["fecha_compra"] = new FormFieldValue(r.FechaEmision.ToString("yyyy-MM-dd"), "Date"),
                            ["num_factura"] = new FormFieldValue(r.NumFacturaProveedor ?? string.Empty, "Text"),
                            // NIT del proveedor (suele ir en un campo OCULTO): filtra listas por proveedor, p. ej.
                            // las OC de ORDENES_COMPRA cuya columna PROVEEDOR es el NIT.
                            ["nit_proveedor"] = new FormFieldValue(r.NitProveedor ?? string.Empty, "Text"),
                        };
                        var planos = await _cfg.GetPlainTextFieldCodesAsync(fid, ct);
                        // Sin dato no se escribe (antes quedaba p. ej. "rut" = "" en el borrador).
                        var data = candidatos.Where(kv => planos.Contains(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value.Value))
                            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
                        // Mapeo configurado (el MISMO de "Direccionar compra") en REVERSA grilla->form; prevalece.
                        foreach (var kv in await BuildMappingPrefillAsync(r, salida, assignee, fid, ct)) { data[kv.Key] = kv.Value; }
                        if (data.Count > 0) { await _forms.SaveAsync(tdr.Id, data, false, cancellationToken: ct); }
                    }
                }
                catch { /* el resumen ya viaja en la descripcion */ }
            }

            // Los PDF del conciliador (por CUFE) como enlaces en la pestana Documentos de la tarea.
            if (!string.IsNullOrWhiteSpace(r.Cufe))
            {
                try
                {
                    var docs = (await _files.ListAsync("ConciliacionDian", r.Cufe, ct: ct))
                        .Where(f => (f.ContentType ?? string.Empty).Contains("pdf", StringComparison.OrdinalIgnoreCase)
                                 || (f.FileName ?? string.Empty).EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    foreach (var f in docs)
                    {
                        // Los archivos del conciliador vienen como {cufe}_dian.pdf / {cufe}_newton.pdf.
                        var fn = f.FileName ?? string.Empty;
                        var nombre = fn.Contains("_newton", StringComparison.OrdinalIgnoreCase) ? "Representacion grafica (NEWTON).pdf"
                            : fn.Contains("_dian", StringComparison.OrdinalIgnoreCase) ? "Factura electronica (DIAN).pdf"
                            : fn;
                        await _tasks.AddAttachmentAsync(
                            new AddTaskAttachmentRequest(result.Value.Item.Id, nombre, f.Url, f.ContentType, f.SizeBytes),
                            actorUserId, actorName, ct);
                    }
                }
                catch { /* best-effort: los documentos siguen disponibles en el conciliador */ }
            }

            // Factura -> tarea: el mapa que pintan el conciliador y el modulo de autorizacion.
            await _cfg.SetSolicitadaAsync(r.Id, num, ct);
            return (true, num);
        }
        catch (Exception ex) { return (false, "Error al crear la tarea: " + ex.Message); }
    }

    private static string BuildResumen(ConciliacionDianRenglonDto r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Factura de compra DIAN (desde el conciliador).");
        sb.AppendLine($"Proveedor: {r.NombreProveedor} (NIT {r.NitProveedor})");
        sb.AppendLine($"Factura: {r.NumFacturaProveedor}");
        sb.AppendLine($"Fecha de emision: {r.FechaEmision:dd/MM/yyyy}");
        sb.AppendLine($"Total: {r.TotalFactura.ToString("#,##0.00", CultureInfo.InvariantCulture)}");
        if (!string.IsNullOrWhiteSpace(r.NumFacturaSoldarco)) { sb.AppendLine($"Documento SISTEMA: {r.NumFacturaSoldarco}"); }
        if (!string.IsNullOrWhiteSpace(r.OrdenCompraSoldarco)) { sb.AppendLine($"Orden de compra: {r.OrdenCompraSoldarco}"); }
        if (!string.IsNullOrWhiteSpace(r.Cufe)) { sb.AppendLine($"CUFE: {r.Cufe}"); }
        sb.Append($"Referencia conciliacion (REG): {r.Id}");
        return sb.ToString();
    }

    // Prellenado grilla->form: el mapeo configurado (DireccionarMaps) en REVERSA. Por cada (FormField <- Target)
    // resuelve el valor del renglon para la columna Target (oc | numsol | asignado | dyn:{fieldKey}) y lo escribe
    // con el tipo de control real del campo. Contraparte de creacion del verbo DIRECCIONAR_CONCILIADOR.
    private async Task<Dictionary<string, FormFieldValue>> BuildMappingPrefillAsync(
        ConciliacionDianRenglonDto r, SalidaFlujoDto salida, Guid assignee, Guid fid, CancellationToken ct)
    {
        var result = new Dictionary<string, FormFieldValue>(StringComparer.OrdinalIgnoreCase);
        if (!salida.DireccionarEnabled || salida.DireccionarMaps is not { Count: > 0 } maps) { return result; }
        var ctrlTypes = await _cfg.GetFieldControlTypesAsync(fid, ct);
        Dictionary<string, ConciliacionFieldDefDto>? dynByKey = null;
        IReadOnlyDictionary<Guid, Dictionary<Guid, string?>>? dynVals = null;
        foreach (var m in maps)
        {
            if (string.IsNullOrWhiteSpace(m.FormField) || string.IsNullOrWhiteSpace(m.Target)) { continue; }
            if (!ctrlTypes.TryGetValue(m.FormField, out var ctrl)) { continue; }  // el campo ya no existe en el form
            var t = m.Target;
            string? raw;
            if (string.Equals(t, "oc", StringComparison.OrdinalIgnoreCase)) { raw = r.OrdenCompraSoldarco; }
            else if (string.Equals(t, "numsol", StringComparison.OrdinalIgnoreCase)) { raw = r.NumFacturaSoldarco; }
            else if (string.Equals(t, "asignado", StringComparison.OrdinalIgnoreCase)) { raw = assignee.ToString(); }
            else if (t.StartsWith("dyn:", StringComparison.Ordinal))
            {
                dynByKey ??= (await _fields.ListDefsAsync(ct))
                    .Where(d => !d.FieldKey.StartsWith("sys:", StringComparison.Ordinal))
                    .ToDictionary(d => "dyn:" + d.FieldKey, d => d);
                if (!dynByKey.TryGetValue(t, out var def)) { continue; }   // columna dinamica ya no existe
                dynVals ??= await _fields.GetValuesAsync(new[] { r.Id }, ct);
                raw = dynVals.TryGetValue(r.Id, out var dd) && dd.TryGetValue(def.Id, out var vv) ? vv : null;
            }
            else { continue; }   // target desconocido
            if (string.IsNullOrWhiteSpace(raw)) { continue; }   // sin dato: no pisar con vacio
            var coerced = CoerceForControl(ctrl, raw!);
            if (coerced is null) { continue; }
            result[m.FormField] = new FormFieldValue(coerced, ctrl);
        }
        return result;
    }

    // Valor crudo del renglon -> formato interno del renderer segun el control: Toggle -> "true"/"false";
    // Date -> yyyy-MM-dd; el resto (Text/Number/Select/Radio/SystemUser) tal cual.
    private static string? CoerceForControl(string ctrl, string raw)
    {
        raw = raw.Trim();
        switch (ctrl)
        {
            case "Toggle":
                var on = raw is "1" or "true" or "True" or "si" or "Si" or "SI"
                    or "yes" or "Yes" or "on" or "On" or "x" or "X";
                return on ? "true" : "false";
            case "Date":
                return DateTime.TryParse(raw, out var dt) ? dt.ToString("yyyy-MM-dd") : raw;
            default:
                return raw;
        }
    }
}
