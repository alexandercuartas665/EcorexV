# ADR-0208: Campos de ficha del Directorio en la fuente reportable native:tercero

- Estado: Aceptado
- Fecha: 2026-11-24
- Modulo: motor de reportes (ADR-0051) + fuentes nativas de Analisis (ADR-0158) + Directorio (000232)
- Origen: pedido de la sesion de reportes: armar como DATO (PanelSpec, sin codigo) el reporte "Directorio - base
  completa" con los campos fijos Y los dinamicos del tercero (direccion del cliente, subsector...).

## Contexto

`native:tercero` (`TerceroReportSource`) solo publicaba columnas de la tabla `terceros`. Los campos que cada tenant
configura viven en `terceros.fichas_json` (`{ seccion: { campo: valor } }`) y no se podian filtrar, agrupar ni
mostrar en un panel. El catalogo de una fuente nativa era ESTATICO (`Describe()`, sin tenant), y los campos de
ficha son distintos en cada tenant. En AGRO ademas hay datos en claves SIN definicion de campo: `cliente.direccion`
(1.200 terceros) se cargo por importacion, mientras las definiciones son `base.direccion` y `fiscal.direccion`.

## Decision

1. **Catalogo por tenant para fuentes nativas**: interfaz opcional `ITenantDescribedSource.DescribeAsync()`.
   `ReportCatalog` la usa al listar y al resolver una fuente; `Describe()` sigue dando la parte fija (clave y
   campos base, sin BD) para el despacho y las pruebas. Todo consumidor (validador de PanelSpec, motor de
   paneles, widgets de Analisis, MCP de reportes) pasa por el catalogo, asi que ve los campos nuevos sin cambios.
2. **Campos de ficha** (`TerceroFichaReportFields`), clave estable `ficha_{seccion}_{campo}` (nunca choca con las
   claves fijas):
   - DEFINIDOS (`TerceroFieldDefinition`): etiqueta y tipo de la definicion (Numero/Moneda -> Decimal agregable,
     Fecha -> Date, Casilla -> Boolean, el resto Texto). Sin separadores ni tablas.
   - Claves con dato SIN definicion: Texto con etiqueta legible ("tipo_de_empresa" -> "Tipo de empresa").
   - Etiquetas repetidas entre si o con un campo fijo se desambiguan con el titulo de la ficha
     ("Direccion (Cliente)"), porque el PanelSpec referencia campos por Key O por DisplayName.
   - Valor tolerante: escalar, objeto envuelto `{ "value": ... }` o lista (unida con ", "). Lista de contenedor
     (Lookup) -> etiqueta de la fila; lista del directorio -> nombre del tercero; por lote, en el mismo contexto.
3. Consulta en el contexto EFIMERO de la fuente (ADR-0138) y con el mismo tope de materializacion; el filtro
   global tenant acota terceros y definiciones. Sin migracion.

## Opciones consideradas

- **Solo claves presentes en los datos**: descartada como unica via; pierde la etiqueta y el tipo de la
  definicion. Se usa como complemento para los datos importados sin definicion.
- **Solo definiciones**: descartada como unica via; en AGRO dejaba fuera justo `cliente.direccion`.
- **Columnas de ficha materializadas en SQL por proveedor**: innecesario; la fuente ya trabaja en memoria.

## Consecuencias

- `native:tercero` publica en AGRO, ademas de los fijos, "Direccion (Cliente)" (`ficha_cliente_direccion`) y
  "Tipo de Empresa / Subsector" (`ficha_general_tipo_de_empresa_subsector`), filtrables y agrupables.
- Pruebas: dual `AnalisisSourcesTests.Tercero_CamposDeFicha_EnCatalogoFiltroYAgrupacion` (catalogo, etiquetas
  desambiguadas, clave sin definicion, valor envuelto, decimal, lookup, filtro, agrupacion, otro tenant no ve los
  campos) y unitarias `TerceroFichaReportFieldsTests`.
