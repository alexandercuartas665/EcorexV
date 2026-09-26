# ADR-0112: Botones URL dinamicos en plantillas WhatsApp (envio del componente button)

**Status:** Accepted
**Date:** 2026-09-26
**Deciders:** Alexander Cuartas (owner), agente de desarrollo
**Relacionado:** ADR-0097 (traer plantillas / import), ADR-0107 (enlace publico de decision /d/{token})

## Contexto

El nodo de flujo "Gestion del agente" (AGROMETALICAS) manda una plantilla HSM con dos botones URL
(Aprobar / Rechazar). El objetivo es que cada boton lleve el enlace de decision `/d/{token}` DISTINTO por
tarea (ADR-0107), para que el cliente apruebe/rechace con un clic. Antes, ECOREX solo enviaba los componentes
`header` + `body`: NUNCA emitia el componente `button`, asi que:

- Los botones URL FIJOS de una plantilla igual llegan (Meta los pinta solos desde la plantilla aprobada).
- Los botones URL DINAMICOS (con sufijo variable `{{1}}`) NO se pueden usar: Meta exige un parametro de boton
  por indice en el envio; sin el, el mensaje no lleva el enlace por-tarea (o Meta lo rechaza).

El import (v0.16.146) ya trae los botones y marca `hasUrlVariable` cuando la url del boton contiene `{{`.

## Decision

Al enviar una plantilla YCloud/Cloud, si la plantilla tiene botones URL con variable Y la regla del nodo
define enlaces de decision, ECOREX emite un componente por boton:

```json
{ "type": "button", "sub_type": "url", "index": N, "parameters": [ { "type": "text", "text": "<sufijo>" } ] }
```

- **Mapeo boton -> enlace:** por el TEXTO del boton (== `buttonLabel` de la regla), robusto ante reordenamientos.
- **Indice:** la posicion del boton en el arreglo de la plantilla (0-based), contando TODOS los botones.
- **Sufijo:** la URL del enlace `/d/{token}` menos el prefijo fijo del boton (lo que va antes de `{{`). Asi, para
  un boton `https://app2.bitcode.com.co/{{button1}}` y enlace `https://app2.bitcode.com.co/d/{token}`, el sufijo
  es `d/{token}` y la URL final que arma Meta es la del enlace.
- Solo botones URL CON variable llevan parametro; los fijos y quick-reply no (Meta los pinta solos).
- La logica pura vive en `WhatsAppButtonComposer.BuildUrlButtonParams` (testeable sin red/EF).

El parametro `urlButtons` / `decisionLinksByButtonLabel` se enhebra por la cadena de envio: NodeNotifyService ->
INotificationChannelSender -> IWhatsAppConnectorService -> IYCloudApiClient. Evolution/Emulator lo ignoran (no
usan HSM de Meta).

## Alternativas consideradas

- **Meter el enlace en una variable del CUERPO** (lo que ya se hacia): funciona como texto, pero no aprovecha los
  botones nativos (el cliente ve un link en el texto, no un boton). Se conserva para plantillas sin botones.
- **Mapear por orden (Nth enlace -> Nth boton):** fragil si el orden difiere; se usa el texto del boton.

## Consecuencias

- Las plantillas con botones URL dinamicos por fin entregan el enlace de decision por-tarea en el boton.
- Requisito del lado de Meta: el boton URL debe definirse con sufijo variable `{{1}}` (p.ej.
  `https://app2.bitcode.com.co/d/{{1}}`) y su URL base debe compartir host con `/d/{token}` para que el sufijo
  calce. Un boton fijo (sin `{{`) sigue funcionando como antes, sin parametro.
- Cambio de contrato (parametros opcionales) en 4 interfaces de envio; compatible hacia atras.

## Action Items

1. [x] `WhatsAppButtonComposer.BuildUrlButtonParams` + tests.
2. [x] Emitir el componente `button` en `YCloudApiClient.SendTemplateAsync`.
3. [x] Enhebrar `urlButtons` por connector service y `decisionLinksByButtonLabel` por el channel sender.
4. [x] `NodeNotifyService`: mapear `EnlacesDecision` (label -> /d/{token}) y pasarlo al envio.
