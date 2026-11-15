# ADR-0170: Correo saliente con MailKit y motor de acciones por filtro con reintento sobre la misma fila

**Estado:** Aceptado (v0.16.330; pendiente de deploy con OK del usuario)
**Fecha:** 2026-11-15
**Decisores:** usuario (dueno del producto), dev principal

## Contexto

La prueba E2E del 2026-11-15 del motor de acciones por filtro (ADR-0056, Cargador 000740) en SOLDARCO
mostro que el motor disparaba bien (segmento, ventana en zona del tenant, paquete) pero NINGUN canal
entregaba:

1. **Correo**: `SmtpEmailSender` usaba `System.Net.Mail.SmtpClient`, que solo habla STARTTLS y NO soporta
   TLS implicito. La config GLOBAL (y la de prod, verificada por SSH en solo lectura) esta en
   `smtp.sendgrid.net:465`: el envio colgaba 15 s y fallaba con "Failure sending mail." (sin causa). Ademas
   el usuario del global no es `apikey`, que es lo que SendGrid exige.
2. **WhatsApp**: el paso del disenador no tenia campo de linea ni de mensaje (TemplateId quedaba null ->
   Skipped "Ventana sin mensaje/plantilla configurada"). Y una linea YCloud/Cloud fuera de la ventana de
   24 h solo acepta plantillas APROBADAS. Los telefonos de los terceros estan en 10 digitos sin +57.
3. **Dedupe**: la fila (paso, ventana, contacto, dia) contaba tambien Failed/Skipped, asi que un fallo
   transitorio o de configuracion bloqueaba el envio todo el dia aunque se corrigiera.
4. **Visibilidad**: las ejecuciones solo se veian en BD.
5. Hallazgo adicional al revisar el guardado: el disenador BORRABA y recreaba todos los pasos y ventanas en
   cada guardado. `ContactWorkflowRun` cuelga del paso con cascada, asi que cada guardado borraba el
   historial y reiniciaba el dedupe: re-guardar el mismo dia volvia a enviar a todo el segmento.

## Decision

- **MailKit** (ya era dependencia de Infrastructure por el lector IMAP de OTP) reemplaza a SmtpClient.
  Modo TLS por puerto (`SmtpEmailSender.ResolveSocketOptions`): 465 = SslOnConnect; 587 con SSL = StartTls;
  otro puerto con SSL = Auto; sin SSL = StartTlsWhenAvailable. Timeout 15 s. El error devuelve la cadena
  de excepciones internas (nunca la clave) y, si el host es SendGrid y el usuario no es `apikey`, la pista.
  Pistas en las pantallas Servidor de correo (global) y Mi cuenta (tenant). Ambas ya tenian "Enviar correo
  de prueba"; ahora muestran el error real.
- **Paso WhatsApp** con config propia en `ContactWorkflowStep.ParamsJson` (`ContactWorkflowWhatsAppParams`:
  LineaId, Texto, PlantillaId, Variables). Evolution/Emulator: texto libre con {nombre} {empresa} {cargo}
  {ciudad} {correo} y vista previa. YCloud/Cloud: plantilla APROBADA + mapeo token -> campo del contacto o
  texto fijo ("txt:..."); el dispatcher envia por `SendTemplateAsync`. Si una variable queda vacia para un
  contacto, ese envio se omite (Meta rechaza parametros vacios). Compatibilidad: ventanas viejas con texto
  en TemplateId y linea en AccountId siguen funcionando y el disenador las sube al paso al guardar.
- **Telefono E.164**: `PhoneNumber.ToE164(phone, defaultCountryCode = "57")` (movil CO de 10 digitos ->
  +57...). No hay pais por tenant en el modelo; el 57 es el default del despliegue (igual que ToMsisdn).
  Se usa en WhatsApp y en la llamada IA.
- **Reintento sobre la misma fila** (`ContactWorkflowRetryPolicy`): Sent nunca se repite; Failed se
  reintenta hasta 3 intentos totales separados 15 min; Skipped (no salio ningun mensaje) se reevalua cada
  15 min sin tope. Columna nueva `ContactWorkflowRun.Attempts` (default 1, migracion
  `AddContactWorkflowRunAttempts` PG + SQL Server) usada como token de concurrencia: dos instancias del
  worker no pueden reclamar (ni enviar) el mismo reintento.
- **Guardado por upsert**: pasos y ventanas viajan con su Id y se actualizan en sitio; solo se borra lo que
  el usuario quito. Conserva historial y dedupe.
- **Panel "Ultimas ejecuciones"** en el disenador (`IContactWorkflowService.ListRecentRunsAsync`): fecha en
  zona del tenant, contacto, paso/canal, estado, intentos y error.

## Opciones consideradas (correo)

| Opcion | Pros | Contras |
|---|---|---|
| MailKit (elegida) | TLS implicito y STARTTLS; errores detallados; ya estaba en el proyecto | Cambia el emisor de todos los correos |
| Seguir con SmtpClient y rechazar 465 al guardar | Cambio minimo | Obliga a reconfigurar; SmtpClient esta obsoleto para codigo nuevo segun Microsoft |

## Consecuencias

- La config global de prod (SendGrid:465) deja de colgarse, pero seguira fallando por autenticacion hasta
  que el usuario cambie el usuario SMTP a `apikey` (no se toco: requiere su OK).
- Las plantillas por linea Cloud: `SendTemplateAsync` aun no soporta Cloud; el paso devolvera ese error.
- Reintentos de Skipped sin tope implican un UPDATE cada 15 min por contacto omitido durante la ventana
  (acotado por el tamano de paquete). Aceptable: no sale ningun mensaje.
