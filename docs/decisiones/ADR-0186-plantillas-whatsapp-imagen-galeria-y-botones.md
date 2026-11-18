# ADR-0186: Plantillas de WhatsApp con imagen de la galeria y botones (agente de plantillas)

Fecha: 2026-11-18
Estado: Aceptada
Version: v0.16.347
Relacionado: ADR-0175 (agente editor de plantillas), ADR-0177 (galeria de imagenes), ADR-0184 (estado YCloud)

## Contexto

El agente de plantillas de WhatsApp solo manejaba encabezado de TEXTO: la imagen la subia el usuario a mano en el
editor, y los botones solo existian en plantillas IMPORTADAS de YCloud (de solo lectura). El usuario pidio que el
agente cree plantillas "espectaculares" con imagenes de una minigaleria y con botones.

Meta acepta la imagen del encabezado como un ejemplo por URL publica (`example.header_url`, ya soportado al
someter) y exige que esa imagen se mande en CADA envio. La galeria de ADR-0177 ya sirve imagenes en una URL
publica anonima `/i/{id}{ext}`.

## Decision

1. **Galeria compartida**: el panel del agente de WhatsApp gana la pestana "Galeria" (mismo componente
   `TemplateGalleryPanel` y mismas imagenes que los correos). Herramienta del agente `list_images` (solo JPG/PNG,
   lo unico que Meta acepta en el encabezado) y argumento `imagen_url` en el borrador.
2. **Botones al crear** (`WhatsAppTemplateButtons`): respuesta rapida, sitio web con URL https FIJA y llamar.
   Se normalizan (agrupados: respuestas rapidas primero, Meta no permite intercalarlos), se validan con las reglas
   de Meta (max 10; max 2 de sitio web; max 1 de llamar; texto de 1 a 25 caracteres, sin variables ni repetidos;
   URL https; telefono internacional) al guardar y en el verificador, se guardan en `ButtonsJson` (misma forma
   que el import) y se compilan al componente `BUTTONS` al someter. `SaveWhatsAppTemplateRequest.Buttons` null =
   conservar los actuales (el editor manual y las versiones viejas no los pisan). Botones URL con variable siguen
   solo para plantillas importadas.
3. **Envio**: el paso WhatsApp del motor de acciones manda la media del encabezado en cada envio; si la plantilla
   tiene encabezado de media sin archivo, el envio queda Skipped con un motivo claro.
4. **Diseno de alto impacto** en el arnes del agente (regla por defecto): imagen de la galeria en Marketing,
   cuerpo escaneable con beneficios en lineas cortas y emojis medidos, negrita solo en lo clave, 1 accion
   principal + respuestas rapidas, pie con opcion de baja en Marketing.
5. La vista previa del panel muestra la imagen real y los botones como los pinta WhatsApp.

## Consecuencias

- El agente puede proponer plantillas completas (imagen + cuerpo + botones) que se someten a Meta tal cual.
- Las imagenes de la galeria son publicas por URL (necesario para Meta y los correos); no subir material
  confidencial.
- Sin migracion: `ButtonsJson`, `HeaderMediaUrl` y `TemplateAsset` ya existian.
