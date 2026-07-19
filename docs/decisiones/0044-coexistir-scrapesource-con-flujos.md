# ADR-0044: coexistir con ScrapeSource/ScrapeRun (no absorber todavia)

Fecha: 2026-07-18
Estado: Aceptada
Contexto: modulo 000730 "Extraccion de Datos", Ola 5.

## Contexto

El modulo /extraccion-datos tenia, antes de este capitulo, un scraper HTTP simple: `ScrapeSource`
(URL + selector CSS, sin JS, sin navegador) con ejecutor server-side (`IScrapeService`, ADR-0025) y su
bitacora `ScrapeRun`. Las Olas 1-4 construyeron el nuevo modelo de FLUJOS (`ScrapeFlow`, ejecutado por
el sub-agente Navegador). La UI de /extraccion-datos hoy muestra los FLUJOS; el scraper simple sigue en
el backend pero sin pantalla propia. Hay que decidir que hacer con el.

El doc 02 s7 plantea dos caminos: ABSORBER (migrar cada `ScrapeSource` a un `ScrapeFlow` degenerado y
retirar `IScrapeService`) o COEXISTIR (dejar el scraper HTTP para URLs publicas triviales, que no
necesitan un agente, y usar los flujos para lo que requiere navegador real).

## Decision

**Coexistir**. `ScrapeSource`/`ScrapeRun`/`IScrapeService` se conservan intactos; los `ScrapeFlow` son
el camino nuevo y principal. Razones:

- **No romper lo que funciona**: el scraper HTTP resuelve el caso barato (una URL publica, sin login, sin
  JS) SIN necesitar una colmena on-prem conectada. Absorberlo obligaria a que hasta el scrape mas trivial
  dependa de un agente, que es un requisito mayor.
- **La absorcion tiene mas sentido cuando el runtime de flujos este PROBADO de punta a punta** (con la
  colmena + proveedor de IA reales), no antes. Migrar ahora seria mover carga a un camino aun no
  validado en vivo.
- El costo de coexistir es bajo: dos modelos con nombres claros (`ScrapeSource` = HTTP simple;
  `ScrapeFlow` = navegador) y una sola pantalla que hoy expone los flujos.

## Consecuencias

- `ScrapeSource`/`ScrapeRun` quedan como estan; su ejecutor `IScrapeService` sigue registrado. No hay
  pantalla nueva para ellos (la UI del modulo es la de flujos); si se necesitara, se re-expone.
- Reevaluar la ABSORCION cuando: (a) el runtime de flujos corra E2E contra la colmena, y (b) se confirme
  que no hay casos de scrape que valga la pena resolver sin agente. En ese momento se migraria cada
  `ScrapeSource` a un `ScrapeFlow` de un paso Extract y se retiraria `IScrapeService`, con su propio ADR.
- Deuda registrada: dos rutas de "extraccion" en el backend hasta esa decision. Es explicita y acotada.

## Alternativas descartadas

- **Absorber ahora**: prematuro (mueve casos triviales a un runtime sin validar en vivo) y mas riesgo por
  menos beneficio inmediato.
- **Borrar `ScrapeSource` ya**: perderia el unico camino que no necesita colmena, sin reemplazo probado.
