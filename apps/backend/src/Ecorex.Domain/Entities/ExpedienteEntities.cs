using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

// ===========================================================================
// Gestor Documental - mitad "Expedientes" (Tabla de Retencion Documental, TRD).
// Portado del modulo 2.15 del hermano PROPIA. Es INDEPENDIENTE del Archivo central:
// alli se guarda "lo que hay", aqui se define "lo que TIENE que haber" y se comprueba.
//
// Dos planos:
//   1. CONFIGURACION (la TRD):  Serie -> Subserie -> Tipologias esperadas + Campos de metadato.
//   2. INSTANCIAS (expedientes): al abrir un expediente desde una subserie se COPIAN sus
//      tipologias y campos. La copia es deliberada: si manana se edita la TRD, los expedientes
//      ya abiertos no deben cambiar de checklist a mitad de camino.
//
// El legacy de ECOREX ya tiene este concepto (familias DOC_SERIES*, DOC_TRDPROCESOS y
// DOC_ARCHIVO_CENTRAL* del inventario del vault), asi que el ETL futuro tiene destino.
// ===========================================================================

/// <summary>Serie documental de la TRD (ej. "Actas y gobierno"). TENANT-SCOPED.</summary>
public class SerieDocumental : TenantEntity
{
    public string Nombre { get; set; } = null!;
    public int Orden { get; set; }

    /// <summary>Soft-delete (regla 5).</summary>
    public bool Activa { get; set; } = true;

    public ICollection<SubserieDocumental> Subseries { get; set; } = new List<SubserieDocumental>();
}

/// <summary>
/// Subserie de una serie (ej. "Actas de Asamblea"). Es la PLANTILLA del expediente: define que
/// documentos se esperan y que metadatos se piden. TENANT-SCOPED.
/// </summary>
public class SubserieDocumental : TenantEntity
{
    public Guid SerieId { get; set; }
    public SerieDocumental? Serie { get; set; }

    public string Nombre { get; set; } = null!;
    public int Orden { get; set; }
    public bool Activa { get; set; } = true;

    public ICollection<SubserieTipologia> Tipologias { get; set; } = new List<SubserieTipologia>();
    public ICollection<SubserieCampo> Campos { get; set; } = new List<SubserieCampo>();
}

/// <summary>Documento esperado dentro de la subserie (ej. "Convocatoria"). TENANT-SCOPED.</summary>
public class SubserieTipologia : TenantEntity
{
    public Guid SubserieId { get; set; }
    public SubserieDocumental? Subserie { get; set; }

    public string Nombre { get; set; } = null!;

    /// <summary>Si es obligatoria, el expediente no se considera completo sin ella.</summary>
    public bool Obligatoria { get; set; }

    public int Orden { get; set; }
}

/// <summary>Metadato que se pedira al cargar cada documento de la subserie. TENANT-SCOPED.</summary>
public class SubserieCampo : TenantEntity
{
    public Guid SubserieId { get; set; }
    public SubserieDocumental? Subserie { get; set; }

    /// <summary>Clave tecnica (unica dentro de la subserie); es la que viaja en el JSON de metadatos.</summary>
    public string Clave { get; set; } = null!;

    /// <summary>Texto que ve la persona.</summary>
    public string Label { get; set; } = null!;

    public int Orden { get; set; }
}

/// <summary>
/// Expediente: la INSTANCIA abierta a partir de una subserie. Serie y Subserie se guardan como
/// texto (snapshot) a proposito: el expediente debe seguir diciendo bajo que TRD nacio aunque
/// despues se renombre o se retire la subserie. TENANT-SCOPED.
/// </summary>
public class Expediente : TenantEntity
{
    /// <summary>Consecutivo legible del expediente. Unico por tenant.</summary>
    public string Codigo { get; set; } = null!;

    public string Nombre { get; set; } = null!;

    /// <summary>Snapshot del nombre de la serie al momento de abrirlo.</summary>
    public string Serie { get; set; } = null!;

    /// <summary>Snapshot del nombre de la subserie al momento de abrirlo.</summary>
    public string Subserie { get; set; } = null!;

    /// <summary>Subserie de la que nacio. Null si la subserie fue borrada despues.</summary>
    public Guid? SubserieId { get; set; }

    /// <summary>Soft-delete.</summary>
    public bool Activo { get; set; } = true;

    public ICollection<ExpedienteTipologia> Tipologias { get; set; } = new List<ExpedienteTipologia>();
    public ICollection<ExpedienteCampo> Campos { get; set; } = new List<ExpedienteCampo>();
}

/// <summary>
/// Una casilla del checklist del expediente: el documento esperado y, si ya se cargo, su archivo
/// y sus metadatos. TENANT-SCOPED.
/// </summary>
public class ExpedienteTipologia : TenantEntity
{
    public Guid ExpedienteId { get; set; }
    public Expediente? Expediente { get; set; }

    public string Nombre { get; set; } = null!;
    public bool Obligatoria { get; set; }
    public int Orden { get; set; }

    /// <summary>Ruta publica del archivo cargado. Null = casilla pendiente.</summary>
    public string? ArchivoUrl { get; set; }
    public string? ArchivoNombre { get; set; }
    public string? ArchivoMime { get; set; }
    public long ArchivoTamano { get; set; }

    /// <summary>SHA-256 del archivo cargado, igual que en las versiones del Archivo central.</summary>
    public string? ArchivoHashSha256 { get; set; }

    /// <summary>Metadatos capturados al cargar: JSON dict claveCampo -> valor.</summary>
    public string? MetaJson { get; set; }
}

/// <summary>
/// Campo de metadato del expediente, copiado de la subserie al abrirlo y editable por expediente.
/// TENANT-SCOPED.
/// </summary>
public class ExpedienteCampo : TenantEntity
{
    public Guid ExpedienteId { get; set; }
    public Expediente? Expediente { get; set; }

    public string Clave { get; set; } = null!;
    public string Label { get; set; } = null!;
    public int Orden { get; set; }
}
