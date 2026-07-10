# Contenedor de datos — Contrato del cliente remoto (handoff)

> Documento de **traspaso** para otra sesion/proyecto. Describe el "cliente" (agente remoto) que
> sincroniza datos de fuentes externas (ej. Alegra) hacia los Contenedores de datos de ECOREX.tareas,
> y el endpoint receptor que ECOREX debe exponer. En esta fase ECOREX solo tiene la **configuracion**
> (modelos, conectores, clientes, procesos); el cliente remoto y el endpoint de ingesta **NO estan
> construidos** — este documento es su especificacion.

## 1. Panorama

```
  Fuente externa            Cliente remoto (a construir)         ECOREX.tareas (este repo)
  (Alegra, ERP, ...)   <->  agente en equipo/nube del cliente  ->  endpoint de ingesta -> Contenedor
                            - conoce credenciales de la fuente      - valida auth (ClientId/Secret)
                            - lee/mapea la data (incl. anidada)     - upsert de filas/celdas (y anidadas)
                            - la empuja a ECOREX (webhook push)     - responde conteos (DataImportResult)
```

El usuario imagina la senal de disparo como **websocket** (ECOREX u orquestador avisa "sincroniza el
contenedor X"), tras lo cual el cliente levanta los datos y los **empuja** por HTTP (webhook) a ECOREX.
El disparo por horario tambien puede ser autonomo del cliente (lee su `ImportProcess.Schedule`).

## 2. Entidades de configuracion (ya existen en ECOREX)

- **`DataContainer`** — el modelo destino (tabla logica). Puede ser **anidado**: un campo de tipo
  `Submodel` apunta a un contenedor hijo (matriz), ej. Factura -> Items. El arbol de filas espeja el
  arbol de contenedores (`DataContainerRow.ParentRowId` + `ParentFieldId`).
- **`DataConnector`** (por contenedor) — COMO/DESDE DONDE se trae la data: `EndpointUrl`, `HttpMethod`,
  `AuthKind` (None/ApiKey/Bearer/Basic), **`CredentialsEncrypted`** (cifrado con DataProtection,
  `ISecretProtector`; JSON con las llaves de la fuente), y **`MappingJson`** (mapeo de la estructura
  de la fuente a los campos del contenedor, con rutas para lo anidado).
- **`DataClient`** (por tenant) — identidad del agente remoto: **`ClientId`** (publico, unico por
  tenant) + **`ClientSecretEncrypted`** (secreto cifrado; se muestra en claro UNA sola vez al crear/rotar).
- **`ImportProcess`** (por contenedor) — liga contenedor + conector + cliente y define **cuando** corre
  (`ScheduleKind` Manual/Interval/Cron, `IntervalMinutes` o `CronExpression`). Solo config; sin ejecutor.

## 3. Autenticacion del cliente

El cliente se autentica en cada push con su par **ClientId + Secret** del tenant. Recomendado:
- Header `X-Ecorex-Client-Id: <ClientId>`.
- Firma HMAC-SHA256 del cuerpo con el `ClientSecret`: `X-Ecorex-Signature: <hex(hmac(secret, rawBody))>`.
  (Evita mandar el secreto en claro por la red; ECOREX recomputa y compara.)
- Alternativa mas simple para v1: `Authorization: Bearer <ClientSecret>` sobre TLS. Definir al construir.

ECOREX valida: cliente existe, `IsActive`, tenant correcto, firma valida. El secreto se guarda cifrado;
para verificar HMAC, ECOREX lo des-cifra en memoria con `ISecretProtector.Unprotect`.

## 4. Flujo de sincronizacion

1. **Disparo**. Dos modos (a definir cual primero):
   - *Push por horario* (autonomo): el cliente lee su `ImportProcess.Schedule` y corre solo.
   - *Notificacion* (websocket / cola): ECOREX u orquestador avisa "sincroniza contenedor X"; el
     cliente reacciona. (Este canal websocket es parte del trabajo a construir.)
2. **Lectura de la fuente**. El cliente usa `DataConnector` (endpoint + credenciales) para leer la
   data. Las credenciales las conoce el cliente (se le entregan de forma segura; ECOREX solo guarda su
   copia cifrada para referencia/validacion, no las expone por API).
3. **Mapeo**. El cliente aplica `MappingJson` para transformar la estructura de la fuente a las filas
   del contenedor. Para modelos anidados, el payload de la fuente (ej. un JSON de Alegra con una
   factura y su arreglo de items) se descompone en fila raiz + filas hijas por cada submodelo.
4. **Push a ECOREX** (endpoint de ingesta, a construir — ver seccion 5).
5. **Respuesta**. ECOREX hace upsert transaccional y responde conteos (importadas/fallidas/errores),
   idempotente por una clave de evento (evitar duplicados en reintentos).

## 5. Endpoint de ingesta en ECOREX (a construir en Ecorex.Api)

Propuesta de contrato (ajustar al implementar):

```
POST /api/data-ingest/{containerId}
Headers: X-Ecorex-Client-Id, X-Ecorex-Signature (HMAC) | Authorization: Bearer
Body (JSON):
{
  "eventId": "uuid",            // idempotencia: mismo eventId => no reprocesar
  "mode": "upsert" | "replace", // upsert por clave natural, o reemplazo total
  "rows": [
    {
      "values": { "<fieldKey|columnName>": <valor escalar>, ... },
      "children": {              // submodelos (matrices) anidados, por campo Submodel
        "<submodelFieldName>": [ { "values": {...}, "children": {...} }, ... ]
      }
    }
  ]
}
Respuesta 200:
{ "success": true, "rowsImported": N, "rowsFailed": M, "errors": [ ... ] }
```

Reglas de ingesta (server-side, a implementar):
- Resolver el contenedor por `containerId` y tenant del cliente (aislar por tenant).
- Validar auth + firma; rechazar cliente inactivo o firma invalida (401/403).
- Idempotencia por `eventId` (tabla de eventos procesados; responder 200 sin reprocesar si repetido).
- Upsert de filas raiz y, recursivamente, filas hijas (`ParentRowId`/`ParentFieldId`) por cada
  submodelo, reusando la logica EAV de `DataContainerService`.
- Todo en transaccion; responder `DataImportResult`.
- Responder rapido (< 200 ms) y procesar async si el volumen es grande (cola/worker), como los
  webhooks del hermano CUBOT.redmanager.

## 6. Que falta construir (resumen del handoff)

- [ ] **Cliente remoto** (proyecto/servicio aparte): lectura de fuentes (Alegra y genericas REST),
      aplicacion del `MappingJson`, descomposicion de anidados, firma HMAC y push. Manejo de
      reintentos + `eventId`.
- [ ] **Canal de notificacion** (websocket/cola) para el disparo "sincroniza X" desde ECOREX.
- [ ] **Endpoint de ingesta** en `Ecorex.Api` (`/api/data-ingest/{containerId}`) con auth por cliente,
      idempotencia y upsert anidado reusando `DataContainerService`.
- [ ] **Ejecutor de horarios** (BackgroundService/cron) que dispare los `ImportProcess` en modo push.
- [ ] **Entrega segura de credenciales de fuente** al cliente (fuera de banda; ECOREX no las expone).

## 7. Ya construido en ECOREX (esta fase)

- Modelos dinamicos con **anidamiento** (contenedores/columnas/filas/celdas EAV en arbol) + import/export
  Excel (ClosedXML).
- Configuracion de **conectores** (con credenciales cifradas), **clientes** (ClientId + secreto cifrado)
  y **procesos** (horarios). Todo tenant-scoped.
- UI de configuracion en 2 columnas (campos | procesos de importacion) y gestion de clientes.
