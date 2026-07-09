using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Subcategoria (concepto) del catalogo de actividades (modulo 000270), nivel 2 de la
/// jerarquia Categoria -> Subcategoria (calca del legacy TIPO_TAR_N). Concentra los flags
/// de gestion del inicio del proceso (RQ07): inicia modulo, cierre manual, requiere cliente,
/// titulo y detalle automaticos. Vincula OPCIONALMENTE un flujo, un formulario y un tablero
/// con la columna que marca "terminado". TENANT-SCOPED. Unica por (TenantId, Codigo).
/// </summary>
public class ActividadSubcategoria : TenantEntity
{
    /// <summary>Categoria (nivel 1) a la que pertenece. Cascade: se borra con la categoria.</summary>
    public Guid CategoriaId { get; set; }
    public ActividadCategoria? Categoria { get; set; }

    /// <summary>Codigo legible unico por tenant (ej. "CAT-01-01").</summary>
    public string Codigo { get; set; } = null!;

    public string Nombre { get; set; } = null!;

    /// <summary>Lista de chequeo separada por ';' (ej. "Revisar sitio;Medir red;Cotizar").</summary>
    public string? Chequeo { get; set; }

    public string? Descripcion { get; set; }

    public int SortOrder { get; set; }

    public bool IsArchived { get; set; }

    // ---- Flags de gestion del inicio del proceso (RQ07 legacy) ----

    /// <summary>Al crear la tarea se debe asignar un cliente (FLAG_CLIENTE).</summary>
    public bool RequiereCliente { get; set; }

    /// <summary>Inicia un modulo/flujo al crear la tarea (FLAG_INICIA_MODULO). Habilita Titulo/Detalle auto.</summary>
    public bool IniciaModulo { get; set; }

    /// <summary>Muestra boton de cierre manual (FLAG_BOTON_CIERRE).</summary>
    public bool CierreManual { get; set; }

    /// <summary>Titulo automatico de la tarea (TITULO_AUTO), soporta tokens como @cliente.</summary>
    public string? TituloAuto { get; set; }

    /// <summary>Detalle predeterminado de la tarea (DETALLE_AUTO).</summary>
    public string? DetalleAuto { get; set; }

    /// <summary>Sedes/sucursales donde aplica el concepto (nombres libres, separados por ';').
    /// Calca del legacy TIPO_TAR_EMPRESA; no hay catalogo de sedes, por eso es texto libre.</summary>
    public string? Sedes { get; set; }

    // ---- Vinculos OPCIONALES a otros modulos (FK Restrict: no cascada) ----

    /// <summary>Flujo de proceso asociado (000291). NO ACTION.</summary>
    public Guid? WorkflowDefinitionId { get; set; }
    public WorkflowDefinition? WorkflowDefinition { get; set; }

    /// <summary>Formulario dinamico asociado (000131). NO ACTION.</summary>
    public Guid? FormDefinitionId { get; set; }
    public FormDefinition? FormDefinition { get; set; }

    /// <summary>Tablero que aplica a las tareas de este concepto. NO ACTION.</summary>
    public Guid? TaskBoardId { get; set; }
    public TaskBoard? TaskBoard { get; set; }

    /// <summary>Columna/estado del tablero que marca la tarea como "terminado". NO ACTION.</summary>
    public Guid? TaskBoardColumnId { get; set; }
    public TaskBoardColumn? TaskBoardColumn { get; set; }

    // ---- Relaciones M:N (tablas hijas Cascade) ----

    /// <summary>Cargos (OrgUnit Classifier=Cargo) con permiso sobre este concepto.</summary>
    public ICollection<ActividadSubcategoriaCargo> Cargos { get; set; } = new List<ActividadSubcategoriaCargo>();

    /// <summary>Terceros/clientes (000232) a los que aplica este concepto.</summary>
    public ICollection<ActividadSubcategoriaTercero> Terceros { get; set; } = new List<ActividadSubcategoriaTercero>();

    /// <summary>Usuarios del tenant que reciben notificacion cuando se crea una tarea de este concepto
    /// (calca del legacy TIPO_TAR_N).</summary>
    public ICollection<ActividadSubcategoriaNotificacion> Notificaciones { get; set; } = new List<ActividadSubcategoriaNotificacion>();
}
