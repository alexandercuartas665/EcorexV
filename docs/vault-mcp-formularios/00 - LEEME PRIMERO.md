---
tipo: indice
tema: formularios
audiencia: sesion de IA conectada al MCP de tableros de ECOREX
---

# Cerebro de FORMULARIOS de ECOREX - LEEME PRIMERO

Este cerebro ensena a una IA a dejar un formulario FUNCIONANDO en ECOREX por el MCP de tableros: no solo su
estructura (secciones y campos), tambien su COMPORTAMIENTO: reglas que corren al enviar, al cambiar un campo o al
pulsar un boton; campos condicionales; registros numerados que se cierran solos; protecciones por rol; versiones.

Lo lees con `guia_ecorex` y el tema `formularios`. Cada `[[Titulo]]` es otra nota de este cerebro:

```json
{"name": "guia_ecorex", "arguments": {"tema": "formularios", "nota": "FO02 - Reglas que son y cuando corren"}}
{"name": "guia_ecorex", "arguments": {"tema": "formularios", "buscar": "CREAR_TAREA"}}
```

La ESTRUCTURA (crear el formulario, secciones, campos, listas, calculos, grillas, plantillas de impresion) esta en
la guia GENERAL: receta "R04 - Crear un formulario completo" y referencia "T09 - Formularios dinamicos"
(`guia_ecorex` sin `tema`). Las herramientas de estructura tienen nombres en ingles (`apply_form_spec`,
`describe_components`...); las de este cerebro, en espanol.

## Orden de lectura

1. [[FO01 - Modelo mental de los formularios]] - definicion, secciones, campos, registro, respuesta.
2. [[FO02 - Reglas que son y cuando corren]] - la pieza que da comportamiento. **Obligatoria antes de crear reglas.**
3. [[FO03 - Catalogo de verbos]] - que puede hacer una regla (y que NO).
4. La receta que corresponda (tabla de abajo).
5. [[FO10 - Trampas conocidas de formularios]].

## Mapa rapido: "quiero..." -> receta

| Quiero... | Receta |
|---|---|
| Que al enviar se cree una tarea (lead, solicitud, PQR) | [[FO04 - Receta formulario que crea una tarea al enviar]] |
| Mostrar, ocultar o exigir campos segun lo que se responde | [[FO05 - Receta campos condicionales y obligatorios]] |
| Un boton que haga algo (imprimir, cerrar el paso, copiar datos, convertir) | [[FO06 - Receta botones que ejecutan acciones]] |
| Registros numerados que se cierran al firmar | [[FO07 - Receta registro transaccional con cierre por firma]] |
| Que ciertos roles no vean o no editen un campo | [[FO08 - Receta proteger campos por rol]] |
| Cambiar el aspecto de un campo (color, tamano, borde) o dejarlo no editable | [[FO12 - Receta diseno visual de un campo]] |
| Cambiar un formulario en uso sin romperlo, o renombrar un campo | [[FO09 - Cambios seguros y versiones]] |
| Ver todas las herramientas y parametros | [[FO11 - Referencia de herramientas de reglas y opciones]] |

## Protocolo

1. **Entiende** que debe pasar y CUANDO (al enviar, al cambiar un campo, al pulsar un boton, en un paso del flujo).
2. **Descubre**: `get_form` (campos y codigos), `reglas_de_formulario` (que reglas ya tiene),
   `listar_verbos_regla` (que verbos hay y sus parametros). Nunca inventes codigos de campo ni nombres de parametro.
3. **Guarda una version** (`guardar_version_formulario`) antes de cambios grandes.
4. **Construye** la estructura (guia general R04) y luego el comportamiento (este cerebro).
5. **Verifica**: `verify_form`, `reglas_de_formulario`, y si es posible un registro de prueba (`create_record`) y
   `historial_regla` para ver que la regla corrio. Avisa al usuario que los registros de prueba son reales.
6. **Reporta** que quedo, como se prueba en pantalla y que falta.
