---
tipo: guia
tema: errores
---

# Solucion de problemas

Volver a [[00 - LEEME PRIMERO]]. Busca el mensaje (o sintoma) y aplica la salida. Ver tambien
[[04 - Trampas conocidas]].

## Conexion y permisos

| Mensaje / sintoma | Causa | Salida |
|---|---|---|
| HTTP 401 | Token invalido, revocado, de otro servidor o usuario inactivo | Nuevo token (ver [[01 - Conectar los MCP]]) |
| `Herramienta desconocida: X` | No existe en esta version del servidor | Revisa `tools/list`; usa la equivalente o hazlo en pantalla |
| "sin permiso" / "no autorizado" en una herramienta | El usuario del token no tiene ese permiso en Roles y permisos | Pide al administrador el permiso; no intentes rodearlo |
| No aparece un tablero/tarea que el usuario dice que existe | Visibilidad del usuario (tablero restringido, alcance) | Confirma con el usuario; usa `buscar_tareas` y `listar_tableros` |
| Un parametro "no existe" | Nombre mal escrito / no adivinar | Revisa la ficha de la herramienta en la Referencia |

## Tareas y flujos

| Mensaje / sintoma | Causa | Salida |
|---|---|---|
| No deja mover/terminar la tarea | La gobierna un flujo en curso | `ver_tarea` -> `completar_paso` con la `opcion` exacta |
| `completar_paso` rechazado por formulario | El paso exige formulario sin diligenciar | El usuario lo llena en la app; luego completa |
| Pide motivo de cierre | Columna de cierre con concepto obligatorio | Envia `motivo_cierre` |
| `publicar_flujo` devuelve avisos y no publica | Grafo con ramas paralelas, nodos sueltos, pasos sin encargado | Corrige con `ver_flujo`; `forzar=true` solo con OK |
| Tareas nuevas no arrancan el flujo | Flujo no publicado o no enganchado | `publicar_flujo` + `enganchar_flujo_a_concepto`; crea la tarea CON el concepto |
| No deja editar el flujo | Esta publicado | `nueva_version_flujo` y edita el borrador nuevo |

## Formularios

| Mensaje / sintoma | Causa | Salida |
|---|---|---|
| `activate` falla | Estructura incoherente | `verify_form` y corrige cada error |
| Lista sin opciones / opciones vacias | `options_json` mal formado (falta `id`) | `[{"id":"A","label":"Opcion A"}]` |
| Calculo no funciona | Referencia sin llaves o funcion inexistente | Usa `{codigo}`; revisa `describe_components` -> `calc` |
| Campo destino de suma se "pisa" | Tiene `calc_expression` y es destino de rollup | Quita la formula del destino |
| `code` duplicado | Ya existe un formulario con ese codigo | `list_forms`; usa otro codigo o edita el existente |

## Contenedores, conectores e importaciones

| Mensaje / sintoma | Causa | Salida |
|---|---|---|
| "No se puede borrar una columna que ya tiene datos" | Proteccion de datos | Renombra (`editar_columna`) o limpia (`limpiar_marca`) |
| "Ningun campo es numerico: solo se puede contar" | Columnas en texto | `editar_columna` tipo `decimal`/`numero` |
| "El conector no tiene un mapeo campo->columna utilizable" | `fields` vacio o columnas que no existen | Corrige `mapeo_json.fields` con nombres exactos de columnas |
| "Para Upsert indica keyColumn" / "La columna clave ... debe estar mapeada" | Upsert sin clave mapeada | Mapea la clave en `fields` y pon `columna_clave` |
| "Tiempo de espera agotado al llamar al API" | Paginas grandes / demasiadas paginas / consulta lenta | Baja `pageSize` y `maxPages`; consulta mas angosta (ver trampa 9 y 11) |
| "Se alcanzo el limite de 50000 filas por corrida" | Tope por corrida | Partir por ventanas de fecha / varias corridas |
| Corrida `agente_desconectado` | El conector/dron corre via agente colmena y esta apagado | Encender el agente en el PC/servidor del cliente |
| Corrida OK pero 0 filas | Ventana vacia, `arrayPath` equivocado o filtro que no casa | Prueba la URL a mano; revisa `arrayPath` (ruta al arreglo) |
| Filas duplicadas en cada corrida | Modo `append` o dron sin `ingesta_llave` | `upsert` + `columna_clave`, o `ingesta_llave` en el paso extraer |
| `publicar_contenedor` rechazado | Falta seccion padre del menu | Envia `padre` con el nombre de una seccion existente |

## IA (agentes y clasificacion)

| Mensaje / sintoma | Causa | Salida |
|---|---|---|
| `clasificar_lote` falla por agente | Agente apagado | `encender_agente` |
| Muchas filas `sin_respuesta` | El agente no devuelve JSON con la clave, o data sin senal | Endurece el prompt (solo JSON, clave exacta); revisa muestras |
| Para "por cupo" | Se agoto el cupo de IA del plan | Avisar al usuario; continuar cuando haya cupo |
| Pendientes no bajan con el paso automatico | La importacion falla antes (no clasifica) | `ejecutar_clasificacion_ahora` + arreglar la importacion |
| "2 lotes sin avance" | El agente no logra clasificar esas filas | Revisar prompt / filas problematicas |
| El agente no responde en conversaciones | Proveedor no activo en el gateway del plan / agente apagado | Verificar con el administrador; `ver_agente` |

## Reportes

| Mensaje / sintoma | Causa | Salida |
|---|---|---|
| No guarda el reporte y explica un campo | Campo inexistente o no sumable/agrupable | `ver_fuente_reporte` para ver campos y capacidades |
| Orden no funciona | Ordenaste por la columna en vez de la agregacion | `orden=[{"campo":"suma","desc":true}]` |
| Los datos "no cambian" | Esperas filtros interactivos | Los filtros son fijos; crea otra version del reporte |
| Fuente no aparece | La tabla no es raiz o no tienes permiso | `listar_fuentes_reporte` |

## WhatsApp

| Mensaje / sintoma | Causa | Salida |
|---|---|---|
| SARA dejo de responder un chat | Escribiste por MCP en ese chat | `reanudar_agente` cuando termines |
| No ves mensajes de un grupo | Los grupos no quedan en Conversaciones | `leer_mensajes` con `fuente='whatsapp'` |
| Riesgo de bloqueo al escribir a un numero nuevo | Primer contacto | Confirmar con el usuario + `primer_contacto_confirmado=true` |
