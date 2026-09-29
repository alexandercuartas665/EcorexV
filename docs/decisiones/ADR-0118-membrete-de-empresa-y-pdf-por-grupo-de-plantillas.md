# ADR-0118: Membrete de empresa por grupo de plantillas y PDF por documento

**Estado:** Aceptado
**Fecha:** 2026-09-28
**Deciden:** Alexander Cuartas (producto), agente de desarrollo

## Contexto

Las plantillas de documento (olas 1-3, ver `plantillas-documento-olas`) permiten redactar
un documento de una tarea a partir de una plantilla del grupo habilitado por su concepto
(000270) y versionarlo en el Gestor Documental. Faltaba poder **imprimir ese documento como
PDF con el encabezado (membrete) de la empresa**.

Decisiones del usuario (dos preguntas):

1. **De donde sale el membrete** -> **por grupo de plantillas**: cada
   `DocumentTemplateGroup` (Cartas, Cotizaciones, Actas...) define su PROPIO membrete, no la
   Entidad principal directamente. El membrete es HTML con tokens `{empresa.*}` que resuelven
   de la Entidad principal del tenant, asi el DISENO es por grupo y los DATOS vienen de la
   empresa.
2. **Como se aplica y donde va el boton** -> **membrete automatico + boton PDF por documento**:
   el membrete se antepone automaticamente al generar el PDF (no es editable dentro del cuerpo
   del documento), y hay un boton "PDF" por documento en la pestana Documentos de la tarea.

## Decision

- **Tokens `{empresa.*}`** en `NotifyTokenResolver`: razon social, nombre comercial, sigla,
  NIT (TaxId+DV), direccion, ciudad, departamento, pais, telefono, email, web, representante
  legal y `{empresa.logo}` (data URI listo para `<img src>`), tomados de la Entidad principal
  del tenant (IsPrincipal, activa). Reutilizables en cualquier plantilla/notificacion.
- **Membrete por grupo**: `DocumentTemplateGroup.HeaderHtml` (HTML con tokens). Se edita en un
  modal dedicado ("Membrete") en `/plantillas-documentos`, con editor TinyMCE tipo carta y la
  paleta de tokens. Servicio `IDocumentTemplateService.SetGroupHeaderHtmlAsync` (solo toca el
  membrete; no pisa nombre/descripcion del grupo).
- **Congelado en el documento**: al redactar (GuardarNuevo) se resuelve el membrete del grupo
  con los tokens de la tarea y se guarda en `Documento.MembreteHtml`. El documento conserva su
  encabezado tal como estaba al redactarlo, aunque el grupo cambie luego (mismo criterio que el
  footer de la pagina de decision, `decision-page-footer-encuesta`). Evita ademas re-resolver
  tokens en un endpoint sin contexto de tenant.
- **PDF**: endpoint `GET /plantillas-doc/documento/{id}/pdf` (AllowAnonymous, acotado por el id
  y su TenantId, como `/cotizacion` y `/formularios/plantilla`). Arma `membrete + cuerpo` en una
  pagina A4 y la renderiza con `IQuotePdfRenderer.RenderHtmlToPdfAsync` (HTML crudo: el logo va
  como data URI embebido, sin loopback). Boton "PDF" por documento en `TaskTemplateDocs`.

## Alternativas consideradas

- **Membrete desde la Entidad principal directamente (una sola plantilla global)**: mas simple
  pero no permite distinto encabezado por tipo de documento. Descartado por decision 1.
- **Re-resolver el membrete del grupo en cada PDF (guardar GroupId en el documento)**: el
  membrete quedaria siempre "al dia", pero obliga a resolver tokens `{empresa.*}` en un endpoint
  sin contexto de tenant (pelea con el filtro global) y hace que un documento historico cambie de
  encabezado si se edita el grupo. Descartado: se prefiere congelar.
- **Header repetido en cada pagina** (Puppeteer headerTemplate): el motor actual usa
  `RenderHtmlToPdfAsync`/`RenderUrlToPdfAsync` sin `displayHeaderFooter`. Para v1 el membrete va
  al inicio del documento (primera pagina). Mejora futura posible.

## Consecuencias

- Cada tenant configura el membrete por grupo una vez; todos los documentos nuevos de ese grupo
  salen con encabezado en el PDF sin pasos extra.
- Documentos ya existentes (creados antes de configurar el membrete) NO tienen `MembreteHtml`:
  su PDF sale sin encabezado hasta que se genere una version/documento nuevo. Aceptable.
- Nuevas columnas (migracion dual `AddPlantillaMembrete`): `document_template_groups.header_html`
  y `documentos.membrete_html` (text / nvarchar(max)). Sin indices nuevos.
- El PDF por-documento comparte la postura de seguridad de los otros PDF (capacidad por GUID v7
  inadivinable, AllowAnonymous).

## Pendientes

- Membrete repetido en cada pagina del PDF (headerTemplate) si se pide.
- E2E del PDF con membrete real en un tenant con Entidad principal + logo cargado.
