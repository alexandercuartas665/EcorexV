# ADR-0107: Enlace publico de decision del cliente por salida de compuerta

**Status:** Accepted
**Date:** 2026-09-22
**Deciders:** Usuario (producto) + agente de desarrollo

## Contexto

Un flujo llega a una compuerta exclusiva ATENDIDA (ej. "Gestion del agente") y hay que dejar que el
CLIENTE (no un usuario del sistema) decida la ruta desde un enlace, sin sesion. Cada salida puede
requerir una captura distinta: una firma (aprobar) u otra una observacion (rechazar). Debe ser
GENERICO (cualquier compuerta, cualquier tenant), no atado al caso comercial.

## Decision

El enlace publico se DEFINE y se ENVIA desde una REGLA DE NOTIFICACION del nodo compuerta (correo o
WhatsApp). Cada regla puede llevar uno o varios "enlaces de decision", cada uno con: la variable de la
plantilla/cuerpo donde va la URL, la SALIDA que resuelve (nodo destino), la captura
(`WorkflowDecisionCapture` = None | Signature | Observation), si la observacion es obligatoria, la
etiqueta del boton y la caducidad. Al dispararse la regla, por cada enlace se crea (o reusa) un
`WorkflowDecisionToken` (secreto aleatorio, un-solo-uso, con caducidad) y su URL `/d/{token}` se
inyecta bajo la variable indicada. El cliente abre `/d/{token}` (anonimo), captura lo pedido y con eso
se RESUELVE la compuerta por esa ruta reusando `ChooseGatewayRouteAsync` (hacia el nodo destino).

Decisiones concretas (confirmadas con el usuario):
- **Todo en la notificacion** (no en "Reglas de salida"): el enlace solo se puede EMITIR por un canal
  (correo/WhatsApp), asi que se configura donde se envia. Un enlace por salida; para enviar dos
  (aprobar/rechazar) en un mismo mensaje, la regla lleva dos enlaces con dos variables.
- **Firma** = pad dibujado (canvas -> PNG), guardada como **evidencia/adjunto de la actividad** (no se
  estampa en el PDF). **Observacion** = queda como comentario de la ruta (ApprovalComment) + bitacora.
- **Pagina publica minima**: solo la accion (sin mostrar documento), sin pedir identidad (se asume el
  contacto de la tarea como firmante).
- **Vigencia**: un-solo-uso + caducidad fija configurable; al usar una salida, la compuerta avanza y
  los enlaces hermanos quedan revocados. La salida sigue pudiendo cerrarse desde la consola (plan B);
  la firma/observacion del cliente solo se captura por el enlace.

## Consecuencias

- La config vive en la regla (`NodeNotifyRule.EnlacesDecision`, dentro de `WorkflowNode.NotifyJson`,
  jsonb): NO requiere migracion propia y viaja con el nodo (se copia en la clonacion publicar->editar
  como el resto de NotifyJson). El nodo compuerta NO lleva metadatos de enlace en sus aristas.
- `WorkflowDecisionToken.Token` es un secreto (capability URL) que se GUARDA (a diferencia de
  `FormToken`, que guarda solo el hash) porque la notificacion inyecta el enlace directo. Riesgo bajo:
  decision unica, caduca, no da acceso a datos.
- Nuevo endpoint/pagina anonima `/d/{token}` y JS de pad de firma (`wwwroot/js/signature-pad.js`).
- Migracion dual (PG + SQL Server): SOLO la tabla `workflow_decision_tokens` (sin columna en edges).

## Alternativas descartadas

- **Configurar en "Reglas de salida"** (primer intento): el enlace no se puede "emitir desde el nodo",
  solo por un canal de notificacion, asi que la config se movio a la regla (feedback del usuario).
- **Un solo enlace con botones** para toda la compuerta: mas simple de enviar, pero no encaja con "url
  por cada salida" ni con plantillas WhatsApp de dos enlaces separados.
- **Guardar solo el hash del token** (como FormToken): impide inyectar el enlace en la notificacion sin
  re-generarlo; se prefirio guardar el secreto por el bajo riesgo.
