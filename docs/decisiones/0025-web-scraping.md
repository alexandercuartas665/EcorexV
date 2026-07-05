# ADR-0025: Extraccion de datos (000730) como scraping declarativo acotado y seguro

- Estado: aceptada
- Fecha: 2026-07-05
- Modulo: NEWFRONT_web_scraping (000730) -> pagina `/extraccion-datos` en Ecorex.SuperAdmin
- Relacionada con: ADR-0016 (prohibicion de ejecucion por reflexion/scripts),
  ADR-0023 (estructura del concepto + tokens del workspace), ADR-0024 (reencuadre funcional)

## Contexto

El legacy `NEWFRONT_web_scraping.aspx` es el CONFIGURADOR de un motor RPA propio: guarda
scripts JavaScript crudos (`WEB_SCRAPING_RS.SCRIPT`) que un runtime WPF (Doom) inyecta en
un `WebBrowser` Trident con `InvokeScript("eval", ...)`. La spec de Capa 6 documenta sus
riesgos: XSS almacenado equivalente a RCE en el host del bot, SQL por concatenacion,
credenciales descifrables con el token en claro, DELETE cross-empresa por el typo
`Emmpresa`, y CERO bitacora de ejecucion. Nada de eso es aceptable en el SaaS multi-tenant.
La propia spec (seccion 13) recomienda para la reconstruccion: selectores CSS/XPath
declarativos en lugar de JS inyectado, y separar configurador de runtime.

## Decision: alcance FUNCIONAL acotado de esta ola

1. **Modelo minimo**: `ScrapeSource` (TenantEntity: Name, Url, Selector?, Kind Html|Json,
   Status Active|Inactive|Error, LastRunAt?, LastResultSummary?) y `ScrapeRun`
   (TenantEntity: SourceId FK cascade, Status Success|Failed, ItemCount, DurationMs,
   ErrorMessage?, ResultJson dual jsonb/nvarchar(max)). Migracion dual `AddScraping`
   aplicada y verificada en PG 5442 y SQL Server 1443. Indice unico (TenantId, Name).
2. **Ejecucion declarativa, NUNCA scripts**: `IScrapeService.RunAsync(sourceId)` hace un
   GET acotado y parsea segun Kind: Json -> conteo de items (array raiz o primera
   propiedad array) + preview tabular; Html -> texto de los nodos que casan con el
   selector CSS. El multi-paso del legacy (pasos, acciones, APIs, clientes cifrados,
   seguimiento trading) queda FUERA de esta ola de forma deliberada.
3. **Toda corrida se persiste** (exito o fallo) con duracion, conteo, motivo y resultado
   recortado, y actualiza LastRunAt/LastResultSummary/Status de la fuente en UNA
   transaccion. El legacy no tenia bitacora; aqui el historial es el corazon del modulo.
   `ResultJson` es SIEMPRE JSON valido (jsonb lo exige) recortado a 64 KB quitando filas
   de la preview, nunca truncando bytes.
4. **Eliminar conserva la trazabilidad** (criterio ADR-0023): una fuente con corridas no
   se borra (Invalid tipado); la UI ofrece desactivarla.

## Decision: guard SSRF ESTRICTO (innegociable)

`SsrfUrlGuard` (Application/Scraping, puro y testeado por unidad con DNS inyectado)
valida ANTES de cada request y en CADA redireccion:

- Solo `http`/`https` absolutas; sin credenciales embebidas (`user@host`).
- **Resuelve DNS y valida TODAS las IPs resultantes** (fail-closed: una privada en un
  registro doble bloquea el host): loopback 127/8 y ::1, RFC1918 (10/8, 172.16/12,
  192.168/16), link-local 169.254/16 (incluida la metadata cloud 169.254.169.254) y
  fe80::/10, CGNAT 100.64/10, 0.0.0.0/8, TEST-NET, benchmarking 198.18/15, multicast,
  clase E, broadcast, IPv6 unique-local fc00::/7, site-local, unspecified, e IPv4
  MAPEADAS en IPv6 (::ffff:10.0.0.1) normalizadas antes de clasificar.
- Solo puertos por defecto (80/443) en hosts publicos ("puertos raros" = superficie a
  servicios internos).
- `ScrapeHttpFetcher`: SOLO GET, User-Agent propio identificable (`EcorexScraper/1.0`),
  timeout total 15s, respuesta maxima 2 MB (tope duro leyendo el stream, ademas del
  Content-Length declarado), `AllowAutoRedirect=false` y maximo 3 redirecciones seguidas
  A MANO re-validando cada salto contra el guard (un destino publico que redirige a la
  red interna se corta sin hacer el request). Sin cookies ni credenciales de ambiente.
- **Excepcion explicita y unica**: `ScrapeGuardOptions.AllowLoopback` se activa SOLO en
  Development (Program.cs re-registra el singleton tras AddInfrastructure) para que la
  fuente demo apunte al endpoint PROPIO `/api/demo/scrape-sample` (JSON estatico de 8
  items) sin depender de internet en dev/tests. En loopback-dev se admite cualquier
  puerto (la app escucha en puertos altos); la red privada NO loopback sigue bloqueada
  incluso en dev. En produccion el default de Infrastructure es sin loopback.
- Limite documentado (TODO): la IP validada no se fija para la conexion posterior, asi
  que un DNS malicioso con TTL 0 podria re-resolver distinto entre validacion y GET
  (rebinding). Mitigarlo exige un handler que conecte a la IP validada (SocketsHttpHandler
  ConnectCallback); queda como deuda declarada de seguridad.

## Decision: AngleSharp para el selector CSS

AngleSharp NO estaba referenciado en la solucion (verificado); se agrega **AngleSharp
1.5.1 estable** a Ecorex.Application porque el selector CSS es central en el reencuadre
del modulo (la alternativa Regex/XPath casero no soporta selectores reales y seria una
falsa seguridad). Justificacion: parser puro de HTML/CSS en memoria, sin red, sin IO y
sin telemetria; mismo criterio de "parser puro en Application" que CsvTableParser
(ADR-0024). `ScrapeContentParser` es estatico y testeado por unidad; un selector invalido
es un error tipado, y del DOM solo se lee `TextContent` (nada se ejecuta).

## UI segun el concepto por-modulo (regla ADR-0023)

`/extraccion-datos` replica la ESTRUCTURA y MEDIDAS de `proto_web_scraping.html` con los
TOKENS del workspace: topbar 14x24 con breadcrumb + badge MOD 000730, layout 300px/1fr
(max 1500), sidebar sticky de fuentes (icono 32x32 r6, dot de estado verde/rojo/gris),
hero r12 con gradiente `--brand-2 -> --brand` y 4 KPIs (ejecuciones, exitosas 30d,
registros extraidos, ultima corrida), franja ambar de alcance (reemplaza la nota del
runtime WPF del proto por los limites reales del ejecutor), cols 1fr/380 (preview tabular
+ JSON crudo en editor oscuro FIJO en ambos temas | panel de configuracion con selector
CSS y ayuda), y tabla de seguimiento con pills dot verde/rojo. Mapeo de paleta: accent
morado -> --brand/--brand-soft, success -> --ok/--t-green(+bg), danger -> --danger/
--t-rose(+bg), warning -> --t-amber(+bg), muted -> --ink-2/3, card/border -> --surface/
--line. Los overlays blancos del hero usan color-mix sobre --on-brand para funcionar en
claro y oscuro. NavMenu: "Extraccion de datos" apunta a la pagina real (unico item
tocado); Modulo.razor retira su entrada del registro de stubs. Policy nueva
`ExtraccionDatos.Editar` (paso 1: nombre estable, requisito = TenantMember).

## Consecuencias

- Un tenant puede definir fuentes y extraer datos reales HOY, sin que exista ningun
  camino de tenant a script/SQL/red interna: no hay campo de script, no hay SQL de
  usuario, y el guard SSRF corre en cada request y cada salto.
- Seeder demo idempotente: fuente "Items demo ECOREX (JSON)" apuntando al endpoint
  propio; si la app arranca en otro puerto (suite E2E) la URL se re-apunta.
- Tests nuevos: unit (SsrfUrlGuardTests exhaustivo + ScrapeHttpFetcherTests de
  redirecciones/topes + ScrapeContentParserTests), integracion dual x3 (CRUD + corrida
  real contra endpoint local, historial exito/fallo con transicion de estado, aislamiento
  cross-tenant) y E2E (crear fuente demo -> ejecutar -> preview 8 items -> historial verde).
- **TODO scheduler (deuda declarada)**: el CICLO del legacy (corridas programadas) NO se
  implementa en esta ola; ira como BackgroundService en Ecorex.Workers con cola y
  rate-limit por tenant cuando se priorice. Tambien quedan como deuda: multi-paso con
  variables/credenciales (exigiria IDataProtection y un diseno propio), robots.txt y
  rate-limit por dominio, extraccion de atributos (href/src) ademas de texto, y el pin
  de IP anti-rebinding descrito arriba.
