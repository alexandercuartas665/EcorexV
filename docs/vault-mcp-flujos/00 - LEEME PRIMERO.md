---
tipo: indice
tema: flujos
audiencia: sesion de IA conectada al MCP de tableros de ECOREX
---

# Cerebro de FLUJOS de ECOREX - LEEME PRIMERO

Este cerebro ensena a una IA a **disenar, configurar y publicar procesos (flujos BPMN)** en ECOREX por el MCP de
tableros, de forma que queden FUNCIONANDO: quien atiende cada paso, como se decide en una compuerta, que avisos
salen, que formulario se exige, en que tablero cae la actividad y cuanto tiempo tiene cada paso.

Lo lees con `guia_ecorex` y el tema `flujos`. Cada `[[Titulo]]` es otra nota de este cerebro:

```json
{"name": "guia_ecorex", "arguments": {"tema": "flujos", "nota": "F02 - Las dos formas de decidir"}}
{"name": "guia_ecorex", "arguments": {"tema": "flujos", "buscar": "SIN NOMBRE"}}
```

La guia GENERAL (sin `tema`) cubre todo lo demas: tareas, contactos, contenedores, reportes, drones, agentes.

## Orden de lectura

1. [[F01 - Modelo mental del motor de flujos]] - que es un nodo, una salida, una compuerta y una version. **Obligatoria.**
2. [[F02 - Las dos formas de decidir]] - compuerta automatica vs decision humana. Es donde mas se equivoca una IA.
3. La receta que corresponda (tabla de abajo).
4. [[F10 - Checklist antes de publicar]] - siempre, antes de `publicar_flujo`.

## Mapa rapido: "quiero..." -> receta

| Quiero... | Receta |
|---|---|
| Aprobacion simple (jefe aprueba o rechaza) | [[F03 - Receta aprobacion de un nivel]] |
| Dos niveles de aprobacion y devolver para corregir | [[F04 - Receta aprobacion en dos niveles con devolucion]] |
| Que el CLIENTE decida desde un enlace (firma u observacion) | [[F05 - Receta decision del cliente por enlace]] |
| Plazos, arrancar despues, mover la actividad de tablero y cerrarla con concepto | [[F06 - Receta tiempos, tableros y cierre]] |
| Encadenar con otro proceso, reglas en un paso, avisos internos | [[F07 - Receta encadenar procesos, reglas y avisos]] |
| Cambiar un flujo que ya esta publicado | [[F08 - Modificar un flujo publicado]] |
| Saber que herramienta usar y con que parametros | [[F11 - Referencia de herramientas de flujos]] |
| Evitar errores ya vividos | [[F09 - Trampas conocidas de flujos]] |

## Protocolo (siempre en este orden)

1. **Entiende el proceso** con el usuario: pasos, quien hace cada uno, donde hay decisiones y QUIEN decide
   (una persona del equipo, el cliente por enlace, o una regla automatica). Si no esta claro quien decide, pregunta.
2. **Dibuja primero** (en texto) el grafo: `inicio -> paso -> compuerta -> salidas -> fin`. Ponle nombre a cada
   salida de compuerta (ej. Aprobada / Rechazada).
3. **Descubre** lo que existe: `listar_flujos`, `listar_conceptos`, `listar_tableros`, `list_forms`,
   `listar_reglas_disponibles`. Nunca inventes ids.
4. **Arma el grafo** en borrador (`crear_flujo`, `agregar_nodo`, `conectar_nodos`).
5. **Configura cada nodo** (encargado, decision, salidas, formularios, avisos, tiempos, tablero).
6. **Revisa** con `ver_flujo`: no debe tener `avisos`. Recorre [[F10 - Checklist antes de publicar]].
7. **Publica** y **engancha** a un concepto. Explica al usuario como se ve en pantalla y que falta (si algo).

## Lo que este cerebro NO cubre (aun)

- El **agente de IA de un nodo** (que la IA atienda el paso, llame o escriba por WhatsApp): se configura en la
  pantalla del flujo (panel del nodo -> Agente). Llegara al MCP en una ola posterior.
- **Crear reglas** nuevas: hoy solo se VINCULAN reglas que ya existen (`agregar_regla_nodo`). Las reglas se crean
  en el modulo Reglas o desde el cerebro de formularios cuando este disponible.
