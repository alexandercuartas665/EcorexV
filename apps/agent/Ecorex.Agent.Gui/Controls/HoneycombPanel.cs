using System;
using Panel = System.Windows.Controls.Panel;
using Size = System.Windows.Size;
using Rect = System.Windows.Rect;
using UIElement = System.Windows.UIElement;

namespace Ecorex.Agent.Gui.Controls;

/// <summary>
/// Panel de teselado en panal para hexagonos pointy-top. Coloca los hijos en filas; las filas impares
/// se desplazan media celda para interlocar (efecto colmena). El numero de columnas se ajusta al total
/// de celdas para mantener un racimo compacto y centrado que crece/decrece con los workers efimeros.
/// </summary>
public sealed class HoneycombPanel : Panel
{
    // Geometria del hexagono (coincide con HexTile: 92x106) + separaciones sutiles.
    private const double HexW = 92;
    private const double HexH = 106;
    private const double GapX = 8;
    private const double CellW = HexW + GapX;          // paso horizontal entre centros de columna
    private const double RowStep = HexH * 0.74;        // filas interlocadas (< HexH => encajan)
    private const double OddOffset = CellW / 2;        // desplazamiento de filas impares

    protected override Size MeasureOverride(Size availableSize)
    {
        int count = InternalChildren.Count;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(HexW, HexH));
        }
        if (count == 0)
        {
            return new Size(0, 0);
        }

        int cols = ResolveColumns(count, availableSize.Width);
        int rows = (int)Math.Ceiling(count / (double)cols);

        double width = cols * CellW + OddOffset;
        double height = (rows - 1) * RowStep + HexH;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int count = InternalChildren.Count;
        if (count == 0)
        {
            return finalSize;
        }

        int cols = ResolveColumns(count, finalSize.Width);
        int rows = (int)Math.Ceiling(count / (double)cols);

        double usedWidth = cols * CellW + OddOffset;
        double usedHeight = (rows - 1) * RowStep + HexH;
        double padX = Math.Max(0, (finalSize.Width - usedWidth) / 2);
        double padY = Math.Max(0, (finalSize.Height - usedHeight) / 2);

        for (int i = 0; i < count; i++)
        {
            int row = i / cols;
            int col = i % cols;
            double x = padX + col * CellW + (row % 2 == 1 ? OddOffset : 0);
            double y = padY + row * RowStep;
            // Centra el hexagono (HexW) dentro de su slot (CellW).
            InternalChildren[i].Arrange(new Rect(x + (CellW - HexW) / 2, y, HexW, HexH));
        }
        return finalSize;
    }

    /// <summary>
    /// Elige columnas para un racimo redondeado (~raiz cuadrada del total), acotado por el ancho.
    /// </summary>
    private static int ResolveColumns(int count, double availableWidth)
    {
        int maxByWidth = 3;
        if (!double.IsInfinity(availableWidth) && availableWidth > 0)
        {
            maxByWidth = Math.Max(1, (int)Math.Floor((availableWidth - OddOffset) / CellW));
        }
        int aesthetic = (int)Math.Ceiling(Math.Sqrt(count * 1.3));
        return Math.Max(1, Math.Min(aesthetic, maxByWidth));
    }
}
