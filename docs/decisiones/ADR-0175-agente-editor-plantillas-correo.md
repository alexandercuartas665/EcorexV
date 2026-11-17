# ADR-0174: Agente editor de plantillas de correo y WhatsApp (nucleo generico de chat + bajas + firma)

Fecha: 2026-11-16
Estado: Aceptada (fase 1: correo; fase 2: WhatsApp)

## Contexto

Las plantillas del motor de acciones (ADR-0056/0170) se editaban como HTML crudo en un textarea, con una vista
previa que pintaba el HTML con `MarkupString` en el origen de la app. El usuario pidio un agente que cree, replique
(desde imagen/PDF/HTML) y edite correos como ya lo hace el constructor de formularios (ADR-0067). Decisiones del
usuario (2026-11-16, una a una): el agente vive SOLO en Plantillas de correo; variables nuevas {vendedor} {firma}
{enlace_baja} {telefono} {sector}; alcance correo + WhatsApp; proveedor propio en Servidores de IA; en WhatsApp el
agente redacta Y somete a Meta tras confirmar.

## Decision

1. **Nucleo generico** `TemplateAgentChatService` (Application/Gestor/TemplateAgent): conversacion persistente
   (`TemplateAgentConversation`/`TemplateAgentMessage`, se retoma al reabrir la plantilla), GATE HUMANO (lecturas sin
   gate; mutaciones se PROPONEN y corren tras Confirmar; herramientas IRREVERSIBLES exigen ademas una confirmacion
   explicita aparte -para someter a Meta en la fase 2-), version antes de cada lote que modifica la plantilla,
   proveedor config-driven, cupo por plan (`IAiUsageService`, source "template-builder") y verificacion DETERMINISTA
   obligatoria al cerrar el turno. Cada tipo de plantilla aporta un `ITemplateAgentToolset` (arnes, herramientas,
   verificador, foto/restaurar). No se extrajo el bucle de formularios: tiene autocuracion de ids de contenedor
   propia de ese dominio; formularios puede migrar a este nucleo despues.
2. **Correo** (`EmailTemplateAgentToolset`): get_template / list_variables / verify_email (solo lectura),
   save_template (documento completo; crea si es nueva; preheader opcional) y send_test (a TU correo de usuario, con
   datos de ejemplo). Arnes con reglas de HTML apto para correo (tablas, estilos en linea, 600 px, sin JS).
3. **Verificador** `EmailTemplateVerifier`: variables desconocidas (error), script/form/iframe/on* (error), enlaces
   vacios/#/javascript: (error), falta {enlace_baja} (error: el motor envia correos masivos), img sin alt, http://,
   bloque style/link, asunto > 80 y HTML > 102 KB (Gmail recorta) como avisos. Lo usan el agente, el cierre forzado y
   el editor en vivo.
4. **Versiones** `TemplateSnapshot` (append-only, generico por Kind): antes de cada lote del agente, antes del guardado
   manual y antes de restaurar (asi se deshace una restauracion). Al restaurar o guardar a mano se deja una NOTA DEL
   SISTEMA en el chat para que el agente no reaplique cambios viejos de su historial (visto en la prueba E2E).
5. **Variables nuevas**: {telefono}, {sector}, {vendedor} (asesor vinculado o el texto legado), {firma} (HTML del
   usuario del asesor, NO se escapa en HTML y va sin etiquetas en texto plano) y {enlace_baja}.
6. **Firma por usuario**: `TenantUser.FirmaHtml` (max 4000), editable en Mi cuenta con vista previa en iframe sandbox.
7. **Bajas de correo**: `EmailUnsubscribe` (tenant, correo normalizado; unico). El enlace es `/baja/{token}` con un
   token de Data Protection de PROPOSITO PROPIO ("Ecorex.EmailUnsubscribe.v1": no descifra secretos ni al reves) que
   lleva (tenant, correo): no hay que guardar nada por contacto antes de enviar y no caduca. La pagina publica valida
   (unico punto cross-tenant, solo lectura, correo enmascarado) y registra con el tenant del token como ambient. El
   motor marca Skipped "El contacto se dio de baja de los correos". Quitar una baja (el contacto pidio volver) es
   borrado fisico: es una marca de exclusion, no un agregado de negocio.
8. **Proveedor**: `AiProviderConfig.UseForTemplateBuilder` ("IA gestion de plantillas", single-select). Si ninguno,
   cae al de formularios y luego a Gemini.
9. **Colaterales**: vista previa en iframe `sandbox=""` (Plantillas de correo y disenador de acciones) en vez de
   `MarkupString`; `EmailTemplate` pasa a soft-delete (regla 5).

Migracion dual aditiva `AddTemplateAgentAndEmailUnsubscribe` (4 tablas + 5 columnas).

## Consecuencias

- Ojo proveedor: `deepseek-v4-flash` (modo razonamiento) devuelve 400 en la segunda vuelta con herramientas porque
  exige reenviar `reasoning_content` y el cliente compartido (`AiProviderClient.OpenAiCompatibleWithTools`) no lo hace.
  Afecta igual al constructor de formularios. Mientras tanto, marcar Gemini como "IA gestion de plantillas".
- El enlace de baja requiere `Ecorex:PublicBaseUrl` / `ECOREX_PUBLIC_BASE_URL`; sin URL publica la variable queda
  vacia (el verificador sigue exigiendola en la plantilla).

## Fase 2: WhatsApp (mismo nucleo)

- `WhatsAppTemplateAgentToolset`: get_whatsapp_template / list_whatsapp_context / verify_whatsapp (solo lectura),
  save_whatsapp_draft (crea/actualiza el BORRADOR por `IWhatsAppTemplateService`: validacion, unicidad, auditoria) y
  submit_to_meta (IRREVERSIBLE: confirmacion explicita aparte con casilla "Entiendo..." y el texto final con ejemplos;
  se niega si la verificacion tiene errores; usa `SubmitAsync`, que en lineas YCloud crea la plantilla en Meta).
- `WhatsAppTemplateVerifier` (reglas de Meta): nombre snake_case, idioma, cuerpo <= 1024, encabezado/pie <= 60 y sin
  variables, variable sin ejemplo, variable al inicio/final, variables pegadas, "Params Words Ratio" (>= 3 palabras
  fijas por variable), Authentication no soportada, media sin URL https (errores); promocion en Utility, variable sin
  usar, saltos excesivos (avisos).
- /plantillas-whatsapp: boton "Agente IA" y "Agente" por fila abren un panel con vista previa tipo WhatsApp (ejemplos
  aplicados), verificacion en vivo, versiones con Restaurar (solo si la plantilla es editable) y el chat. El editor
  manual ahora CONSERVA los ejemplos de las variables al guardar (antes los pisaba con el catalogo).
- Arnes: el agente no afirma haber hecho una accion que solo propuso (visto en el E2E: "Listo, ya envie" antes de
  confirmar).
