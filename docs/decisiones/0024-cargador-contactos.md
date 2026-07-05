# ADR-0024: Cargador de contactos (000873) como importador CSV sobre el CRM real

- Estado: aceptada
- Fecha: 2026-07-05
- Modulo: comer_ContactLoader (000873) -> pagina `/cargador-contactos` en Ecorex.SuperAdmin

## Contexto

El modulo legacy `comer_ContactLoader.aspx` NO carga archivos: es un explorador /
segmentador de contactos scrapeados por N8N (la spec de Capa 6 lo documenta y advierte
que el nombre "Loader" es enganoso). En ECOREX.tareas no existe la ingesta N8N ni la
base `dbn8n`; lo que SI existe es el CRM heredado del backbone (entidad `Lead` +
`PipelineService` + pagina `/pipeline`). El requerimiento de esta ola pide un
**importador masivo de contactos funcional end-to-end sobre ese CRM real**, con la
estructura visual del prototipo `proto_contact_loader.html` y los tokens del workspace
(mismo criterio que ADR-0023: estructura del concepto + sistema visual maestro).

## Decision

1. **Reencuadre funcional**: la pagina `/cargador-contactos` se implementa como
   importador CSV -> Leads del tenant (no como explorador N8N). La estructura del proto
   se conserva y se reinterpreta: sidebar 300px (archivo + mapeo de columnas + historial
   de cargas en lugar de fuentes/filtros/presets), 4 KPIs (filas / validas / duplicadas /
   invalidas), tabs (Previsualizacion / Errores / Resultado) y grilla con avatares,
   badges por fila y paginacion de 30 (PageSize del legacy). Tokens del workspace:
   accent->--brand, success->--t-green/--ok, warn->--t-amber, danger->--t-rose,
   card->--surface, border->--line; claro/oscuro conmutan solos.
2. **CSV primero, Excel despues**: no hay ninguna libreria de Excel referenciada en la
   solucion (ClosedXML/EPPlus ausentes), asi que el alcance es CSV (UTF-8; coma, punto y
   coma o tabulador autodetectados; comillas RFC 4180; filas rotas reportadas con numero
   de linea). Soporte .xlsx queda como deuda documentada; agregarlo sera otra decision
   (libreria + licencia).
3. **Parser puro en Application**: `CsvTableParser` (estatico, sin IO) + DTOs
   (`CsvTable`, `ContactColumnMapping.AutoMap` con sinonimos ES/EN) para que el parseo y
   el mapeo sean testeables por unidad sin base de datos.
4. **Mapeo al modelo Lead REAL**: nombre -> `ContactName` (obligatorio), telefono ->
   `ContactPhone`, destino -> `Destination`, valor -> `EstimatedValue`; email y empresa
   NO existen como columnas del Lead y van al documento de campos configurables
   `FieldValuesJson` (claves `email` / `empresa`), consistente con el pipeline de campos
   por etapa.
5. **Dedup por telefono o email del TENANT**: una fila es duplicada si su telefono
   (solo digitos; con mas de 10 se comparan los ULTIMOS 10, tolerando prefijo de pais)
   o su email (case-insensitive, leido de `FieldValuesJson.email`) ya existe en un lead
   del tenant (activo o archivado) o en una fila anterior del mismo archivo. Sin telefono
   ni email no hay clave de comparacion y la fila se acepta. El filtro global por tenant
   hace imposible el cruce entre tenants (la deteccion de duplicados tambien es
   tenant-scoped, verificado por test dual).
6. **Carga transaccional con historial**: `IContactLoaderService.ImportAsync` inserta
   las filas validas como `Lead` en la PRIMERA etapa del pipeline (asignadas al
   importador, mismo criterio que `LeadService.CreateAsync`), registra `LeadActivity`
   "lead.imported" y persiste `ContactImportBatch` (TenantEntity: FileName, TotalRows,
   Inserted, Duplicates, Invalid) TODO dentro de una unica transaccion (regla 4 de
   CLAUDE.md). Las invalidas/duplicadas se saltan y se reportan por fila; el resultado
   son los conteos del proto. Migracion dual `AddContactImports` (PG + SQL Server).
7. **NavMenu**: el item "Cargador de contactos" (000740/000873) deja de apuntar a
   `/pipeline` y apunta a `/cargador-contactos` (cambio de una linea).

## Consecuencias

- El importador alimenta el embudo real: lo cargado aparece de inmediato en `/pipeline`.
- Recargar el mismo archivo es idempotente (todo cae como duplicado), verificado por test.
- Deudas: soporte .xlsx; explorador N8N del legacy (si algun dia se migra la ingesta
  scraper sera un modulo aparte, como recomienda la spec: `comer_ContactExplorer`);
  limite de archivo 2 MB en el InputFile; el dedup no usa el nombre como clave.
