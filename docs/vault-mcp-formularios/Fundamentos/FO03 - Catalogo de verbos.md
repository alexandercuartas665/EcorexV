---
tipo: fundamento
tema: formularios
---

# FO03 - Catalogo de verbos

Volver a [[00 - LEEME PRIMERO]]. Base: [[FO02 - Reglas que son y cuando corren]].

Resumen de los verbos registrados. El catalogo REAL y sus parametros exactos salen siempre de `listar_verbos_regla`
(puede haber verbos nuevos). `*` = obligatorio.

| Verbo | Que hace | Parametros clave | Momento tipico |
|---|---|---|---|
| `CREAR_TAREA` | Crea UNA tarea clasificada por concepto con datos del formulario | `subcategoriaId`* (concepto), `titleTemplate` (`{campo}`), `descriptionTemplate`, `assigneeUserId`, `requesterNameField`/`EmailField`/`PhoneField`, `marcaCreadaField` (anti-duplicado) | Al enviar |
| `GENERAR_TAREAS_DESDE_TABLA` | Una tarea por fila de una grilla (o filas fijas) | `activityTypeId`*, `tableField`, `titleKey`, `titlePrefix`, `assigneeUserId` | Al enviar |
| `PASAR_CAMPOS` | Copia campos a otros campos | `mappings`* = `[{"source":"a","target":"b"}]` | Al cambiar un campo / boton |
| `BLOQUEAR_CAMPO_XCONDICION` | Oculta/muestra/exige un campo segun otro | (usa `condicion_entre_campos`) | Al cambiar un campo |
| `ASIGNAR_CONSECUTIVO` | Pone el siguiente numero de un consecutivo en un campo | `sequenceCode`*, `prefix`, `padding`, `targetField` | Al enviar / boton |
| `IMPRIMIR_PLANTILLA` | Genera el documento de una plantilla de impresion | `template`* (nombre exacto), `format` (print/pdf/img) | Boton (atajo: `wire_print_button`) |
| `CONVERTIR_A_FORMULARIO` | Crea un registro de OTRO formulario copiando datos y lo abre | `targetCode`*, `mapping`, `gridMapping`, `defaults`, `openMode` | Boton (atajo: `wire_convert_button`) |
| `CARGAR_FILAS_DESDE_GRILLA` | Copia filas de una grilla a otra | `sourceGrid`*, `targetGrid`*, `columnMap`* | Boton |
| `COMPLETAR_PASO_FLUJO` | Cierra el paso actual del flujo de la tarea | `requireFields` (campos que deben tener valor), `requireMessage`, `targetNodeBpmnId` (salida si el paso es compuerta), `onGatewayNoTarget` | Boton |
| `REINICIAR_FLUJO_TAREA` | Reinicia el flujo de la tarea y lo reasigna a quien diga un campo | `assigneeFieldCode`*, `restartNodeBpmnId`, `onEmptyField` | Boton |
| `NOTIFICAR` | **Solo REGISTRA** la intencion de aviso en la actividad de la tarea (no envia correo) | `message`*, `recipient` | Paso de flujo |
| `EJECUTAR_DRON` | Lanza un dron de extraccion con datos de la tarea | `flowId`*, `gridField`, `requireFields` | Boton |
| `GENERAR_REQUERIMIENTOS_DESDE_TABLA` | Necesidades de compra por fila (modulo Compras) | `tableField`* ... | Al enviar |
| `REGISTRAR_RESPUESTA_RFQ`, `DIRECCIONAR_CONCILIADOR`, `DEVOLVER_CONCILIADOR` | Propios de los modulos de Compras y Conciliacion | ver `listar_verbos_regla` | Los configura su modulo |

## Elegir bien

- **Avisar de verdad** a alguien (correo/WhatsApp): NO con `NOTIFICAR`; usa los avisos del nodo del flujo
  (cerebro de flujos, `agregar_notificacion_nodo`).
- **Crear una tarea al enviar**: `CREAR_TAREA` (una, por concepto: hereda el tablero y el flujo del concepto) o
  `GENERAR_TAREAS_DESDE_TABLA` (varias, por tipo de actividad). Ver [[FO04 - Receta formulario que crea una tarea al enviar]].
- **Mostrar/ocultar**: para casos simples basta `visible_when_json` del campo (estructura); para EXIGIR un campo
  segun otro usa `condicion_entre_campos`.
