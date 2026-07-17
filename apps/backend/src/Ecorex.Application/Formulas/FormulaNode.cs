namespace Ecorex.Application.Formulas;

/// <summary>
/// Nodo del arbol de una formula ya parseada (ADR-0029). Es un arbol inmutable: parsear una vez y
/// evaluar muchas (por cada fila/tercero/item) sin volver a leer el texto.
/// </summary>
public abstract record FormulaNode;

/// <summary>Literal numerico. Siempre decimal: aqui se calcula dinero.</summary>
public sealed record FormulaNumber(decimal Value) : FormulaNode;

/// <summary>Referencia a otro campo, escrita <c>{clave}</c> en la formula.</summary>
public sealed record FormulaRef(string FieldKey) : FormulaNode;

/// <summary>Menos unario: <c>-{descuento}</c>.</summary>
public sealed record FormulaNegate(FormulaNode Operand) : FormulaNode;

/// <summary>Operacion binaria. <see cref="Op"/> es uno de + - * /.</summary>
public sealed record FormulaBinary(char Op, FormulaNode Left, FormulaNode Right) : FormulaNode;

/// <summary>Llamada a funcion: ROUND, MIN, MAX, ABS o SUM. El nombre va en mayusculas.</summary>
public sealed record FormulaCall(string Name, IReadOnlyList<FormulaNode> Args) : FormulaNode;
