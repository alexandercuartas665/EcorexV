# ADR-0116: Codigo automatico al crear la respuesta de un formulario (num_cotizacion)

**Status:** Accepted
**Date:** 2026-09-28
**Deciders:** Alexander Cuartas (producto), agente de desarrollo

## Contexto

En el formulario de cotizacion (COT) el campo "COD COT" (`num_cotizacion`) lo tecleaba el usuario a
mano. Se pidio que se GENERE solo al CREAR la cotizacion (aunque quede en borrador), con formato
`{iniciales del vendedor}-{consecutivo}`, p.ej. `RG-0001`. El "vendedor" NO es un campo del formulario:
son las iniciales del ASIGNADO de la tarea (`TaskItem.AssigneeTenantUserId`). El consecutivo es GLOBAL
por tenant (un solo contador, no uno por vendedor): `RG-0001`, `JA-0002`, `RG-0003`.

## Decision

1. **Config self-serve** en el disenador (`FormDesigner`, pestana Registro/Datos): bloque "Codigo
   automatico al crear" con casilla de habilitado, desplegable del campo Text destino y ancho del
   consecutivo. Persiste en `FormDefinition` (`AutoCodeEnabled`, `AutoCodeTargetFieldCode`,
   `AutoCodePadWidth`) via `SetTransactionalAsync`. Migracion **dual** `AddFormAutoCode`.

2. **Generacion al crear**: en `FormResponseService.CreateTaskFormAsync` (justo despues de armar el
   `data`), `ApplyAutoCodeAsync` resuelve las iniciales del asignado con `MemberInitials.From(nombre)`
   (primeras letras de las 2 primeras palabras, mayuscula) y pide el consecutivo.

3. **Consecutivo GLOBAL por tenant reutilizando `SequenceService`** (contador atomico con CAS+retry,
   portable dual) bajo un codigo constante `"AUTOCOD"` (un unico contador por tenant, no por campo ni
   por vendedor). `EnsureSequenceAsync` antes de `NextAsync`.

4. **Idempotencia**: si el campo destino ya trae un valor real (distinto del `reference` que el
   autorelleno heredado pudo dejar), NO se pisa; no se regenera en autoguardados.

Solo aplica a cotizaciones NUEVAS; NO hay backfill de las existentes en este cambio.

## Opciones consideradas

- **A (elegida): contador global via `SequenceService` con codigo `"AUTOCOD"`.** Reutiliza el motor de
  secuencias (ya usado para `record_number`), atomico, dual y sin tabla nueva. Contra: un unico namespace
  de consecutivo por tenant (justo lo pedido).
- **B: contador por vendedor.** Descartada: el requisito es explicitamente un consecutivo GLOBAL.
- **C: `MAX(num)+1` al vuelo.** Descartada por race conditions y por acoplar el numero al parse del
  texto del campo.

## Consecuencias

- **Mas facil**: el vendedor no teclea el codigo; queda trazable a quien tiene asignada la tarea; el
  consecutivo no se repite ni bajo concurrencia (CAS de `SequenceService`).
- **A vigilar**: las iniciales dependen del nombre del asignado (si no hay asignado -> `XX`). El
  contador es por tenant; si se quisiera reiniciar por ano habria que versionar el codigo de secuencia.

## Action Items

1. [x] `FormDefinition` + EF config (PG y SqlServer) + migracion dual `AddFormAutoCode`.
2. [x] `ApplyAutoCodeAsync` en `CreateTaskFormAsync` (iniciales + `SequenceService` codigo `AUTOCOD`).
3. [x] Bloque "Codigo automatico al crear" en `FormDesigner` (checkbox + destino + ancho).
4. [x] Tests matriz dual (`FormAutoCodeTests`): iniciales+consecutivo, +1 global entre vendedores,
   idempotencia en autoguardado, sin duplicado bajo concurrencia.
