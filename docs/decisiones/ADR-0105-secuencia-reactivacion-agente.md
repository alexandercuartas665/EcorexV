# ADR-0105: Secuencia de Reactivacion del agente (revivir contactos dormidos)

- Estado: Aceptado
- Fecha: 2026-09-16
- Version: v0.16.80
- Relacionado: ADR-0092 (WhatsApp por plantilla / ventana 24h), ADR-0102 (plantillas YCloud), CierreJson (config por agente)

## Contexto

Un contacto que dejo de responder sin cerrar (conversacion dormida) se pierde. Se quiere que CADA agente
pueda configurar una "Secuencia de Reactivacion": tras X horas de inactividad emite un mensaje; tras mas
horas, otro; etc. Debe respetar la ventana de 24h de Meta: dentro de 24h del ULTIMO mensaje del cliente se
puede enviar texto libre; fuera de ella SOLO una plantilla aprobada (HSM).

## Decision

### Modelo de datos (siguiendo el patron de CierreJson, no una tabla de pasos)
- **`AiAgent.ReactivacionJson`** (jsonb): configuracion por agente (habilitada + lista de pasos). Mismo patron
  que `CierreJson`/`AgentCierreConfig`. Cada paso = `{ offsetHoras, mensajeTexto?, plantilla?, idioma?, habilitado }`.
  Se decidio JSON (no una tabla `ai_agent_followups`) para ser consistente con el Cierre y no agregar esquema.
- **Estado por conversacion en columnas de `Conversation`** (no una tabla nueva, evita romper ~8 fakes de
  `IApplicationDbContext`): `ReactivacionUltimoPaso` (int, cuantos pasos habilitados ya se enviaron) y
  `ReactivacionUltimoEnvioAt` (DateTimeOffset?). Una conversacion la atiende un solo agente (un agente por
  linea), asi que el estado por conversacion basta.
- **`AiAgentRunLogKind.Reactivacion`**: nuevo valor del enum para la bitacora (sin migracion; el enum se
  guarda como int).
- **Migracion DUAL** (PG + SqlServer): 3 columnas (`reactivacion_json` jsonb en ai_agents;
  `reactivacion_ultimo_paso` int NOT NULL default 0 y `reactivacion_ultimo_envio_at` en conversations). Con
  `Down`. No hay seed. `reactivacion_ultimo_paso` default 0 es seguro para filas existentes.

### Motor (Ecorex.SuperAdmin/RealTime, NO Ecorex.Workers)
`AgentReactivationWorker` (BackgroundService) vive en SuperAdmin, no en Ecorex.Workers, porque el compose de
PRODUCCION solo levanta `ecorex-app` (= SuperAdmin); un hosted service en Ecorex.Workers nunca correria en
prod. Registrado dentro del gate `ECOREX_DISABLE_WORKERS` junto a los demas workers. Cadencia configurable
(`AgentReactivation:IntervalMinutes`, default 5). Barrido cross-tenant (solo ids de tenant, con
`IgnoreQueryFilters`) y luego, por tenant, `AmbientTenantContext.Begin` + `IAgentReactivacionService.RunTenantAsync`
(el filtro global de EF aisla al tenant). Mismo patron que `ScheduledJobWorker`.

`AgentReactivacionService.RunTenantAsync` (Application, tenant-scoped por el filtro global): para cada agente
activo con reactivacion, recorre las conversaciones ACTIVAS de sus lineas conectadas y:
- Excluye: numeros en la lista negra del tenant (opt-out, `TenantBlockedNumbers`), leads cerrados
  (`ArchivedAt` o `Status != Open`) o tomados por un asesor humano (`AssignedToTenantUserId`), y conversaciones
  que un paso de flujo esta esperando (`WorkflowStepHistory.PendingWhatsAppConversationId`).
- Calcula el ULTIMO entrante del cliente (posterior a `AgentContextResetAt` si lo hubo). Sin entrante -> nada.
- **Reinicio**: si el cliente respondio DESPUES del ultimo seguimiento (`lastInbound > ReactivacionUltimoEnvioAt`),
  la secuencia vuelve a 0 (revivio).
- Elige el siguiente paso pendiente (`ReactivacionUltimoPaso`) cuyo `offsetHoras` ya se cumplio.
- **Ventana de 24h (Meta)**: `<=24h` -> `mensajeTexto` por el conector (`SendTestAsync`); `>24h` -> plantilla
  aprobada (`INotificationChannelSender.SendWhatsAppTemplateAsync`, que resuelve variables + header). Si `>24h`
  y el paso NO tiene plantilla -> se OMITE el paso (avanza) y se registra el motivo; NUNCA texto libre >24h.
- Persiste el saliente + avanza el estado + bitacora (`AiAgentRunLog` Kind Reactivacion). Un solo envio por
  conversacion por corrida; idempotente (el estado evita reenviar un paso).

### UI (Agentes.razor)
Nuevo acordeon "Reactivacion / Seguimiento" (junto a "Cierre"): toggle + lista editable de pasos (horas,
texto, selector de plantilla filtrado a APROBADAS) + aviso de la regla de 24h. Guarda via
`IAgentReactivacionService.SaveConfigAsync`.

## Condiciones de parada / reinicio (resumen)
- **Reinicio**: el cliente responde (nuevo entrante posterior al ultimo seguimiento) -> secuencia a 0.
- **No mas pasos**: lead cerrado (archivado / Won / Lost), opt-out (lista negra), conversacion archivada, o
  ya se enviaron todos los pasos (espera respuesta).

## Consecuencias
- Solo YCloud soporta plantillas (ADR-0092): fuera de 24h en lineas no-YCloud el paso se omite.
- El barrido es O(conversaciones activas por linea); se acota a 500 conversaciones por agente por corrida.
- Multi-tenant intacto (filtro global + AmbientTenantContext por tenant). Sin secretos. ASCII.

## Pruebas
`AgentReactivacionServiceTests` (7): paso <=24h usa texto; paso >24h usa plantilla; >24h sin plantilla se
omite y loguea; el cliente responde -> reinicio (no reenvia); lead cerrado -> nada; numero en lista negra ->
nada; solo conversaciones de las lineas del agente. Build de la solucion verde; migracion dual consistente.
