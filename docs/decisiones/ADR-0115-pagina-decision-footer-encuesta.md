# ADR-0115: Pagina de decision del cliente - footer dinamico y encuesta reportable

**Status:** Accepted
**Date:** 2026-09-28
**Deciders:** Alexander Cuartas (producto), agente de desarrollo

## Contexto

El enlace publico de decision del cliente (`/d/{token}`, `Decision.razor`) que emite una regla de
notificacion de un nodo compuerta (ADR-0113) se veia plano. El usuario pidio: (1) rediseno visual
"bonito" (mockup), (2) un **footer dinamico** que se pueda componer como HTML al armar la
notificacion (p.ej. datos de pago), y (3) poder configurar **preguntas de encuesta** cuyas
respuestas queden **reportables**.

## Decision

Tres olas sobre la misma pagina y la config del enlace (`NotifyDecisionLink`):

1. **Visual**: rediseno de `Decision.razor` (header gradiente, tarjetas, nombre de empresa). Se agrega
   `CompanyName` a `DecisionTokenValidation` (del tenant).

2. **Footer dinamico**: `NotifyDecisionLink.FooterHtml` (HTML con tokens). Se resuelve al ARMAR la
   notificacion (`NodeNotifyService`, con el `INotifyTokenResolver` y los tokens ya calculados) y se
   **congela** (ya sustituido) en el token (columna `footer_html`). La pagina lo pinta al pie. El HTML
   es de autor del **tenant** (confiable para su propia pagina), y se sanitiza best-effort (quita
   `<script>`/`<iframe>`/`on*`/`javascript:`) como defensa en profundidad en la pagina publica.

3. **Encuesta = formulario REPORTABLE** (decision central): `FormResponse.DefinitionId` es un FK
   OBLIGATORIO; **no existe una respuesta de formulario sin un `FormDefinition`**, y el lector de
   reportes arma las columnas desde las `FormQuestions` de la definicion. Por eso una encuesta
   **reportable** DEBE apoyarse en un formulario real. Se decidio que la encuesta **referencia un
   formulario existente** (`NotifyDecisionLink.SurveyFormDefId`) construido en el disenador y marcado
   reportable. Al armar, se serializan sus preguntas y se congelan en el token (`survey_json`); la
   pagina las renderiza; al enviar, las respuestas aterrizan como `FormResponse` (DefinitionId = ese
   form, Reference = numero de la tarea, Status = Submitted) -> reportable y visible en la tarea.

## Opciones consideradas (encuesta)

- **A (elegida): referenciar un FormDefinition reportable.** Reutiliza el motor de formularios y el de
  reportes; respuestas reportables. Contra: el usuario define las preguntas en el disenador, no inline.
- **B: preguntas inline en el enlace.** Mas directo de configurar, pero las respuestas NO serian
  reportables (el lector de reportes exige una definicion con `FormQuestions`). Descartada por el
  requisito "reportable".

## Consecuencias

- **Mas facil**: enlaces de decision bonitos, con pie de pagina compuesto por el usuario y encuestas
  que caen en la tarea y en reportes sin infra nueva.
- **A vigilar**: el footer HTML lo escribe el tenant (sanitizacion best-effort, no un parser). La
  encuesta exige tener el formulario creado y reportable antes; si el form se borra, la respuesta se
  omite (guard) sin romper la decision.

## Action Items

1. [x] Ola 1: rediseno `Decision.razor` + `CompanyName`.
2. [x] Ola 2: `FooterHtml` en config + resolver/congelar en el token + render + textarea en FlowEditor.
3. [x] Ola 3: `SurveyFormDefId` + serializar preguntas + render + `FormResponse` en `ApplyAsync`.
4. [x] Migracion dual `AddDecisionTokenFooterSurvey` (footer_html + survey_json).
