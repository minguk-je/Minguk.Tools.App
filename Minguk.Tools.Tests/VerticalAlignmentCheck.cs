using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Minguk.Tools.Tests;

/// <summary>
/// 한 줄 안의 글자와 칸이 세로 가운데로 맞는지 잰다(사용자 규칙 2026-09-14 - 화면의 컨트롤은 모두 세로 가운데 정렬).
/// </summary>
/// <remarks>
/// 도구 줄의 편집 항목(BarEditItem)은 앞 글자("폴더"·"확대")와 칸이 한 링크 컨트롤 안에 있고, 폼 칸(LayoutItem)은 라벨과 칸이
/// 한 항목 안에 있다. 둘 다 "글자 가운데 - 칸 가운데" 를 재어 1.5px 넘게 어긋난 것을 적는다. 눈으로는 1~3px 가 안 보이다가
/// 줄 전체를 보면 글자만 떠 보인다.
/// </remarks>
internal static class VerticalAlignmentCheck
{
    public static int Report(FrameworkElement root, string screen)
    {
        var misaligned = new List<string>();
        var checkedCount = 0;

        foreach (var container in Descendants<FrameworkElement>(root).Where(IsPair))
        {
            if (!container.IsVisible || container.ActualHeight <= 0) continue;

            var editor = Descendants<Control>(container).FirstOrDefault(c => c is DevExpress.Xpf.Editors.BaseEdit && c.IsVisible && c.ActualHeight > 0);
            if (editor is null) continue;

            // 칸 밖에 있는 글자 - 라벨.
            var label = Descendants<TextBlock>(container)
                .FirstOrDefault(t => t.IsVisible && t.ActualHeight > 0 && !string.IsNullOrWhiteSpace(t.Text) && !IsInside(t, editor));
            if (label is null) continue;

            checkedCount++;

            var labelCenter = TextCenter(label, root);
            var editorCenter = Center(editor, root);
            var offset = labelCenter - editorCenter;

            if (Math.Abs(offset) > 1.5)
                misaligned.Add($"'{label.Text}' {offset:+0.0;-0.0}px ({container.GetType().Name})");
        }

        if (misaligned.Count > 0)
        {
            Console.WriteLine($"[FAIL] {screen}: 세로 가운데가 안 맞는 글자·칸 {misaligned.Count}/{checkedCount}쌍 - {string.Join(" · ", misaligned.Take(12))}");
            return 1;
        }

        Console.WriteLine($"[PASS] {screen}: 글자·칸 {checkedCount}쌍이 세로 가운데로 맞는다");
        return 0;
    }

    /// <summary>표 칸(그리드)을 품은 폼 칸은 뺀다 - 머리글 글자를 라벨로 잘못 잰다.</summary>
    private static bool IsPair(FrameworkElement element)
        => (element is DevExpress.Xpf.LayoutControl.LayoutItem && !Descendants<DevExpress.Xpf.Grid.DataControlBase>(element).Any())
           || element.GetType().Name is "BarEditItemLinkControl" or "LightweightBarItemLinkControl";

    /// <summary>
    /// 글자가 실제로 그려지는 줄의 가운데. TextBlock 이 칸 높이로 늘어나면 글자는 상자 맨 위에 그려져 상자 가운데로 재면 안 보인다(실측: 도구 줄 22px 상자에 15px 글자).
    /// </summary>
    private static double TextCenter(TextBlock text, FrameworkElement root)
    {
        var line = new FormattedText(text.Text, System.Globalization.CultureInfo.CurrentUICulture, text.FlowDirection,
            new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize, Brushes.Black, 1.0).Height;

        // TextBlock 은 남는 높이를 아래로 둔다 - 글자는 상자 위(안쪽 여백 뒤)에 붙는다.
        return text.TransformToAncestor(root).Transform(new Point(0, text.Padding.Top + line / 2)).Y;
    }

    private static double Center(FrameworkElement element, FrameworkElement root)
        => element.TransformToAncestor(root).Transform(new Point(0, element.ActualHeight / 2)).Y;

    private static bool IsInside(DependencyObject child, DependencyObject ancestor)
    {
        for (var current = child; current is not null; current = VisualTreeHelper.GetParent(current))
            if (ReferenceEquals(current, ancestor)) return true;

        return false;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var inner in Descendants<T>(child)) yield return inner;
        }
    }
}
