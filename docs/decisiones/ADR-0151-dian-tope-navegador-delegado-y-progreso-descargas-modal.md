# ADR-0151: DIAN - subir el tope del navegador delegado (3->20 min) y mostrar el progreso de descargas en vivo en el modal

**Status:** Accepted
**Date:** 2026-11-04
**Deciders:** Usuario (dueno del producto), Claude Opus 4.8

## Contexto

El dron de extraccion masiva de compras DIAN (flujo DRON GESTION DE COMPRAS DIAN, tenant SOLDARCO)
traia "pocos documentos" (~45) y el usuario sospechaba que "a veces se queda en una misma pagina".

Dos hallazgos:

1. **Causa raiz del "pocos documentos": un tope de 3 min en el agente.**
   `DelegatedBrowserSubAgent.Timeout` (en `Ecorex.Agent.Core`, el Navegador que el Servicio pide
   prestado a la colmena por el pipe, ADR-0039 s4) cortaba CUALQUIER secuencia de navegador delegada
   a los 3 min. El paso legitimo "Esperar fin de descargas" (que espera a que el motor de ventanas
   termine de bajar todos los ZIP) puede tardar mucho mas de 3 min en una extraccion grande (6
   ventanas de 30 dias), aunque el paso pidiera 10 o 19 min. El tope del agente ganaba y mataba la
   corrida a los 3 min -> solo aterrizaban los documentos bajados hasta ese momento.

2. **Falta de visibilidad: el modal de estado no mostraba cuantos documentos iba bajando el bot.**
   El modal "Extraccion en curso" del conciliador (ConciliacionDian.razor, boton "Importar del bot")
   mostraba los pasos del flujo, pero el paso de espera se veia como un "Trabajando..." opaco: no se
   sabia si el bot avanzaba o estaba colgado.

## Decision

1. **Subir `DelegatedBrowserSubAgent.Timeout` de 3 a 20 min.** Sigue siendo finito (si una colmena
   muere a mitad, el servidor no espera un acuse eterno), pero deja respirar a los pasos de espera
   legitimos. No dana la deteccion de colmenas muertas en pasos cortos: el servidor conserva su
   propio tope por-accion (~60s + WaitMs en `BrowserRunService`) y corta primero cuando la accion es
   rapida; este tope del agente solo manda cuando la accion legitimamente pide una espera larga.

2. **Progreso de descargas en vivo en el modal.** El motor de descargas (paso InjectScript del flujo)
   mantiene un elemento oculto `#dron-prog` con el texto "descargando: N documentos (ventana X/Y,
   pag Z)", que actualiza cada vez que baja un ZIP. En el servidor, el paso Wait con selector se
   atiende con un nuevo metodo `BrowserRunService.RunWaitConProgresoAsync` que, en vez de una sola
   espera ciega, sondea cada 4s con un Eval firmado que devuelve `{fin, prog}`: `fin` = existe el
   selector de fin (`#dron-fin`); `prog` = el texto de `#dron-prog`. Cada cambio de `prog` se empuja
   al modal por el callback de progreso existente (`FlowStepProgress.Detail` -> SignalR). Al detectar
   `fin`, toma una captura final y registra "Descargas finalizadas (descargando: N ...)". Si se agota
   el tiempo, registra "Espera agotada (timeout)" y el flujo sigue a Procesar con lo que haya en disco
   (comportamiento benigno, igual que antes).

## Alternativas consideradas

- **Quitar el tope del agente:** descartado. Sin tope, una colmena que muere deja al servidor
  esperando para siempre. 20 min es generoso pero acotado.
- **Subir solo el `WaitMs` del paso:** no bastaba. El `WaitMs` ya pedia 19 min, pero el tope del
  agente (3 min) lo recortaba antes. Habia que subir el tope del agente.
- **Streamear el progreso por un canal nuevo:** descartado. Reusar el `FlowStepProgress.Detail` que
  ya viaja al modal por SignalR evita superficie nueva; el sondeo cada 4s es suficiente para un
  contador que el usuario lee de reojo.

## Consecuencias

- **Mas facil:** una corrida grande ya no se corta a los 3 min (validado: una sola corrida baja 153
  docs en ~16 min, llegando hasta agosto, contra ~45 antes). El usuario ve el contador subir en vivo
  en el modal, asi distingue "avanzando" de "colgado".
- **Mas dificil / a vigilar:** el sondeo cada 4s agrega Evals al pipe mientras el motor descarga
  (tiny, serializados por WebView2; despreciable). Si el motor entra en un bucle de reintento de
  Turnstile sobre un documento (sintoma "se queda en una misma pagina"), la espera puede tardar
  minutos extra hasta que el motor agota los reintentos y llega a `fin`; con 19 min de espera se
  resuelve solo, pero conviene endurecer el motor (tope de reintentos por documento) en otra pasada.
- **Version:** el agente que porta el tope de 20 min es el MSI v1.9.4 (ya instalado en la PC del
  usuario). El backend que porta el progreso en el modal es v0.16.274.

## Pendientes

- Deploy a prod (agente del server a v1.9.4 + flujo DIAN + backend v0.16.274): a la espera del OK por
  version del usuario.
- Endurecer el motor de descargas: tope de reintentos por documento para no quedarse en una pagina.
- Reflejar este ADR en el vault.
