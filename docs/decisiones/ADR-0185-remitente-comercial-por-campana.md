# ADR-0185: Remitente comercial por campana en el paso E-mail del gestor de contactos

Fecha: 2026-11-18
Estado: Aceptada
Version: v0.16.347
Relacionado: ADR-0056/0170 (motor de acciones por filtro), ADR-0177 (marca de correo)

## Contexto

Los correos del motor de acciones salen con el remitente del servidor SMTP resuelto en cascada (el del tenant
en /configuracion o, si no tiene, el GLOBAL de la plataforma). En prod, SOLDARCO no tiene servidor propio, asi
que sus campanas llegaban como "soporte <soporte@bitcode.com.co>". El usuario pidio que lleguen "como si fuera
de un correo comercial". Se decidieron las dos vias:

A. (Sin codigo) configurar el servidor de correo propio del tenant con la cuenta comercial.
B. (Este ADR) poder fijar el remitente POR CAMPANA, para separar comercial de soporte/notificaciones.

## Decision

1. `IEmailSender` gana una sobrecarga con `EmailSenderOverride(FromName, FromEmail, ReplyTo)`. Es un metodo
   por defecto de la interfaz que ignora el remitente, asi las implementaciones de prueba no cambian.
2. `SmtpEmailSender.ResolveSender` (puro): el nombre visible propio siempre gana; la direccion propia solo se
   usa como From si es del MISMO dominio que la direccion configurada en el servidor (el dominio autenticado
   en SendGrid/Office365). Si es de otro dominio NO se pone como From (romperia SPF/DKIM: spam o rechazo) y
   pasa a Reply-To, para que las respuestas lleguen al comercial. "Responder a" explicito gana como Reply-To.
3. El paso E-mail guarda el remitente en `ParamsJson` (`ContactWorkflowEmailParams`: RemitenteNombre,
   RemitenteCorreo, ResponderA). Sin migracion: `ParamsJson` ya existe. Vacio = remitente del servidor.
4. El disenador del gestor de contactos muestra "Remitente de la campana (opcional)" con la regla explicada.

## Consecuencias

- Una campana de SOLDARCO puede salir como "Comercial SOLDARCO <soporte@bitcode.com.co>" con respuestas a
  comercial@soldarco.com sin configurar nada mas. Para que la DIRECCION sea @soldarco.com hay que: (A) poner el
  servidor de correo propio de SOLDARCO en /configuracion, o (B) autenticar el dominio soldarco.com en
  SendGrid (registros DNS) y configurarlo como servidor del tenant.
- Las notificaciones y correos transaccionales no cambian (siguen con el remitente del servidor).
