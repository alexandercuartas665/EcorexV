# ADR-0035: Asignacion por nodo por dependencias/cargos del organigrama (ola F1)

- Estado: aceptada
- Fecha: 2026-07-07
- Relacionada con: ADR-0017 (dependencias/organigrama 000850), ADR-0014 (workflow
  engine BPMN), ADR-0022/0034 (editor de flujos), ADR-0016 (rules engine).

## Contexto

El motor de flujos (ADR-0014) materializa nodos BPMN (`WorkflowNode`) pero no
define QUIEN atiende cada paso. El legacy (GestionMovil, `PERMISO_CARGO`) asigna los
pasos por CARGO del organigrama, no por usuario directo: cuando un funcionario cambia
de puesto, el flujo no se rompe. El usuario decidio replicar ese modelo: la
asignacion de un nodo apunta a una **Dependencia** o **Cargo** del organigrama
(modulo 000850), y en tiempo de ejecucion se expande al conjunto de usuarios
candidatos. Esta ola (F1) construye SOLO el modelo de asignacion y el resolver que
lo consume; la bandeja y el "atender" son la ola F2.

## Decisiones

### 1. Clasificador semantico en OrgUnit (Dependencia / Cargo / Funcionario)

Se agrega el enum `OrgUnitClassifier { Dependencia, Cargo, Funcionario }` y el campo
`OrgUnit.Classifier` (default `Dependencia` para filas heredadas, via la migracion).
La jerarquia semantica se expresa sobre el `ParentId` ya existente: una Dependencia
contiene Cargos y un Cargo contiene Funcionarios. Se agrega `OrgUnit.TenantUserId`
(nullable, FK NO ACTION conceptual hacia `TenantUser.Id`) que SOLO se usa cuando
`Classifier=Funcionario`: es el usuario del tenant que ocupa ese puesto. El servicio
valida coherencia jerarquica suave (un Cargo cuelga de una Dependencia o raiz; un
Funcionario cuelga de un Cargo y exige `TenantUserId`).

### 2. WorkflowNodePolicy: solo Dependencia o Cargo son asignables

Entidad nueva `WorkflowNodePolicy` (TenantEntity): `WorkflowNodeId` (FK cascade),
`OrgUnitId` (FK NO ACTION a OrgUnit), `SortOrder`. Unico por `(WorkflowNodeId,
OrgUnitId)`. Solo se permiten unidades con `Classifier` Dependencia o Cargo (un
Funcionario NUNCA es asignable a un nodo; el servicio lo rechaza con error tipado
`Invalid`). El vinculo por nodo NO viaja en el XML BPMN, asi que se permite tambien
sobre definiciones publicadas (mismo criterio que `WorkflowNodeForm`/`WorkflowNodeRule`
del ADR-0022). La FK cascade nodo->policy borra las asignaciones al borrar el nodo o
la definicion; hacia la unidad es NO ACTION (una unidad del organigrama nunca se
borra fisicamente: se archiva).

### 3. Resolver nodo -> usuarios (INodeAssigneeResolver + OrgAssigneeTree puro)

`INodeAssigneeResolver.ResolveCandidatesAsync(workflowNodeId)` devuelve los
`TenantUserId` DISTINTOS candidatos a atender el nodo: para cada Dependencia|Cargo de
la policy, la union de (a) los funcionarios descendientes (recursivo por ParentId,
via su `TenantUserId`), (b) los `OrgUnitMember.TenantUserId` de la unidad y sus
descendientes, (c) el `ResponsibleTenantUserId`. La logica de arbol vive en la clase
PURA `OrgAssigneeTree` (recibe la lista plana de unidades + miembros, testeable sin
EF, tolera ciclos en datos). El resolver EF solo carga las proyecciones y delega. Lo
consume la ola F2 (bandeja/atender); en F1 el editor lo usa como feedback ("N
funcionarios atenderan este paso").

### 4. Editor: panel real de asignacion (reemplaza el placeholder)

El acordeon "Asignar usuarios" de `FlowEditor.razor` deja de ser un placeholder: para
un nodo Task con "Permite asignacion manual" muestra las dependencias/cargos
asignadas (con boton quitar), un selector del arbol de OrgUnits filtrado a
Dependencia|Cargo y el conteo de candidatos resueltos. Si el nodo no admite
asignacion, muestra un mensaje. `Dependencias.razor` gana el selector de Classifier
al crear/editar y, para Funcionario, el dropdown del usuario del tenant, mas un
badge/color de clasificador en el arbol.

## Consecuencias

- La asignacion sobrevive a los cambios de personal: se re-liga el funcionario al
  cargo, sin tocar el flujo.
- La migracion `AddNodeAssignment` es dual (PG 5442 + SQL Server 1443), aplicada y
  verificada en ambos motores; puramente aditiva (columna + tabla).
- Deuda / ola F2: la asignacion EFECTIVA del paso (elegir el usuario concreto de
  entre los candidatos), la bandeja y el "atender" no se implementan aqui; consumen
  este resolver. El editor no permite reordenar policies (SortOrder se fija al alta).
