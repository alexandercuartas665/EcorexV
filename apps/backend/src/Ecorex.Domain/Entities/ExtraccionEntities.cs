using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

// =====================================================================================================
// Modulo "Extraccion de datos" (Automatizaciones) - migracion del legacy NEWFRONT_web_scraping (000730).
// Configurador de un motor de scraping/RPA ("el dron"): cada extraccion (CODIGO B000x) define pasos
// ("paginas"), acciones (scripts JS), clientes (drones), apis, advertencias y seguimiento. Multi-tenant
// real (TenantEntity + filtro global) en vez de la columna SUCURSAL del legacy. Estados/tipos/condiciones
// se guardan como texto (fieles al legacy). LegacyReg conserva el REG (int identity) de origen para el ETL
// y futuros re-sync. Todas las tablas WEB_SCRAPING* -> estas entidades.
// =====================================================================================================

/// <summary>Cabecera de una extraccion (WEB_SCRAPING). CODIGO unico por tenant (B0003/B0004/B0005...).</summary>
public sealed class ExtraccionDefinicion : TenantEntity
{
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    /// <summary>ACTIVO / DESACTIVADO / ERROR.</summary>
    public string Estado { get; set; } = "ACTIVO";
    /// <summary>Ciclo/frecuencia (informativo en el legacy).</summary>
    public string? Ciclo { get; set; }
    public string? Url { get; set; }
    public string? Destino { get; set; }
    /// <summary>REG (identity) del legacy, para trazabilidad/re-sync.</summary>
    public int? LegacyReg { get; set; }

    public ICollection<ExtraccionPaso> Pasos { get; set; } = new List<ExtraccionPaso>();
    public ICollection<ExtraccionCliente> Clientes { get; set; } = new List<ExtraccionCliente>();
    public ICollection<ExtraccionApi> Apis { get; set; } = new List<ExtraccionApi>();
    public ICollection<ExtraccionSeguimiento> Seguimientos { get; set; } = new List<ExtraccionSeguimiento>();
}

/// <summary>Paso ("pagina") del guion (WEB_SCRAPING_R): una URL a visitar y sus banderas.</summary>
public sealed class ExtraccionPaso : TenantEntity
{
    public Guid DefinicionId { get; set; }
    public ExtraccionDefinicion? Definicion { get; set; }
    public int? LegacyReg { get; set; }
    /// <summary>Nombre del paso (TABLA en el legacy).</summary>
    public string NombrePaso { get; set; } = string.Empty;
    public string? UrlPaso { get; set; }
    public int Orden { get; set; }
    /// <summary>Espera de la URL (TIEMPO).</summary>
    public string? Tiempo { get; set; }
    public string? Inicia { get; set; }
    public string? Termina { get; set; }
    public string? Relevo { get; set; }
    /// <summary>SQL de exploracion/alimentacion del paso.</summary>
    public string? SqlExplora { get; set; }
    public bool FlagRepetir { get; set; }
    public bool FlagNoNavegar { get; set; }
    public bool FlagUrlToken { get; set; }

    public ICollection<ExtraccionAccion> Acciones { get; set; } = new List<ExtraccionAccion>();
    public ICollection<ExtraccionAdvertencia> Advertencias { get; set; } = new List<ExtraccionAdvertencia>();
}

/// <summary>Accion ("detalle") de un paso (WEB_SCRAPING_RS): el script JS y que hacer con el resultado.</summary>
public sealed class ExtraccionAccion : TenantEntity
{
    public Guid PasoId { get; set; }
    public ExtraccionPaso? Paso { get; set; }
    public int? LegacyReg { get; set; }
    public int? LegacyPedReg { get; set; }
    /// <summary>El JavaScript que se inyecta/ejecuta en la pagina (corazon del scraping).</summary>
    public string? Script { get; set; }
    /// <summary>Tipo de resultado: Tabla/TablaID/Variable/Api/WeBresponse/EjecutarSQL/Exploracion/Ensamblado/mouse/tramite/cerran dron.</summary>
    public string? Tipo { get; set; }
    /// <summary>Codigo del contenedor destino (GEN_DATAWARE / Contenedor de datos).</summary>
    public string? ContenedorCodigo { get; set; }
    public string? Espera { get; set; }
    public int Orden { get; set; }
    /// <summary>=, &gt;, &gt;=, &lt;=, &lt;&gt;</summary>
    public string? Condicion { get; set; }
    public string? Valor { get; set; }
    public string? PaginaDesde { get; set; }
    public string? PaginaHasta { get; set; }
    public string? Variable { get; set; }
    public string? Operacion { get; set; }
    public string? ApiNombre { get; set; }
    public string? SqlExplora { get; set; }
}

/// <summary>Advertencia de un paso (WEB_SCRAPING_RA).</summary>
public sealed class ExtraccionAdvertencia : TenantEntity
{
    public Guid PasoId { get; set; }
    public ExtraccionPaso? Paso { get; set; }
    public int? LegacyReg { get; set; }
    public string? Etiqueta { get; set; }
    /// <summary>Notificar / Detener.</summary>
    public string? Accion { get; set; }
}

/// <summary>Cliente/instancia del dron (WEB_SCRAPING_CLI): destino de notificacion + token de autenticacion.</summary>
public sealed class ExtraccionCliente : TenantEntity
{
    public Guid DefinicionId { get; set; }
    public ExtraccionDefinicion? Definicion { get; set; }
    public int? LegacyReg { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Correo { get; set; }
    public string? Slack { get; set; }
    /// <summary>Token del dron (credencial). Se conserva del legacy.</summary>
    public string? Token { get; set; }
    /// <summary>Activo/Inactivo/Reiniciar/Pausado/Actualizacion.</summary>
    public string? Estado { get; set; }
    public string? Referencia { get; set; }

    public ICollection<ExtraccionClienteVariable> Variables { get; set; } = new List<ExtraccionClienteVariable>();
}

/// <summary>Variable de un cliente (WEB_SCRAPING_RAV): credenciales. El valor viene CIFRADO con el esquema
/// legacy (AdmCrypto + token del cliente) y no es descifrable aqui: se guarda opaco para referencia y debe
/// re-ingresarse/re-keyearse en el sistema nuevo.</summary>
public sealed class ExtraccionClienteVariable : TenantEntity
{
    public Guid ClienteId { get; set; }
    public ExtraccionCliente? Cliente { get; set; }
    public int? LegacyReg { get; set; }
    public string Nombre { get; set; } = string.Empty;
    /// <summary>Valor cifrado del legacy (opaco, no usable hasta re-keying).</summary>
    public string? ValorLegacyCifrado { get; set; }
}

/// <summary>API configurable de una extraccion (WEB_SCRAPING_API).</summary>
public sealed class ExtraccionApi : TenantEntity
{
    public Guid DefinicionId { get; set; }
    public ExtraccionDefinicion? Definicion { get; set; }
    public int? LegacyReg { get; set; }
    public string Nombre { get; set; } = string.Empty;
    /// <summary>Modelo/definicion de la API (XML en el legacy).</summary>
    public string? XmlConfig { get; set; }

    public ICollection<ExtraccionApiVariable> Variables { get; set; } = new List<ExtraccionApiVariable>();
}

/// <summary>Variable de una API (WEB_SCRAPING_APIAV).</summary>
public sealed class ExtraccionApiVariable : TenantEntity
{
    public Guid ApiId { get; set; }
    public ExtraccionApi? Api { get; set; }
    public int? LegacyReg { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Valor { get; set; }
}

/// <summary>Seguimiento/observacion de una extraccion (WEB_SCRAPING_SEGUIMIENTO).</summary>
public sealed class ExtraccionSeguimiento : TenantEntity
{
    public Guid DefinicionId { get; set; }
    public ExtraccionDefinicion? Definicion { get; set; }
    public int? LegacyReg { get; set; }
    public string? Seguimiento { get; set; }
    /// <summary>Activo/Perdedor/Espera.</summary>
    public string? Estado { get; set; }
}
