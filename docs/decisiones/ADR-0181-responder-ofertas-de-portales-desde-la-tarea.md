# ADR-0181: Responder ofertas de portales desde la tarea (prellenado, reglas por fila y boton EJECUTAR_DRON)

Fecha: 2026-11-18
Estado: Aceptada
Version: (sin release aun; rama feat/agentes-sistema)
Relacionado: ADR-0166 (paso Crear actividad), ADR-0167 (paso Notificar), ADR-0171 (candado por flujo), ADR-0016 (verbos tipados)

## Contexto

El DRON PLATAFORMAS INGENIOS (SOLDARCO, portal pdpartner de INCAUCA) ya baja cada pedido nuevo como tarea con un
formulario (cabecera + grilla de items) y avisa por WhatsApp. Falta la vuelta: que el comercial llene la oferta en la
tarea y que el dron la escriba en el portal. Decisiones del usuario (2026-11-18): se llena en la tarea ECOREX; el dron
la deja GUARDADA sin enviar y el clic final de "Enviar" lo da una persona; los items se prellenan con la ultima
cotizacion del mismo codigo; se lanza con un boton del formulario; corregir y volver a pulsar reescribe; item no
cotizado = "No cotiza" + motivo; responsable fijo editable; el plazo de la oferta es el vencimiento de la tarea;
encabezado por defecto COP + vigencia 30 dias; diseno valido para varios portales distintos.

La inspeccion del portal (Fase 0, solo lectura) confirmo que pdpartner tiene el estado "Diligenciadas sin finalizar"
(guarda borradores) y que cada fila de la grilla tiene sus inputs (TxtMarca_i, TxtCantCot_i, TxtVrUnit_i, TxtDiasEnt_i,
TxtDcto_i, TxtIva_i, btnTotal_i, CBNotCot_i + DDLMotNoCot_i con AGOTADO / NO LO VENDE / NO LO DISTRIBUYE).

## Decision

1. **Crear actividad** (ScrapeCrearActividad.Config) gana tres claves opcionales, editables en el editor del paso:
   - `vencimiento`: ruta/plantilla de fecha -> DueDate de la tarea, fin del dia (23:59) en la zona del tenant (regla 9).
   - `camposFijos`: codigo -> literal para lo que el registro no trae (acepta {hoy}/{hoy+N}); no pisa lo leido.
   - `prellenado`: sugerencias para las celdas VACIAS de la grilla desde un Contenedor historico: por clave (ej. CODIGO)
     se elige la fila con mayor `ordenColumna` (numerico si se puede; ej. PETOF), excluyendo el mismo registro y las
     filas sin `requiereColumna` (ej. VLR_UNI). Nunca pisa valores ya presentes.
2. **Reglas por fila de grilla** (FormGridRowRules, DATO en options_json de la columna, igual que required/min):
   `requiredWhen` (obligatoria si otra columna de la fila esta marcada), `requiredUnless` (obligatoria salvo que otra
   este marcada) y `maxCol` (valor <= otra columna). "Marcada" = no vacia y distinta de false/no/0/[]. Se validan al
   ENVIAR el formulario (FormFieldValidator.ValidateGrid) y desde los botones (ValidateGridValue). No se agrega un tipo
   de columna checkbox: "No cotiza" es una lista Si.
3. **Verbo EJECUTAR_DRON** (SuperAdmin, registrado como IRuleVerb junto al runtime del dron): un Button del formulario
   valida (campos requeridos + grilla completa con sus reglas), comprueba flujo/agente/candado y lanza el flujo en
   SEGUNDO PLANO con dos variables de corrida (no persistidas): `{{TAREA}}` y `{{FORM}}` (objeto JSON con todos los
   campos; las grillas anidadas como arreglos: `JSON.parse('{{FORM}}')`, `{{FORM.items.0.vlr_uni}}` o un bucle sobre
   FORM.items). Al terminar deja un comentario en la tarea (pasos OK o los que fallaron) y adjunta la ultima captura.
4. **Un flujo por portal** ("Responder oferta - <portal>"): el formulario de la tarea es el contrato; cada portal tiene
   su propio flujo que lee FORM y escribe en su pagina. El flujo decide si guarda o envia (pdpartner: GUARDA).

## Consecuencias

- Agregar un portal nuevo es configuracion (flujo + formulario), no codigo.
- El borrador no se valida al guardar (comportamiento previo); por eso el boton valida antes de lanzar.
- La corrida usa el candado por flujo: dos tareas no pueden pasar ofertas al mismo portal a la vez (el boton avisa).
- Pendiente: ids de Guardar/Finalizar de pdpartner (solo aparecen con una oferta pendiente; el flujo FASE0 local ya
  esta listo para leerlos) y el cierre automatico al ver la oferta Concluida en el historico (fase 3).
- Pendiente de UI: el disenador de formularios conserva las claves nuevas de columna pero aun no las edita.
