---
tipo: receta
modulo: drones
referencia: "[[T04 - Drones de extraccion web]]"
---

# R08 - Crear un dron de extraccion web

Volver a [[00 - LEEME PRIMERO]]. Referencia: [[T04 - Drones de extraccion web]].

## Paso 0 - Dron o conector?

| Situacion | Usa |
|---|---|
| La fuente tiene API o datos abiertos | **Conector** ([[R06 - Conector API e importacion programada]]): mas rapido y estable |
| Solo hay una pagina web (tabla, listado, enlaces a PDF) | **Dron** |
| La pagina tiene reCAPTCHA | Ni dron: busca otra fuente o descarga manual |
| Reto anti-bot por JavaScript (Radware y similares) | Dron: el navegador real del agente colmena normalmente lo pasa (prueba) |

El dron corre en un **agente colmena** del cliente (PC o servidor con navegador real), no en la nube.

## Paso 1 - Preparar el destino

La tabla destino debe existir con las columnas que vas a llenar ([[R05 - Crear un contenedor de datos]]),
incluida una **columna unica** para no duplicar (ej. `url`). Luego:
```json
{"name":"listar_destinos_extraccion","arguments":{}}
```
Lista las tablas destino posibles (`Contenedor / Tabla`) con su id.

## Paso 2 - Cabecera

```json
{"name":"crear_dron","arguments":{"nombre":"Convocatorias portal X","url_inicio":"https://www.ejemplo.gov.co/contratacion/",
  "contenedor":"<id_tabla_destino>","descripcion":"Baja convocatorias y enlaces a pliegos"}}
```
-> `dron_id`. Paginacion por numero de pagina opcional: `pagina_var`, `pagina_desde`, `pagina_hasta`.

## Paso 3 - Pasos (en orden)

Ver los tipos con su configuracion en [[T04 - Drones de extraccion web]] (`agregar_paso`). Los mas usados:

| Tipo | Campos clave | Para que |
|---|---|---|
| `navegar` | `url` (admite `{{VARIABLE}}`) | Ir a una pagina |
| `esperar` | `espera_ms` (o `selector`) | Esperar render / reto anti-bot. **No uses `mapeo_json` para el tiempo** |
| `clic` | `selector` | Pulsar un boton/enlace/pestana |
| `inyectar_script` | `script` | Ejecutar JS (rellenar un buscador, expandir secciones) |
| `extraer` | `script` (JS que RETORNA filas), `contenedor_destino`, `mapeo_json`, `ingesta_llave` | Sacar datos a la tabla |
| `ia` | `instruccion`, `tope_pasos`, `tope_segundos` | Un agente maneja el navegador segun instrucciones |
| (cualquiera) | `tope_segundos` | Mas tiempo para un paso lento antes de que el servidor cancele la corrida |
| `procesar_archivos` / `cerrar_navegador` / `notificar` / `crear_actividad` | segun tipo | Post-proceso |

Ejemplo minimo (abrir, esperar, extraer):
```json
{"name":"agregar_paso","arguments":{"dron":"Convocatorias portal X","tipo":"navegar","nombre":"Abrir portal",
  "url":"https://www.ejemplo.gov.co/contratacion/"}}
{"name":"agregar_paso","arguments":{"dron":"Convocatorias portal X","tipo":"esperar","nombre":"Esperar render","espera_ms":6000}}
```

### El paso `extraer` (el importante)

El `script` debe **devolver un arreglo de objetos** (una fila por objeto). Plantilla robusta:
```javascript
function txt(el){ return el ? (el.textContent||'').replace(/\s+/g,' ').trim() : ''; }
var rows = [], vistos = {};
document.querySelectorAll('.item-convocatoria').forEach(function(it){
  var a = it.querySelector('a[href]');
  if(!a) return;
  var u = a.href;
  if(vistos[u]) return;          // dedup dentro de la corrida
  vistos[u] = 1;
  rows.push({ titulo: txt(it.querySelector('.titulo')), descripcion: txt(it.querySelector('.resumen')), url: u });
});
return rows;
```
```json
{"name":"agregar_paso","arguments":{"dron":"Convocatorias portal X","tipo":"extraer","nombre":"Extraer convocatorias",
  "script":"<el JS de arriba>","contenedor_destino":"<id_tabla_destino>",
  "mapeo_json":"{\"titulo\":\"titulo\",\"descripcion\":\"descripcion\",\"url\":\"url\"}",
  "ingesta_llave":"url"}}
```
- `mapeo_json` del dron = `{"campo_del_objeto":"columna_destino"}`.
- **`ingesta_llave`** = columna (mapeada y unica) para **upsert**. Sin ella cada corrida AGREGA filas (duplica).
- Si **ninguna columna es unica**, arma una clave compuesta en el JS (`clave: numero + '|' + u`), mapeala a una
  columna nueva y usa esa como llave. Si la tabla YA tenia filas, primero rellenalas con la misma plantilla
  (`rellenar_columna` `{numero}|{url}`) y DESPUES activa la llave ([[04 - Trampas conocidas]] #17).
- Guarda tu script en un archivo: por MCP no se puede volver a leer, y `editar_paso` con `script` lo reemplaza entero.
- El script es **sincrono**: nada de `async`/`await`/`fetch`/`.then` (el paso devolveria `{}` y 0 filas sin error).
  Lee el DOM ya pintado; para un API del portal usa `XMLHttpRequest` sincrono ([[04 - Trampas conocidas]] #24).

### Portales lentos (login, ERP, consultas pesadas)

La corrida completa va en UNA orden al agente con un tiempo total calculado por paso (navegar 30 s, extraer 20 s,
esperar = su `espera_ms`, el resto 5 s). Si se acaba, el servidor cancela y el agente cierra el navegador sin
guardar filas. Al paso lento ponle `tope_segundos`:
```json
{"name":"editar_paso","arguments":{"paso_id":"<id del paso extraer o navegar lento>","tope_segundos":180}}
```
Usa `espera_ms` solo para pausas que SIEMPRE hacen falta; `tope_segundos` es un limite, no una pausa.

## Paso 4 - Variables (opcional)

```json
{"name":"definir_variable","arguments":{"dron":"Convocatorias portal X","nombre":"USUARIO_PORTAL","valor":"compras@miempresa.com"}}
{"name":"definir_variable","arguments":{"dron":"Convocatorias portal X","nombre":"CLAVE_PORTAL","secreta":true,
  "pedir_al_correr":true,"prompt":"Clave del portal"}}
```
Se usan como `{{USUARIO_PORTAL}}`. `secreta:true` la cifra (no se vuelve a leer en claro). Mejor aun:
`pedir_al_correr` para que el usuario la digite al correr y no quede guardada.

## Paso 5 - Programar

```json
{"name":"guardar_programacion","arguments":{"dron":"Convocatorias portal X","nombre":"Diaria 7am","tipo":"cron",
  "cron":"0 7 * * *","activa":true}}
```
`tipo`: `manual`, `intervalo` (`cada_minutos`), `cron` (hora de la empresa). `tramos` = barrido por tramos (corre
varias veces moviendo dos variables, ej. `DIAS_DESDE`/`DIAS_ATRAS`). Devuelve la **proxima corrida**.

## Paso 6 - Correr y verificar

- **No hay herramienta MCP para correr ya.** El usuario usa "Ejecutar ahora" en **/extraccion-datos** (asignando el
  agente colmena), o se espera la programacion.
- Revisa:
```json
{"name":"listar_corridas","arguments":{"dron":"Convocatorias portal X","limite":5}}
{"name":"ver_dron","arguments":{"dron":"Convocatorias portal X"}}
```
  `resultado` (ok/error/omitida/agente_desconectado), filas insertadas/actualizadas, detalle.
- Valida el upsert: corre dos veces seguidas; el conteo de la tabla NO debe subir la segunda vez (verifica con un
  reporte de conteo, [[R07 - Crear reportes e indicadores]]).

## Mantenimiento

`editar_dron` (cabecera), `editar_paso` (solo lo que envies), `reordenar_pasos` (lista COMPLETA de `paso_id`),
`eliminar_paso` / `eliminar_variable` / `eliminar_programacion` / `eliminar_dron` (confirma antes).

## Enriquecer lo extraido

Un dron trae lo que se ve en la pagina. Para extraer datos de los **PDF enlazados** (presupuesto, plazos,
requisitos) crea un **agente de IA** con lector de medios ([[R09 - Crear y configurar un agente de IA]]); para
etiquetar filas masivamente usa [[R10 - Clasificar datos con IA]] con `configurar_paso_clasificacion` sobre la
programacion del dron (con el id de la programacion y `tabla`, que es obligatoria cuando la programacion es de un
dron).
