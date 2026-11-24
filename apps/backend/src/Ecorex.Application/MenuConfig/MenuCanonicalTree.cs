using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.MenuConfig;

/// <summary>Entrada del menu estandar del sistema (seccion, enlace rapido o elemento).</summary>
public sealed record CanonicalMenuEntry(
    MenuNodeKind Kind,
    string Name,
    string? Route,
    string? IconKey = null,
    string? LegacyCode = null,
    IReadOnlyList<CanonicalMenuEntry>? Children = null)
{
    public IReadOnlyList<CanonicalMenuEntry> Items => Children ?? Array.Empty<CanonicalMenuEntry>();
}

/// <summary>Elemento del menu estandar con el nombre de su seccion (para catalogos y permisos).</summary>
public sealed record CanonicalMenuItem(string Name, string Route, string? LegacyCode, string SectionName, string? SectionRoute);

/// <summary>
/// MENU ESTANDAR del sistema (vista "Completo"): FUENTE UNICA del arbol canonico. Antes vivia solo dentro
/// de DatabaseSeeder.EnsureDefaultMenuAsync, asi que el editor de menu no podia "cargar el estandar" en una
/// vista nueva y la matriz de Roles y permisos solo veia los modulos de la vista predeterminada del tenant
/// (los tenants con menu recortado o sembrado antes de un modulo nuevo no podian dar permisos sobre el).
/// Ahora lo usan: el alta de tenants (seeder), "Cargar menu estandar" del editor, el selector "Agregar
/// elemento" y el catalogo de permisos. Al agregar un modulo nuevo al producto, se agrega AQUI.
/// </summary>
public static class MenuCanonicalTree
{
    /// <summary>Los stubs modulo/... se marcan InDevelopment (metadata; no entran a permisos).</summary>
    public static MenuNodeState StateFor(string? route)
        => route is not null && route.StartsWith("modulo/", StringComparison.Ordinal)
            ? MenuNodeState.InDevelopment
            : MenuNodeState.Ready;

    private static CanonicalMenuEntry Quick(string name, string route, string icon)
        => new(MenuNodeKind.QuickLink, name, route, icon);

    private static CanonicalMenuEntry Section(string name, string slug, string icon, params CanonicalMenuEntry[] items)
        => new(MenuNodeKind.Section, name, slug, icon, null, items);

    private static CanonicalMenuEntry Item(string name, string route, string? legacyCode = null)
        => new(MenuNodeKind.Item, name, route, null, legacyCode);

    public static readonly IReadOnlyList<CanonicalMenuEntry> Roots = new List<CanonicalMenuEntry>
    {
        // ---- Quick links (antes de "Modulos") ----
        Quick("Inicio", "inicio", "home"),
        Quick("Anuncios", "anuncios", "megaphone"),

        // ADR-0038: se retiro "Mis pasos" (000637) y "Crear una actividad" (la creacion se unifico al
        // wizard). El grupo "Procesos" (categorias con flujo) lo despliega NavMenu por IsProcessGroup.
        // "Administrar actividades" (000636) vive en "Sistema - Actividades" (es el modulo de TABLEROS).
        Section("Mis Procesos", "misproc", "list",
            Item("Proyectos", "proyectos", "000042"),
            Item("Programar actividad", "programar-actividad", "000889")),

        Section("Negocio", "nego", "briefcase",
            Item("Directorio General", "directorio-general", "000232"),
            Item("Cargador de contactos", "cargador-contactos", "000740")),

        Section("Automatizacion", "auto", "automation",
            Item("Flujos del proceso", "flujos", "000291"),
            Item("Formularios", "formularios", "000131"),
            Item("Power BI Service", "modulo/power-bi-service", "000788")),

        // Modulos de automatizacion operativa.
        Section("Automatizaciones", "automatizaciones", "automation",
            Item("Conciliacion DIAN Compras", "conciliacion-dian", "001679"),
            Item("Buzones OTP", "extraccion-buzones", "000731")),

        // Modulo Requerimientos y Compras (Capa 10). Backfill para BD ya sembradas: EnsureRequerimientosCompraMenuAsync.
        Section("Compras", "compras", "cube",
            Item("Requerimientos de Compra", "requerimientos-compra", "001680"),
            Item("Configuracion de Requerimientos", "requerimientos-config", "001681")),

        // Los cinco catalogos de inventario (000556/000506/000502/000606/000498) viven en UN modulo con
        // tarjetas + modal; sus rutas individuales siguen existiendo pero salen del menu.
        Section("Sistema \u00b7 Inventarios", "inv", "cube",
            Item("Configuracion", "inventario-configuracion", "000556"),
            Item("Items de inventarios", "inventario-items", "000066")),

        Section("Sistema \u00b7 Actividades", "act", "check-square",
            // Tableros Kanban (crear/configurar) es un modulo DISTINTO de "Administrar actividades" (bandeja).
            Item("Tableros", "tableros", "000635"),
            Item("Administrar actividades", "actividades", "000636"),
            Item("Maquetas", "maqueta"),        // ADR-0180
            Item("Cerebros", "cerebros"),       // ADR-0200
            Item("Formacion", "formacion"),     // ADR-0202
            Item("Conceptos", "conceptos", "000270"),
            // Prioridades, Estados y Tipos de proyecto unificados en un solo modulo con CRUD.
            Item("Configuracion actividades", "actividad-configuracion", "000691")),

        Section("Sistema \u00b7 CRM", "syscrm", "users",
            Item("Conceptos actividades", "modulo/conceptos-actividades", "000125"),
            Item("Estados", "modulo/estados-crm", "000272"),
            Item("Perfiles cliente/prov", "modulo/perfiles-cliente-prov", "000166"),
            Item("Servicios o productos", "modulo/servicios-o-productos", "000249"),
            Item("Tipos de empresas", "modulo/tipos-de-empresas", "000231"),
            Item("Vendedores", "modulo/vendedores", "000124"),
            Item("Origen clientes", "modulo/origen-clientes", "000324"),
            Item("Grupos de actividades", "modulo/grupos-de-actividades", "000126")),

        Section("Sistema \u00b7 General", "gen", "gear",
            // "Configuracion de entidad" legacy = plan/cuenta del tenant -> "Mi cuenta".
            Item("Mi cuenta", "mi-cuenta", "000615"),
            Item("Configuracion de la entidad", "configuracion-entidad", "000616"),
            Item("Calendario operativo", "config-calendario"),   // ADR-0106
            Item("Extraccion de datos", "extraccion-datos", "000730"),
            // El marketplace de plantillas (ADR-0097) se abre como modal desde Flujos y Formularios.
            Item("Dependencias", "dependencias", "000850"),
            Item("Administrador de Menu", "configuracion-menu", "000194"),       // ADR-0030
            Item("Administracion de usuarios", "admin-usuarios", "000073"),      // ADR-0031
            Item("Roles y permisos", "roles-permisos", "000198"),                // ADR-0032
            Item("Contenedor de datos", "contenedor-datos", "000920"),
            // Backfill para BD ya sembradas: EnsurePlantillasDocumentoMenuItemAsync.
            Item("Plantillas de documento", "plantillas-documentos")),

        Section("Sistema \u00b7 Desarrollo", "dev", "gear",
            Item("Autocompletado formularios", "modulo/autocompletado-formularios", "000801"),
            Item("Modulos web", "modulos-web", "000109"),
            Item("Notificaciones", "modulo/notificaciones-config", "000288"),
            Item("Objetos del sistema", "modulo/objetos-del-sistema", "000137"),
            Item("Parametros XML", "modulo/parametros-xml", "000057"),
            Item("Reglas", "reglas", "000802"),
            Item("Reportes", "metricas", "000119"),
            Item("Servicios web", "modulo/servicios-web", "000053"),
            Item("SQL Admin", "sql-admin", "000077"),
            Item("Tipos de documentos \u00b7 Consecutivos", "modulo/consecutivos", "000136")),

        Section("Infraestructura IA", "ia", "robot",
            Item("Agentes", "agentes", "000867"),
            Item("Agentes Colmena", "agentes-colmena", "000868"),
            Item("Lineas WhatsApp", "lineas"),
            Item("Conversaciones", "conversaciones"),
            Item("Bitacora del agente", "bitacora-agente"),
            Item("Plantillas WhatsApp", "plantillas-whatsapp"),
            // Backfill para BD ya sembradas: EnsurePlantillasCorreoMenuItemAsync (v0.16.340).
            Item("Plantillas de correo", "plantillas-correo"),
            Item("Configuracion de voz", "config-voz")),

        Section("CRM (heredado)", "crm", "crm",
            Item("Asesores", "asesores"),
            Item("Automatizaciones", "automatizaciones"),
            Item("Lista negra", "lista-negra")),
    };

    /// <summary>Elementos (Kind Item) del menu estandar con su seccion, en orden de aparicion.</summary>
    public static IEnumerable<CanonicalMenuItem> AllItems()
    {
        foreach (var root in Roots.Where(r => r.Kind == MenuNodeKind.Section))
        {
            foreach (var item in root.Items.Where(i => i.Kind == MenuNodeKind.Item && !string.IsNullOrWhiteSpace(i.Route)))
            {
                yield return new CanonicalMenuItem(item.Name, item.Route!, item.LegacyCode, root.Name, root.Route);
            }
        }
    }

    /// <summary>
    /// Construye los nodos del menu estandar para una vista (ids nuevos, SortOrder global en orden de
    /// aparicion, todo visible). Es exactamente lo que siembra el alta de tenants.
    /// </summary>
    public static List<MenuNode> BuildNodes(Guid tenantId, Guid menuViewId)
    {
        var nodes = new List<MenuNode>();
        var order = 0;

        MenuNode Add(CanonicalMenuEntry e, Guid? parentId)
        {
            var node = new MenuNode
            {
                TenantId = tenantId,
                MenuViewId = menuViewId,
                ParentId = parentId,
                Kind = e.Kind,
                Name = e.Name,
                IconKey = e.IconKey,
                LegacyCode = e.LegacyCode,
                Route = e.Route,
                State = e.Kind == MenuNodeKind.Item ? StateFor(e.Route) : MenuNodeState.Ready,
                IsVisible = true,
                SortOrder = order++
            };
            nodes.Add(node);
            return node;
        }

        foreach (var root in Roots)
        {
            var parent = Add(root, null);
            foreach (var child in root.Items)
            {
                Add(child, parent.Id);
            }
        }
        return nodes;
    }
}
