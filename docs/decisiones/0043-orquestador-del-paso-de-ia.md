# ADR-0043: orquestacion del paso de IA server-side, con el JS de la IA firmado por el servidor

Fecha: 2026-07-18
Estado: Aceptada
Contexto: modulo 000730 "Extraccion de Datos", Ola 4 (paso de IA).

## Contexto

Un paso de IA de un flujo de extraccion (doc 03 s2) NO manda un JS fijo: un agente de IA maneja el
navegador para cumplir una instruccion en lenguaje natural, acotado por una allow-list de tools, un tope
de pasos y un tope de tiempo. Hay que decidir DONDE corre el bucle (agente<->navegador) y COMO se
autoriza el JS que la IA genera.

Dos tensiones:
1. El **LLM** es server-side (AI Provider Gateway, cupos por plan). El **navegador** es el sub-agente de
   la colmena (on-prem). El bucle tiene que puentear los dos.
2. El sub-agente Navegador **rechaza JS sin firma** (fail-closed, ADR/olas del Navegador). El doc 03 s2
   asumia que el JS de la IA iria por el **MCP local** del agente (loopback, sin firma). Pero el servidor
   no puede alcanzar ese MCP loopback; su unica via al navegador es el hub, que exige firma.

## Decision

**El bucle corre en el SERVIDOR** (`AiStepOrchestrator`), reusando el tool-calling del AI Provider
Gateway (`IAiProviderClient.CompleteWithToolsAsync`, Claude + OpenAI-compat, ya existente). Las TOOLS que
se le ofrecen al modelo son acciones del navegador (`navegar`, `leer_html`, `esperar`, y -solo si el
operador las incluyo en la allow-list- `evaluar_js`, `clic`), mas `guardar_filas` (el sumidero que
ingiere en la tabla destino). Cada tool call se ejecuta en el agente por el **canal request/response**
(`IBrowserActionChannel`, sobre el hub), no por el MCP loopback.

Como ese canal pasa por el hub, **el servidor FIRMA el JS que la IA genera** (`AgentSign.SignJs`, igual
que el compilador determinista) antes de mandarlo. El servidor "avala" JS generado por un LLM; la
contencion es triple y explicita (doc s2): la **allow-list de tools** (las potentes -eval/clic- no se
ofrecen salvo que el operador las habilite; vacia = solo lectura), el **tope de pasos** y el **tope de
tiempo**; ademas sigue aplicando la **allow-list de DOMINIOS** del agente. El consumo se registra en el
modulo de tokens (`IAiUsageService`) y respeta el cupo del plan.

## Consecuencias

- El paso de IA es construible y **testeable server-side sin la colmena ni llaves reales**: el AI
  Gateway, el canal del navegador, el resolver de proveedor y el sumidero de filas son seams; los tests
  cubren el bucle, la traduccion tool->accion, la firma, la allow-list y la ingesta.
- Se aparta del "MCP loopback sin firma" del doc 03 s2 a proposito: la orquestacion server-side sobre el
  hub es equivalente en capacidad y encaja con lo ya construido, y firmar mantiene el fail-closed del
  agente intacto (no se relaja la seguridad del Navegador para el caso de IA).
- Riesgo asumido: el servidor firma JS de un LLM. Mitigado por la allow-list de tools (default seguro:
  sin eval/clic), los topes, y la allow-list de dominios. Endurecimientos futuros (sandbox del eval,
  lista blanca de patrones) quedan como backlog.
- Seleccion de proveedor: por ahora se toma el primer `AiProviderConfig` habilitado; afinar la eleccion
  por-paso (el `AiProviderId`/modelo que configuro el operador) queda como mejora.

## Alternativas descartadas

- **Bucle en el agente (colmena) usando el MCP loopback sin firma**: evitaria firmar JS de IA, pero
  mueve la orquestacion + las llaves del LLM + el control de cupo al on-prem, rompiendo el gobierno
  central del AI Gateway y complicando el despliegue. Se prefiere el servidor como duenno del bucle.
- **No firmar y relajar el fail-closed del agente para tool calls de IA**: debilita la defensa que
  protege TODO el sub-agente Navegador. Inaceptable.
