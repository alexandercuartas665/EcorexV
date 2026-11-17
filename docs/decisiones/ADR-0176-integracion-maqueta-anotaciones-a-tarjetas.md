# ADR-0176: Integracion de la maqueta TRONOX - anotaciones a tarjetas (POST /api/maqueta/solicitudes)

Fecha: 2026-11-17
Estado: Aceptada
Version: v0.16.338

## Contexto

La maqueta TRONOX (repo `tronox.maqueta`, pagina estatica `admin.html` servida en `http://localhost:8096`) tiene un
admin de anotaciones: se navega la maqueta, se selecciona un elemento, se anota y se adjunta una captura. El cliente
YA llama a un contrato fijo (`docs/INTEGRACION-ECOREX-ANOTACIONES.md` de ese repo): `POST {baseUrl}/api/maqueta/
solicitudes` con `Authorization: Bearer {token}` y un JSON con titulo, descripcion Markdown, categoria, prioridad,
modulo, metadatos del archivo real (origenApp, handoff, ruta, archivoMaqueta, selector) y la captura PNG en base64.
Cada anotacion debe quedar como TARJETA en un tablero, idempotente por `idLocal`. El usuario pidio que caigan en el
tenant cliente ACUARTAS.

## Decision

1. **Autenticacion = token PERSONAL de usuario con alcance nuevo `integ:maqueta`** (reusa `TenantApiToken` +
   `PersonalAccessTokenService` de ADR-0159). Se emite en `/conectar-ia` eligiendo "Maqueta (anotaciones)". El token
   resuelve tenant + usuario: las tarjetas caen en el tenant del usuario que emitio el token (para ACUARTAS: un usuario
   de ACUARTAS emite el token) y quedan a su nombre ("Nombre (via maqueta)"). Un token de maqueta no abre el MCP de
   tableros ni al reves. Se descarto una clave global en `.env`: no dice el tenant, no tiene dueno para la bitacora y
   no se puede revocar desde la app.
2. **Idempotencia = entidad nueva `ExternalTaskLink`** (TenantId, Source, ExternalKey, TaskItemId, MetadataJson,
   LastCaptureSha256, ReceivedCount), unica por (TenantId, Source, ExternalKey). Source = `origen` del cliente
   ("maqueta-tronox"). Se descarto la etiqueta `maq:{idLocal}`: llenaria el catalogo de etiquetas del tenant con una
   etiqueta por anotacion. La entidad es generica: sirve a futuras integraciones que creen tareas desde otro sistema.
3. **Mapeo**: tablero por NOMBRE (`tablero`, por defecto "Maqueta TRONOX - Ajustes"; se crea con las columnas por
   defecto si no existe) -> primera columna; titulo; descripcion Markdown tal cual; prioridad (Alta/Media/Baja);
   etiquetas del catalogo del tenant con `modulo` y `categoria` (se crean si faltan); `autor` -> RequesterName;
   metadatos completos en `ExternalTaskLink.MetadataJson` (y ya vienen legibles en la descripcion); captura ->
   gestor unificado de archivos (`IFileStorageService`, modulo "maqueta", Azure Blob/disco, nunca inline en BD) +
   adjunto de la tarea. Clasificacion: concepto ligado al tablero si existe; si no, primer tipo de actividad activo; si
   el tenant no tiene ninguno (caso ACUARTAS hoy), se crea el tipo "Maqueta / Solicitud de ajuste".
4. **Reenvio** del mismo idLocal: actualiza la MISMA tarjeta (titulo, descripcion, prioridad, autor, etiquetas
   faltantes) via `ITaskItemService.UpdateAsync` (concurrencia optimista); la captura solo se vuelve a adjuntar si
   cambio (SHA-256). No mueve la tarjeta de columna. Responde 200 `estado:"actualizada"`; el alta responde 201
   `estado:"creada"`. `id` = numero de la tarea (T00123), `url` = deep-link `/actividades?board=..&task=..` con la URL
   publica (ECOREX_PUBLIC_BASE_URL) o, si no esta, la del request.
5. **CORS propio del endpoint** (no `UseCors` global): origenes en `MaquetaIntegration:AllowedOrigins` (coma), por
   defecto `http://localhost:8096` y `http://127.0.0.1:8096`; preflight OPTIONS -> 204 con `POST, OPTIONS` y
   `Authorization, Content-Type`. Cuerpo hasta 15 MB; captura decodificada hasta 8 MB.

## Consecuencias

- Migracion dual `AddExternalTaskLink` (PG 20261117203832, SQL Server 20261117203812): solo crea la tabla.
- Para que la maqueta apunte a otro tenant basta emitir el token con un usuario de ese tenant; no hay codigo por tenant.
- Riesgo aceptado: dos envios SIMULTANEOS del mismo idLocal pueden dejar una tarjeta sobrante (el indice unico
  rechaza el segundo vinculo y se responde 409). El admin envia una anotacion por clic.
- Si la maqueta se sirve en otro puerto/host, hay que agregar el origen a `MaquetaIntegration:AllowedOrigins`.
- Pruebas duales: `MaquetaSolicitudesTests` (mismo idLocal no duplica, captura igual no se repite, tenant sin tipos,
  alcance del token).
