# ADR-0187: Formularios de la tarea padre en un salto de flujo (copia editable en la hija)

Fecha: 2026-11-18
Estado: Aceptada
Version: v0.16.350
Relacionado: ADR-0076 (salto de flujo crea tarea hija), ADR-0077 v2 (formulario obligatorio por salida),
ADR-0078 (formularios derivados / convertir)

## Contexto

Al saltar de un flujo a otro (ADR-0076) se crea una TAREA HIJA con numero propio. Los formularios del padre se
muestran en la hija como "Tarea padre" (solo consulta), pero siguen anclados al numero del padre ("T00886-1").

Caso real AGROMETALICAS (2026-11-18): la tarea T00886 (flujo FLW-007 "GESTION COMERCIAL IA") lleno la
cotizacion COT y salto a FLW-F15A6C, creando T00894. En T00894:

- la COT del padre aparecia como heredada, en Borrador y sin poder editarse;
- el paso "Se prepara la cotizacion para el cliente" EXIGE COT, y el obligatorio (FirstUnfilledRequiredFormAsync)
  busca formularios anclados al numero de la HIJA ("T00894" / "T00894-n"): no encontraba ninguno y bloqueaba.

## Decision

1. **Copia al saltar, solo lo exigido**: al crear la hija, `ChildTaskStarter` copia a la hija los formularios
   del padre cuya definicion es OBLIGATORIA en algun nodo del flujo DESTINO (version publicada efectiva). Uno
   por definicion: el activo del padre, o el original "{numero}", o el mas antiguo (misma regla que la UI).
   La copia es un BORRADOR editable anclado a la hija ("{hija}-{n}"), con todo el Data; el campo "numero" toma
   el numero de la hija (igual que "Copiar").
2. **Solo lo exigido, no todo**: un salto a un flujo que no exige esos formularios (p.ej. cotizacion -> OT,
   FLW-F15A6C -> FLW-002, 152 hijas en prod) no duplica registros ni ensucia los reportes.
3. **"Traer a esta tarea"**: en la tarjeta heredada de la hija, si la tarea esta abierta y aun no tiene uno
   propio de ese formulario, un boton copia el formulario del padre (mismo servicio
   `IFormResponseService.CopyParentFormToTaskAsync`). Resuelve las hijas que ya existen (T00894) sin SQL.
4. **El heredado sigue siendo del padre**: se abre SIEMPRE en solo consulta (antes un borrador heredado podia
   abrirse en modo Fill desde la hija). La copia NO marca `DerivedFromResponseId`: ese campo significa
   "convertido" (ADR-0078) y la cotizacion del padre no se convierte.
5. **Validaciones**: solo se copian formularios anclados al numero del padre de esa tarea; la tarea debe tener
   padre y no estar cerrada. Filtro global de tenant aplica.

Ademas (arreglo menor): el subtitulo "cliente" de la tarjeta ya no toma casillas si/no ni encabezados cuya
etiqueta mencione al cliente ("El cliente suministra el material" pintaba "true").

## Alternativas descartadas

- **Contar los formularios del padre en el obligatorio y editarlos desde la hija**: un solo registro, pero rompe
  la regla de ADR-0076 (lo del padre es del padre), el auto-envio al cerrar no los alcanza y quedarian en
  borrador para siempre.
- **Copiar todos los formularios del padre**: duplica OT/cotizaciones en saltos que no los necesitan.

## Consecuencias

- Las hijas nuevas de saltos cuyo flujo destino exige el formulario lo tienen editable desde el primer paso.
- Las hijas existentes se destraban con "Traer a esta tarea".
- Sin migraciones.
