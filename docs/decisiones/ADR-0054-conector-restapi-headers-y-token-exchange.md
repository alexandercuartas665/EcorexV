# ADR-0054: Conector RestApi con headers arbitrarios e intercambio de token (auth 2 pasos)

- Estado: Aceptado
- Fecha: 2026-08-01

## Contexto

El conector RestApi del contenedor de datos (modulo Contenedor de datos, TENANT-scoped) ya sabia
hacer GET con auth simple (None/ApiKey/Bearer/Basic), recorrer paginacion, ubicar el arreglo por
`ArrayPath` y mapear campos->columnas, tanto en el motor in-process (`ApiImportService`, boton
"Probar"/import manual) como via agente Colmena (`RestExecutor` del agente, ADR-0048). Faltaban tres
cosas para APIs reales tipo Siigo:

1. **Headers HTTP estaticos arbitrarios** (ej. `Partner-Id: <valor>`) enviados en toda llamada.
2. **Auth de 2 pasos (intercambio de token)**: un POST de login (usuario + secreto) que devuelve un
   `access_token` en el JSON, y de ahi en mas todas las llamadas reales llevan
   `Authorization: Bearer <access_token>` (mas los headers estaticos).
3. Que el mapeo/ArrayPath/paginacion y estas dos novedades se configuren desde la consola del TENANT
   sin editar JSON crudo, y con un boton "Probar" autonomo del conector.

Todo debe ser **configurable por el usuario del cliente**, sin hardcodear ninguna fuente (Siigo es
solo el caso guia), y seguir siendo funcionalidad del tenant (policy `Perm:contenedor-datos:View`),
NO del PlatformAdmin.

## Decision

### Dominio

- `ConnectorAuthKind` gana el valor `TokenExchange` (auth de 2 pasos).
- `DataConnector` gana dos columnas JSON **no secretas**: `HeadersJson` (lista de `{name,value}`) y
  `TokenExchangeJson` (`{ tokenUrl, method, username, secretParamName, tokenJsonPath, applyHeaderName,
  applyPrefix, bodyFormat }`).
- **El SECRETO del login reutiliza `CredentialsEncrypted`** (cifrado con `ISecretProtector`) cuando
  `AuthKind == TokenExchange`. Se reutiliza esa columna a proposito: hay UN solo secreto por conector
  (sea la clave Basic/Bearer o el `access_key` del login), y no tiene sentido proliferar columnas
  cifradas. El secreto NUNCA se guarda en claro ni viaja en `HeadersJson`/`TokenExchangeJson`.
- Migracion **dual** `AddConnectorHeadersAndTokenExchange` (PostgreSQL `jsonb`, SQL Server
  `nvarchar(max)`), un `AddColumn` por columna, snake_case, sin indices, encadenada tras
  `AddFormContainerInlineLabels` (ADR-0053), siguiendo el patron de `AddFormCardLayout` (ADR-0047).

### Motor in-process (`ApiImportService`)

- Los headers estaticos se aplican en TODA request.
- Con `TokenExchange`: antes del fetch real se hace UNA vez el login (POST/`method` a `tokenUrl`, con
  cuerpo JSON o form que lleva `username` + el secreto descifrado bajo `secretParamName`, con los
  headers estaticos aplicados tambien al login), se extrae el token por `tokenJsonPath` y se aplica
  como header `applyHeaderName = applyPrefix + token` en las llamadas reales. El token queda cacheado
  para toda la corrida (la lista de headers ya lo lleva a todas las paginas).
- `ApplyAuth` sigue soportando None/ApiKey/Bearer/Basic igual que antes; para `TokenExchange` NO se
  aplica `ApplyAuth` (la credencial es el secreto del LOGIN, no un token para el header directo).
- La `tokenUrl` pasa el mismo control anti-SSRF (`IsBlockedHost`, http(s) absoluta) que el endpoint.

### Contrato del agente y ejecutor (`apps/agent`)

- `RestFetchSpec` (en `Ecorex.Contracts.Agent`) gana `Headers` (lista de `RestHeader{Name,Value}`) y
  `TokenExchange` (`RestTokenExchangeSpec`), ambos opcionales al final del record: los specs viejos se
  comportan igual (compatibilidad hacia atras). El secreto del login viaja aparte, en
  `ConnectorSpec.Secret` (ADR-0040), igual que hoy viajan las credenciales Basic/Bearer; NUNCA en el
  spec.
- `ProcessRunner.BuildRestSpec` (servidor) puebla `Headers` desde `HeadersJson` y `TokenExchange`
  desde `TokenExchangeJson` + el secreto descifrado; las columnas dedicadas son la fuente autoritativa
  y, si estan vacias, se respeta lo que trajera el propio `MappingJson`.
- El `RestExecutor` del agente aplica los headers estaticos en toda llamada y, cuando hay
  `TokenExchange`, resuelve el token UNA vez (login con el secreto) antes del fetch y lo aplica como
  header en lista y detalle. Mantiene la costura de prueba (`_fetchForTest`) para tests sin red.

### CRUD y UI (tenant)

- `DataImportConfigService` propaga `HeadersJson`/`TokenExchangeJson` (y el secreto cifrado via
  `Credentials`) en Save/Map, con el tenant-scoping del filtro global.
- `ContenedorDatos.razor`: el editor del conector gana la opcion de auth "Intercambio de token", su
  formulario (URL de token, metodo, usuario, secreto, ruta JSON del token, header destino + prefijo),
  una seccion de **Headers** con filas repetibles, y una **UI estructurada** para ArrayPath +
  paginacion + mapeo columna<-campo (con un probe real que descubre campos), que lee/escribe el MISMO
  `MappingJson` (RestFetchSpec) que ya consume el agente. Se conserva un modo "JSON avanzado"
  colapsable como respaldo (y para no perder configuraciones con `Fanout`). Un boton **"Probar"**
  autonomo ejecuta un fetch real via `ApiImportService` (aplicando headers + token exchange) y muestra
  ok/error, numero de registros y una muestra.

### Alternativas consideradas

- **Columna cifrada nueva para el secreto del login**: descartada; hay un solo secreto por conector y
  reutilizar `CredentialsEncrypted` evita proliferar columnas y rutas de cifrado.
- **Guardar todo (headers, token exchange) dentro de `MappingJson`**: se podria, pero mezclaria config
  no secreta de auth con el mapeo declarativo, y complica el CRUD y el precedente de secreto. Columnas
  dedicadas dejan `MappingJson` para lo que ya era (RestFetchSpec de datos) y hacen la UI mas clara.
- **Hardcodear el flujo Siigo**: descartado por regla dura; todo es declarativo y configurable.

## Consecuencias

- Un conector RestApi puede autenticarse contra APIs de 2 pasos (Siigo y similares) y enviar headers
  arbitrarios, tanto en el import in-process como via agente, sin tocar codigo.
- El scope sigue siendo del TENANT (misma policy), sin cruces con PlatformAdmin ni entre tenants.
- **El MSI del agente Colmena debe regenerarse y redistribuirse manualmente** (ADR-0049) para que los
  agentes ya instalados entiendan los nuevos campos `Headers`/`TokenExchange` del `RestFetchSpec`. El
  codigo del agente queda compilando; la generacion/instalacion del instalador es un paso manual
  posterior del operador (no se ejecuta `build-installer.ps1` en esta entrega). Los agentes viejos
  siguen atendiendo specs sin esos campos igual que antes.
