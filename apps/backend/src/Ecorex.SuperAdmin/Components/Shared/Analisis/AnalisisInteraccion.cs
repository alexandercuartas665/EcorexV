using Ecorex.Application.Analisis;

namespace Ecorex.SuperAdmin.Components.Shared.Analisis;

// Tipos de la interaccion por clic de los graficos del modulo Analisis (ADR-0158 Ola 7c).

/// <summary>
/// Elemento tocado en un grafico ECharts: categoria/fila/dia/bloque (<see cref="Eje"/>), serie/columna/
/// sub-bloque (<see cref="Serie"/>), el valor pintado y la posicion del clic dentro del grafico (px).
/// </summary>
public sealed record GraficoClic(string Eje, string? Serie, decimal? Valor, double X, double Y);

/// <summary>Elemento a resaltar en el grafico (los demas se atenuan).</summary>
public sealed record GraficoMarca(string Eje, string? Serie);

/// <summary>Lo que la tarjeta entrega al anfitrion al elegir "Filtrar" o "Ver registros" sobre un elemento.</summary>
public sealed record AnalisisPuntoAccion(AnalisisWidgetDto Widget, IReadOnlyList<CondicionCruzada> Condiciones, string Etiqueta, decimal? Valor);
