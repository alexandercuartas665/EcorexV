# ADR-0055: Contactos de WhatsApp por LID: persistir el remoteJid original y usarlo como destino de envio

- Estado: Aceptado
- Fecha: 2026-08-01
- Contexto: modulo 2.3 (Conversaciones WhatsApp) sobre lineas Evolution.

## Contexto

WhatsApp puede identificar a un contacto entrante por su **LID** (Linked Identifier),
un identificador de privacidad con la forma `<numero>@lid` (ej. `265222870818864@lid`).
El "numero" del LID **no es un telefono real**: es un id interno de WhatsApp.

El sistema guardaba en `Conversation.ContactPhone` solo los **digitos** del jid
(`key.remoteJid` del webhook), y al enviar RECONSTRUIA el destino como
`{digitos}@s.whatsapp.net`. Para un contacto por LID ese jid no existe, asi que
Evolution devolvia **HTTP 400** en TODO el envio saliente:

- `sendText` / `sendMedia` / `sendLocation` (campo `number`), y
- `sendReaction` / `deleteMessageForEveryone` (campo `key.remoteJid`).

Con numeros reales (`@s.whatsapp.net`) el flujo funcionaba, porque la reconstruccion
coincidia con el jid real.

## Decision

Persistir el **jid COMPLETO** original del contacto y usarlo como destino del envio,
con fallback a la reconstruccion actual cuando no haya jid guardado:

1. **Entidad + BD:** nueva columna `Conversation.RemoteJid` (`remote_jid`, nullable,
   maxlen 120). Guarda el jid con su sufijo (`@s.whatsapp.net` o `@lid`). Null en
   conversaciones viejas (anteriores a esta funcion). Migracion **dual**
   `AddConversationRemoteJid` (PG `character varying(120)`, SQL Server `nvarchar(120)`),
   encadenada tras `AddConnectorHeadersAndTokenExchange`.

2. **Captura:** el webhook (`EvolutionWebhookParser`) ya extrae el jid completo
   (`key.remoteJid`); ahora lo propaga en `IngestMessageRequest.RemoteJid`. La
   extraccion de `phone` (solo digitos) NO cambia: sigue siendo la clave de
   conversacion por `(tenant, linea, contacto)`.

3. **Persistencia:** `ChatIngestService.IngestTrustedAsync` guarda/actualiza
   `RemoteJid` al crear o encontrar la conversacion (dentro del mismo SaveChanges).

4. **Envio:** los metodos de `IWhatsAppConnectorService` reciben un parametro opcional
   `remoteJid` al final. El destino se calcula:
   - `sendText`/`sendMedia`/`sendLocation` (campo `number`): `remoteJid` si viene, si
     no `digitos`. Evolution v2 acepta un jid completo (incluido `@lid`) en `number` y
     enruta bien tanto LID como numeros normales.
   - `sendReaction`/`deleteMessageForEveryone` (campo `key.remoteJid`): `remoteJid` si
     viene, si no `{digitos}@s.whatsapp.net`.
   Los callers (`ChatService`, `AgentConversationService`) pasan `conv.RemoteJid`
   desde la entidad ya cargada.

## Consecuencias

- Los contactos por LID vuelven a recibir texto, media, ubicacion, reacciones y
  borrado-para-todos.
- **Compatibilidad total hacia atras:** con `remoteJid` null (conversaciones viejas o
  callers que no lo pasan) el comportamiento es identico al de hoy (reconstruccion
  `{digitos}@s.whatsapp.net`). Las conversaciones viejas se auto-curan al entrar el
  siguiente mensaje del contacto (el webhook actualiza `RemoteJid`).
- No cambian las firmas de `IEvolutionApiClient`/`EvolutionApiClient`: solo se les pasa
  un string de destino distinto.
- El proveedor de campo `number` en Evolution es tolerante al jid completo; si un futuro
  proveedor exigiera solo digitos, habria que separar el destino por proveedor.
