# ADR-0164 - Servidor MCP de WhatsApp por linea Evolution API

- Estado: Aceptado (implementado en rama `claude/mcp-whatsapp-evolution`, v0.16.310, SIN deploy)
- Fecha: 2026-11-13
- Relacionado: ADR-0159 (MCP personal de tableros), ADR-0067 (MCP de administracion), ADR-0125 v2 (llave del hilo)

## Contexto

El usuario quiere operar sus lineas de WhatsApp (Evolution API) desde una IA externa (Claude, Codex, Gemini) con
"todo el poder" de Evolution: enviar, leer, contactos, grupos... y tiene VARIAS lineas conectadas. Pidio que la
configuracion viva EN CADA LINEA del modulo de Lineas (conectar la linea y luego su opcion de configuracion).

Hallazgos del mapeo del codigo:
- Un solo cliente Evolution v2 (`IEvolutionApiClient`) que usaba ~13 endpoints de ~95 (v2.3.7).
- Todas las lineas Evolution de un tenant comparten servidor y llave (propio o maestro); el nombre real de la instancia
  se deriva: `ecorex_{tenant}_{linea}`. Las credenciales nunca deben llegar a la IA.
- El webhook solo ingiere `messages.upsert`, DESCARTA `fromMe` y los grupos. Si el MCP enviara directo a Evolution, el
  mensaje no quedaria en Conversaciones y el agente SARA responderia encima.
- SARA cede el turno cuando el ultimo mensaje es un saliente con autor humano (`SentByTenantUserId`).
- Las paginas /lineas y /conversaciones no exigian permiso de modulo (corregido aparte en v0.16.309).

## Decision

1. Un servidor MCP POR LINEA: `POST /api/mcp/whatsapp/{lineId}` (cada linea es un servidor en el cliente, p. ej.
   `whatsapp-ventas`). Token PERSONAL nuevo de alcance `mcp:whatsapp` (sin migracion: `TenantApiToken.Scope` es texto).
   Un token de tableros no abre WhatsApp ni al reves. La linea se resuelve con el FILTRO GLOBAL: una linea de otro
   tenant da 404 aunque se cambie el id. Configuracion en /lineas -> "Configurar MCP" (URL, token y bloques por cliente).
2. Separado del MCP de tableros (que ya tiene 83 herramientas): 36 herramientas mas en el mismo servidor degradan a
   varios clientes (Cursor corta en 40; Codex/Gemini eligen peor con catalogos grandes).
3. Puerta generica a Evolution: `IEvolutionApiClient.CallAsync` + `IWhatsAppConnectorService.EvolutionCallAsync(lineId,
   metodo, ruta con {instance}, cuerpo)` que resuelve servidor + llave + instancia. Cubre todo Evolution sin un metodo
   por endpoint. Implementacion por defecto en la interfaz (los dobles de prueba no cambian).
4. ENVIOS a un chat 1:1 por `IChatService` (texto, imagen/video/documento/nota de voz, ubicacion): queda en
   Conversaciones a nombre "Usuario (via IA)" con autor humano -> SARA cede el turno. Lo que IChatService no envia
   (respuesta citada, contacto, encuesta, lista, sticker, video-nota) va directo a Evolution y se REGISTRA con
   `IChatService.RecordOutboundAsync`. Conversacion nueva con `GetOrCreateForLineAsync` (misma llave que la ingesta).
   Grupos: directo, sin registro (el sistema no guarda hilos de grupo).
5. SALVAGUARDAS anti-bloqueo: un destinatario por llamada; a un numero que nunca escribio a la linea solo con
   `primer_contacto_confirmado=true`; limite de envios por linea (20/min, 300/hora); estados exigen `para_todos` o lista.
6. PERMISOS con la matriz de roles: `conversaciones` (Ver = leer; Crear = enviar/reaccionar; Editar = gestionar chats,
   etiquetas, agente, grupos; Eliminar = borrar mensaje, salir de grupo) y `lineas` (perfil, privacidad, reinicio).
   Owner/Admin y sin rol = sin restriccion (regla B2). Auditoria `mcp.whatsapp.*` de toda escritura.
7. EXCLUIDO a proposito: logout, borrar/crear instancia, webhook, proxy y settings de Evolution (desconectarian
   Conversaciones y SARA), llamadas y plantillas de Cloud API.
8. Pausar SARA por chat reutiliza la LISTA NEGRA (`IBlockedNumberService`), igual que el comando Manejo_asesor: sin
   migracion; aplica al numero en todas las lineas (se informa a la IA).
9. Media: por URL https publica con descarga blindada (sin redirecciones, IP validada al conectar: bloquea redes
   privadas, loopback, link-local/metadata y DNS rebinding; tope 16 MB) o base64 pequeno (2 MB). `descargar_media`
   devuelve el archivo como bloque MCP image/audio para que la IA lo VEA/OIGA.
10. Host JSON-RPC COMUN (`McpJsonRpcHost`) para tableros y WhatsApp (protocolo, auth por alcance, auditoria, media);
    bloques de configuracion de clientes comunes (`McpClientSnippets`).

## Herramientas (36)

- Lectura (11): estado_linea, listar_chats, leer_mensajes, buscar_contactos, verificar_numeros, ver_perfil_contacto,
  descargar_media, listar_grupos, ver_grupo, listar_etiquetas, catalogo_business.
- Envio (9): enviar_texto, enviar_media, enviar_ubicacion, enviar_contacto, enviar_encuesta, enviar_lista,
  publicar_estado, reaccionar, escribiendo.
- Gestion (8): editar_mensaje, borrar_mensaje, marcar_leido, gestionar_chat, etiquetar_chat, pausar_agente,
  reanudar_agente, guardar_contacto (Directorio; WhatsApp no permite crear contactos en la agenda).
- Grupos y linea (8): crear_grupo, editar_grupo, participantes_grupo, invitacion_grupo, salir_grupo, perfil_linea,
  privacidad_linea, reiniciar_linea.

## Consecuencias

- La IA opera WhatsApp sin romper el historial ni pisar a SARA; cada envio queda auditado y atribuido.
- El limite de envios vive en memoria del proceso (si se escala a varias instancias, pasarlo a Redis).
- Los mensajes de grupo y los enviados desde el telefono siguen sin entrar a Conversaciones (limitacion del webhook,
  no del MCP); el MCP los lee de Evolution (`fuente='whatsapp'`), lo que depende de que Evolution guarde mensajes.
- Listas/botones pueden no mostrarse en todas las cuentas (restriccion de WhatsApp a interactivos no oficiales).

## Validacion

- `McpWhatsAppTests` matriz dual PG + SQL Server 18/18 contra el conector REAL (instancia `ecorex_tenant_linea`): catalogo,
  envio por la capa de chat con autor "(via IA)" y SARA cediendo turno, primer contacto, encuesta registrada / grupo no,
  permisos y aislamiento entre tenants, pausa del agente, limite de envios, media para la IA y anti-SSRF, alcances de token.
  `McpTablerosTests` 20/20 tras mover el host (sin regresion).
- En vivo (local, linea temporal propia): modal en /lineas, emision de token, `initialize` con instrucciones de la linea,
  `tools/list` = 36 sin problemas de esquema, 404 linea inexistente, 401 sin token / token de otro alcance; tableros
  sigue con 83 herramientas. No se llamo a Evolution real (la unica linea Evolution local es de un cliente).

## Fuera de alcance

- OAuth para ChatGPT/claude.ai web (igual que ADR-0159).
- Ingerir grupos, `fromMe` y acuses de entrega (`messages.update`) en el webhook.
- Interruptor por linea para habilitar/deshabilitar el MCP (hoy lo gobiernan el permiso y el token).
