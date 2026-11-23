# ADR-0204: La guia de los MCP como cerebro de plataforma (guia_ecorex)

- Estado: Aceptado
- Fecha: 2026-11-23
- Modulo: Cerebros (ADR-0200) + MCP personal de tableros (ADR-0159)
- Relacionados: ADR-0200 ola 3 (cerebros plantilla de plataforma), ADR-0182/0183 (MCP de maquetas y cerebro),
  ADR-0203 (ver_conector). Spec: `docs/mcp/SPEC-guia-mcp-como-cerebro.md` (aprobada por el usuario 2026-11-23).

## Contexto

Una sesion de IA nueva de un cliente, con el MCP de tableros conectado, no supo crear un contenedor ni un reporte: las
`instructions` del servidor son un parrafo y `tools/list` da esquemas, no el "como". Ya existe el vault
`docs/vault-mcp-ecorex/` (protocolo, reglas, trampas, recetas y la referencia de las herramientas), pero habia que
copiarlo al PC del cliente. Los cerebros plantilla (ola 3) ya permiten publicar un vault a todas las empresas como
cerebro bloqueado, pero su lectura por MCP solo existe en el MCP de maquetas (alcance `integ:maqueta`), que el cliente
normal (token `mcp:tableros`) no tiene.

## Decision

1. **`BrainTemplate.IsMcpGuide`** (bool, default false; migracion dual `AddBrainTemplateMcpGuide`, solo agrega la
   columna). `IBrainTemplateService.SetMcpGuideAsync` la marca o desmarca; **una sola activa**: al marcar una se
   desmarcan las demas en el mismo `SaveChanges` (una transaccion), auditado como `cerebro.plantilla.guia-mcp`. En
   `/cerebros-plantilla` hay un check "Es la guia de los MCP" y la tarjeta muestra el distintivo "Guia MCP".
2. **Herramienta `guia_ecorex {nota?, buscar?}`** en el MCP de tableros, de solo lectura y **sin gate de modulo**
   (leer el manual no expone datos del tenant). Sin argumentos: la nota de entrada ("00 - LEEME PRIMERO" o la primera
   que empiece por "00") y los titulos de todas las notas; `nota`: la nota completa (titulo exacto normalizado o
   parcial unico) con sus enlaces `[[...]]`; `buscar`: titulo + fragmento, maximo 20, primero los que coinciden en el
   titulo. `nota` y `buscar` son excluyentes.
3. **Origen del contenido** (`McpGuideService`): el cerebro SEMBRADO de la empresa (`Brain.SourceTemplateId` = la
   plantilla guia), que es lo publicado; si la empresa no lo tiene (creada despues de publicar), **respaldo** con las
   notas de la plantilla, solo si `PublishedVersion > 0`. Sin guia marcada, archivada o sin publicar: error claro. El
   cerebro y sus notas pasan por el filtro global de tenant (una empresa nunca lee el cerebro de otra); de la plantilla
   (metadato de plataforma, sin tenant) solo se lee la marcada como guia.
4. **`instructions` del servidor de tableros**: la primera frase manda llamar `guia_ecorex` antes de construir algo
   nuevo y buscar ahi el texto de los errores. No se agrega a los MCP de maquetas y WhatsApp porque esos servidores no
   exponen `guia_ecorex` (la frase los mandaria a una herramienta que no tienen).

## Opciones consideradas

- **Reusar `IMockupBrainService` por slug**: el slug del cerebro sembrado sale del nombre y puede variar por empresa;
  buscar por `SourceTemplateId` es directo y estable. Se lee con el mismo modelo (`MockupNotes`, `MockupBrainLinks`).
- **Exponer en tableros los cerebros propios de la empresa** (`listar_cerebros`, `ver_nota`): decision del usuario,
  queda para una entrega aparte.
- **Meter el manual completo en `instructions`**: demasiado largo y sin busqueda; descartado.

## Consecuencias

- MCP de tableros: 158 herramientas (`guia_ecorex` siempre expuesta). Migracion dual de una columna, sin datos.
- Cuando cambian las herramientas: ECOREX.mcp regenera y valida el vault, el Super Admin reimporta el ZIP y publica;
  los cerebros bloqueados se sincronizan y `guia_ecorex` sirve la version nueva.
- Pruebas dual: `BrainTemplatesTests.GuiaMcp_UnaSola_SembradoYRespaldo_Aislado` (una sola guia, sin publicar = error,
  sembrado vs respaldo, aislamiento, archivada = apagada) y el catalogo de `McpTablerosTests` (solo lectura,
  despacho, `nota`/`buscar` excluyentes).
