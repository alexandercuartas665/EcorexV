# ADR-0132: Integridad de respuestas de formulario (soft-delete + auditoria + guards)

Fecha: 2026-10-26
Estado: Aceptada

## Contexto

En prod (tenant AGROMETALICAS, tarea T00521) una cotizacion ENVIADA al cliente y aprobada se perdio sin rastro.
Causa raiz: `FormResponseService.DeleteRecordAsync` hacia un DELETE FISICO de la fila `form_responses` (sin
soft-delete ni auditoria) y, al reabrir la tarea, `GetOrCreateDraftAsync` -que solo buscaba borradores- creaba un
draft nuevo VACIO que "tapaba" la respuesta. No habia ninguna pista: los cambios/borrados de respuestas dentro de un
tenant no se registran en `AdminAuditLog` (eso es solo PlatformAdmin). Solo se recupero el contenido porque el PDF
generado quedo en disco del contenedor.

## Decision

1. **Soft-delete** de `form_responses` (`ISoftDeletable`: `IsDeleted` + `DeletedAt` + `DeletedByTenantUserId`). Nunca
   DELETE fisico: se preserva el jsonb de `Data`. Filtro global combinado **tenant + `!IsDeleted`**
   (`ApplyTenantSoftDeleteFilter`), fail-closed; para ver/restaurar se usa `IgnoreQueryFilters`.
2. **Auditoria INMUTABLE y tenant-scoped** (tabla `form_response_audits`, append-only): `Create/Submit/Delete/Void/
   PdfSnapshot` con actor (usuario o agente IA), cuando (CreatedAt), responseId, definicion, reference y **SNAPSHOT
   del jsonb** anterior (Delete/Void) o enviado (Submit/PdfSnapshot). El autosave continuo NO se audita por fila
   (seria ruido; el snapshot de Submit captura el estado final).
3. **Guards**: `DeleteRecordAsync` RECHAZA borrar una respuesta `Submitted` (una enviada se ANULA con Void, no se
   borra). `GetOrCreateDraftAsync` ABRE la respuesta existente para la `(definicion, reference)` -enviada primero,
   luego borrador- en vez de crear un draft vacio que la tape.
4. **Snapshot al generar el PDF**: `QuoteDocumentRenderer` llama `RecordPdfSnapshotAsync` (best-effort) para dejar el
   estado que se envio al cliente, por si luego se pierde la fila.

## Consecuencias

- Migracion DUAL `AddFormResponseSoftDeleteAndAudit` (aditiva: 3 columnas en form_responses + tabla
  form_response_audits con indices por tenant).
- Ya no se puede borrar una respuesta enviada desde la UI (cambio de comportamiento deseado). El "Eliminar" de
  borradores pasa a ser soft-delete auditado.
- Toda consulta normal de respuestas excluye las soft-borradas (filtro global); procesos que necesiten verlas usan
  IgnoreQueryFilters.
- La auditoria permite reconstruir datos y saber quien/cuando hizo cada accion sobre una respuesta.
