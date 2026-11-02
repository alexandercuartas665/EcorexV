# ADR-0145: disparo de la integracion contable del conciliador (boton manual + fuentes integradas)

**Status:** Accepted
**Date:** 2026-11-02
**Deciders:** Alexander Cuartas (producto), Claude Opus 4.8 (implementacion)

## Contexto

La pestana "Integraciones" del conciliador DIAN (ADR previo / memoria `integracion-contable-cargar-documentos`)
ya permitia CONFIGURAR N integraciones con el sistema contable (core VB.NET, `sweb_cargar_documentos.ashx`):
URL, concepto fijo, usuario, 4 documentos a enviar (PDF/XML del proveedor de facturacion y de la DIAN) y el
mapeo de parametros (documento/tipo/observacion/empresa/cedula/contrato) a columnas de la parrilla. Pero
NADA disparaba el envio: solo existia "Probar conexion" (un ping con payload de prueba, sin archivos).

El usuario definio el disparo:

- **Boton manual "Ejecutar integracion"** en la barra superior, junto a "Aplicar reglas", con reconfirmacion.
  NO depende del flujo de tareas.
- Envia **solo las facturas PENDIENTES que tienen los datos** que alimentan la integracion (el parametro
  "documento" mapeado resuelve a un valor) **y al menos uno de los archivos marcados**.
- **Los archivos salen de los ya capturados por CUFE**: el mismo almacen unificado que alimenta el visor de
  autorizacion (modulo `ConciliacionDian`, Ref1 = CUFE), clasificados por el `Ref2` canonico que fija el bot
  DIAN / la ingesta NEWTON (`DIAN_PDF`, `DIAN_XML`, `NEWTON_PDF`, `NEWTON_XML`).

Decisiones confirmadas con el usuario:

- **Elegible** = el parametro `documento` resuelve a valor no vacio **Y** hay >= 1 de los archivos marcados.
- **Anti-reenvio** = marca "enviado" por renglon en `TenantConfiguration` (mapa renglon->fecha ISO), igual que
  el patron "solicitadas". "Pendiente" = no marcado. **Sin migracion EF.**

## Decision

Nuevo servicio de Application `IConciliacionIntegracionService` (`ConciliacionIntegracionService`), testeable,
con acceso directo a renglones + EAV, al almacen de archivos y a HTTP:

- `EjecutarAsync(documentoId, simular, ct)`: carga las integraciones habilitadas, los renglones del documento
  (estado Conciliar/Conciliado), los campos dinamicos (EAV) y el set de ya-enviados. Por cada renglon resuelve
  los parametros mapeados (columna estatica o `dyn:*`, o texto fijo), clasifica los archivos del CUFE en los 4
  buckets por `Ref2` (fallback heuristico extension+origen para archivos viejos), y:
  - arma el `multipart/form-data` (concepto + usuario fijos + parametros mapeados no vacios + archivos segun los
    4 flags, leidos por `IFileStorageService.ReadAsync`) y lo postea a la URL de cada integracion habilitada.
  - resultado por renglon con prioridad: error > enviado > sin_archivos > sin_datos.
  - marca "enviado" (idempotente) solo los que enviaron con exito y sin error.
  - `simular=true`: no postea ni marca; cuenta elegibles para la confirmacion.
- UI: boton "Ejecutar integracion" junto a "Aplicar reglas" -> dry-run -> dialogo de confirmacion con el conteo
  (N a enviar, ya enviadas, sin dato, sin archivos) -> "Enviar N documento(s)" ejecuta y recarga.
- `IAutorizacionComprasConfigService` gana `GetEnviadosContableAsync` / `AddEnviadosContableAsync` (marca en
  `TenantConfiguration`, clave `conciliacion-dian.enviados-contable`).

Nombres de los campos multipart de los 4 documentos: `pdf_proveedor`, `pdf_dian`, `xml_proveedor`, `xml_dian`
(mas `concepto`, `usuario`, `documento` y los parametros mapeados). Son explicitos y estables.

## Consecuencias

- El envio al contable es una accion manual, idempotente, sin tocar el esquema (reglas = datos, marca = config).
- Reusa el almacen de archivos y la clasificacion por `Ref2` del visor: no duplica el origen de los documentos.
- Cuando la Fase 2 (ADR-0084) reemplace las fuentes DUMMY por la conexion externa, el envio no cambia (los
  archivos ya viven en el almacen unificado por CUFE).

## Pendiente / a confirmar

- **Nombres de los campos multipart de archivos** contra el handler real `sweb_cargar_documentos.ashx` del
  core: no se pudo validar el POST real (handler externo + requiere binarios reales). Ajustar los 4 nombres si
  el handler espera otros.
- **De donde salen los 4 archivos** en produccion (ingesta NEWTON + bot DIAN ya los dejan por CUFE con el Ref2
  correcto) y la **autenticacion** contra el core.

## Validacion (E2E, dev 5263, BD local `ecorex`@5442, tenant SKY SYSTEM)

- Build `Ecorex.sln` verde (Application y SuperAdmin compilan; el fallo de solution fue solo lock de DLLs del
  server de preview). Boton "Ejecutar integracion" visible junto a "Aplicar reglas" (v0.16.257).
- Sin integraciones habilitadas -> mensaje "No hay integraciones habilitadas." (guarda correcto).
- Con 1 integracion habilitada (documento <- Orden Compra) y 1 `stored_file` NEWTON_PDF sembrado para el CUFE de
  FE-5500: el dry-run reporto **1 factura a enviar - 5 sin archivos** sobre los 6 renglones estado Conciliar/
  Conciliado (prueba la resolucion de columna, la clasificacion por Ref2 y la elegibilidad). No se ejecuto el
  POST real (handler externo + archivo de prueba sin binario). Datos de prueba revertidos (config vuelta a
  vacia; queda una fila stored_file inerte en la copia local).

## Archivos

- `apps/backend/src/Ecorex.Application/Automatizaciones/ConciliacionDian/ConciliacionIntegracionService.cs` (nuevo).
- `.../ConciliacionDian/AutorizacionComprasConfigService.cs` (Get/AddEnviadosContableAsync).
- `apps/backend/src/Ecorex.Application/DependencyInjection.cs` (registro DI).
- `apps/backend/src/Ecorex.SuperAdmin/Components/Pages/ConciliacionDian.razor` (boton + dialogo + handlers).
- `apps/backend/src/Ecorex.SuperAdmin/AppVersion.cs` (0.16.257).

## Actualizacion 2026-11-02 (progreso en vivo + lotes + fix timeout, AppVersion 0.16.258)

El usuario reporto que el envio "no deja ver el progreso ni que lotes se generan". Cambios:

- **Progreso en vivo**: `EjecutarAsync` recibe un `Func<IntegracionProgress,Task>? onProgress` (en vez de
  `IProgress<T>`, que en Blazor Server no pinta sin `SynchronizationContext`) y lo invoca/espera factura a
  factura; la UI pinta barra + "Procesando X de Y" + "Cancelar envio", y al terminar la tabla autoritativa
  `IntegracionRunResult.Detalle` (Factura/Proveedor/Estado/`LOT / detalle`).
- **Lotes visibles**: `ParseRespuesta` extrae el/los LOT de la respuesta del contable (`documentos[]`/`numero`);
  el renglon se marca con su LOT (trazabilidad + anti-reenvio), con marcado INCREMENTAL por renglon exitoso.
- **Fix de clasificacion de timeout**: el timeout propio del `HttpClient` tambien se manifiesta como
  `OperationCanceledException`; se re-lanzaba y la UI lo mostraba como "Envio cancelado" con tabla vacia. Ahora
  solo la cancelacion REAL del usuario (`ct.IsCancellationRequested`) aborta; el timeout del contable queda como
  error legible del renglon ("sin respuesta (timeout)"). Techo por POST 60s -> 30s.

Validado E2E (dev 5263, SKY SYSTEM) contra un stub local que devuelve `{ok:true,documentos:["LOT..."]}`: FE-5500
-> Enviado -> `LOT000000000042` visible; 2a corrida -> "1 ya enviada" (no reenvia). El handler real `:47640`
quedo sin responder (acepta TCP, nunca contesta) -> revisar que el core VB.NET este arriba.

## Action Items

1. [ ] Confirmar los nombres de campo multipart de los 4 documentos contra `sweb_cargar_documentos.ashx` y la auth.
2. [ ] Reconciliar las ~43 facturas SOLDARCO de la corrida masiva abortada (no re-ejecutar sobre CCD-0004 SOLDARCO hasta reconciliar).
3. [ ] Commit + deploy a prod (OK del usuario por version).
4. [ ] Probar el POST real contra el core (con `:47640` respondiendo) con una factura que tenga sus 4 documentos por CUFE.
