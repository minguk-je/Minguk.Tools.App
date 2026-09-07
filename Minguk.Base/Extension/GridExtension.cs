using System.Windows;
using System.Windows.Controls;

namespace Minguk.Base.Extension;

public static class GridExtensions
{
    public static UIElement? GetControlByIndex(this Grid grid, int row, int column)
    {
        return grid.Children.Cast<UIElement>().FirstOrDefault(e => Grid.GetRow(e) == row && Grid.GetColumn(e) == column);
    }
}