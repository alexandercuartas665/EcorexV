---
tipo: receta
modulo: agentes
referencia: "[[T06 - Agentes de IA]]"
---

# R09 - Crear y configurar un agente de IA

Volver a [[00 - LEEME PRIMERO]]. Referencia: [[T06 - Agentes de IA]].

Los agentes son los mismos del modulo de Agentes de la aplicacion: atienden WhatsApp, operan en nodos de flujo,
clasifican filas de contenedores o leen documentos. Por MCP los **creas y configuras**; conectarlos a una linea de
WhatsApp o a un nodo de flujo se hace en la pantalla.

## Paso 0 - Definir el trabajo del agente

Antes de crear, deja claro con el usuario:
1. **Rol** (que es) y **tarea** (que hace con lo que recibe).
2. **Entrada** (mensajes de chat, filas JSON, PDF/imagenes) y **salida exacta** (texto al cliente, JSON con campos
   fijos, filas a escribir).
3. **Proveedor**: claude, gemini, chatgpt o deepseek (debe estar activo en el gateway de IA del plan de la
   empresa; si no, el agente falla al responder). DeepSeek es economico para clasificar volumen.
4. **Herramientas**: que puede y que NO puede hacer.

Mira si ya existe uno parecido: `listar_agentes` (puedes `duplicar_agente` y ajustar).

## Paso 1 - Crear (nace APAGADO)

```json
{"name":"crear_agente","arguments":{
  "nombre":"Clasificador de pedidos","rol":"Clasificador","proveedor":"deepseek",
  "prompt":"Eres un clasificador de pedidos.\nRecibes un arreglo JSON de filas con id_pedido, cliente y descripcion.\nPara CADA fila decide la categoria segun el OBJETO del pedido (no segun el cliente), una de: hardware, software, servicios, otros.\nResponde SOLO un arreglo JSON, sin texto adicional: [{\"id_pedido\":\"...\",\"categoria\":\"...\"}].\nSi no hay informacion suficiente usa 'otros'. No inventes ids."}}
```
- `modelo` opcional (vacio = el por defecto del proveedor).
- `herramientas_deshabilitadas`: lista de herramientas a APAGAR. **Si la omites quedan TODAS habilitadas**,
  incluida `agregar_filas_a_contenedor` (escribir en un contenedor). Para un agente que solo clasifica o solo
  conversa, apaga lo que no necesite.

### Como escribir un buen prompt de sistema
- Rol + tarea en 2 lineas. Reglas numeradas. Que hacer cuando falta informacion.
- **Salida exacta**: si otra pieza del sistema la lee (clasificacion, extraccion), exige **solo JSON** con los
  nombres de campo EXACTOS (la columna clave + las de salida).
- Criterio de decision explicito (ej. "clasifica por el objeto del contrato, no por la entidad").
- Lista cerrada de valores para categorias (evita sinonimos que rompen los reportes).

## Paso 2 - Configurar

```json
{"name":"ver_agente","arguments":{"agente":"Clasificador de pedidos"}}
```
Muestra prompts, recursos y herramientas con su estado por agente.

| Ajuste | Herramienta |
|---|---|
| Como lee PDF/imagenes que recibe | `agente_lector_medios` (`prompt`: que extraer de cada documento) |
| Prompt adicional que se activa por una regla | `agregar_prompt_agente` (`nombre`, `regla`, `cuerpo`) |
| Archivos/datos de apoyo (catalogo PDF, ubicacion, texto) | `agregar_recurso_agente` (`tipo`: imagen, video, audio, pdf, ubicacion, texto; `detalle`, `archivo_url`) |
| Prompt, proveedor, herramientas, tableros permitidos, round-robin de asignados, concepto por defecto | `editar_agente` |

Ejemplo de lector de medios para pliegos/licitaciones:
```json
{"name":"agente_lector_medios","arguments":{"agente":"Inspector de pliegos",
  "prompt":"Del documento extrae: objeto, presupuesto oficial (numero en COP), plazo de ejecucion, fecha de cierre, modalidad, requisitos habilitantes y lugar. Si un dato no aparece escribe null. No resumas el documento completo."}}
```
`editar_agente` cambia solo lo que envies; si envias una **lista** (herramientas, tableros, asignados) reemplaza
la lista completa. Cada cambio de prompt queda versionado: `historial_prompts_agente`, `restaurar_version_prompt`.

## Paso 3 - Validar y encender

- Para clasificadores: enciende y prueba con un lote chico (`clasificar_lote` con `limite: 5`) y revisa el
  resultado antes de escalar ([[R10 - Clasificar datos con IA]]).
- Para agentes conversacionales: el usuario lo prueba en la pantalla del agente antes de conectarlo a una linea.
```json
{"name":"encender_agente","arguments":{"agente":"Clasificador de pedidos"}}
{"name":"apagar_agente","arguments":{"agente":"Clasificador de pedidos"}}
```

## Paso 4 - Mantener

- Copia para experimentar: `duplicar_agente` (la copia nace apagada).
- Borrar: `eliminar_agente`, `eliminar_prompt_agente`, `eliminar_recurso_agente` (confirma antes).
- El consumo de IA se registra por empresa y respeta el cupo del plan.
