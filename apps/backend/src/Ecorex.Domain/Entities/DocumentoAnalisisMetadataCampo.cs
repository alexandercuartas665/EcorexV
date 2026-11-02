using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Un campo de METADATA configurable del Analizador (self-serve por tenant). Define QUE dato debe extraer el
/// agente de cada paquete y, a la vez, las columnas que muestra el modal de "Procesados". La lista de campos
/// se inyecta en el prompt del agente (bloque CAMPOS_METADATA) y sus claves son las llaves del MetadataJson
/// que devuelve el agente por paquete.
///
/// Multi-tenant por el filtro global (TenantEntity). Borrado REAL (no soft-delete): es config, igual que
/// <see cref="DocumentoAnalisisAgentePrompt"/>, y asi el indice unico por (tenant, clave) no choca al recrear.
/// </summary>
public sealed class DocumentoAnalisisMetadataCampo : TenantEntity
{
    /// <summary>Clave tecnica del campo (snake/lower, unica por tenant). Es la llave en el MetadataJson del paquete.</summary>
    public string Clave { get; set; } = string.Empty;

    /// <summary>Etiqueta legible para la UI (encabezado de columna en el modal).</summary>
    public string Etiqueta { get; set; } = string.Empty;

    /// <summary>Tipo del dato: texto | numero | fecha | lista. Guia al agente para normalizar el valor.</summary>
    public string Tipo { get; set; } = "texto";

    /// <summary>Pista opcional para el agente (que significa el campo / donde buscarlo). Va al prompt.</summary>
    public string? Descripcion { get; set; }

    /// <summary>Orden de aparicion en el prompt y en el modal.</summary>
    public int Orden { get; set; }

    /// <summary>Si el campo esta activo (se inyecta al prompt y se muestra). Permite ocultar sin borrar.</summary>
    public bool Activo { get; set; } = true;
}
