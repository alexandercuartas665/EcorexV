using System;
using System.Text.Json;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;
using Xunit;

namespace Ecorex.Application.Tests;

// Round-trip de ContactWorkflowCallParams con los campos IA nuevos (ADR-0056). ContactWorkflowService
// serializa/deserializa el paso Llamada hacia/desde params_json con estas MISMAS opciones
// (JsonSerializerDefaults.Web); este test asegura que Modo/AgenteId/PromptExtra/Objetivo y la LISTA
// FormulariosPermitidos sobreviven el ida y vuelta, y que el modo CRM clasico sigue intacto.
public class ContactWorkflowCallParamsTests
{
    private static readonly JsonSerializerOptions Opts = new(JsonSerializerDefaults.Web);

    [Fact]
    public void IaCall_RoundTrips_WithNewFields_IncludingFormList()
    {
        var agente = Guid.NewGuid();
        var f1 = Guid.NewGuid();
        var f2 = Guid.NewGuid();
        var original = new ContactWorkflowCallParams(
            Modo: "ia",
            AgenteId: agente,
            PromptExtra: "Ofrece el plan Pro con 20% de descuento, tono cordial.",
            Objetivo: nameof(ContactCallObjetivo.LlenarFormulario),
            FormulariosPermitidos: new[] { f1, f2 });

        var json = JsonSerializer.Serialize(original, Opts);
        var back = JsonSerializer.Deserialize<ContactWorkflowCallParams>(json, Opts);

        Assert.NotNull(back);
        Assert.Equal("ia", back!.Modo);
        Assert.Equal(agente, back.AgenteId);
        Assert.Equal(original.PromptExtra, back.PromptExtra);
        Assert.Equal(nameof(ContactCallObjetivo.LlenarFormulario), back.Objetivo);
        Assert.NotNull(back.FormulariosPermitidos);
        Assert.Equal(new[] { f1, f2 }, back.FormulariosPermitidos!);
        // En modo IA no se usan los campos CRM.
        Assert.Null(back.Comercial);
        Assert.Null(back.Subcategoria);
    }

    [Fact]
    public void CrmCall_RoundTrips_Unchanged()
    {
        var comercial = Guid.NewGuid().ToString();
        var subcategoria = Guid.NewGuid().ToString();
        var original = new ContactWorkflowCallParams(
            Comercial: comercial,
            Prioridad: "Alta",
            Categoria: "Ventas",
            Subcategoria: subcategoria);

        var json = JsonSerializer.Serialize(original, Opts);
        var back = JsonSerializer.Deserialize<ContactWorkflowCallParams>(json, Opts);

        Assert.NotNull(back);
        Assert.Equal(comercial, back!.Comercial);
        Assert.Equal("Alta", back.Prioridad);
        Assert.Equal("Ventas", back.Categoria);
        Assert.Equal(subcategoria, back.Subcategoria);
        // El modo IA queda ausente en el CRM clasico (compatibilidad hacia atras).
        Assert.Null(back.Modo);
        Assert.Null(back.AgenteId);
        Assert.Null(back.FormulariosPermitidos);
    }
}
