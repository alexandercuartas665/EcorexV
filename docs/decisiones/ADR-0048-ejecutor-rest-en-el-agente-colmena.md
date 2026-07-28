# ADR-0048: Ejecutor REST en el agente Colmena (RestFetchSpec + fan-out/aplanado)

- Estado: Aceptado
- Fecha: 2026-07-28
- Contexto: apps/agent (Ecorex.Agent.Core) + apps/backend (Ecorex.SuperAdmin.Agents)
- Relacionado: ADR-0039 (agente Colmena), ADR-0040 (la credencial viaja por el canal), ADR-0045
  (modulo del agente), doc 02 (protocolo del canal).

## Contexto

El agente Colmena ya ejecutaba conectores `Database` (SQL solo-lectura, `GatewayExecutor`) pero los
conectores `RestApi` caian a un acuse (`AckAsync`) que solo cerraba el canal: no se ejecutaban. Hacia
falta el equivalente REST del Gateway: GET HTTP de solo-lectura, con auth, paginacion y -clave- el
patron OCS Inventory (lista de equipos -> por cada uno un detalle -> aplanar el software instalado en
una fila por programa). La solucion debia ser DECLARATIVA (sin hardcode de OCS): el agente recibe un
spec por el canal y lo ejecuta.

## Decision

1. **`RestExecutor`** (Ecorex.Agent.Core/Services/RestExecutor.cs), analogo a `GatewayExecutor`:
   HttpClient propio (no el del hub SignalR), timeout configurable, **solo GET**, limites de filas y
   de llamadas de detalle, streaming de `FetchResultMsg` en chunks (500 filas/chunk). La credencial
   llega en `ConnectorSpec.Secret` (ADR-0040), nunca se persiste ni se loguea (los errores usan la URL
   sin query, `UriPartial.Path`).

2. **`RestFetchSpec`** en el contrato compartido (libs/Ecorex.Contracts.Agent/AgentProtocol.cs), nuevo
   campo opcional `FetchRequestMsg.Rest`. Es al conector RestApi lo que `QuerySpec` al Database. El
   agente NUNCA referencia el backend web; el contrato es la fuente de verdad comun.

3. **Parseo tolerante** (RestJson.cs, publico/estatico para poder probarlo sin red): la coleccion
   objetivo puede ser un arreglo, un objeto-indexado por id (`{"1":{...},"2":{...}}`, forma de OCS),
   venir bajo `ArrayPath`, o bajo los envoltorios `data/items/results/records/rows`. Rutas con puntos
   e indices (`hardware.NAME`, `bios[0].SSN`, `accountinfo[0].TAG`) y clave vacia `""`.

4. **Fan-out + aplanado** declarativo (`RestFanoutSpec`): por cada item de la lista un GET al detalle;
   se desanida el objeto-indexado del detalle (`DetailUnwrapIndexed`), se ubica el arreglo hijo
   (`ChildArrayPath`, en OCS la clave vacia `""`) y se emite UNA fila por elemento hijo repitiendo las
   columnas del padre (`ParentFields`) + columnas del hijo (`ChildFields`).

5. **Wiring**: `RealHiveConnection` despacha `Kind=="RestApi"` con `Rest != null` al `RestExecutor`
   (si no hay `Rest`, sigue el acuse anterior). En el servidor, `ProcessRunner` deja pasar
   `ConnectorKind.RestApi` y arma el `RestFetchSpec`; `AgentImportService.DispatchFetchAsync` gana un
   parametro opcional `RestFetchSpec? rest`.

6. **Donde vive la config REST**: en `DataConnector.MappingJson` (campo jsonb existente, sin
   migracion) como JSON del propio `RestFetchSpec`. `BaseUrl`, metodo y tipo de auth se toman de los
   campos normales del conector (EndpointUrl/HttpMethod/AuthKind) cuando el JSON no los trae. Asi el
   operador configura endpoint+auth como en cualquier conector y solo describe el fan-out/mapeo en el
   textarea "Mapeo JSON" de la seccion Conectores del Contenedor. La ingesta reusa el mismo
   `IRowIngestService` que el resto: el `RestExecutor` emite filas con clave = NOMBRE de la columna
   destino, y el mapeo por nombre de siempre hace el resto.

## Esquema de RestFetchSpec (resumen)

```
RestFetchSpec:
  BaseUrl        string  # base absoluta http(s), ej https://host/ocsapi/v1
  ListPath       string  # path o URL del endpoint LISTA, ej /computers
  HttpMethod     string = "GET"     # solo GET (solo-lectura)
  AuthKind       string = "None"    # None | Basic | Bearer | ApiKey
  ArrayPath      string?            # ruta a la coleccion en la lista; null/"" = tolerante
  Paging         RestPagingSpec?    # None | Offset(start/limit) | Page(page)
  Fanout         RestFanoutSpec?    # presente = fan-out lista->detalle; null = fila directa por item
  Fields         RestFieldMap[]?    # modo simple (sin fanout): una fila por item
  TimeoutSeconds int = 30
  MaxRows        int = 100000
  MaxDetailCalls int = 5000

RestPagingSpec: Mode, OffsetParam="start", LimitParam="limit", PageParam="page", StartValue=0, PageSize, MaxPages
RestFieldMap:   Column (columna destino), Path (ruta con puntos/indices; "" = elemento), Default?
RestFanoutSpec: DetailPathTemplate ("/computer/{id}"), IdSource ("key"|"field"), IdField?,
                DetailUnwrapIndexed=true, ChildArrayPath (null=tolerante, ""=clave vacia),
                ParentFields[], ChildFields[]
```

## Ejemplo OCS Inventory (pegar en "Mapeo JSON" del conector RestApi)

Conector: EndpointUrl = `https://inv.bitcode.com.co/ocsapi/v1`, AuthKind = Basic,
Credenciales = `usuario:clave`. Destino: tabla "Software OCS" (18 columnas). MappingJson:

```json
{
  "listPath": "/computers",
  "arrayPath": null,
  "paging": { "mode": "Offset", "offsetParam": "start", "limitParam": "limit", "pageSize": 100, "maxPages": 20 },
  "fanout": {
    "detailPathTemplate": "/computer/{id}",
    "idSource": "key",
    "detailUnwrapIndexed": true,
    "childArrayPath": "",
    "parentFields": [
      { "column": "Equipo", "path": "hardware.NAME" },
      { "column": "TAG", "path": "accountinfo[0].TAG" },
      { "column": "Serial", "path": "bios[0].SSN" },
      { "column": "Modelo", "path": "bios[0].SMODEL" },
      { "column": "Usuario", "path": "hardware.USERID" },
      { "column": "SO", "path": "hardware.OSNAME" },
      { "column": "IP", "path": "hardware.IPADDR" },
      { "column": "RAM_MB", "path": "hardware.MEMORY" },
      { "column": "CPU", "path": "hardware.PROCESSORT" },
      { "column": "Dominio", "path": "hardware.WORKGROUP" },
      { "column": "UltimoInventario", "path": "hardware.LASTDATE" }
    ],
    "childFields": [
      { "column": "Programa", "path": "NAME" },
      { "column": "Version", "path": "VERSION" },
      { "column": "Publisher", "path": "PUBLISHER" },
      { "column": "FechaInstalacion", "path": "INSTALLDATE" },
      { "column": "Carpeta", "path": "FOLDER" },
      { "column": "Bits", "path": "BITSWIDTH" },
      { "column": "GUID", "path": "GUID" }
    ]
  }
}
```

Nota: `hardware.WINPRODKEY` NO se mapea (dato sensible). Un equipo sin software emite igualmente una
fila con solo las columnas del padre, para no perderlo.

## Consecuencias

- El agente ejecuta REST de verdad; el patron OCS queda cubierto de punta a punta y reusa la ingesta,
  la bitacora y el "Actualizar datos"/scheduler que ya existian para Database.
- Read-only por construccion (solo GET). La superficie de riesgo del canal la sigue acotando el hecho
  de que solo se hace GET; no hay escritura posible a la fuente.
- Sin migraciones: la config vive en `MappingJson` (jsonb existente). No se toco ninguna entidad.
- Pendiente / no incluido (documentado, no bloqueante): un diseNador visual de fan-out/mapeo en la UI
  (hoy se pega el JSON del RestFetchSpec en el textarea "Mapeo JSON"); logging de la ruta fetch en el
  feed de la colmena (heredado de ADR-0045); politica de allow-list de hosts para el REST del agente
  (hoy se permite cualquier http(s), porque el agente on-prem debe poder alcanzar la LAN/Internet y la
  seguridad la da el GET solo-lectura).
