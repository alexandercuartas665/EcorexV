# ADR-0176: Marca de correo, galeria de imagenes y estilos de diseno del agente de correos

Fecha: 2026-11-17
Estado: Aceptada
Version: v0.16.338
Relacionado: ADR-0175 (agente editor de plantillas), ADR-0056/0170 (motor de acciones, paso E-mail)

## Contexto

Con v0.16.337 el agente de correos ya producia por defecto correos "de agencia" (banda de marca, tarjetas, CTA,
pie). El usuario pidio tres cosas mas para subir el nivel (2026-11-17):

1. Que los correos lleven **logo y color de marca** reales (hoy el agente inventaba la paleta y dejaba marcadores
   como "[Direccion de la empresa]" en el pie).
2. Una **pequena galeria** para cargar imagenes (fotos, banners) y que el agente las use.
3. Un **selector de estilos** para que la IA afine el detalle del diseno.

En correo las imagenes NO pueden ir incrustadas (Gmail/Outlook bloquean `data:` base64 y SVG): deben ser una
URL publica `https://`. El gestor de archivos existente (`/archivo/{id}`) exige sesion, asi que no sirve para
correos (el lector no esta logueado).

## Decision

1. **Marca de correo** (`EmailBrand`, tenant-scoped, una fila por tenant, indice unico): logo especifico para
   correos (opcional; si no, `Tenant.LogoUrl`), color principal, color de acento, estilo por defecto, sitio web y
   texto adicional del pie. Los datos de contacto (direccion, ciudad, telefono, correo) se toman de la ficha de la
   empresa. `EmailBrandService` valida colores `#RRGGBB` (acepta `#abc`) y la URL del sitio. Se edita en
   /plantillas-correo (tarjeta "Marca de correo" + modal con muestra en vivo de banda y boton).
2. **Galeria**: se REUTILIZA `TemplateAsset` (galeria de plantillas heredada del backbone, sin UI) agregando
   `Alt`, `Width`, `Height`, `StoredFileId` y soft-delete. `TemplateGalleryService` sube el binario al gestor
   unificado de archivos (modulo `galeria-plantillas`, Azure Blob o disco), valida el formato por la FIRMA del
   contenido (PNG/JPG/GIF; SVG y WEBP se rechazan porque no se ven en Gmail/Outlook), tope 3 MB, y lee las
   dimensiones de la cabecera (`ImageDimensions`, sin librerias). Se registra en la consola (depende de
   `IFileStorageService`, que solo existe ahi; asi no rompe `ValidateOnBuild` de otros hosts).
3. **Endpoint publico** `/i/{id-del-archivo}{ext}` (AllowAnonymous) que usa `IFileStorageService.ReadPublicAsync`,
   ACOTADO al modulo de la galeria: ningun otro archivo del sistema se expone sin sesion. La llave es un Guid v7
   (no adivinable); cache `public, max-age=31536000, immutable` (el contenido de una llave no cambia).
   "Quitar" de la galeria es soft-delete: el binario se sigue sirviendo para los correos ya enviados.
4. **Estilos de diseno** (`EmailDesignStyles`): catalogo de 7 estilos (corporativo, moderno degradado,
   minimalista, newsletter editorial, elegante premium, promocional de impacto, carta personal), cada uno con una
   GUIA concreta (medidas, colores, componentes). El arnes trae la guia de TODOS (texto estable: aprovecha el
   cache del prompt) y el estilo elegido viaja al inicio del mensaje del usuario como `[estilo: clave]` (el panel
   lo antepone con `MessagePrefix` y lo oculta al mostrar el historial). Asi no se cambio la interfaz generica
   `ITemplateAgentToolset` ni hizo falta columna en la conversacion. `EmailTemplate.Estilo` guarda con que
   estilo se compuso (lo fija `save_template` con el parametro `estilo`); el editor lo preselecciona y, si el
   usuario elige otro, ofrece "Redisenar en <estilo>".
5. **Agente**: nueva herramienta de solo lectura `list_images` (galeria con URL absoluta, alt y dimensiones);
   `list_variables` devuelve la marca completa; el arnes agrega secciones MARCA (colores exactos, logo, datos
   reales del pie, sitio web como destino por defecto de botones), ESTILOS e IMAGENES (solo URLs de la galeria,
   del logo o del usuario; nunca bancos externos; width/alt/estilo de imagen fluida). Se reforzo la regla de
   honestidad ("acabo de enviar" antes de confirmar, visto en el E2E de v0.16.337).
6. **Verificador**: reglas nuevas para `<img>`: sin `src` (error); `data:` base64, ruta relativa, `http://` y SVG
   (avisos).

Migracion dual aditiva `AddEmailBrandGalleryStyles` (tabla `email_brands` + 7 columnas en `template_assets` + 1 en
`email_templates`).

7. **Editor en modal + tarjetas** (pedido del usuario, misma version): la lista de plantillas pasa a TARJETAS con
   miniatura del correo renderizado (iframe sandbox escalado) y una tarjeta "+ Nueva plantilla"; el editor abre en
   un MODAL de 2 columnas: izquierda con pestanas Vista previa | HTML | Galeria | Versiones, derecha el selector de
   estilo + chat. Al terminar el agente, la tarjeta aparece/actualiza en la lista de fondo. La galeria se extrajo a
   `TemplateGalleryPanel` (pestana del editor, modal de la lista y selector de logo).
8. **Versiones visibles**: pestana con la lista (etiquetas legibles), "Ver" (pinta la version en la vista previa con
   un aviso, sin tocar la actual), "Restaurar" (la actual queda guardada: se puede deshacer) y "Guardar version
   ahora". `ITemplateSnapshotService.GetPayloadAsync` (miembro por defecto) y la foto de correo incluye `Estilo`.
9. **Idioma**: regla de espanol al FINAL de ambos arneses (en el E2E, tras el JSON de una herramienta, Gemini
   respondio en frances).

## Consecuencias

- Las imagenes solo se ven en el correo si la app tiene URL publica (`ECOREX_PUBLIC_BASE_URL`); en local el
  verificador avisa (http://localhost o ruta relativa) y la vista previa del editor si las muestra.
- `/i/...` es el unico punto anonimo del gestor de archivos; cualquier modulo nuevo que quiera exponerse debe
  pasar por una decision explicita (no se abre por defecto).
- El estilo `personal` es una excepcion deliberada a "no parrafos planos" (entregabilidad: llega a Principal).
