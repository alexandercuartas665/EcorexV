using System.Diagnostics;
using Ecorex.Application.Admin;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Infrastructure.Sql;

/// <summary>
/// Consola SQL admin (000077). Ejecuta SQL crudo usando el DbConnection del EcorexDbContext
/// (el proveedor activo lo resuelve la DI: Postgres en prod, SQL Server en dev dual) y registra
/// TODO en sql_console_logs. Se permiten DML y DDL sin restricciones por requerimiento del dueno
/// del producto; la unica defensa es la AUDITORIA: cada query queda guardada con usuario, tenant
/// y resultado. El acceso lo gobierna la policy Perm:sql-admin:View (Owner/Admin por gobierno).
/// </summary>
public sealed class SqlConsoleService(EcorexDbContext db, ITenantContext tenant) : ISqlConsoleService
{
    // Timeout de la query en segundos (una consulta pesada no debe colgar el circuito Blazor).
    private const int QueryTimeoutSeconds = 30;

    public async Task<SqlConsoleExecutionDto> EjecutarAsync(
        string sql, Guid actorUserId, string? actorUserName,
        int rowLimit = 1000, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return new SqlConsoleExecutionDto(false, "EMPTY", Array.Empty<string>(),
                Array.Empty<IReadOnlyList<string?>>(), null, null, 0, "Query vacia.");
        }

        var queryType = DetectarTipo(sql);
        var sw = Stopwatch.StartNew();
        string? errorMessage = null;
        int? rowsAffected = null;
        int? rowsReturned = null;
        var columnas = new List<string>();
        var filas = new List<IReadOnlyList<string?>>();
        var success = false;

        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
            {
                await conn.OpenAsync(ct);
            }

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = QueryTimeoutSeconds;

            if (queryType == "SELECT")
            {
                // Lectura: capturamos columnas y filas hasta rowLimit.
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    columnas.Add(reader.GetName(i));
                }
                var count = 0;
                while (await reader.ReadAsync(ct))
                {
                    if (count >= rowLimit) { break; }
                    var fila = new string?[reader.FieldCount];
                    for (var i = 0; i < reader.FieldCount; i++)
                    {
                        fila[i] = reader.IsDBNull(i) ? null : Convert.ToString(reader.GetValue(i));
                    }
                    filas.Add(fila);
                    count++;
                }
                rowsReturned = count;
                success = true;
            }
            else
            {
                rowsAffected = await cmd.ExecuteNonQueryAsync(ct);
                success = true;
            }
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            success = false;
        }
        finally
        {
            sw.Stop();
        }

        var result = new SqlConsoleExecutionDto(
            success, queryType, columnas, filas,
            rowsAffected, rowsReturned, sw.ElapsedMilliseconds, errorMessage);

        // Registrar SIEMPRE en auditoria (exito o error). En un contexto NUEVO (AddAsync + Save)
        // para no contaminar el estado del DbContext despues de un DDL crudo.
        try
        {
            db.SqlConsoleLogs.Add(new SqlConsoleLog
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.TenantId,
                UserId = actorUserId == Guid.Empty ? null : actorUserId,
                UserName = string.IsNullOrWhiteSpace(actorUserName) ? null : actorUserName.Trim(),
                Query = sql,
                QueryType = queryType,
                RowsAffected = rowsAffected,
                RowsReturned = rowsReturned,
                DurationMs = sw.ElapsedMilliseconds,
                Success = success,
                ErrorMessage = errorMessage,
                ExecutedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            // Si la auditoria falla no debe romper la respuesta; el error original ya esta en `result`.
        }

        return result;
    }

    public async Task<IReadOnlyList<SqlConsoleLogDto>> ListarHistorialAsync(int take = 50, CancellationToken ct = default)
    {
        if (take <= 0) { take = 50; }
        if (take > 500) { take = 500; }
        return await db.SqlConsoleLogs.AsNoTracking()
            .OrderByDescending(x => x.ExecutedAt)
            .Take(take)
            .Select(x => new SqlConsoleLogDto(
                x.Id, x.TenantId, x.UserId, x.UserName, x.Query, x.QueryType,
                x.RowsAffected, x.RowsReturned, x.DurationMs, x.Success,
                x.ErrorMessage, x.ExecutedAt))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<SqlTableInfoDto>> ListarTablasAsync(CancellationToken ct = default)
    {
        // pg_stat_user_tables.n_live_tup es la estimacion del planner; mucho mas rapido que COUNT(*).
        // Solo aplica a PostgreSQL: en SQL Server (dev dual) el explorador queda vacio (la ejecucion
        // de SQL crudo sigue funcionando en ambos proveedores).
        if (!db.Database.IsNpgsql()) { return Array.Empty<SqlTableInfoDto>(); }

        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) { await conn.OpenAsync(ct); }

        var resultado = new List<SqlTableInfoDto>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT relname, COALESCE(n_live_tup, 0) AS filas
                FROM pg_stat_user_tables
                WHERE schemaname = 'public'
                ORDER BY relname;";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var nombre = reader.GetString(0);
                var filas = reader.GetInt64(1);
                var (descripcion, grupo) = ResolverMetadata(nombre);
                resultado.Add(new SqlTableInfoDto(nombre, descripcion, filas, grupo));
            }
        }
        return resultado;
    }

    /// <summary>Descripciones humanas + grupo logico por tabla (dominio ECOREX.tareas). Las tablas
    /// no listadas caen a "Otros" con una descripcion generica. Para agregar: una linea aqui.</summary>
    private static (string Descripcion, string Grupo) ResolverMetadata(string nombre)
    {
        return nombre switch
        {
            // ---- Plataforma / SaaS ----
            "tenants" => ("Cuenta cliente (empresa) — una fila por tenant del SaaS.", "Plataforma"),
            "tenant_users" => ("Usuarios pertenecientes a un tenant.", "Plataforma"),
            "platform_users" => ("Usuarios globales de la plataforma (super admin).", "Plataforma"),
            "tenant_configurations" => ("Configuracion por tenant (branding, opciones).", "Plataforma"),
            "data_protection_keys" => ("Llaves de encripcion ASP.NET (NO TOCAR).", "Plataforma"),
            "admin_audit_logs" => ("Auditoria de acciones del super admin / plataforma.", "Plataforma"),
            "audit_logs" => ("Auditoria general del sistema.", "Plataforma"),
            "sql_console_logs" => ("Auditoria de TODA query ejecutada en esta consola.", "Plataforma"),

            // ---- Menu / navegacion ----
            "menu_nodes" => ("Arbol del menu (secciones, subgrupos, items) data-driven.", "Menu y Roles"),
            "menu_views" => ("Vistas de menu por tenant (perfiles de navegacion).", "Menu y Roles"),
            "roles" => ("Roles del tenant.", "Menu y Roles"),
            "role_permissions" => ("Permisos por rol y modulo (matriz).", "Menu y Roles"),

            // ---- Tareas y proyectos ----
            "task_items" => ("Tareas / actividades (TaskItem): el nucleo operativo.", "Tareas y Proyectos"),
            "task_item_activities" => ("Trazas/worklog de cada tarea (bitacora).", "Tareas y Proyectos"),
            "projects" => ("Proyectos.", "Tareas y Proyectos"),
            "project_members" => ("Miembros (ACL) de cada proyecto.", "Tareas y Proyectos"),
            "project_milestones" => ("Hitos de los proyectos.", "Tareas y Proyectos"),
            "task_boards" => ("Tableros Kanban.", "Tareas y Proyectos"),
            "task_board_columns" => ("Columnas/estados de cada tablero.", "Tareas y Proyectos"),
            "notifications" => ("Notificaciones in-app por usuario.", "Tareas y Proyectos"),

            // ---- Conceptos de actividad ----
            "actividad_categorias" => ("Categorias de conceptos de actividad (000270).", "Conceptos"),
            "actividad_subcategorias" => ("Sub-categorias/conceptos: gobiernan el inicio de la tarea.", "Conceptos"),

            // ---- Flujos de proceso (BPMN) ----
            "workflow_definitions" => ("Definiciones de flujo BPMN (versionadas).", "Flujos"),
            "workflow_nodes" => ("Nodos del flujo (tareas, gateways, eventos).", "Flujos"),
            "workflow_edges" => ("Aristas / sequence flows del flujo.", "Flujos"),
            "workflow_node_policies" => ("ACL por nodo: cargo -> candidatos.", "Flujos"),
            "workflow_node_forms" => ("Formulario asociado a cada nodo (form-por-paso).", "Flujos"),
            "workflow_node_rules" => ("Reglas enganchadas por nodo.", "Flujos"),
            "workflow_instances" => ("Instancias de flujo en ejecucion (casos).", "Flujos"),
            "workflow_step_histories" => ("Historial de pasos por caso (append-only).", "Flujos"),

            // ---- Formularios dinamicos ----
            "form_definitions" => ("Disenos de formularios dinamicos (000131).", "Formularios"),
            "form_fields" => ("Campos de cada formulario (EAV).", "Formularios"),
            "form_responses" => ("Respuestas/llenados de formularios (jsonb).", "Formularios"),

            // ---- Reglas ----
            "rules" => ("Reglas de negocio (verbos tipados, 000802).", "Reglas"),
            "rule_documents" => ("Documentos/parametros de reglas.", "Reglas"),
            "rule_execution_logs" => ("Bitacora de ejecucion de reglas.", "Reglas"),

            // ---- Organigrama / dependencias ----
            "org_units" => ("Unidades organizativas (dependencias, 000850).", "Organizacion"),
            "org_unit_members" => ("Miembros por unidad (cargos, jefes).", "Organizacion"),
            "cargos" => ("Cargos del organigrama.", "Organizacion"),
            "terceros" => ("Terceros / contactos (directorio, CRM).", "Organizacion"),

            // ---- Programaciones (000889) ----
            "scheduled_jobs" => ("Actividades programadas (recurrencia).", "Programaciones"),
            "scheduled_job_runs" => ("Bitacora de corridas del scheduler.", "Programaciones"),

            // ---- IA / Agentes ----
            "ai_agents" => ("Agentes IA configurados.", "IA y WhatsApp"),
            "ai_agent_run_logs" => ("Bitacora de ejecucion del agente.", "IA y WhatsApp"),
            "ai_provider_configs" => ("Configuracion de proveedores IA.", "IA y WhatsApp"),
            "ai_usage_logs" => ("Tokens consumidos y costo por ejecucion IA.", "IA y WhatsApp"),

            // ---- WhatsApp / mensajeria ----
            "whats_app_lines" => ("Lineas/numeros WhatsApp del tenant (Evolution/Cloud/YCloud).", "IA y WhatsApp"),
            "conversations" => ("Conversaciones de WhatsApp.", "IA y WhatsApp"),
            "messages" => ("Mensajes de las conversaciones.", "IA y WhatsApp"),
            "whats_app_templates" => ("Plantillas HSM de WhatsApp.", "IA y WhatsApp"),
            "evolution_master_configs" => ("Config master del servidor Evolution API.", "IA y WhatsApp"),
            "tenant_evolution_configs" => ("Config Evolution por tenant.", "IA y WhatsApp"),

            // ---- Billing / Wompi ----
            "subscriptions" => ("Suscripciones SaaS por tenant.", "Billing"),
            "plans" => ("Planes/tarifas del SaaS.", "Billing"),
            "payments" => ("Pagos procesados.", "Billing"),

            _ => ("(Sin descripcion registrada — usa la tabla a tu criterio.)", "Otros")
        };
    }

    /// <summary>Clasifica la query por su primera palabra significativa (auditoria + decidir
    /// SELECT via reader vs DML/DDL via ExecuteNonQuery).</summary>
    private static string DetectarTipo(string sql)
    {
        var trimmed = sql.TrimStart();
        // Saltar comentarios SQL de linea (-- ...) al inicio.
        while (trimmed.StartsWith("--"))
        {
            var nl = trimmed.IndexOf('\n');
            if (nl < 0) { trimmed = ""; break; }
            trimmed = trimmed[(nl + 1)..].TrimStart();
        }
        var space = trimmed.IndexOfAny(new[] { ' ', '\t', '\n', '\r', '(' });
        var first = (space < 0 ? trimmed : trimmed[..space]).ToUpperInvariant();
        return first switch
        {
            "SELECT" or "WITH" or "TABLE" or "VALUES" => "SELECT",
            "INSERT" => "INSERT",
            "UPDATE" => "UPDATE",
            "DELETE" => "DELETE",
            "CREATE" or "ALTER" or "DROP" or "TRUNCATE" or "GRANT" or "REVOKE" => "DDL",
            _ => "OTHER"
        };
    }
}
