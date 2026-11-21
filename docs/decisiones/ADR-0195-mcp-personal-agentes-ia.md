# ADR-0195 - MCP personal: autoria de Agentes de IA

- Estado: Aceptada
- Fecha: 2026-11-21
- Contexto: ADR-0159 (MCP personal de tableros), ADR-0193 (MCP personal: drones de extraccion),
  ADR-0194 (MCP personal: contenedores de datos), modulo Agentes de IA (000867, clave "agentes";
  ADR-0090 agentes en nodos de flujo)

## Contexto

El MCP personal de tableros (`/api/mcp/tableros`, ADR-0159) ya permite a una IA externa, con el token `ecx_`
del usuario, operar tareas/tableros, el Directorio, disenar flujos/formularios, armar drones de extraccion
(ADR-0193) y crear contenedores de datos (ADR-0194). Pero NO podia crear ni configurar los **Agentes de IA**
del modulo 000867 (los que hoy existen como ANA, SARA, etc.). Eso bloquea el caso real "Inspector de Proyectos
FOMAG": un agente DeepSeek que lea los PDF de pliegos ligados en el contenedor FOMAG y escriba de vuelta los
campos del proyecto. La regla de trabajo es que el token pueda operar la plataforma por MCP para lo que haga
falta.

El modulo expone un servicio de aplicacion maduro: `IAiAgentService` (crear/editar/duplicar/encender-apagar el
agente; prompts enrutados y recursos; lector de medios; historial de versiones de prompts).

## Decision

Se agrega un bloque de **17 herramientas** al MCP personal (nuevo partial `TableroMcpToolset.Agentes.cs`),
capa delgada sobre `IAiAgentService`, que corre COMO el usuario del token y queda auditada. Abstraccion:
"agente" = `AiAgent` (proveedor/modelo/prompt de sistema/lector de medios/herramientas on-off/tableros/round-robin/
concepto por defecto); "recursos" = `AiAgentResource`; "prompts enrutados" = `AiAgentPrompt`.

- Lectura (3): `listar_agentes`, `ver_agente`, `historial_prompts_agente`.
- Escritura (11): `crear_agente`, `editar_agente`, `agente_lector_medios`, `encender_agente`,
  `apagar_agente`, `duplicar_agente`, `restaurar_version_prompt`, `agregar_recurso_agente`,
  `editar_recurso_agente`, `agregar_prompt_agente`, `editar_prompt_agente`.
- Destructivas (3): `eliminar_agente`, `eliminar_recurso_agente`, `eliminar_prompt_agente`.

Detalles:

- **Proveedor**: claude / gemini / chatgpt / deepseek (sinonimos tolerados: anthropic, google, gpt/openai).
  El agente usa la IA que tenga configurada el proveedor; `modelo` vacio = el por defecto del proveedor.
- **Nace apagado**: `crear_agente` y `duplicar_agente` dejan el agente APAGADO (el servicio fuerza
  `IsActive=false`). Se enciende con `encender_agente` tras validarlo (regla de la casa: no sale a produccion
  sin revisar).
- **Edicion por campo**: `UpdateAsync` del servicio es reemplazo total; para que `editar_agente`,
  `editar_recurso_agente` y `editar_prompt_agente` conserven lo no enviado, el MCP relee el estado actual
  (del DTO del agente, o de `AiAgentResources`/`AiAgentPrompts` para recurso/prompt) y fusiona solo lo que
  llega en los argumentos. El lector de medios se fija aparte (`agente_lector_medios` ->
  `SetMediaReaderPromptAsync`), que `UpdateAsync` no toca.
- **Herramientas del agente (escribir al contenedor)**: `DisabledToolsJson` es una whitelist POR EXCLUSION
  (las que NO estan en la lista quedan habilitadas). Crear el agente sin `herramientas_deshabilitadas` deja
  habilitadas TODAS, incluida `agregar_filas_a_contenedor` (con la que el agente escribe en un contenedor de
  datos). `ver_agente` muestra un aviso explicito cuando no hay ninguna deshabilitada.
- **Resolucion por nombre o id**: agentes (y, en `editar_agente`, tableros por nombre/id, asignados por
  email/nombre y concepto por codigo/nombre). Recursos y prompts se referencian por id (los da `ver_agente`).

### Permiso (coherente con ADR-0194)

El modulo Agentes de IA SI esta en la matriz de roles (clave `agentes`, ruta del menu 000867). Las herramientas
exigen el permiso efectivo del actor sobre esa clave (`NeedAgent` -> `perms.Can`), con el mismo criterio que
flujos y contenedores: lecturas = View, `crear_agente` = Create, `eliminar_*` = Delete, el resto = Edit.
Owner/Admin y usuarios sin rol = acceso total. El aislamiento multi-tenant lo garantiza el filtro global de EF.

### Disponibilidad condicional

Las herramientas solo se publican si `IAiAgentService` fue inyectado (nuevo parametro OPCIONAL `agents` al final
del constructor de `TableroMcpToolset`, `= null`). En produccion se inyecta por DI (ya registrado); en pruebas
que no lo necesitan, no. Asi las pruebas de catalogo previas siguen validas y se agrego una nueva con agentes.

### Fuera de v1

Cierre del agente, reactivacion/seguimiento y datos-cache del agente son jsonb que la pantalla Blazor fija
aparte y que `IAiAgentService` no expone; quedan como follow-up (requieren metodos nuevos en el modulo de
agentes, otra area). La idempotencia por url al escribir de vuelta al contenedor depende del fix de motor de
ingesta pendiente (ver memoria FOMAG), no de este ADR.

## Consecuencias

- El MCP personal pasa de **124 a 141 herramientas**. Auditoria: toda escritura/destructiva exitosa queda en
  `super_admin_audit_logs` (el servicio escribe `AdminAuditLog` en cada mutacion), como el resto del MCP.
- Una IA puede crear un agente de punta a punta (proveedor, prompt, lector de medios, prompts enrutados,
  recursos, herramientas), duplicarlo y encenderlo/apagarlo, sin la app.
- Cobertura dual (PostgreSQL + SQL Server, Testcontainers) en `McpTablerosTests`: catalogo consistente con
  agentes + flujo funcional crear(deepseek, apagado)/configurar/lector-medios/prompt/recurso/encender-apagar/
  duplicar/borrar.

## Alternativas consideradas

- Exponer una herramienta dedicada para "escribir al contenedor" desde el agente: innecesario, porque el agente
  ya lo hace en runtime con `agregar_filas_a_contenedor` (ContenedorDatosToolset); basta NO deshabilitarla.
- Incluir cierre/reactivacion/datos-cache en esta ola: aplazado; no estan en `IAiAgentService` y requeririan
  tocar el modulo de agentes.
- Crear el agente ya encendido: descartado; la regla es validar antes de produccion (nace apagado).
