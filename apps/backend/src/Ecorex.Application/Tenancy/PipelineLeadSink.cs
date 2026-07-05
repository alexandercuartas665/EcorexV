using System.Globalization;
using System.Text;

namespace Ecorex.Application.Tenancy;

/// <summary>
/// Adaptador CRM del cierre comercial (implementacion viva de <see cref="IAgentLeadSink"/>). Aqui, y SOLO
/// aqui, vive el acoplamiento del cierre del agente con el dominio Lead/BusinessUnit/pipeline: mapea el canal
/// (tipo de cliente) a la unidad de negocio correcta (por nombre) y crea el lead al inicio del embudo,
/// preservando el comportamiento historico que antes vivia dentro de PipelineToolset.
/// </summary>
public sealed class PipelineLeadSink : IAgentLeadSink
{
    private readonly ILeadService _leads;
    private readonly IBusinessUnitService _units;

    public PipelineLeadSink(ILeadService leads, IBusinessUnitService units)
    {
        _leads = leads;
        _units = units;
    }

    public async Task<AgentLeadResult> CreateLeadAsync(AgentLeadRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var nombre = request.ContactName?.Trim();
        if (string.IsNullOrWhiteSpace(nombre)) { return AgentLeadResult.Failure("Falta el nombre del cliente (cliente_nombre)."); }

        var telefono = string.IsNullOrWhiteSpace(request.ContactPhone) ? null : request.ContactPhone!.Trim();
        var resumen = string.IsNullOrWhiteSpace(request.Summary) ? null : request.Summary!.Trim();

        var unit = await ResolveUnitAsync(request.ChannelHint ?? "", cancellationToken);
        var req = new CreateLeadRequest(nombre!, telefono, resumen, request.EstimatedValue,
            request.Currency ?? "COP", StageId: null, BusinessUnitId: unit?.Id);
        var lead = await _leads.CreateAsync(req, actorUserId, cancellationToken);
        if (lead is null) { return AgentLeadResult.Failure("No se pudo crear el lead. Verifica que el pipeline tenga al menos una etapa."); }

        // Nota con el detalle capturado, para el asesor que retome el lead.
        if (!string.IsNullOrWhiteSpace(resumen))
        {
            try { await _leads.AddNoteAsync(lead.Id, $"[Agente IA] {resumen}", "yellow", actorUserId, cancellationToken); }
            catch { /* la nota es complementaria: si falla, el lead ya quedo creado */ }
        }

        return AgentLeadResult.Success(lead.Id, unit?.Name ?? "(sin unidad)",
            "Lead registrado en el pipeline. Un asesor lo contactara para continuar.");
    }

    // Resuelve la unidad de negocio a partir del canal/tipo de cliente que indico el agente.
    private async Task<BusinessUnitDto?> ResolveUnitAsync(string tipo, CancellationToken ct)
    {
        var units = await _units.ListAsync(includeInactive: false, ct);
        if (units.Count == 0) { return null; }
        var t = Normalize(tipo);
        bool NameHas(BusinessUnitDto u, params string[] keys) => keys.Any(k => Normalize(u.Name).Contains(k));

        if (t.Contains("b2b") || t.Contains("empresa") || t.Contains("mayor") || t.Contains("suministro") || t.Contains("negocio"))
        {
            return units.FirstOrDefault(u => NameHas(u, "b2b", "empresa"));
        }
        if (t.Contains("curso") || t.Contains("formacion") || t.Contains("capacit"))
        {
            return units.FirstOrDefault(u => NameHas(u, "curso"));
        }
        // Producto al detal (uso personal): por defecto para cualquier mencion de producto.
        if (t.Contains("producto") || t.Contains("detal") || t.Contains("retail") || t.Contains("personal"))
        {
            return units.FirstOrDefault(u => NameHas(u, "detal")) ?? units.FirstOrDefault(u => NameHas(u, "producto"));
        }
        return null;
    }

    private static string Normalize(string s)
    {
        var n = (s ?? string.Empty).Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in n)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) { sb.Append(c); }
        }
        return sb.ToString();
    }
}
