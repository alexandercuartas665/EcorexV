# ADR-0184: Sincronizar el estado de aprobacion de plantillas WhatsApp con YCloud/Meta

Fecha: 2026-11-18
Estado: Aceptada
Version: v0.16.349
Relacionado: ADR-0029 (plantillas HSM), ADR-0175 (agente editor de plantillas), ADR-0056/0170 (motor de
acciones, paso WhatsApp)

## Contexto

Con lineas YCloud, "Someter" (boton o herramienta `submit_to_meta` del agente) ya CREA la plantilla en Meta y
la deja en revision (`Submitted`). Pero `SyncStatusAsync` seguia siendo un STUB de ADR-0029: la plantilla solo
pasaba a Aprobada/Rechazada si alguien pulsaba "Importar desde YCloud" a mano. Consecuencia: el paso WhatsApp
del gestor de contactos (que con YCloud exige plantilla Aprobada) nunca la ofrecia, y el usuario no se enteraba
de un rechazo ni de su motivo.

YCloud no expone un "estado de una plantilla" por id en el cliente actual; si expone el listado paginado por
WABA (`ListTemplatesAsync`), que trae estado, id y motivo de rechazo de todas.

## Decision

1. **Reconciliador puro** `WhatsAppTemplateStatusSync` (testeable): mapea el estado de Meta al enum
   (APPROVED, REJECTED, PAUSED, DISABLED/DELETED, PENDING/IN_APPEAL -> Submitted; un estado desconocido NO
   cambia nada), empareja por id del proveedor y luego por (nombre, idioma), y aplica SOLO estado, motivo de
   rechazo, id del proveedor y `ReviewedAt`. Nunca toca cuerpo, variables ni botones (eso sigue siendo del
   import manual, que conserva los tokens con nombre).
2. **Servicio**: `SyncStatusAsync(id)` (una plantilla) y `SyncPendingAsync()` (todas las `Submitted` del
   tenant activo en lineas YCloud). Una llamada de listado por linea. Cada cambio se audita
   (`wa-template.status-sync`). `FindTenantsWithPendingTemplatesAsync` es el barrido de plataforma (solo ids,
   `IgnoreQueryFilters`) para el worker.
3. **Worker** `WhatsAppTemplateStatusWorker` en Ecorex.SuperAdmin (prod solo levanta ese servicio): cada 10
   minutos, por tenant con plantillas en revision, `SyncPendingAsync` dentro de `AmbientTenantContext.Begin`.
   Respeta `ECOREX_DISABLE_WORKERS`. 10 minutos: Meta revisa en minutos u horas y no castiga el rate limit.
4. **Agente**: herramienta `check_meta_status` (sin gate: solo reconcilia el estado) para responder "ya la
   aprobaron?" y explicar el motivo de un rechazo.
5. **UI**: en Plantillas WhatsApp, boton "Estado" por plantilla YCloud y "Actualizar estados" cuando hay
   plantillas en revision. En el paso WhatsApp del gestor de contactos se listan las plantillas de la linea que
   aun no se pueden usar (en revision / rechazada con motivo / pausada) con "Consultar ahora".

## Consecuencias

- Una plantilla sometida aparece sola en el paso WhatsApp del motor a lo sumo ~10 minutos despues de que
  Meta la apruebe.
- El costo es una llamada de listado por linea YCloud con plantillas en revision cada 10 minutos; sin
  plantillas pendientes, el worker no llama a YCloud.
- Plantillas de lineas Cloud/Emulator siguen con la transicion local (sin estado remoto que consultar).
- Pendiente (no incluido): avisar al creador de la plantilla cuando Meta la apruebe o rechace.
