# ADR-0102: Crear plantillas de WhatsApp (HSM) desde el sistema via YCloud

**Status:** Accepted
**Date:** 2026-09-13
**Deciders:** desarrollo + operacion WhatsApp

## Contexto

El modulo de Plantillas (WhatsAppTemplate) permitia crear/editar borradores e IMPORTAR las plantillas que ya
existian en YCloud (`ImportFromYCloudAsync`), pero "Someter" era un STUB: solo cambiaba el estado local a
Submitted, sin llamar a ningun proveedor. El cliente de bajo nivel `YCloudApiClient.CreateTemplateAsync`
(POST /whatsapp/templates) ya existia y esta en la interfaz, pero nadie lo invocaba. El usuario pidio poder
CREAR plantillas nuevas desde el sistema por API.

YCloud (BSP oficial de WhatsApp) si expone la creacion de plantillas por API (las somete a Meta para
aprobacion). Meta exige el cuerpo con placeholders POSICIONALES ({{1}},{{2}}...) + un arreglo de ejemplos;
el modulo las guarda con tokens AMIGABLES ({{cliente}}) y su ejemplo en VariablesJson.

## Decision

`WhatsAppTemplateService.SubmitAsync` deja de ser stub PARA LINEAS YCLOUD: compila la plantilla y la crea de
verdad via `IYCloudApiClient.CreateTemplateAsync`.

- **Resolucion de linea:** por `WhatsAppTemplate.WhatsAppLineId`. Si la linea es YCloud y tiene API key +
  WABA, se crea por API; si no (Cloud/Evolution/Emulator o sin credenciales), se conserva la transicion
  LOCAL (stub historico) para no romper esos flujos.
- **Compilacion (nuevo `WhatsAppTemplateComponents`):** tokens amigables del BODY -> posicionales {{1}}..{{n}}
  en el orden de VariablesJson (solo las variables USADAS ocupan posicion), + `example.body_text`. HEADER de
  texto y FOOTER viajan como texto plano (variables en header/footer quedan para una fase posterior).
  Categoria en MAYUSCULAS (MARKETING/UTILITY/AUTHENTICATION).
- **Respuesta:** se guarda `ProviderTemplateId` y se mapea el estado del proveedor
  (APPROVED->Approved, REJECTED->Rejected, PAUSED/DISABLED, resto->Submitted). En error, NO se cambia el
  estado (queda Draft para corregir y reintentar).
- **Reconciliacion de aprobacion:** `SyncStatusAsync` sigue como stub; el estado real (Approved/Rejected) se
  actualiza re-importando con `ImportFromYCloudAsync` (que ya existe). Se documenta en la UI.

## Consecuencias

- Desde el sistema se pueden crear plantillas HSM reales en YCloud (lineas YCloud). Beneficia el alta de
  plantillas para el Cierre del agente y las notificaciones por nodo (que usan plantillas).
- Sin migraciones ni cambios de contrato (misma firma de `SubmitAsync`; el boton "Someter" ya la llamaba).
- Limitaciones conocidas (fase posterior): variables en HEADER/FOOTER, botones, y `SyncStatusAsync` real
  (hoy se reconcilia por import).

## Actualizacion 2026-09-13 (v0.16.60): header de imagen + enlace a la tarea

Se levanto la limitacion "variables/media en el header" en su parte de MEDIA:

- **Header de imagen (y documento/video)** de punta a punta: nueva columna `WhatsAppTemplate.HeaderMediaUrl`
  (migracion dual). El compilador arma `{type:HEADER, format:IMAGE, example:{header_url:[url]}}` al crear
  (formato confirmado contra la doc de YCloud: `example.header_url` = arreglo de URLs publicas; imagen
  .jpg/.jpeg/.png, <=5MB). El envio agrega el componente `{type:header, parameters:[{type:image, image:{link}}]}`.
  La misma URL fija sirve de ejemplo al crear y de media al enviar (caso "banner de marca").
- **Enlace a la tarea en el canal de PLANTILLA**: `NodeNotifyService` expone el deep-link como variable
  `{{enlace}}`/`{{url}}`/`{{link}}`, de modo que una plantilla HSM lo incluya como parametro de cuerpo (antes el
  link solo se anexaba en correo/Telegram/grupo).

Sigue pendiente (fase posterior): variables en HEADER de TEXTO, botones (incl. boton URL dinamico), y
`SyncStatusAsync` real.

## Referencias

- Codigo: `Ecorex.Application/Tenancy/WhatsAppTemplateComponents.cs` (compilador),
  `WhatsAppTemplateService.SubmitAsync` (cableado + MapProviderStatus),
  `Ecorex.Infrastructure/YCloud/YCloudApiClient.CreateTemplateAsync` (ya existia),
  UI `PlantillasWhatsApp.razor` (nota del boton "Someter").
- Tests: `WhatsAppTemplateComponentsTests` (compilacion posicional + componentes).
