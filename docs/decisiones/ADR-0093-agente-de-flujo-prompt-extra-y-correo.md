# ADR-0093: Prompt extra por nodo y herramienta de correo para el agente de flujo

**Estado:** Propuesta
**Fecha:** 2026-09-06
**Deciders:** Alexander Cuartas (orquestador)
**Relacionados:** ADR-0090 (agentes de IA en nodos de flujo), ADR-0091 (Colmena + Retell), ADR-0092 (WhatsApp),
ADR-0056 (paso de correo del motor de acciones / IEmailSender).

## Contexto

El agente de un nodo ya decide/llena formularios y consigue datos por web (Colmena), llamada (Retell) y
WhatsApp. Faltan dos cosas que pidio el usuario al configurar el agente:

1. **Instrucciones por nodo (prompt extra).** Hoy el agente usa solo su `SystemPrompt` base (del `AiAgent`).
   No hay forma de decirle, EN ESTE PASO, que hacer: p.ej. "para conseguir el correo del cliente, abre su
   ficha en LinkedIn con Colmena buscando por nombre+empresa y extrae el cargo y el correo". Sin eso, tener
   la herramienta `buscar_web` no basta: el agente no sabe QUE hacer con ella.
2. **Escribir correos.** El agente deberia poder redactar y ENVIAR un correo (avisar, pedir un dato,
   confirmar). Inventario: `IEmailSender.SendAsync(to, subject, htmlBody)` (SMTP real, config tenant-first con
   fallback global; devuelve Ok=false sin lanzar si no hay config). NO existe correo ENTRANTE (sin IMAP ni
   webhook), asi que no hay como esperar una respuesta hoy.

## Decision

### 1. Prompt extra por nodo

`WorkflowNodeAgent` gana `ExtraPrompt` (texto, nullable, <=4000). Se ANTEPONE/adjunta al prompt de sistema
del agente en AMBOS caminos del invoker: la decision/compuerta (BuildSystemPrompt) y el llenado de formulario
(BuildFormSystemPrompt). Es el lugar donde el usuario le explica al agente QUE hacer en ese paso y COMO usar
sus herramientas (incluida Colmena: que URL abrir y que extraer). Solo instrucciones; no cambia el contrato
de salida.

### 2. Herramienta de correo (enviar, sincrona)

- `WorkflowNodeAgent.CanSendEmail` (bool, default false): permiso EXPLICITO por nodo. Sin el, no hay tool.
- Herramienta `enviar_correo(destinatario, asunto, cuerpo)` en el bucle de llenado (RunFormFillAsync), del
  mismo tipo SINCRONO que `buscar_web` (NO pausa el paso): el invoker llama `IEmailSender.SendAsync` y le
  devuelve al modelo `{ok}` o `{ok:false,error}`. El cuerpo lo redacta LIBRE el agente (asunto + cuerpo,
  guiado por el prompt extra); el texto plano se convierte a HTML seguro (escape + saltos de linea). El FROM
  lo fija la config del tenant (el agente no lo elige). Tope de correos por paso (anti-bucle).
- Se ofrece solo si `CanSendEmail`. Si el tenant no tiene correo saliente configurado, `SendAsync` devuelve
  Ok=false y el agente lo trata como "no se pudo" (no inventa que envio).

### 3. Modo "enviar y esperar respuesta" (FUTURO, no en este corte)

El usuario eligio "ambos, empezando por enviar". El modo pausa/reanudacion por correo (como WhatsApp/llamada)
queda DOCUMENTADO como extension: requiere correo ENTRANTE (IMAP/webhook) + correlacion a un paso
(`PendingEmailThreadId` o similar), infraestructura que hoy no existe. Se abre cuando se construya el correo
entrante; el patron a seguir es el de ADR-0092 (WhatsApp).

## Guardarrailes

- Permiso explicito por nodo para el correo. Prompt extra es solo texto (no eleva privilegios).
- El correo saliente sin config -> Ok=false -> el agente no asume exito. Tope de envios por paso.
- Cupo de IA, AiUsageLog y autoria (executedByAiAgentId) siguen aplicando. Multi-tenant: el correo resuelve
  la config del tenant activo por el filtro global; el FROM/credenciales nunca los ve el agente.

## Opciones consideradas

- **Prompt extra vs system prompt del agente.** Se elige un prompt POR NODO: el mismo agente sirve en varios
  pasos con instrucciones distintas sin clonarlo.
- **Correo libre vs plantilla.** El usuario eligio LIBRE (asunto+cuerpo): maxima flexibilidad, guiado por el
  prompt extra. Se puede sumar el modo plantilla (reusando EmailTemplateService) mas adelante.
- **Enviar en el invoker vs devolver una "peticion" al runner.** Se elige enviar en el invoker, igual que
  `buscar_web` (efecto de red sincrono, sin escribir BD), por consistencia y simplicidad.

## Consecuencias

Mas facil: darle al agente instrucciones especificas por paso (clave para Colmena) y que notifique/pida por
correo. A vigilar: costo (un agente podria enviar de mas -> tope por paso) y que el correo saliente este
configurado en el tenant. El modo respuesta-por-correo queda pendiente de correo entrante.

## Plan de accion

- [ ] Esquema: `WorkflowNodeAgent.{ExtraPrompt, CanSendEmail}` + migracion dual aditiva.
- [ ] Invoker: anteponer ExtraPrompt en ambos prompts; herramienta `enviar_correo` (IEmailSender) con tope.
- [ ] Contexto/DTOs: ExtraPrompt + CanSendEmail en la asignacion y el DTO del editor.
- [ ] Config por nodo (modal del editor): textarea de instrucciones + toggle "Enviar correos".
- [ ] CI dual, solo ASCII, commit a fase-0/clon-backbone. Validacion de runtime real en prod.
