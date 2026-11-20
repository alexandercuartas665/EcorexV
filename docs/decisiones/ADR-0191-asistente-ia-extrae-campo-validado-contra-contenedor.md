# ADR-0191: el asistente de IA del conciliador extrae un campo del documento y el sistema lo valida contra un contenedor

**Status:** Accepted
**Date:** 2026-11-20
**Deciders:** Alexander Cuartas (producto), Claude Opus 5.5 (implementacion, sesion ECOREX.agentes)

## Contexto

El asistente de IA del conciliador DIAN ("Analizar con IA") solo decidia la ASIGNACION: por cada factura, que regla
por proveedor se cumple (y con ella el usuario y la salida de tarea). El usuario necesita que el mismo asistente
LLENE CAMPOS, empezando por la orden de compra: muchas veces la OC no viene en cac:OrderReference del XML sino escrita
en las observaciones, y mal (el proveedor omite el prefijo y deja solo el numero: "OC 12387" en vez de OMP0012387).
El lector determinista (v0.16.358) ya rescata "OMP..." bien escrito en las notas; lo mal escrito necesita criterio.

Hallazgo: la IA solo veia los campos de la parrilla, NO el texto del documento (las notas viven en el XML guardado).

## Decision

1. **Una sola consulta.** A cada regla del asistente se le agrega un bloque OPCIONAL "Extraer campo"
   (`AsistenteExtraccionDto` dentro de `AsistenteProveedorReglaDto.Extraer`): campo destino (oc | numsol | dyn:{key}
   de texto), instruccion de extraccion, contenedor + columna de busqueda + columna a devolver, y prefijos validos.
   Proveedor "*" = regla para todas las facturas (la del proveedor especifico gana).
2. **La IA recibe el texto del documento**: al analizar se lee el XML guardado por CUFE (DIAN, si no NEWTON) y
   `DianUblParser.TextoParaIa` arma notas + OrderReference/AdditionalDocumentReference/DespatchDocumentReference +
   descripcion de items (max 1500 caracteres). Responde `[{f, r, v:[candidatos]}]`.
3. **La IA propone, el sistema valida** (`ExtraccionCampoMatcher`, puro y testeable; el servicio lo arma leyendo una
   vez el contenedor con contexto efimero): match EXACTO (solo letras/numeros, mayusculas) y, si no, POR NUMERO
   (digitos sin ceros a la izquierda, minimo 4) dentro de los prefijos validos, prefiriendo el prefijo del candidato
   si existe. Los prefijos tambien acotan el exacto. Nunca se propone un valor que no exista en el contenedor.
4. **Asignacion y extraccion son independientes**: se extrae aunque la condicion no se cumpla o la factura ya tenga
   tarea, siempre que el campo destino este vacio.
5. **Nada se escribe sin confirmar**: modal de propuestas con columna "Campo extraido" (validada con check / varias
   con desplegable para elegir / no existe / no encontrado, y lo que la IA leyo). "Aplicar seleccion" escribe primero
   los campos (solo si siguen vacios y respetando "no reusar") y luego crea las tareas, para que el formulario de la
   tarea salga con el dato.

Sin migracion: la configuracion vive en TenantConfiguration (`conciliacion-dian.asistente-reglas`, JSON).

## Opciones consideradas

- **A. Que la IA escriba directo el valor que lee.** Rechazada: puede inventar o devolver un numero sin prefijo que
  no existe; no hay garantia de que la OC sea real.
- **B. Dos consultas (asignacion y extraccion por separado).** Rechazada por el usuario: doble costo y latencia.
- **C (elegida). Una consulta + validacion determinista contra el contenedor + confirmacion humana.**

## Consecuencias

- Mas facil: llenar OC (u otro campo) desde observaciones mal escritas, con la base de datos como juez.
- Costo: se leen los XML de las facturas con extraccion (I/O) y el prompt crece; lotes de 10 cuando hay extraccion.
- Una sola extraccion por factura (la regla especifica gana a "*"). Varios campos por factura: futuro si se pide.
- Validado E2E en local (SOLDARCO, MECANIZADOS, 6 facturas): con prefijo OMP las OSE quedan "no existe"; con
  "OMP, OSE" 5/6 validadas (descarta PRD0089626 que no esta en el contenedor); aplicar escribio solo la marcada.
