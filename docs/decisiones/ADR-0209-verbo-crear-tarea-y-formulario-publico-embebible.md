# ADR-0209: Verbo CREAR_TAREA y formulario publico /f/{token} embebible en cualquier dominio

**Estado:** Aceptado
**Fecha:** 2026-11-24
**Decisores:** usuario (SOLDARCO, formulario de leads FRM-LEAD), ECOREX.desarrollo

## Contexto

SOLDARCO quiere captar leads con su formulario publico (`FRM-LEAD`, def 9b38521e) embebido como iframe en su
sitio web. Dos huecos:

1. El motor de reglas solo crea tareas con `GENERAR_TAREAS_DESDE_TABLA` (N tareas desde un campo tabla,
   clasificadas por ActivityType). Un lead debe crear UNA tarea clasificada por CONCEPTO (subcategoria) para
   que caiga en el tablero del concepto (f1.REGISTROS DE LEAD) como las demas.
2. Toda respuesta HTML del app sale con `X-Frame-Options: SAMEORIGIN` y
   `Content-Security-Policy: frame-ancestors 'self'`. No vienen de un middleware propio (ni de un proxy: el
   contenedor ecorex-app atiende directo) sino del framework: `UseAntiforgery` agrega X-Frame-Options al
   emitir el token y Blazor Interactive Server agrega frame-ancestors (ContentSecurityFrameAncestorsPolicy).
   Con eso el iframe en otro dominio queda bloqueado.

## Decision

1. **Verbo `CREAR_TAREA`** (`Rules/Verbs/CrearTareaVerb.cs`): crea una tarea con `SubcategoriaId` (o
   `activityTypeId` de respaldo) y SIN BoardId, asi `TaskItemService` deriva el tablero del concepto y la
   ubica en su primera columna. Titulo/descripcion por plantilla con tokens `{campo}` del FormData (o
   `titleField`); asignado por param (form anonimo sin actor); solicitante (nombre/correo/telefono) desde
   campos; `marcaCreadaField` como anti-duplicado (si trae valor no crea; tras crear escribe el numero de la
   tarea con `RuleAction.SetValue`). `SourceTaskId` = tarea del contexto, igual que el verbo de tabla.
2. **Framing solo para /f/** (`Endpoints/PublicFormFraming.cs`): middleware registrado antes de
   `UseAntiforgery` que, en `OnStarting` (cuando el framework ya puso sus cabeceras), quita
   `X-Frame-Options` y fija `frame-ancestors *` conservando otras directivas CSP. El usuario eligio
   "cualquier dominio". El resto del app (consola, /login, /admin) conserva el anti-framing.

## Opciones consideradas

- **Lista blanca de dominios por tenant** en vez de `*`: mas estricto, pero exige configuracion por cliente
  y el usuario pidio cualquier dominio. Queda como evolucion si hace falta.
- **Desactivar el anti-framing global** (`SuppressXFrameOptionsHeader`, policy null): abre la consola a
  clickjacking. Descartado.
- **Reusar GENERAR_TAREAS_DESDE_TABLA con filas fijas**: no clasifica por concepto ni arma el titulo con
  datos del formulario. Descartado.

## Consecuencias

- El formulario publico se puede embeber en sitios de terceros; el riesgo de clickjacking en /f/ es bajo
  (formulario anonimo de captura, sin sesion). Verificado en local: /f/ embebido desde otro origen carga y
  el circuito Blazor responde (validacion server-side); /login embebido queda bloqueado.
- Cookies de terceros: el visor anonimo no depende de cookies (el circuito no las usa), asi que el bloqueo
  de cookies de terceros del navegador no lo afecta.
- Sin migracion. La configuracion (boton/regla on-field/token) la aplica la sesion de formularios por datos.
