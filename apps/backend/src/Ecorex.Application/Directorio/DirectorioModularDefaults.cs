using Ecorex.Domain.Enums;

namespace Ecorex.Application.Directorio;

/// <summary>
/// Datos por defecto del 2do motor de contactos ("Directorio Modular", Capa 8): las 5 categorias, las
/// 6 secciones y sus campos, portados del prototipo (schema.js de 020. Soldarco/09.Directorio modular).
/// Es SOLO data (el seeder los materializa). Convencion clave (opcion A, separacion de espacios): las
/// SECCIONES viven en TerceroFichaDefinition con clave prefijada <see cref="SeccionPrefix"/> para no
/// colisionar con las fichas del motor Clasico ni aparecer en su configurador. Las CATEGORIAS viven en
/// su propia tabla (DirectorioCategoria), sin colision. Solo ASCII.
/// </summary>
public static class DirectorioModularDefaults
{
    /// <summary>Prefijo de las claves de seccion del motor Modular (separacion del Clasico).</summary>
    public const string SeccionPrefix = "mod_";

    /// <summary>Seccion: clave (sin prefijo), titulo, icono, color, aplicaA (null=ambas), areas CSV, protegida, descripcion.</summary>
    public sealed record SeccionDef(string Key, string Title, string Icono, string Color, string? AplicaA, string Areas, bool Protegida, string Descripcion);

    /// <summary>Campo: seccion, clave, etiqueta, tipo, ancho(1/2/3), opciones (linea por opcion), requeridoEn, soloLectura, filtrable, descripcion.</summary>
    public sealed record CampoDef(string Seccion, string Key, string Label, TerceroFieldType Type, int Column,
        string? Options = null, string? RequeridoEn = null, bool ReadOnly = false, bool ShowInFilter = false, string? Descripcion = null);

    /// <summary>Categoria: clave, titulo, icono, color, areas CSV, protegido, homologaSeccion (sin prefijo), secciones (sin prefijo, en orden).</summary>
    public sealed record CategoriaDef(string Key, string Title, string Icono, string Color, string Areas,
        bool Protegido, string? HomologaSeccion, string[] Secciones);

    private static string O(params string[] opts) => string.Join("\n", opts);

    public static readonly SeccionDef[] Secciones =
    [
        new("publica",    "Seccion publica",         "fa-globe",                "#4f46e5", null,       "admin,comercial,contabilidad,logistica", true,  "Datos minimos de identificacion. Visible para todas las areas. Se puede activar o desactivar en cada categoria."),
        new("comercial",  "Seccion comercial",       "fa-chart-line",           "#0ea5e9", null,       "comercial", false, "Perfilamiento y seguimiento comercial del tercero."),
        new("tributaria", "Seccion tributaria",      "fa-file-invoice-dollar",  "#f59e0b", null,       "contabilidad", false, "Datos del RUT. La numeracion corresponde a las casillas del formato."),
        new("cliente",    "Condiciones de cliente",  "fa-user-tie",             "#10b981", null,       "comercial,contabilidad", false, "Condiciones comerciales pactadas con el cliente."),
        new("proveedor",  "Condiciones de proveedor","fa-truck",                "#8b5cf6", null,       "logistica,contabilidad", false, "Condiciones de abastecimiento y pago al proveedor."),
        new("empleado",   "Datos laborales",         "fa-id-badge",             "#ef4444", "contacto", "admin", false, "Vinculacion laboral de la persona con la empresa."),
    ];

    public static readonly CategoriaDef[] Categorias =
    [
        new("publico",     "Publico",     "fa-globe",                "#4f46e5", "admin,comercial,contabilidad,logistica", true,  null,      ["publica"]),
        new("comercial",   "Comercial",   "fa-handshake",            "#10b981", "comercial",              false, null,      ["publica", "comercial", "cliente"]),
        new("proveedores", "Proveedores", "fa-truck",                "#8b5cf6", "logistica,contabilidad", false, null,      ["publica", "proveedor"]),
        new("laboral",     "Laboral",     "fa-id-badge",             "#ef4444", "admin",                  false, null,      ["publica", "empleado"]),
        new("fiscal",      "Fiscal",      "fa-file-invoice-dollar",  "#f59e0b", "contabilidad",           false, "publica", ["tributaria"]),
    ];

    // Catalogos grandes (ciudades, CIIU, responsabilidades DIAN, vendedores, bancos...) quedan como Select
    // SIN opciones sembradas: se cablean con los catalogos reales en un paso posterior (el prototipo trae
    // solo un subconjunto). Los Select con opciones INLINE del prototipo si se siembran completos.
    private const TerceroFieldType T = TerceroFieldType.Text;

    public static readonly CampoDef[] Campos =
    [
        // ---- publica ----
        new("publica", "codigo",            "Codigo",             T, 1, ReadOnly: true, Descripcion: "Consecutivo automatico"),
        new("publica", "ide",               "IDE",                T, 1, ShowInFilter: true, Descripcion: "Identificacion tributaria o documento"),
        new("publica", "nombre_empresa",    "Nombre empresa",     T, 2, RequeridoEn: "empresa"),
        new("publica", "telefono_empresa",  "Telefono empresa",   TerceroFieldType.Phone, 1),
        new("publica", "contacto",          "Contacto",           T, 2, RequeridoEn: "contacto"),
        new("publica", "telefono_contacto", "Telefono contacto",  TerceroFieldType.Phone, 1),
        new("publica", "cargo",             "Cargo",              T, 1),
        new("publica", "correo",            "Correo electronico", TerceroFieldType.Email, 2, ShowInFilter: true),
        new("publica", "ciudad",            "Ciudad",             TerceroFieldType.Select, 1, ShowInFilter: true),
        new("publica", "pais",              "Pais",               TerceroFieldType.Select, 1),
        new("publica", "fecha_creacion",    "Fecha de creacion",  TerceroFieldType.Date, 1, ReadOnly: true),
        new("publica", "usuario_creador",   "Usuario",            T, 1, ReadOnly: true),

        // ---- comercial ----
        new("comercial", "estado_ciclo_vida",     "Estado del ciclo de vida",  TerceroFieldType.Select, 1, O("Prospecto","Oportunidad","Cliente activo","Cliente inactivo","Perdido"), ShowInFilter: true),
        new("comercial", "atencion_comercial",    "Atencion comercial",        TerceroFieldType.Select, 1, O("Directa","Telefonica","Virtual","Mixta")),
        new("comercial", "calificacion_comercial","Calificacion comercial",    TerceroFieldType.Select, 1, O("A","B","C","D"), ShowInFilter: true),
        new("comercial", "frecuencia_compra",     "Frecuencia de compra",      TerceroFieldType.Select, 1, O("Semanal","Quincenal","Mensual","Trimestral","Esporadica")),
        new("comercial", "fidelizacion",          "Fidelizacion",              TerceroFieldType.Select, 1, O("Alta","Media","Baja","Sin fidelizar")),
        new("comercial", "sector_ciiu",           "Sector economico CIIU",     TerceroFieldType.Select, 2),
        new("comercial", "segmento",              "Segmento",                  TerceroFieldType.Select, 1, ShowInFilter: true),
        new("comercial", "subsegmento",           "Subsegmento",               TerceroFieldType.Select, 1),
        new("comercial", "origen_cliente",        "Origen del cliente",        TerceroFieldType.Select, 1, O("Referido","Sitio web","Feria","Llamada en frio","Redes sociales","Licitacion")),
        new("comercial", "canal_contacto",        "Canal de contacto",         TerceroFieldType.Select, 1, O("WhatsApp","Correo","Telefono","Visita","Sitio web")),
        new("comercial", "volumen_compra",        "Volumen de compra",         TerceroFieldType.Currency, 1),
        new("comercial", "antiguedad_relacion",   "Antiguedad en la relacion", TerceroFieldType.Select, 2, O("Menos de 1 ano","Entre 1 y 3 anos","Entre 3 y 5 anos","Mas de 5 anos")),
        new("comercial", "zona_comercial",        "Zona comercial",            TerceroFieldType.Select, 1, O("Norte","Sur","Centro","Oriente","Occidente"), ShowInFilter: true),
        new("comercial", "nivel_organizacion",    "Nivel de organizacion",     TerceroFieldType.Select, 1, O("Microempresa","Pequena","Mediana","Grande","Corporativa")),
        new("comercial", "comercial_responsable", "Comercial responsable",     TerceroFieldType.Select, 1, ShowInFilter: true),
        new("comercial", "fecha_ultimo_contacto", "Fecha ultimo contacto",     TerceroFieldType.Date, 1),
        new("comercial", "lista_precios",         "Lista de precios",          TerceroFieldType.Select, 1, O("General","Mayorista","Distribuidor","Preferencial")),

        // ---- tributaria (RUT: numeracion por casilla) ----
        new("tributaria", "nit",                          "5 - Numero de identificacion tributaria", T, 2),
        new("tributaria", "dv",                           "6 - DV",                                  T, 1),
        new("tributaria", "tipo_contribuyente",           "24 - Tipo de contribuyente",              TerceroFieldType.Select, 1, O("Persona juridica","Persona natural")),
        new("tributaria", "tipo_documento",               "25 - Tipo de documento",                  TerceroFieldType.Select, 1, O("NIT","Cedula de ciudadania","Cedula de extranjeria","Pasaporte","NIT de otro pais")),
        new("tributaria", "numero_identificacion",        "26 - Numero de identificacion",           T, 1),
        new("tributaria", "primer_apellido",              "31 - Primer apellido",                    T, 1),
        new("tributaria", "segundo_apellido",             "32 - Segundo apellido",                   T, 1),
        new("tributaria", "primer_nombre",                "33 - Primer nombre",                      T, 1),
        new("tributaria", "otros_nombres",                "34 - Otros nombres",                      T, 1),
        new("tributaria", "razon_social",                 "35 - Razon social",                       T, 2),
        new("tributaria", "nombre_comercial",             "36 - Nombre comercial",                   T, 2),
        new("tributaria", "sigla",                        "37 - Sigla",                              T, 1),
        new("tributaria", "pais_rut",                     "38 - Pais",                               TerceroFieldType.Select, 1),
        new("tributaria", "departamento",                 "39 - Departamento",                       TerceroFieldType.Select, 1),
        new("tributaria", "ciudad_rut",                   "40 - Ciudad / Municipio",                 TerceroFieldType.Select, 1),
        new("tributaria", "direccion_principal",          "41 - Direccion principal",                T, 3),
        new("tributaria", "correo_rut",                   "42 - Correo electronico",                 TerceroFieldType.Email, 2),
        new("tributaria", "codigo_postal",                "43 - Codigo postal",                      T, 1),
        new("tributaria", "telefono_1",                   "44 - Telefono 1",                         TerceroFieldType.Phone, 1),
        new("tributaria", "telefono_2",                   "45 - Telefono 2",                         TerceroFieldType.Phone, 1),
        new("tributaria", "codigo_actividad",             "46 - Codigo actividad",                   TerceroFieldType.Select, 2),
        new("tributaria", "fecha_inicio_actividad",       "47 - Fecha de inicio",                    TerceroFieldType.Date, 1),
        new("tributaria", "codigo_actividad_secundaria",  "48 - Codigo actividad secundaria",        TerceroFieldType.Select, 2),
        new("tributaria", "fecha_inicio_secundaria",      "49 - Fecha de inicio",                    TerceroFieldType.Date, 1),
        new("tributaria", "responsabilidades",            "53 - Responsabilidades, calidades y atributos", TerceroFieldType.Table, 3,
            Options: """
            {"columns":[
              {"key":"codigo","label":"Codigo","type":"select","autollena":"descripcion","options":[
                {"value":"O-13","label":"Gran contribuyente"},
                {"value":"O-15","label":"Autorretenedor"},
                {"value":"O-23","label":"Agente de retencion en la fuente a titulo de renta"},
                {"value":"O-47","label":"Regimen simple de tributacion - SIMPLE"},
                {"value":"O-48","label":"Impuesto sobre las ventas - IVA"},
                {"value":"O-49","label":"No responsable de IVA"},
                {"value":"O-14","label":"Informante de exogena"},
                {"value":"O-16","label":"Obligacion de facturar por ingresos de bienes y/o servicios"}
              ]},
              {"key":"descripcion","label":"Responsabilidad, calidad o atributo","type":"texto","readonly":true}
            ]}
            """),

        // ---- cliente ----
        new("cliente", "cupo_credito",          "Cupo de credito",             TerceroFieldType.Currency, 1),
        new("cliente", "plazo_pago",            "Plazo de pago",               TerceroFieldType.Select, 1, O("Contado","15 dias","30 dias","45 dias","60 dias","90 dias")),
        new("cliente", "forma_pago",            "Forma de pago",               TerceroFieldType.Select, 1, O("Efectivo","Transferencia","Cheque","Tarjeta de credito")),
        new("cliente", "aplica_retencion",      "Se le practica retencion",    TerceroFieldType.Checkbox, 1),
        new("cliente", "observaciones_cliente", "Observaciones",               TerceroFieldType.TextArea, 3),

        // ---- proveedor ----
        new("proveedor", "tipo_proveedor",      "Tipo de proveedor",              TerceroFieldType.Select, 1, O("Bienes","Servicios","Mixto")),
        new("proveedor", "plazo_entrega",       "Plazo de entrega (dias)",        TerceroFieldType.Number, 1),
        new("proveedor", "banco",               "Banco",                          TerceroFieldType.Select, 1),
        new("proveedor", "cuenta_bancaria",     "Cuenta bancaria",                T, 2),
        new("proveedor", "certificado_calidad", "Cuenta con certificacion de calidad", TerceroFieldType.Checkbox, 1),

        // ---- empleado ----
        new("empleado", "cargo_empleado", "Cargo",            T, 2),
        new("empleado", "area_empleado",  "Area",             TerceroFieldType.Select, 1, O("Administracion","Comercial","Contabilidad","Logistica")),
        new("empleado", "fecha_ingreso",  "Fecha de ingreso", TerceroFieldType.Date, 1),
        new("empleado", "tipo_contrato",  "Tipo de contrato", TerceroFieldType.Select, 1, O("Indefinido","Termino fijo","Obra o labor","Prestacion de servicios")),
    ];

    /// <summary>Clave de seccion con prefijo Modular (la que se guarda en TerceroFichaDefinition.FichaKey).</summary>
    public static string SeccionKey(string key) => SeccionPrefix + key;
}
