# ADR-0085: Gestion por FILA de GridDetail (pildoras que abren subformularios ligados a una fila/persona)

- Estado: Aceptado (base de motor; UI de pildoras en el renderer pendiente)
- Fecha: 2026-09-01
- Contexto: el CRM "Contacto Cliente" (FRM-CONTACTO, SOLDARCO) paso de un Subform pesado por persona a una
  TABLA ligera (GridDetail campo `personas`). Cada persona (fila) debe poder abrir gestiones (Cotizacion,
  Oportunidad, PQR, Soporte, Pedido, Leads) via pildoras, guardando cada gestion LIGADA A ESA FILA/persona.

## Problema
Las filas del GridDetail viven en el JSONB del padre (no son FormResponse), asi que no habia a que colgar un
FormRecordLink. Se necesita IDENTIDAD ESTABLE de fila + un vinculo fila -> registro-de-gestion.

## Decision
- IDENTIDAD DE FILA: cada fila del GridDetail lleva una clave estable `__rowId` (GUID en texto) en su propio
  objeto del JSONB. Sobrevive a reordenar/insertar/borrar filas (viaja con la fila). El renderer la asigna al
  crear la fila y la conserva; no es una columna visible.
- VINCULO: se EXTIENDE FormRecordLink con `ParentRowId` (string?, max 64; null = subform clasico a nivel de
  campo). Una gestion = un FormResponse hijo + FormRecordLink(ParentResponseId=contacto, ParentFieldCode=
  `personas`, ParentRowId=__rowId, ChildResponseId=gestion). Migracion dual AddFormRecordLinkParentRowId.
  Indice (ParentResponseId, ParentFieldCode, ParentRowId) para listar gestiones de una fila.
- SERVICIO (IFormResponseService): AddRowChildAsync(parent, field, rowId, childDef) crea la gestion ligada a
  la fila; ListRowChildrenAsync(parent, field, rowId) lista las gestiones de la fila; CountRowChildrenAsync(
  parent, field) -> rowId -> (childDefId -> conteo) para los badges de las pildoras. Tenant-scoped; el padre
  debe existir (estar guardado) para colgar hijos.

## Config de la columna de pildoras (a cablear en el OptionsJson del GridDetail)
Una columna extra del grid con type "gestion":
    { "id": "gestion", "type": "gestion", "label": "Gestiones",
      "pills": [ { "label": "Cotizacion", "def": "FRM-CRM-COT", "color": "#0d9488" },
                 { "label": "Oportunidad", "def": "FRM-CRM-OPP", "color": "#4f46e5" }, ... ] }
`def` referencia la def-detalle por CODIGO (se resuelve a definitionId). Colores/labels los pone diseno.

## Pendiente (UI, siguiente entrega)
Renderer (DynamicFormRenderer): celda de la columna "gestion" que pinta una pildora por tipo (con badge de
conteo), y al hacer clic ASIGNA __rowId si falta + guarda el padre + AddRowChildAsync + abre el subformulario
de la gestion en modal (renderer anidado). Aplica igual en el visor publico /f/{token} (usa el MISMO
DynamicFormRenderer); el alta de hijo corre server-side con el tenant del token.
