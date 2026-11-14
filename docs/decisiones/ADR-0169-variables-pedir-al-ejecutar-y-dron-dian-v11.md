# ADR-0169: Variables "Pedir al ejecutar" y DRON DIAN v11 (ventanas cortas, saltar lo ya cargado)

**Estado:** Aceptado (local; pendiente de entrega al tronco y de migrar la config a prod con OK del usuario)
**Fecha:** 2026-11-14
**Decisores:** usuario (dueno del producto), sesion ECOREX.dian

## Contexto

En el conciliador DIAN de SOLDARCO aparecian "filas fantasma" (documentos que estan en NEWTON y no en la
DIAN). La causa no era la DIAN sino el dron de descarga:

1. El script v8 (prod) se saltaba paginas y en las ventanas 2..6 aceptaba la grilla vieja. La v10 lo
   corrigio, pero la corrida del 2026-11-13 mostro un segundo limite: la busqueda de la DIAN parece
   devolver como maximo 100 registros, y SOLDARCO recibe 70-80 facturas por semana, asi que una ventana
   de 30 dias perdia la mayoria (septiembre: 64 DIAN contra 308 fantasmas).
2. Cada corrida volvia a bajar TODO el rango (~10 s por documento con su token de Turnstile), lo que hace
   inviable mirar muchos dias atras sin que la sesion DIAN venza.
3. El usuario quiere poder recuperar meses viejos por tramos y que el modulo le PREGUNTE el desplazamiento
   al lanzar el bot, mostrando el valor por defecto.
4. El barrido de corridas colgadas cerraba como "colgada" toda corrida de mas de 20 min, aunque siguiera
   viva (la sola espera de descargas del dron dura 19 min).

## Decision

- **Variable de flujo "Pedir al ejecutar"** (generica, no solo DIAN): `ScrapeVariable.AskOnRun` +
  `ScrapeVariable.Prompt` (migracion `AddScrapeVariableAskOnRun`, PG + SQL Server). Se marca en el editor de
  variables de Extraccion de datos (no aplica a secretas). Cuando un modulo dispara los flujos
  ("Importar del bot" del conciliador), primero lista esas variables (`IBrowserRunService.GetAskVariablesAsync`)
  y abre un modal prellenado con el valor guardado. Las respuestas viajan como `overrides` a
  `RunFlowStepwiseAsync`: se ponen en la capa de sesion (la que ya se superpone a las variables en cada paso)
  y se retiran al terminar. **Nunca se persisten.**
- **Saltar lo ya cargado:** el conciliador inyecta `CUFES_CONOCIDOS` (prefijos de 12 hex de los CUFE que la
  fuente Bot ya proceso en el rango DIAS_DESDE..DIAS_DESDE+DIAS_ATRAS, con una semana de holgura). Un checkbox
  del modal permite volver a bajar todo. Corridas programadas o "Ejecutar ahora" no lo traen y bajan todo
  (comportamiento anterior).
- **Script DRON v11 (config del flujo, no codigo):** ventanas de `VENTANA_DIAS` (por defecto 7),
  desplazamiento `DIAS_DESDE`, salta los `CUFES_CONOCIDOS`, guarda la info de la ULTIMA pagina en la traza y,
  si una ventana termina exactamente en un multiplo de 100 registros, la parte en dos y la vuelve a buscar.
  Config recomendada: DIAS_ATRAS=30 (pedir), DIAS_DESDE=0 (pedir), espera de descargas 60 min (termina antes
  al ver el fin).
- **Barrido de colgadas:** `BrowserRunService` lleva un set en memoria de corridas vivas; el barrido las salta.
  Tras un reinicio el set nace vacio y las huerfanas si se cierran.

## Opciones consideradas

- Valores de una sola corrida guardados en BD: descartado (ensucia la config y deja valores viejos).
- Inyectar los CUFE desde el motor generico: descartado (es conocimiento del modulo DIAN); lo hace el
  conciliador, que es quien dispara el flujo.
- Mandar los CUFE completos: descartado por tamano (2000 x 96 caracteres); 12 hex (48 bits) bastan.

## Consecuencias

- La corrida diaria baja solo lo nuevo (minutos en vez de una hora) y se puede mirar 30-45 dias atras.
- Recuperar meses viejos = corridas separadas cambiando DIAS_DESDE en el modal.
- El tope de 100 queda por confirmar con la traza de la v11 (la regla de partir es inocua si no existe).
- La config v11 vive en la BD local; migrarla a prod requiere OK explicito del usuario.
