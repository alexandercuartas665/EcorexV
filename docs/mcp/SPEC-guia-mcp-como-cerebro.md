# SPEC: la guia de los MCP como cerebro de plataforma (guia_ecorex)

Fecha: 2026-11-23 - Autor: ECOREX.mcp - Estado: APROBADA por el usuario (decisiones en la seccion 6)
Relacionado: ADR-0200 (Cerebros: olas 1-4), ADR-0159 (MCP personal de tableros), ADR-0182/0183 (MCP de maquetas y
cerebro digital), vault `docs/vault-mcp-ecorex/` (commit 752abd10, en el tronco como 0601fdf7).

## 1. Problema

Una sesion de IA NUEVA de un cliente, con el MCP de tableros conectado, no supo crear un contenedor ni un reporte.
Las `instructions` del servidor son un parrafo y `tools/list` da esquemas, no el "como": orden de pasos, trampas,
recetas. Ya existe el vault `docs/vault-mcp-ecorex/` (30 notas: protocolo, reglas, 60 trampas, 11 recetas y la
referencia de 209 herramientas, validada contra los esquemas reales), pero hoy hay que COPIARLO al PC del cliente y
pegar un CLAUDE.md. Objetivo: que CUALQUIER IA conectada al MCP lo reciba sola, sin copiar archivos, y que se
actualice desde un solo lugar.

## 2. Lo que ya existe (revisado en el codigo)

- **Cerebro plantilla de plataforma** (ADR-0200 ola 3): `BrainTemplate` + `BrainTemplateNote`, pantalla
  `/cerebros-plantilla` (PlatformOperator). `ImportVaultAsync` importa un ZIP de Obsidian (`ReadVault`: titulo =
  nombre del archivo, ignora carpetas, `[[enlaces]]` por titulo, tope 200.000 caracteres por nota). **Publicar** siembra
  en todas las empresas / por plan / elegidas un `Brain` propio de cada tenant (`SourceTemplateId`, `IsLocked` = solo
  lectura, se re-sincroniza en cada publicacion).
- **Lectura por MCP** (ola 4): `listar_cerebros`, `listar_notas`, `buscar_notas` (subcadena en titulo y cuerpo),
  `ver_nota`, `grafo_cerebro` con parametro `cerebro` (slug). PERO solo en el **MCP de maquetas**
  (`/api/mcp/maquetas`, alcance `integ:maqueta`). El cliente normal tiene token `mcp:tableros`, asi que **no ve
  cerebros**.
- El vault cumple las reglas de titulo (sin `[ ] | # ^ / \`), la nota mas grande es de 30.000 caracteres y son 30
  notas: entra completo sin avisos.
- Limitaciones heredadas: el slug del cerebro sembrado sale del nombre (`UniqueSlugAsync`), puede variar por empresa;
  la siembra NO corre sola al crear una empresa nueva (hay que volver a publicar).

## 3. Propuesta

### 3.1 Contenido (sin codigo; lo hacen ECOREX.mcp y el Super Admin)

1. ECOREX.mcp ajusta el vault para leerse por MCP (la nota de entrada dice "lee esto con `guia_ecorex`"; la
   plantilla CLAUDE.md queda como opcion para quien quiera copia local) y genera el ZIP desde
   `docs/vault-mcp-ecorex/`.
2. ECOREX.mcp mueve al repo el generador de la referencia y el validador (`tools/vault-mcp/`, sin rutas locales ni
   datos privados: los patrones de privacidad se leen de un archivo local ignorado por git).
3. El Super Admin, en `/cerebros-plantilla`: crea "Guia de uso de ECOREX por IA", categoria "Plataforma", BLOQUEADA,
   destino **todas las empresas**, importa el ZIP, la marca como guia del MCP (3.2.1) y **Publica**.

### 3.2 Codigo (dev principal; un solo ADR nuevo, sugerido ADR-0204)

1. **Marcar cual plantilla es la guia.** Columna `BrainTemplate.IsMcpGuide` (bool, default false; migracion dual
   PG + SQL Server, sin datos). Una sola activa: al marcar una, se desmarca la anterior en la misma transaccion,
   auditado (`cerebro.plantilla.guia-mcp`). En `/cerebros-plantilla`, un check "Es la guia de los MCP".
2. **Nueva tool de solo lectura en el MCP de tableros: `guia_ecorex`** (ejecucion en `TableroMcpToolset`, archivo
   partial nuevo como en ADR-0193..0203). Sin gate de permisos de modulo: cualquier token `mcp:tableros` la lee.
   Esquema: `{"nota": string?, "buscar": string?}` (los dos opcionales, excluyentes).
   - Sin argumentos -> devuelve la nota de entrada ("00 - LEEME PRIMERO") + la lista de titulos de todas las notas.
   - `nota` -> esa nota completa (titulo exacto o normalizado, como `ver_nota`), con sus `[[enlaces]]` salientes.
   - `buscar` -> notas cuyo titulo o cuerpo contienen el texto (titulo + fragmento), max 20.
   - Origen del contenido: el cerebro SEMBRADO de la empresa (`Brain.SourceTemplateId` = plantilla con `IsMcpGuide`),
     para que lean lo publicado. Si la empresa no lo tiene (empresa creada despues de publicar), **respaldo**: leer
     las notas de la plantilla directamente si `PublishedVersion > 0`. Asi una empresa nueva funciona desde el dia 1.
   - Sin guia marcada o sin publicar -> error claro: "La guia aun no esta publicada por la plataforma".
   - Reusa `IMockupBrainService` (`ListNotesAsync` / `GetNoteAsync` por slug del cerebro sembrado) y lectura directa
     de `BrainTemplateNotes` para el respaldo. Nada de SQL crudo; tenant-scoped por el filtro global.
3. **`instructions` del servidor de tableros**: la PRIMERA frase pasa a ser: "Antes de construir o cambiar algo que no
   hayas hecho en esta sesion, llama `guia_ecorex` (sin argumentos) y sigue la receta que corresponda; ante un error,
   `guia_ecorex(buscar=<texto del error>)`." El resto del parrafo actual se puede ACORTAR (la guia lo cubre mejor).
   Opcional: la misma frase en las instrucciones de los MCP de maquetas y WhatsApp.
4. **Descripcion de la tool** (lo que lee el modelo en `tools/list`): "Manual de uso de este MCP: protocolo, reglas,
   trampas conocidas y recetas paso a paso (contenedores, conectores, reportes, flujos, formularios, drones, agentes,
   clasificacion IA). Llamala antes de construir algo nuevo." El catalogo pasa de 157 a 158.
5. **Tests** (condicion de merge, dual PG + SQL Server): catalogo consistente con `guia_ecorex` de solo lectura;
   funcional: plantilla guia publicada + sembrada -> sin argumentos devuelve la entrada y los titulos; `nota` y
   `buscar` funcionan; empresa SIN cerebro sembrado lee el respaldo de la plantilla; sin guia publicada -> error;
   aislamiento: una empresa no lee el cerebro sembrado de otra.

### 3.3 Fuera de esta entrega (siguientes, si se quieren)

- Leer los cerebros PROPIOS de la empresa (procesos, clientes) desde el MCP de tableros (`listar_cerebros`,
  `ver_nota` generales). Util para que la IA del cliente use su propio conocimiento; se decide aparte.
- Sembrar automaticamente las plantillas al crear una empresa (hoy hay que republicar). El respaldo de 3.2.2 cubre la
  guia mientras tanto.
- Generar la "Referencia" en vivo desde el catalogo del servidor en vez de notas generadas.

## 4. Como se mantiene al dia

Cuando se agregan o cambian herramientas: ECOREX.mcp corre `tools/vault-mcp/` (vuelve a bajar el catalogo, regenera
la referencia y valida las llamadas de ejemplo contra los esquemas), commitea el vault, y el Super Admin reimporta el
ZIP y publica. Los cerebros bloqueados se sincronizan solos y `guia_ecorex` sirve la version nueva. Agregar este paso
al checklist de cada ADR que toque un MCP.

## 5. Verificacion en vivo (post-deploy, ECOREX.mcp)

1. `tools/list` del MCP de tableros = 158 e incluye `guia_ecorex`; `initialize` trae la frase nueva en `instructions`.
2. `guia_ecorex` sin argumentos devuelve el LEEME y 30 titulos; `nota="R05 - Crear un contenedor de datos"` la
   devuelve completa; `buscar="Tiempo de espera agotado"` encuentra la receta y la trampa.
3. Prueba de humo con una sesion de IA nueva (sin vault local): "crea un contenedor con una tabla de pedidos y un
   reporte de ventas por mes". Debe llamar `guia_ecorex` primero y seguir R05/R07 sin errores de parametros.

## 6. Decisiones del usuario (2026-11-23)

1. Destino de la guia: **todas las empresas**.
2. Las empresas ven la guia tambien en su modulo Cerebros (cerebro de plataforma, solo lectura): **si**.
3. Leer los cerebros propios de la empresa por el MCP de tableros: **despues**, en una entrega aparte.
