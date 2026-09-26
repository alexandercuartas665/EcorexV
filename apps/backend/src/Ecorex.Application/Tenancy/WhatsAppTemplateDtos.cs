using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tenancy;

/// <summary>Una variable usada en la plantilla: token amigable + valor de ejemplo (lo exige Meta
/// para revisar). Al someter (en una fase futura), el servicio asignaria posiciones {{1}}..{{n}}
/// en orden de aparicion.</summary>
public sealed record WhatsAppTemplateVariable(string Token, string Example);

/// <summary>Un boton de la plantilla, IMPORTADO de Meta (solo lectura). <paramref name="Type"/> en
/// MAYUSCULAS (URL | QUICK_REPLY | PHONE_NUMBER | COPY_CODE | ...); <paramref name="HasUrlVariable"/> = boton
/// URL con sufijo variable {{1}} (dinamico por envio).</summary>
public sealed record WhatsAppTemplateButtonDto(
    string Type, string? Text, string? Url, string? PhoneNumber, bool HasUrlVariable);

/// <summary>Valor que el usuario escribe en el ENVIO DE PRUEBA para un boton URL dinamico: el indice del boton
/// en la plantilla y el sufijo (lo que reemplaza {{1}} en su URL).</summary>
public sealed record WhatsAppTestButtonValue(int Index, string Text);

/// <summary>Fila del grid + detalle de una plantilla HSM (todos los campos).</summary>
public sealed record WhatsAppTemplateDto(
    Guid Id,
    string Name,
    string Language,
    WhatsAppTemplateCategory Category,
    WhatsAppTemplateHeaderType? HeaderType,
    string? HeaderText,
    string? HeaderMediaUrl,
    string BodyText,
    string? FooterText,
    IReadOnlyList<WhatsAppTemplateVariable> Variables,
    IReadOnlyList<WhatsAppTemplateButtonDto> Buttons,
    WhatsAppProvider? Provider,
    Guid WhatsAppLineId,
    string? WhatsAppLineName,
    string? WabaId,
    WhatsAppTemplateStatus Status,
    string? ProviderTemplateId,
    string? RejectionReason,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? ReviewedAt,
    bool IsActive);

/// <summary>Alta/edicion de una plantilla HSM.</summary>
public sealed record SaveWhatsAppTemplateRequest(
    string Name,
    string Language,
    WhatsAppTemplateCategory Category,
    string BodyText,
    Guid WhatsAppLineId,
    WhatsAppTemplateHeaderType? HeaderType = null,
    string? HeaderText = null,
    string? HeaderMediaUrl = null,
    string? FooterText = null,
    IReadOnlyList<WhatsAppTemplateVariable>? Variables = null);

/// <summary>Definicion de una variable disponible en el editor (token amigable + ejemplo por
/// defecto). Sirve para armar la ayuda del editor.</summary>
public sealed record WhatsAppTemplateVariableDef(string Token, string Label, string Description, string DefaultExample);

/// <summary>Resultado de traer plantillas desde el proveedor (YCloud): cuantas se crearon, se
/// actualizaron, quedaron sin cambios y cuantas se omitieron, mas mensajes por plantilla.</summary>
public sealed record WhatsAppImportReport(
    int Imported, int Updated, int Unchanged, int Skipped, IReadOnlyList<string> Messages);

/// <summary>Catalogo de variables de sesion que el editor puede insertar.</summary>
public static class WhatsAppTemplateVariableCatalog
{
    public static readonly IReadOnlyList<WhatsAppTemplateVariableDef> All = new[]
    {
        new WhatsAppTemplateVariableDef("empresa", "Empresa", "Nombre de la empresa (tenant)", "SKY SYSTEM"),
        new WhatsAppTemplateVariableDef("asesor", "Asesor", "Asesor asignado de la sesion", "Laura Gomez"),
        new WhatsAppTemplateVariableDef("cliente", "Cliente", "Nombre del contacto / lead", "Juan Perez"),
        new WhatsAppTemplateVariableDef("proyecto", "Proyecto", "Proyecto o actividad relacionada", "Implementacion CRM"),
        new WhatsAppTemplateVariableDef("fecha", "Fecha", "Fecha tentativa", "15 de julio"),
        new WhatsAppTemplateVariableDef("codigo", "Codigo", "Codigo o consecutivo", "T00042"),
    };

    public static WhatsAppTemplateVariableDef? Find(string token)
        => All.FirstOrDefault(v => string.Equals(v.Token, token, StringComparison.OrdinalIgnoreCase));
}
