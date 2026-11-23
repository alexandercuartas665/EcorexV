# ADR-0202: Modulo de Formacion (cursos, manuales, videos y wizards HTML con evaluacion y certificado)

- Estado: Aceptado
- Fecha: 2026-11-22
- Modulo: Formacion (nuevo) + gestor de archivos (StoredFile) + organigrama (000850)
- Relacionados: ADR-0179/0180 (maquetas en la nube: mismo servido de HTML), ADR-0160 (saneador HTML), ADR-0033
  (permisos por modulo)

## Contexto

El usuario quiere en ECOREX el modulo de formacion de su proyecto hermano DokTrino ("Capacitaciones"), que permite
subir manuales y videos, y ademas cargar wizards o manuales interactivos en HTML. El de DokTrino es una compuerta
para sus encuestas TRD: cursos -> modulos -> lecciones (VIDEO/IMAGEN/PDF/TEXTO) con un quiz final, intentos y
desbloqueo; el avance por leccion no se guarda (vive en memoria del componente), no admite HTML ni YouTube/Vimeo,
no asigna por usuario/cargo, no emite certificado y carga cada video completo en memoria al subirlo y servirlo.

Decisiones del usuario (2026-11-22): alumnos = usuarios internos del tenant; catalogo abierto + cursos obligatorios
asignados por usuario/cargo/area con fecha limite; wizard = .zip (carpeta) o .html suelto; alcance: evaluacion
final, enlaces YouTube/Vimeo, certificado PDF y reporte de avance.

## Decision

### Ola 1: nucleo
1. **Modelo** (migracion dual `AddFormacionModule`): `Curso` (agregado, borrado logico, publicado/borrador,
   categoria, portada, duracion), `CursoModulo` y `CursoLeccion` (orden, Cascade con el curso), `CursoAsignacion`
   (Todos | Usuario | Unidad del organigrama + `FechaLimite` DateOnly en la zona del tenant), `CursoProgreso`
   (inscripcion implicita al abrir) y `CursoLeccionVista` (avance PERSISTIDO por leccion; porcentaje y completado
   se recalculan al marcar y cuando el curso gana o pierde lecciones).
2. **Tipos de leccion**: Video (subido), VideoEnlace (solo YouTube -> youtube-nocookie y Vimeo -> player, validado
   al guardar), Pdf, Imagen, Texto (HTML saneado con `ConfidentialityHtmlSanitizer` y mostrado en iframe sandbox
   sin scripts), WizardHtml y Archivo descargable.
3. **Streaming**: `IFileStorageService` gana `SaveStreamAsync` y `OpenReadAsync` (con implementacion por defecto
   para los dobles de prueba). Los binarios fluyen del `InputFile` al blob/disco sin pasar por un `byte[]` (tope
   1 GB video, 100 MB PDF/archivo/wizard, 20 MB imagen) y se sirven en `/formacion/recurso/{leccionId}` con
   `enableRangeProcessing` sobre un stream buscable (adelantar el video sin descargarlo entero). SVG se fuerza a
   descarga.
4. **Wizard HTML**: un `.html` suelto se envuelve en un ZIP como `index.html`; se valida con
   `MockupService.InspectZip` y se sirve con `MockupSiteCache` (el mismo motor de las maquetas). Se muestra en un
   iframe con `sandbox` SIN `allow-same-origin` (origen opaco: el HTML subido no puede actuar como el usuario en
   ECOREX) y la respuesta lleva `Content-Security-Policy: sandbox ...` (aislado aunque se abra en otra pestana).
   Como ese iframe no envia la cookie, los archivos van por `/formacion/w/{token}/...` con un token de Data
   Protection de 12 h que la pagina emite solo a quien ya pudo ver la leccion; el token trae leccion + tenant y la
   consulta se acota a ese tenant y a un curso vivo.
5. **Pantallas**: `/formacion` (obligatorios para ti + catalogo con chips y busqueda), `/formacion/curso/{id}`
   (reproductor con temario, "Marcar vista y continuar"), `/formacion/admin` y `/formacion/admin/{id}` (editor de
   datos, portada, modulos/lecciones con orden y subida, asignaciones). Menu "Formacion" bajo "Cerebros" (backfill
   idempotente). Permisos: `Perm:formacion:View` (alumno) y `Perm:formacion:Edit` (administrar); un curso no
   publicado solo lo ve quien puede editar (vista previa).

### Ola 2: evaluacion, certificado y avance
6. **Evaluacion final** (migracion dual `AddFormacionEvaluacion`): en `Curso` `EvaluacionActiva`, `PuntajeMinimo`
   (70) e `IntentosMax` (3); `CursoPregunta` (opcion multiple, una correcta, retroalimentacion) y `CursoIntento`
   (auditoria de respuestas). Se habilita al ver todas las lecciones, se califica en el servidor (el indice correcto
   nunca viaja al alumno), bloquea al agotar intentos y el desbloqueo da una nueva tanda (`IntentosPerdonados`) sin
   borrar la historia. Todo dentro de una transaccion.
7. **Terminado** = lecciones completas y, si el curso exige evaluacion con preguntas, aprobada. Es lo que usan el
   catalogo (completado / vencido) y el certificado.
8. **Certificado**: codigo de 12 caracteres (alfabeto sin ambiguos, `RandomNumberGenerator`, unico global con
   indice filtrado) emitido al terminar. PDF en `/formacion/certificado/{cursoId}` con el renderizador headless
   existente (`IQuotePdfRenderer.RenderHtmlToPdfAsync`); fecha en la zona del tenant. Verificacion PUBLICA en
   `/formacion/verificar/{codigo}` (unica lectura sin tenant: por codigo no adivinable, datos minimos, noindex).
9. **Reporte de avance** `/formacion/admin/{id}/avance`: obligados por las asignaciones (Todos/usuario/unidad con
   descendientes) + quienes iniciaron; estado (no iniciado, en curso, falta evaluacion, bloqueado, terminado),
   vencidos, intentos, mejor nota, certificado; desbloquear; exportar a Excel (ClosedXML).

## Opciones consideradas

- **Iframe del wizard con `allow-same-origin` y cookie** (como las maquetas): mas simple y con localStorage, pero un
  HTML subido correria con la sesion del usuario dentro de ECOREX. Descartada: se prefiere el origen opaco + token;
  si un wizard necesita almacenamiento local se evaluara una bandera por leccion.
- **Copiar el `byte[]` como DokTrino**: un video de 500 MB ocupa 500 MB de RAM por peticion. Descartada.
- **Certificado por plantilla de documento configurable**: mas flexible, pero no hace falta en la primera version.
  Se deja el HTML fijo con los datos del tenant.

## Consecuencias

- Dos migraciones duales (solo agregan tablas y columnas con default). Sin datos que migrar.
- `IFileStorageService` gana dos metodos con implementacion por defecto (no rompe dobles ni implementaciones).
- Pruebas de integracion dual `FormacionModuleTests` (estructura/orden/tipos/subidas, catalogo y asignaciones con
  fecha limite y vencido, avance persistido y recalculo, aislamiento y borrado logico, wizard firmado por tenant,
  evaluacion con intentos/bloqueo/desbloqueo, certificado y verificacion, avance y Excel).
- Pendiente: notificar asignaciones/vencimientos, recertificacion periodica, plantilla de certificado editable.
