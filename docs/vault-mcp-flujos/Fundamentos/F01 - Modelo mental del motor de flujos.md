---
tipo: fundamento
tema: flujos
---

# F01 - Modelo mental del motor de flujos

Volver a [[00 - LEEME PRIMERO]].

## Piezas

| Pieza | Que es | Herramientas |
|---|---|---|
| **Flujo** (definicion) | Un proceso con version. Nace en **borrador**; al publicarse queda activo. | `crear_flujo`, `editar_flujo`, `publicar_flujo` |
| **Nodo** | Un elemento del proceso: `inicio`, `tarea`, `compuerta` o `fin`. | `agregar_nodo`, `renombrar_nodo`, `eliminar_nodo` |
| **Salida** (arista) | Flecha de un nodo a otro. Tiene **nombre** y, en compuertas, **condicion**. | `conectar_nodos`, `nombre_salida`, `condicion_ruta` |
| **Concepto** | Tipo de actividad. Si tiene un flujo enganchado, cada tarea NUEVA de ese concepto arranca el flujo. | `enganchar_flujo_a_concepto` |
| **Instancia** | Una ejecucion del flujo dentro de una tarea concreta. | (la atiende la gente o `completar_paso`) |

## Como avanza

- **inicio**: se completa solo al crear la tarea; el "dueno" del inicio es quien la creo.
- **tarea**: un paso humano. Queda pendiente hasta que su encargado lo cierra.
- **compuerta**: una decision. Elige UNA de sus salidas (ver [[F02 - Las dos formas de decidir]]).
- **fin**: termina la instancia (o abre un ciclo nuevo si tiene `reinicio_nodo`).

> REGLA DE ORO: un nodo que **no es compuerta** sigue **TODAS** sus salidas a la vez (ramas en paralelo).
> Si un paso tiene dos flechas de salida, se activan las dos. Toda decision va en una compuerta.

## Quien atiende un paso (encargado)

`asignar_encargado_nodo` con `origen`:

| Origen | Quien |
|---|---|
| `politica` | Las personas de los **cargos/dependencias** que asignes con `asignar_cargo_nodo` (cualquiera lo toma). |
| `hereda_inicio` | Quien creo la tarea. |
| `hereda_anterior` | Quien cerro el paso anterior. |
| `campo_formulario` | El usuario elegido en un campo de formulario (`campo` = codigo del campo). |

Una compuerta o un fin solo tienen encargado si los marcas como **decision humana** (`decision_humana_nodo`).

## Borrador vs publicado (que se puede cambiar en vivo)

| Se cambia SOLO en borrador (requiere `nueva_version_flujo`) | Se cambia aun publicado (en vivo) |
|---|---|
| Nodos, salidas, nombres de salida, condiciones | Encargado y cargos |
| Decision humana (`decision_humana_nodo`) | Formularios del nodo y su obligatoriedad |
| Reinicio (`reinicio_nodo`) | Avisos (`agregar_notificacion_nodo`), plazos, arranque |
|  | Tablero/columna, concepto de cierre, salto, color, nota, reglas |
|  | Agente de IA del paso y todos sus recursos ([[F12 - Receta agente de IA en un paso]]) |

Detalle en [[F08 - Modificar un flujo publicado]].

## Lo que configura cada nodo (todo se ve en `ver_flujo`)

- `decision_humana` (compuertas y fines), `agente_ia`, `encargado_origen`, `cargos`
- `reinicia_en`, `tablero_destino`, `columna_destino`, `concepto_cierre`, `salta_a_flujo`
- `plazo`, `tiempo_arranque`, `avisos_al_llegar` (numerados), `formularios` (con `obligatorio_en_salidas` y
  `crear_al_llegar`), `reglas`, `color`, `nota`

Y cada salida: `nombre` y `condicion`. Si `ver_flujo` devuelve `avisos`, hay algo roto: corrigelo antes de publicar.
