using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Minguk.Tools.Markup.Regions;

/// <summary>
/// 고른 칸의 마스크 꼭짓점 손잡이 - 칸 어도너(<see cref="RegionCellAdorner"/>) 위 층. 꼭짓점을 끌어 옮기고, 변 가운데 점을 끌면 꼭짓점이 늘고,
/// 꼭짓점을 오른쪽 버튼으로 누르면 빠진다(셋은 남는다).
/// </summary>
/// <remarks>
/// 사용자(2026-09-23) "Adorner 안에 폴리곤 식의 구역을 자유롭게 지정해서 이부분만 매칭". 새 점을 찍어 그리는 모드를 두지 않고 처음 모양(팔각형)을 고쳐 가게 했다 -
/// 미리보기의 빈 곳 누르기는 이미 "새 영역" 이라 그리기 모드를 두면 같은 누름이 두 뜻이 된다. 손잡이 끌기는 VS·Figma 의 다각형 편집과 같다.
///
/// 칸 어도너 안이라 칸의 회전을 같이 탄다 - 좌표는 칸 픽셀(돌리기 전)이고 0~1 에 칸 크기를 곱하면 된다. 크기 조절 손잡이보다 위에 놓여(나중에 넣은 시각 자식)
/// 꼭짓점이 모서리 가까이 있어도 꼭짓점이 먼저 잡힌다.
///
/// <b>끄는 동안에는 손잡이를 새로 만들지 않는다</b> - 변 가운데를 끌면 꼭짓점이 하나 늘어 손잡이 수가 바뀌는데, 그때 잡고 있던 손잡이를 트리에서 빼면
/// 마우스 잡기가 풀려 끌기가 끊긴다. 끄는 동안은 잡은 것만 보이고(다른 것은 숨김) 그것만 꼭짓점을 따라가며, 놓은 뒤에 다시 만든다.
/// </remarks>
public sealed class RegionMaskEditor : Canvas
{
    private readonly RegionCellItem _item;
    private readonly List<(Thumb Thumb, int Index, bool IsVertex)> _handles = [];
    private Thumb? _active;
    private int _activeIndex = -1;

    public RegionMaskEditor(RegionCellItem item)
    {
        _item = item;
        DataContext = item;

        // 칸 항목의 이벤트를 붙어 있는 동안만 받는다 - 어도너가 떼이면(칸 고름을 풀면) 끊는다.
        Loaded += (_, _) => { _item.DisplayMaskChanged -= OnMaskChanged; _item.DisplayMaskChanged += OnMaskChanged; Rebuild(); };
        Unloaded += (_, _) => _item.DisplayMaskChanged -= OnMaskChanged;

        Rebuild();
    }

    /// <summary>놓인 꼭짓점 손잡이 수. 하네스가 본다.</summary>
    public int VertexHandleCount => _handles.FindAll(h => h.IsVertex).Count;

    /// <summary>놓인 변 가운데 손잡이 수. 하네스가 본다.</summary>
    public int MidpointHandleCount => _handles.FindAll(h => !h.IsVertex).Count;

    private void OnMaskChanged(object? sender, EventArgs e)
    {
        if (_active is null) Rebuild();
        else InvalidateArrange();
    }

    private void Rebuild()
    {
        Children.Clear();
        _handles.Clear();

        var count = _item.DisplayMask.Count;

        if (count < 3) return;

        // 변 가운데 먼저 - 꼭짓점이 위에 그려지고 먼저 잡힌다.
        for (var i = 0; i < count; i++)
        {
            var index = i;
            var mid = new RegionMaskMidThumb { ToolTip = "끌면 꼭짓점이 하나 는다" };

            mid.DragStarted += (_, _) =>
            {
                if (_item.Owner is not { } canvas) return;

                canvas.BeginMaskDrag(_item);
                Start(mid, canvas.InsertMaskVertex(_item, index));
            };
            Hook(mid);
            Add(mid, index, isVertex: false);
        }

        for (var i = 0; i < count; i++)
        {
            var index = i;
            var vertex = new RegionMaskVertexThumb { ToolTip = "끌어 옮긴다 · 오른쪽 버튼으로 뺀다(셋은 남는다)" };

            vertex.DragStarted += (_, _) =>
            {
                if (_item.Owner is not { } canvas) return;

                canvas.BeginMaskDrag(_item);
                Start(vertex, index);
            };
            vertex.MouseRightButtonDown += (_, e) => e.Handled = true;
            vertex.MouseRightButtonUp += (_, e) =>
            {
                e.Handled = true;
                _item.Owner?.RemoveMaskVertex(_item, index);
            };
            Hook(vertex);
            Add(vertex, index, isVertex: true);
        }

        InvalidateArrange();
    }

    /// <summary>끌기·놓기는 두 손잡이가 같다 - 잡은 꼭짓점을 마우스로, 놓으면 저장하고 손잡이를 다시 만든다.</summary>
    private void Hook(Thumb thumb)
    {
        thumb.DragDelta += (_, e) =>
        {
            if (ReferenceEquals(_active, thumb) && _activeIndex >= 0) _item.Owner?.DragMaskVertex(_item, _activeIndex);
            e.Handled = true;
        };
        thumb.DragCompleted += (_, _) =>
        {
            _active = null;
            _activeIndex = -1;
            _item.Owner?.EndDrag(_item);
            Rebuild();
        };
    }

    private void Start(Thumb thumb, int index)
    {
        _active = thumb;
        _activeIndex = index;
        InvalidateArrange();
    }

    private void Add(Thumb thumb, int index, bool isVertex)
    {
        _handles.Add((thumb, index, isVertex));
        Children.Add(thumb);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var points = _item.DisplayMask;
        var count = points.Count;

        foreach (var (thumb, index, isVertex) in _handles)
        {
            var dragging = _active is not null;
            var isActive = ReferenceEquals(thumb, _active);

            thumb.Visibility = !dragging || isActive ? Visibility.Visible : Visibility.Hidden;

            Point at;

            if (isActive && _activeIndex >= 0 && _activeIndex < count) at = points[_activeIndex];
            else if (index >= count) at = default;
            else if (isVertex) at = points[index];
            else
            {
                var a = points[index];
                var b = points[(index + 1) % count];
                at = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
            }

            // 손잡이 템플릿이 제 크기 절반만큼 왼쪽 위로 비켜 그린다 - 여기서는 점 자리에 왼쪽 위를 둔다.
            thumb.Arrange(new Rect(new Point(at.X * arrangeSize.Width, at.Y * arrangeSize.Height), thumb.DesiredSize));
        }

        return arrangeSize;
    }
}
