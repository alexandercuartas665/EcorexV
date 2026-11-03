# ADR-0147: "Procesar archivos" lee los ZIP del agente cuando el servidor no ve la carpeta

Fecha: 2026-11-03
Estado: Aceptada
Relacionada: ADR-0045 (bitacora de agentes colmena), canal request/response del Navegador
(`IBrowserActionChannel`), sub-agente Archivos (handler `FileRequest`/`FileResult` del agente).

## Contexto

El paso "Procesar archivos descargados" del dron DIAN (`BrowserRunService.RunProcesarArchivosStepAsync`)
parsea los ZIP ya bajados (DIAN UBL), hace UPSERT por CUFE en la fuente Bot, sube PDF/XML al blob y, si
se pide, importa al CCD del mes. Hasta ahora leia la carpeta con `Directory`/`ZipFile.OpenRead` del
**filesystem del propio servidor**.

Eso funciona en una sola maquina (app y agente en la misma PC, dev). En PRODUCCION no: la app corre en
Linux (Docker) y el agente que descarga de la DIAN corre en Windows, en la red del cliente. El servidor
NO ve la carpeta de descargas del agente. Sintoma real: "solo se subieron 2 archivos de 15 descargados"
-el server procesaba lo poco que alcanzaba a ver (o nada), no los ZIP que el agente tenia en su disco-.

El agente ya sabe tocar su filesystem: existe el sub-agente Archivos (handler `FileRequest`/`FileResult`,
desde 2026-07-16) con acciones List/ReadBytes/Delete sobre una carpeta de la allow-list. Lo que faltaba en
el servidor era un canal para pedirle esas acciones y AWAITAR el resultado, como ya existe para el
Navegador (`IBrowserActionChannel`).

## Decision

1. **Canal `IFileActionChannel`** (`Agents/FileActionChannel.cs`), gemelo exacto de
   `IBrowserActionChannel`: une el `FileRequest` saliente con el `FileResult` entrante por
   `correlationId` usando un `TaskCompletionSource`, con timeout por espera. Singleton (mantiene las
   esperas entre invocaciones del hub). `AgenteHub` inyecta el canal y, al recibir un `FileResult`,
   llama `_fileChannel.TryResolve(msg)` (resuelve la espera; si no hay, no hace nada).

2. **Discriminador robusto por construccion** en `RunProcesarArchivosStepAsync`:
   `cfg.ForzarViaAgente || !Directory.Exists(carpeta)` -> camino "via agente"
   (`RunProcesarArchivosViaAgenteAsync`). Es decir: si el servidor NO ve la carpeta en su propio disco
   (prod), fetchea del agente; si la ve (local de una sola maquina), toma el camino directo de siempre.
   No depende de configurar "prod vs dev": se elige solo por si la ruta existe localmente. El flag
   `ForzarViaAgente` solo sirve para PROBAR el camino remoto en dev con una sola PC.

3. **El camino via agente replica el directo byte por byte**, pero con las acciones del sub-agente:
   - `List` remoto para la espera de "asentado" (encuesta hasta que el conteo no cambie) y para enumerar.
   - `ReadBytes` (base64) por ZIP -> `DianUblParser.ParseZipBytes(byte[])` (nuevo overload que parsea
     desde los BYTES, no desde un path) -> MISMO upsert por CUFE en la fuente Bot.
   - Blob: extrae PDF+XML del ZIP en memoria y los sube por CUFE (idempotente, igual que el directo).
   - "Mover procesados" en remoto = `Delete` del ZIP de origen tras confirmar el SaveChanges (el blob ya
     conserva PDF+XML). Si la carpeta no es de escritura (sin prefijo `rw:` en la allow-list) el borrado
     falla y NO pasa nada: el dedup por CUFE evita reprocesar (solo se releen los bytes en vano).

4. **No hay que recompilar el agente**: el handler `FileRequest`/`FileResult` ya existe. El requisito del
   operador en la colmena de prod es de CONFIGURACION: habilitar la capacidad "Archivos" y agregar la
   carpeta de descargas a la allow-list (con prefijo `rw:` si se quiere limpiar el origen).

## Por que un canal nuevo y no reusar el del Navegador

El canal del Navegador transporta `BrowserRequest`/`BrowserResult` (acciones de pagina). Los archivos son
otro contrato (`FileRequest`/`FileResult`, acciones List/ReadBytes/Delete) y ya tienen su propio handler en
el agente. Reusar el mismo tipo mezclaria dos dominios; un canal gemelo es trivial (misma mecanica de
correlationId + TCS) y deja cada contrato aislado.

## Por que leer bytes y no montar la carpeta en el servidor

Un recurso compartido (SMB/NFS) entre el Linux de prod y el Windows del cliente seria fragil, exigiria red
y credenciales entre ambos entornos, y rompe el modelo de la colmena (el agente es quien tiene acceso a la
maquina del cliente, no al reves). Leer los ZIP por el canal ya establecido del agente no agrega
infraestructura ni supuestos de red.

## Consecuencias

- El paso procesa TODOS los ZIP del agente en prod (se acaba el "solo 2 de 15"), sin tocar el agente.
- El camino local de una sola maquina queda intacto (sin regresion): solo cambia cuando la carpeta no
  existe localmente o se fuerza el flag.
- Tope de 5 MB por `ReadBytes` del sub-agente (los ZIP DIAN entran holgados); un archivo que no entre se
  cuenta como fallido y no rompe el paso.
- El borrado del origen depende del permiso `rw:` de la allow-list; sin el, el dedup por CUFE evita duplicar.
- Sin migracion: el flag vive en el `MappingJson` del paso (texto); no hay esquema nuevo.
