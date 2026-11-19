# ADR-0188 - Eventos de entrega de correo (SendGrid Event Webhook) y bajas automaticas

- Estado: Aceptado
- Fecha: 2026-11-18
- Version: v0.16.353
- Relacionados: ADR-0056/0170 (motor de acciones por filtro), ADR-0175 (bajas de correo), ADR-0185 (remitente comercial)

## Contexto

El motor de acciones del gestor de contactos envia correos por SMTP (MailKit). Hasta ahora la ejecucion
solo registraba `Sent`, que significa "el servidor SMTP lo acepto". No sabiamos si el correo se entrego, se
abrio, reboto o lo marcaron como spam, y una direccion que rebota seguia recibiendo correos en cada campana,
lo que dana la reputacion del dominio (SOLDARCO quiere enviar con SendGrid desde su propio dominio).

## Decision

1. **Correlacion por argumentos de SendGrid.** Cuando el servidor SMTP es SendGrid, `SmtpEmailSender` agrega la
   cabecera `X-SMTPAPI` con `unique_args` = `ecx_tenant` (tenant del contexto) y `ecx_run` (id de la ejecucion
   del motor, asignado ANTES de enviar). Solo ids, nunca datos personales; JSON compacto sin espacios. Ademas
   se guarda en `ContactWorkflowRun.ExternalRef` el id `queued as X` que devuelve el SMTP, como respaldo de
   correlacion (prefijo de `sg_message_id`).
2. **Webhook publico** `POST /webhooks/sendgrid/{token}`. El token es aleatorio (32 bytes base64url), se guarda
   en `TenantEmailConfig.EventWebhookToken` (SMTP propio del tenant) o en `EmailConfig.EventWebhookToken`
   (servidor GLOBAL), se genera desde la UI y se puede regenerar (revoca el anterior). Token desconocido = 404.
   - URL del tenant: todo el lote se aplica en el scope ambiente de ese tenant; eventos que digan ser de otro
     tenant se descartan.
   - URL global: la cuenta envia por todos; cada evento se aplica en el tenant de su `ecx_tenant` (sin el, se
     ignora).
   - Aplicar es idempotente; si falla se responde 500 y SendGrid reintenta.
3. **Estado real de entrega** en `ContactWorkflowRun` (columnas nuevas): `DeliveryStatus` (enum
   `EmailDeliveryStatus`, string), `DeliveryUpdatedAtUtc`, `OpenedAtUtc`, `ClickedAtUtc`, `DeliveryDetail`.
   El estado solo SUBE por precedencia (Processed < Deferred < Delivered < Opened < Clicked < Unsubscribed <
   SpamReport < Bounced < Dropped) porque los eventos llegan desordenados. Un clic implica apertura.
   Se ve en la columna "Entrega" del panel "Ultimas ejecuciones" del disenador de workflows.
4. **Bajas automaticas** reutilizando `EmailUnsubscribe` (ADR-0175), que el motor ya respeta, con origenes
   nuevos: `rebote` (bounce definitivo, o dropped por "Bounced Address"/"Invalid"), `spam` (spamreport o dropped
   por "Spam Reporting Address") y `proveedor` (unsubscribe/group_unsubscribe o dropped por "Unsubscribed
   Address"). Un bloqueo temporal (`bounce` tipo `blocked`) NO suprime. `group_resubscribe` solo levanta bajas de
   origen `proveedor` (nunca las de nuestro enlace, manuales, rebotes ni spam). Con la URL del tenant se
   suprimen tambien rebotes de correos que no son del motor (mismo tenant). El motor muestra el motivo al omitir
   ("Correo suprimido: reboto...").
5. **UI**: bloque "Eventos de entrega (SendGrid)" en `/configuracion` (SMTP propio, solo si el host es SendGrid) y
   en `/servidor-correo` (global, PlatformOperator), con la URL, copiar, regenerar y los pasos para configurar el
   Event Webhook y el Open/Click Tracking en SendGrid. La lista de bajas de Plantillas de correo muestra el
   origen legible.

Sin cambios de dependencias. Migracion dual `AddEmailDeliveryEvents` (PostgreSQL + SQL Server), solo columnas
nuevas con default.

## Alternativas descartadas

- **Firma ECDSA del Signed Event Webhook**: mas fuerte que la URL secreta, pero exige guardar la llave publica
  por cuenta. Queda como endurecimiento posterior; la URL secreta es aleatoria, revocable y no expone datos.
- **API v3 de SendGrid en lugar de SMTP**: no hace falta para correlacionar (X-SMTPAPI funciona por SMTP) y
  ataria el envio a un proveedor.
- **Tabla nueva de supresiones**: `EmailUnsubscribe` ya es la lista de exclusion por (tenant, correo) del
  motor; basta un origen nuevo.

## Consecuencias

- Las campanas muestran entregado/abierto/clic/rebote/spam por contacto, y las direcciones malas dejan de
  recibir correos sin intervencion.
- Las aperturas dependen del Open Tracking de SendGrid (pixel), que algunos clientes bloquean: "Entregado" sin
  "Abierto" no prueba que no se leyo.
- Requiere `Ecorex:PublicBaseUrl` en el servidor (ya existe en prod) para construir la URL del webhook.
