using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Plantilla de DOCUMENTO (HTML enriquecido) que un usuario usa para redactar un documento dentro de una
/// tarea. El HTML admite tokens de contexto de UN namespace.clave estilo <see cref="Workflows"/> de
/// notificacion: {tarea.contacto}, {tarea.numero}, {form.codigo}, {sistema.fecha}, etc. (los resuelve
/// INotifyTokenResolver con los datos de la tarea/contacto/formularios). Al usarla, se resuelven los tokens
/// y el resultado se edita en el editor rico; luego se guarda como Documento versionado (Gestor Documental).
/// Entidad TENANT-SCOPED. Pertenece a un <see cref="DocumentTemplateGroup"/>.
/// </summary>
public class DocumentTemplate : TenantEntity
{
    /// <summary>Grupo/categoria al que pertenece la plantilla.</summary>
    public Guid GroupId { get; set; }
    public DocumentTemplateGroup? Group { get; set; }

    /// <summary>Nombre visible de la plantilla (ej. "Carta de cobro estandar").</summary>
    public string Name { get; set; } = null!;

    /// <summary>Contenido HTML de la plantilla, con tokens {ns.clave} ({tarea.x} / {form.x} / {sistema.x}).</summary>
    public string HtmlContent { get; set; } = "";

    /// <summary>Plantilla activa (las inactivas no se ofrecen al redactar, pero se conservan).</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Orden de aparicion dentro del grupo.</summary>
    public int SortOrder { get; set; }
}
