using System.Globalization;
using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.Requerimientos;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Rules.Verbs;

/// <summary>
/// Verbo REGISTRAR_RESPUESTA_RFQ (Capa 10, Ola C: la "vuelta"): al ENVIAR el formulario de cotizacion que el
/// proveedor contesto, empata cada fila de la grilla por su CLAVE tecnica oculta (la misma que la Ola B inyecto)
/// contra las lineas del RFQ y: (1) escribe en cada <see cref="Ecorex.Domain.Entities.RequerimientoRfqLinea"/> los
/// campos de oferta (precio, vigencia en dias, lead time, marca, nota); (2) vuelca la oferta al COMPARADOR como
/// una <see cref="Ecorex.Domain.Entities.RequerimientoCotizacion"/> (upsert por producto+proveedor, Fuente=Rfq) para
/// que el comprador la compare y la IA la evalue; (3) marca el RFQ como Respondida. El mapeo de columnas sale de la
/// config del tenant (<see cref="IRequerimientoConfigService.GetRfqFormMapAsync"/>), unica fuente de verdad que ya
/// uso la Ola B. Idempotente: re-enviar re-escribe la linea y re-hace el upsert (no duplica). No hay ciclo de DI: el
/// motor resuelve los verbos de forma perezosa (IServiceProvider) al ejecutar, nunca en el constructor.
/// </summary>
public sealed class RegistrarRespuestaRfqVerb : IRuleVerb
{
    public const string VerbName = "REGISTRAR_RESPUESTA_RFQ";

    private readonly IRequerimientoService _req;
    private readonly IRequerimientoConfigService _config;
    private readonly IApplicationDbContext _db;

    public RegistrarRespuestaRfqVerb(IRequerimientoService req, IRequerimientoConfigService config, IApplicationDbContext db)
    {
        _req = req; _config = config; _db = db;
    }

    public string Name => VerbName;

    public RuleVerbDescriptor Descriptor { get; } = new(
        VerbName,
        "Registrar respuesta de cotizacion (RFQ)",
        "Al enviar el formulario que el proveedor respondio, empata cada fila por su clave oculta y guarda la "
        + "oferta en la linea del RFQ y en el comparador de compras. El mapeo de columnas se toma de la "
        + "configuracion del modulo (formulario de cotizacion RFQ); este verbo no necesita parametros.",
        [
            new RuleVerbParamDescriptor("autoComplete", "Completar paso", RuleParamType.Boolean, Required: false,
                "Si es true y la regla es autonoma de un nodo, el paso del flujo se completa solo.")
        ]);

    public async Task<RuleVerbResult> ExecuteAsync(RuleContext context, CancellationToken cancellationToken)
    {
        var autoComplete = context.GetBoolParam("autoComplete");

        if (context.FormResponseId is not Guid respId)
        {
            return RuleVerbResult.Ok("Sin respuesta de formulario: nada que registrar.", 0, autoCompleteStep: autoComplete);
        }

        // Mapeo de columnas (unica fuente de verdad, la misma que uso la Ola B para inyectar). Sin grilla, sin
        // columna clave o sin columna de precio no hay forma de empatar ni volcar la oferta: no se hace nada.
        var map = await _config.GetRfqFormMapAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(map.GridFieldCode)
            || string.IsNullOrWhiteSpace(map.ColClave)
            || string.IsNullOrWhiteSpace(map.ColPrecio))
        {
            return RuleVerbResult.Ok(
                "El formulario de cotizacion (RFQ) no tiene mapeadas las columnas de clave/precio: nada que registrar.",
                0, autoCompleteStep: autoComplete);
        }

        // El RFQ se liga a su respuesta por FormResponseId (lo fijo la Ola B al sembrar el formulario). Si no hay
        // RFQ para esta respuesta, el formulario no es de cotizacion: se omite en silencio.
        var rfq = await _db.RequerimientoRfqs
            .Include(r => r.Lineas)
            .FirstOrDefaultAsync(r => r.FormResponseId == respId, cancellationToken);
        if (rfq is null)
        {
            return RuleVerbResult.Ok("La respuesta no corresponde a un RFQ: nada que registrar.", 0, autoCompleteStep: autoComplete);
        }

        // Filas de la grilla, indexadas por su clave tecnica oculta (primera gana).
        var rows = ParseRows(context.FormData.GetValueOrDefault(map.GridFieldCode!));
        var rowByClave = new Dictionary<string, Dictionary<string, string?>>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var clave = row.GetValueOrDefault(map.ColClave!);
            if (!string.IsNullOrWhiteSpace(clave)) { rowByClave.TryAdd(clave!, row); }
        }

        var now = DateTimeOffset.UtcNow;
        var respondidas = 0;   // lineas que trajeron alguna respuesta (precio)
        var ofertas = 0;       // ofertas volcadas al comparador
        DateTimeOffset? vigenciaMin = null;

        foreach (var linea in rfq.Lineas)
        {
            if (!rowByClave.TryGetValue(linea.ClaveProducto, out var row)) { continue; }

            var precio = TryNum(row.GetValueOrDefault(map.ColPrecio!), out var p) ? p : (decimal?)null;
            int? vigenciaDias = !string.IsNullOrWhiteSpace(map.ColVigencia)
                && int.TryParse((row.GetValueOrDefault(map.ColVigencia!) ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var vd)
                ? vd : null;
            var leadTime = !string.IsNullOrWhiteSpace(map.ColLeadTime) ? Trim(row.GetValueOrDefault(map.ColLeadTime!)) : null;
            var marca = !string.IsNullOrWhiteSpace(map.ColMarca) ? Trim(row.GetValueOrDefault(map.ColMarca!)) : null;
            var nota = !string.IsNullOrWhiteSpace(map.ColNota) ? Trim(row.GetValueOrDefault(map.ColNota!)) : null;

            // Persiste la respuesta en la linea del RFQ (aunque la fila venga sin precio: queda el resto).
            linea.PrecioUnitario = precio;
            linea.VigenciaDias = vigenciaDias;
            linea.LeadTime = leadTime;
            linea.Marca = marca;
            linea.Nota = nota;

            if (precio is not decimal precioFirme || precioFirme <= 0m) { continue; }
            respondidas++;

            DateTimeOffset? vigenciaHasta = vigenciaDias is int dias && dias > 0 ? now.AddDays(dias) : null;
            if (vigenciaHasta is DateTimeOffset vh && (vigenciaMin is null || vh < vigenciaMin)) { vigenciaMin = vh; }

            // Upsert al comparador por (producto, proveedor). El servicio deriva la MISMA clave a partir de
            // ItemId/Descripcion, asi que la oferta queda bajo el mismo producto que la linea.
            await _req.GuardarCotizacionAsync(
                new SaveCotizacionRequest(
                    Id: null,
                    ItemId: linea.ItemId,
                    Descripcion: linea.Descripcion,
                    ProveedorId: rfq.ProveedorId,
                    PrecioUnitario: precioFirme,
                    TiempoEntrega: leadTime,
                    CaracteristicasDespacho: null,
                    CondicionesPago: null,
                    VigenciaHasta: vigenciaHasta,
                    Notas: nota,
                    ProveedorContactoId: rfq.ProveedorContactoId,
                    FechaCotizacion: now,
                    Fuente: RequerimientoCotizacionFuente.Rfq),
                linea.Cantidad, cancellationToken);
            ofertas++;
        }

        // El proveedor contesto: el RFQ pasa a Respondida (comparar/adjudicar ocurre luego en planeacion).
        if (respondidas > 0)
        {
            rfq.Estado = RfqEstado.Respondida;
            rfq.FechaRespuesta = now;
            if (vigenciaMin is not null) { rfq.VigenciaHasta = vigenciaMin; }
        }
        // Persiste linea(s) y cabecera del RFQ (GuardarCotizacionAsync ya guardo las ofertas; este save cubre el
        // resto, incluso cuando no hubo ninguna oferta con precio).
        await _db.SaveChangesAsync(cancellationToken);

        var msg = respondidas == 0
            ? "El RFQ no trajo ninguna linea con precio."
            : $"{respondidas} linea(s) con respuesta; {ofertas} oferta(s) enviada(s) al comparador.";
        return RuleVerbResult.Ok(msg, ofertas, autoCompleteStep: autoComplete);
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static bool TryNum(string? s, out decimal n)
    {
        s = (s ?? "").Trim().Replace(",", ".");
        return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out n);
    }

    /// <summary>Parsea el valor de una grilla (arreglo JSON de objetos) a filas fieldCode->valor. Vacio/invalido =
    /// lista vacia. Calcado de CargarFilasDesdeGrillaVerb.ParseRows.</summary>
    private static List<Dictionary<string, string?>> ParseRows(string? gridValue)
    {
        var result = new List<Dictionary<string, string?>>();
        if (string.IsNullOrWhiteSpace(gridValue)) { return result; }
        try
        {
            using var doc = JsonDocument.Parse(gridValue);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) { return result; }
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) { continue; }
                var row = new Dictionary<string, string?>(StringComparer.Ordinal);
                foreach (var prop in el.EnumerateObject())
                {
                    row[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString(),
                        JsonValueKind.Number => prop.Value.GetRawText(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        JsonValueKind.Null => null,
                        _ => prop.Value.GetRawText()
                    };
                }
                result.Add(row);
            }
        }
        catch (JsonException) { /* invalido: lista vacia */ }
        return result;
    }
}
