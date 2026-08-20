using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Plantilla de CORREO del motor de acciones por filtro de contactos (ADR-0056, paso E-mail).
/// Entidad TENANT-SCOPED. <see cref="Asunto"/> y <see cref="CuerpoHtml"/> admiten variables de merge
/// {nombre}, {empresa}, {cargo}, {ciudad}, {correo} que se reemplazan con los datos del contacto
/// (Tercero) al enviar; el cuerpo escapa las variables (HTML) para evitar inyeccion.
/// </summary>
public class EmailTemplate : TenantEntity
{
    /// <summary>Nombre visible de la plantilla (ej. "Bienvenida prospectos").</summary>
    public string Nombre { get; set; } = null!;

    /// <summary>Asunto del correo (texto plano; admite variables de merge).</summary>
    public string Asunto { get; set; } = "";

    /// <summary>Cuerpo del correo en HTML (admite variables de merge; las variables se escapan).</summary>
    public string CuerpoHtml { get; set; } = "";

    /// <summary>Solo las plantillas activas se ofrecen en el disenador y las usa el motor.</summary>
    public bool Activa { get; set; } = true;
}
