# ADR-0162: Render de PDF (Puppeteer) con reintento y cap de concurrencia

**Estado:** Aceptado
**Fecha:** 2026-11-11
**Decide:** Claude Opus 4.8 (a pedido del usuario)

## Contexto

En prod (app2.bitcode.com.co, v0.16.303) la ruta
`/formularios/plantilla/{responseId}/pdf?templateId=...` devolvia `500` de forma
intermitente. El log del contenedor `ecorex-app` mostro la causa exacta:

```
PuppeteerSharp.TargetClosedException: Protocol error (Emulation.setTouchEmulationEnabled): Target closed.
(Connection failed to process Network.policyUpdated. Timeout of 1000 ms exceeded.)
  ...
  at Ecorex.Infrastructure.Rendering.PuppeteerQuotePdfRenderer.RenderUrlToPdfAsync(...) :line 25
```
(y la variante `Protocol error (Performance.enable): Session closed. Most likely the Page has been closed`).

Es decir: el Chromium headless que la app levanta para "imprimir" la pagina **se
cerraba a mitad del arranque** (crear pagina / setear viewport), antes de poder
generar el PDF. Evidencia clave en el mismo log: la **mayoria** de PDFs SI se
generaban (decenas de cotizaciones enviadas correctamente), o sea el fallo era
**intermitente**, no una mala configuracion. El renderer ya usaba los flags
correctos para contenedor (`--no-sandbox`, `--disable-setuid-sandbox`,
`--disable-dev-shm-usage`).

Dos defectos del diseno previo lo disparaban:

1. **Se lanzaba un Chromium nuevo por cada peticion** (`await using var browser =
   await LaunchAsync()` dentro de cada metodo), sin limite. Cuando caian varios
   PDFs a la vez, varios Chromium arrancaban en paralelo -> pico de memoria/CPU ->
   alguno excedia el timeout interno de 1 s de PuppeteerSharp al procesar un evento
   CDP (`Network.policyUpdated`) y Chromium cerraba el target.
2. **No habia reintento**: ese fallo transitorio reventaba la peticion con `500`.

## Decision

Endurecer `PuppeteerQuotePdfRenderer` con dos defensas, sin cambiar las firmas de
`IQuotePdfRenderer` ni el contrato de las 3 rutas (`RenderUrlToPdf`,
`RenderHtmlToPdf`, `RenderUrlToImage`), que ahora comparten un nucleo
`RunAsync(gate + retry)`:

1. **Cap de concurrencia** con un `SemaphoreSlim` **estatico** (el servicio es
   `AddScoped`, por lo que el limite debe ser de proceso, no por peticion). No se
   lanzan mas de `N` Chromium a la vez. `N` por defecto = **2**, ajustable por env
   `ECOREX_PDF_MAX_CONCURRENCY`. Esto corta el pico de recursos que mataba los targets.
2. **Reintento de transitorios**: se captura `PuppeteerException` (base de
   `TargetClosedException` y `NavigationException`) y se **relanza un Chromium fresco**
   en cada intento, con backoff lineal (400ms * intento). Por defecto **2 reintentos**
   (hasta 3 intentos), ajustable por `ECOREX_PDF_RENDER_RETRIES`. Al agotar intentos
   se relanza la ultima excepcion preservando su stack (`ExceptionDispatchInfo`).

Ademas se agregan `--disable-gpu` y `--no-zygote` a los args de lanzamiento (menos
procesos hijos que se cuelguen en contenedor); los flags previos se conservan.

## Alternativas consideradas

- **Pool de navegadores reutilizados**: mas eficiente, pero mas complejo (ciclo de
  vida, paginas huerfanas, reinicio ante crash). El cap + reintento resuelve el
  sintoma con mucho menos riesgo; el pool queda como mejora futura si hiciera falta.
- **Solo subir RAM/swap del host**: trata el OOM pero no el timeout de PuppeteerSharp
  bajo contencion. Se puede combinar, pero no sustituye al cap+reintento.

## Consecuencias

- Los `500` intermitentes del render de PDF se mitigan: un target muerto se reintenta
  con un navegador nuevo, y nunca hay mas de N Chromium compitiendo por memoria.
- Bajo rafagas, los PDFs se **serializan** (tope N) en vez de fallar: mas latencia por
  cola, pero sin errores. Afinable por env sin recompilar.
- Si el fallo persistiera pese a los reintentos, se devuelve el error real (stack
  preservado) para diagnostico.

## Verificacion

- Build Debug de Infrastructure + SuperAdmin: 0 errores.
- E2E en 5234/AGRO sobre la MISMA plantilla que fallaba en prod: `200`,
  `content-type application/pdf`, 52.557 bytes, cabecera `%PDF-`.
- 4 PDFs en paralelo: los 4 `200` en ~20.8 s (= 2 tandas de 2), confirmando que el
  semaforo serializa a N=2 sin deadlock ni errores.
