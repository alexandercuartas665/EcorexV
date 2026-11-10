# ADR-0159: Servidor MCP de tableros para IAs externas, con token PERSONAL por usuario

**Status:** Accepted (Fase 1)
**Date:** 2026-11-08
**Deciders:** Alexander Cuartas (dev principal)

## Contexto

El usuario quiere que una IA externa (Claude, Gemini, ChatGPT...) opere el modulo de tareas:
consultar tableros, crear/comentar/mover tareas y avanzar los pasos de los flujos (BPMN).

Ya existia un adaptador MCP (`POST /api/mgmt/agent/mcp`, ADR-0057), pero NO sirve para usuarios finales:

- Se abre con `ECOREX_MGMT_API_KEY`: clave de PLATAFORMA, cross-tenant, sin identidad de usuario
  (actua como `Guid.Empty`; la auditoria registra QUE, no QUIEN).
- La visibilidad de tableros/tareas (ADR-0117 tableros restringidos, ADR-0136 alcance por usuario,
  ADR-0038 pasos ruteados) depende del usuario actual: sin usuario, la IA veria todo.
- Expone TODOS los toolsets del agente interno (incluido el autor de formularios), y las herramientas de
  tareas (`TasksToolset`) estan atadas a una conversacion de WhatsApp (adjuntos, cliente, reenvio de PDF).
- Protocolo 2024-11-05.

Ademas, al revisar `TenantApiToken` (ADR-0058) se encontro que el Bearer de `/api/config` aceptaba
CUALQUIER token activo sin mirar su `Scope`: reutilizar la tabla para tokens de usuario habria abierto la
API de configuracion (alcance admin) a cualquier usuario.

## Decision

1. **Token PERSONAL** sobre `TenantApiToken` (sin tabla nueva): columna `TenantUserId` (nullable) +
   `Scope = "mcp:tableros"`. Lo emite/revoca cada usuario para si mismo en `/conectar-ia` (enlace
   "Conectar IA" en la barra superior). Solo se guarda el hash SHA-256; el valor se muestra una vez.
   Tope de 10 activos por usuario. Emision/revocacion auditadas (`mcp.token.*`).
   - `IPersonalAccessTokenService.ResolveAsync` exige: token activo + alcance pedido + con dueno +
     usuario `Active` + tenant no suspendido/bloqueado/cerrado. Es la unica lectura cross-tenant (por hash).
2. **Separacion estricta de alcances**: `/api/config` solo acepta tokens `Scope == "admin"` y SIN dueno;
   su emision fuerza `admin` y su listado/revocacion ignoran los personales. El MCP solo acepta
   `mcp:tableros` personales.
3. **Endpoint nuevo `POST /api/mcp/tableros`** (`McpTablerosEndpoints`): MCP Streamable HTTP 2025-06-18
   (negocia 2025-03-26 / 2024-11-05), respuesta JSON directa, sin sesiones; GET/DELETE -> 405;
   notificaciones -> 202; sin token -> 401 + `WWW-Authenticate: Bearer`. Publica `instructions` y
   `annotations.readOnlyHint` por herramienta.
4. **`TableroMcpToolset`** (Application/Mcp), capa DELGADA sobre `IActivityBoardService`,
   `ITaskItemService` e `IWorkflowInboxService`. NO se registra como `IAgentToolset` (no entra al catalogo
   de agentes internos ni al MCP de `/api/mgmt`). 15 herramientas:
   - Lectura: `listar_tableros`, `ver_tablero`, `buscar_tareas`, `ver_tarea`, `mis_pasos_pendientes`,
     `listar_conceptos`, `listar_usuarios`.
   - Escritura: `crear_tarea` (por concepto -> arranca su flujo), `mover_tarea`, `comentar`,
     `asignar_tarea`, `checklist`, `registrar_tiempo`, `tomar_paso`, `completar_paso`.
5. **Actua COMO el usuario**: `actorUserId` = PlatformUser del token; bitacora "Nombre (via IA)";
   auditoria `mcp.tableros.{tool}` (actor humano) en cada escritura exitosa.
6. **Visibilidad = la del tablero**: Owner/Admin todo; si no, lo suyo (encargado, asignado M:N o paso de
   flujo ruteado) o tareas de un tablero visible cuyo alcance no lo limita. Sin acceso se responde igual
   que "no existe".
7. **Regla de flujo**: una tarea con flujo `Running` NO se mueve de columna a mano (`mover_tarea` se
   niega y sugiere `completar_paso`). `completar_paso` reusa el motor: compuerta atendida por destino
   (ADR-0072), opciones de aprobacion, y el formulario obligatorio (ADR-0077) lo sigue exigiendo el motor.
   Pasos atendidos por un agente interno no se cierran desde el MCP.

## Consecuencias

- Clientes compatibles HOY (Bearer en header): Claude Code, Gemini CLI, Gemini aplicacion/Code Assist y Codex
  (estos dos ultimos via `mcp-remote`), Claude Desktop (via `mcp-remote`). La pagina `/conectar-ia` entrega los
  snippets listos. La variante `mcp-remote` para Gemini es el respaldo cuando la forma `httpUrl` del CLI falla.
- claude.ai web y ChatGPT (conectores remotos) exigen OAuth 2.1 -> **Fase 2** (no incluido).
- Migracion aditiva dual (PG + SQL Server): `AddPersonalMcpTokens` (columna nullable + indice).
- Pruebas: `McpTablerosTests` (matriz dual) cubre resolucion/revocacion/alcance/usuario inactivo,
  aislamiento de tablero restringido entre usuarios, y la regla de flujo (no mover, tomar y completar).

## Addendum 2026-11-09 (pruebas E2E en BITCODE local)

- Directorio: nuevas tools `buscar_contacto`, `crear_contacto` (reusa la deduplicacion de DirectorioToolset) y
  `vincular_contacto` (ITaskItemService.SetContactTerceroAsync). Total 18 herramientas.
- `crear_tarea` en tenants que clasifican SOLO por concepto (sin tipos de actividad activos): sin 'concepto'
  usa el unico concepto ligado al tablero; si no hay uno claro, devuelve la lista para elegir.
- BUG del motor corregido: `StartInstanceAsync` guardaba como iniciador el id de PLATAFORMA (lo que pasan
  UI/tablero/MCP); un paso "hereda al iniciador" quedaba sin encargado. Ahora se normaliza a TenantUser.

## Addendum 2026-11-09 (b): gestion de contactos del Directorio

- Las tools del Directorio pasan a un archivo parcial propio (`TableroMcpToolset.Directorio.cs`). Nuevas:
  `ver_contacto`, `editar_contacto`, `nota_contacto`, `vincular_persona_empresa`, `contacto_interno`,
  `tareas_del_contacto`, `listar_campos_contacto`, `inactivar_contacto`, `reactivar_contacto`; `buscar_contacto`
  gana filtros (tipo, ciudad, sector, estado) y busca por digitos del telefono. Total 27 herramientas.
- Capa delgada sobre ITerceroService / ITerceroVinculoService / ITerceroFieldService. Sin migraciones.
- Permisos: cada tool del Directorio respeta la matriz de roles del usuario (View/Edit/Delete del modulo
  `directorio-general` y los sub-permisos crear-empresa / crear-cliente / crear-sospechoso), igual que la pantalla.
  Esto tambien cierra un hueco: `crear_contacto` no miraba los sub-permisos.
- No duplicar: `editar_contacto` rechaza un NIT o telefono (ultimos 10 digitos) que ya sea de otro contacto activo;
  `contacto_interno` liga la persona existente (misma cedula, email o telefono) en vez de crearla.
- Rastro: cada cambio deja nota en la ficha a nombre de "Usuario (via IA)" y la auditoria `mcp.tableros.*` guarda
  antes/despues (`McpToolResult.AuditBefore/AuditAfter`).
- Baja logica: `inactivar_contacto` exige motivo, pone Estado=Inactivo (nunca DELETE) y se anuncia con
  `destructiveHint=true` para que el cliente pida confirmacion; `reactivar_contacto` la revierte.
- Ligar persona -> empresa es visible en AMBOS directorios: si la persona no tiene empresa queda como contacto
  principal (EmpresaId, lo unico que muestra el Clasico); si ya tiene otra, vinculo M:N (Modular).
- Campos repetidos en varias fichas (ej. base.direccion / fiscal.direccion): se aceptan como `ficha.clave` y la
  ambiguedad se responde con las opciones; `listar_campos_contacto` devuelve `clave_completa`.
- Fuera de alcance: fusionar duplicados, importar contactos desde Excel por la IA.

## Fuera de alcance (siguientes fases)

- OAuth 2.1 (Authorization Server + metadata `/.well-known/oauth-protected-resource`) para ChatGPT/claude.ai.
- Llenar formularios de paso desde la IA (hoy el motor lo exige y el usuario lo diligencia en la app).
- Rate limiting por token y permisos finos por herramienta (p.ej. token de solo lectura).
- Que un Owner/Admin vea y revoque los tokens personales de otros usuarios.
