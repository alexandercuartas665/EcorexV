# ADR-0066 - El perfil del tercero deja de ser un chip y pasa a ser un DIRECTORIO

- Estado: ACEPTADA (2026-08-07). Implementado en `fase-0/clon-backbone`.
- Fecha: 2026-08-07
- Relacionado: modulo 000232 (Directorio General), 000740 (Cargador de contactos),
  [ADR-0029 campos configurables del tercero], [ADR-0033 sub-permisos nombrados].

## Contexto

El editor de tercero (`TerceroModal`, compartido por 000232 y 000740) clasificaba al tercero con
una columna de chips multi-seleccion ("Cliente", "Cliente sospechoso", "Proveedor", "Empleado").
Cada chip encendido abria su ficha de datos, y una misma pantalla podia terminar mostrando cinco
fichas apiladas. Consecuencias:

- El usuario tenia que saber de antemano que combinacion de chips corresponde a que datos.
- Un chip encendido por error abria una ficha vacia; uno apagado por error ESCONDIA datos ya
  capturados (y el guardado, que solo serializaba las fichas visibles, los borraba).
- La fila TIPO del listado (Todos / Clientes / Proveedores / Empleados) y los chips del modal eran
  dos representaciones del mismo concepto, mantenidas por separado y ya desalineadas.

## Decision

**El perfil se deduce del DIRECTORIO en el que esta parado el usuario, no de un chip.**

1. La fila TIPO del Directorio General es la lista de directorios:
   **Publico | Fiscal | Comercial | Cartera | Proveedores | Laboral**.
2. Cada directorio filtra el listado por su perfil (`TerceroPerfil`) y, en el editor, abre
   EXACTAMENTE dos bloques: la ficha **Publico** (datos basicos, que son columnas de la tabla) y la
   ficha de ese directorio. Ninguna se puede colapsar.
3. `Publico` no tiene bit propio: es la vista completa (sin filtro) y solo edita los datos basicos.
4. Al guardar, el modal marca el perfil del directorio activo (`|=`), sin tocar los demas: un
   tercero puede pertenecer a varios directorios y se edita desde cada uno por separado.
5. El guardado serializa TODAS las fichas cargadas, no solo la visible. Es la condicion que hace
   segura la regla 2: si no, editar desde Cartera borraria la ficha fiscal.

### Cambios en `TerceroPerfil`

- `Cartera = 64` (nuevo). Directorio nuevo con ficha propia, sin campos por defecto: el tenant los
  configura desde "Configurar campos".
- `Sospechoso = 2` **se retira**. El embudo del Cargador (000740) ya modela esa etapa con la columna
  de la bolsa (`BolsaColumna` "Sospechoso"), asi que el perfil era una segunda fuente de verdad.
  `PromoverProspectoAsync` pasa a crear el tercero sin perfil (queda en Publico). El bit 2 no se
  reutiliza.
- `Cliente = 1` **se conserva** pero deja de ser directorio: lo sigue usando el Cargador al convertir
  un prospecto y el KPI "Clientes". Su ficha queda en el catalogo, marcada "(legado)", para no perder
  los datos ya capturados.

## Consecuencias

- Un tercero creado desde un directorio queda listado ahi automaticamente; ya no hay forma de
  guardarlo "sin clasificar" salvo desde Publico, que es explicito.
- Registros anteriores marcados solo como `Cliente` no aparecen en ningun directorio distinto de
  Publico hasta que se abran desde uno. El listado los sigue mostrando con un tag apagado "Cliente".
- Registros con el bit 2 (Sospechoso) quedan con un bit que ya nadie interpreta. No se borra por
  migracion: es invisible y reutilizarlo esta prohibido por el comentario del enum.
- El sub-permiso `directorio-general:crear-sospechoso` desaparece del catalogo de la matriz de roles;
  las filas `RolPermiso` que lo referencien quedan huerfanas y sin efecto.
  `directorio-general:crear-cliente` pasa a acotar la creacion en el directorio **Cartera**.
- `TerceroTabTipo` cambio de valores (`Todos/Clientes/Proveedores/Empleados` ->
  `Publico/Fiscal/Comercial/Cartera/Proveedores/Laboral`). Es un enum de UI, no se persiste.

## Anexo (2026-08-07) - El formulario Publico captura empresa Y contacto

Segunda vuelta sobre la misma decision, con el mockup del usuario:

- La ficha Publico deja de pedir "tipo de contacto" (Empresa/Contacto) e "identificar por"
  (NIT/CC/...). El tipo se DEDUCE de lo que se llene y el documento pasa a ser un identificador
  libre **IDE** de hasta 20 caracteres (`TerceroIdTipo.Ide`, valor nuevo del enum: se muestra sin
  prefijo, a diferencia de NIT/CC).
- El formulario tiene dos bloques, EMPRESA (nombre, telefono) y CONTACTO (nombre, cargo, telefono,
  correo). Al crear se da de alta lo que este lleno: solo empresa, solo contacto, o **los dos y su
  relacion**, via `ITerceroService.CreateEmpresaConContactoAsync`, que inserta ambos en un unico
  `SaveChanges` (el Id es Guid v7 generado en la app, asi que la relacion se arma antes de insertar).
- Regla de captura: debe quedar **al menos un telefono**, el de la empresa o el del contacto. Se
  valida al crear; al editar no, porque el modal solo muestra el telefono del registro abierto.
- Al editar solo se toca el registro abierto: si es una empresa, el bloque contacto queda de consulta
  (sus contactos se gestionan en la pestana Relaciones); si es una persona, el bloque empresa queda
  de consulta.
- **Criterio de busqueda** dentro del modal (`FindIdByCriterioAsync`): busca por IDE, telefono o
  correo y CARGA el tercero encontrado en el modal, que pasa a modo edicion. Sirve para no duplicar.
  Prioriza el IDE; con telefono/correo prefiere la persona sobre la empresa.
- Campos del mockup que NO existen en el modelo (codigo, clase, pais) se dibujan deshabilitados y no
  se guardan, siguiendo la regla del usuario para campos nuevos.
- Se conservan Sector, Vendedor asignado y Estado aunque el mockup no los dibuje: este modal es el
  unico lugar donde se editan y el listado los usa para filtrar y mostrar.

## Alternativas descartadas

- **Renombrar "Cliente" a "Cartera"**: heredaba los datos de la ficha de cliente, pero mezclaba dos
  conceptos (el cliente del embudo comercial vs. las condiciones de credito y cobro) en un mismo bit.
- **Dejar los chips y ademas los directorios**: mantiene las dos representaciones desalineadas, que
  es justo el problema que se queria cerrar.
