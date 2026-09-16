using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

using DevExpress.Mvvm;

using DevExpress.Xpf.Core;
using DevExpress.Xpf.Editors;
using DevExpress.Xpf.Grid;
using DevExpress.Xpf.LayoutControl;

using Minguk.Tools.Projects.Settings;
using Minguk.Tools.ViewModels.Settings;

namespace Minguk.Tools.Markup.Settings;

/// <summary>
/// 설정 양식 트리(<see cref="SettingsItem"/>)를 DevExpress <see cref="LayoutControl"/> 로 그리는 판. 설정 탭 가운데(<c>docs/솔루션-설정.md</c>).
/// </summary>
/// <remarks>
/// <b>MVVM</b>(사용자, 2026-09-17) - 화면 모델은 이 컨트롤을 모른다. <see cref="Form"/>(칸 모델 트리 <see cref="SettingsFieldViewModel"/>)·<see cref="SelectedItem"/>·
/// <see cref="DropCommand"/>·<see cref="LayoutChangedCommand"/> 를 바인딩으로 받는다. 화면 모델이 <c>FindControl</c> 로 판을 찾아 <c>Build</c>·<c>Refresh</c> 를 부르던 것을 걷어냈다.
///
/// <b>미리보기</b>: 칸마다 편집기를 달고, 바꾸면 칸 모델의 <see cref="SettingsFieldViewModel.Value"/> 에 쓴다. 칸 모델이 바뀌면(스크립트가 씀) 편집기를 고친다. 덮어쓴 값은 라벨을 굵게.
/// <b>디자인</b>: <c>IsCustomization</c> 으로 끌어 옮긴다. 옮긴 결과는 <see cref="ReadBack"/> 이 컨트롤 트리를 걸어 양식 트리로 되읽는다 -
/// LayoutControl 이 옆에 놓을 때 이름 없는 가로 묶음을 스스로 만들기도 해서, 표시(Tag)가 없는 묶음은 라벨 없는 구역으로 받는다.
///
/// 트리를 XML 로 두지 않고 우리 트리로 되읽는 이유는 양식 문서에 적었다. 판은 값·파일을 모른다 - 화면 모델이 준 조회 함수로만 본다.
/// </remarks>
public sealed class SettingsFormCanvas : ContentControl
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>그린 칸 모델마다 편집기를 고치는 일. 다시 지을 때 구독을 푼다.</summary>
    private readonly List<(SettingsFieldViewModel Field, System.ComponentModel.PropertyChangedEventHandler Handler)> _subscriptions = [];
    private LayoutControl? _layout;
    private bool _pushing;

    public SettingsFormCanvas()
    {
        Focusable = false;
        AllowDrop = true;

        PreviewDragEnter += OnPreviewDragOver;
        PreviewDragOver += OnPreviewDragOver;
        PreviewDragLeave += OnPreviewDragLeave;
        PreviewDrop += OnPreviewDrop;

        Unloaded += (_, _) => Unsubscribe();
    }

    public static readonly DependencyProperty FormProperty = DependencyProperty.Register(
        nameof(Form), typeof(SettingsFormState), typeof(SettingsFormCanvas),
        new PropertyMetadata(null, (d, e) => ((SettingsFormCanvas)d).Build(e.NewValue as SettingsFormState)));

    /// <summary>그릴 것 - 뿌리 칸 모델과 디자인인가. 새 객체가 오면 다시 짓는다.</summary>
    public SettingsFormState? Form
    {
        get => (SettingsFormState?)GetValue(FormProperty);
        set => SetValue(FormProperty, value);
    }

    public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
        nameof(SelectedItem), typeof(SettingsItem), typeof(SettingsFormCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, e) => ((SettingsFormCanvas)d).ShowSelection(e.NewValue as SettingsItem)));

    /// <summary>디자인에서 고른 칸(구역도 된다). 누르면 판이 쓰고, 화면 모델이 쓰면 테두리를 옮긴다.</summary>
    public SettingsItem? SelectedItem
    {
        get => (SettingsItem?)GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public static readonly DependencyProperty DropCommandProperty = DependencyProperty.Register(
        nameof(DropCommand), typeof(ICommand), typeof(SettingsFormCanvas));

    /// <summary>도구 상자의 칸을 판에 놓았다 - 인자 <see cref="SettingsDrop"/>.</summary>
    public ICommand? DropCommand
    {
        get => (ICommand?)GetValue(DropCommandProperty);
        set => SetValue(DropCommandProperty, value);
    }

    public static readonly DependencyProperty LayoutChangedCommandProperty = DependencyProperty.Register(
        nameof(LayoutChangedCommand), typeof(ICommand), typeof(SettingsFormCanvas));

    /// <summary>
    /// 디자인에서 손을 뗐다 - 끌어 옮긴 결과를 칸 트리에 되읽은 뒤 부른다. 인자는 순서·묶음이 바뀌었는가(bool). 화면 모델이 바뀌었으면 저장한다.
    /// </summary>
    public ICommand? LayoutChangedCommand
    {
        get => (ICommand?)GetValue(LayoutChangedCommandProperty);
        set => SetValue(LayoutChangedCommandProperty, value);
    }

    public static readonly DependencyProperty SizeChangedCommandProperty = DependencyProperty.Register(
        nameof(SizeChangedCommand), typeof(ICommand), typeof(SettingsFormCanvas));

    /// <summary>「나누기」 막대를 끌어 앞 칸 크기를 바꿨다(손을 뗄 때) - 인자 <see cref="SettingsItemSize"/>. 화면 모델이 그 칸이 든 양식에 저장한다.</summary>
    public ICommand? SizeChangedCommand
    {
        get => (ICommand?)GetValue(SizeChangedCommandProperty);
        set => SetValue(SizeChangedCommandProperty, value);
    }

    /// <summary>크기 조절을 켠 칸들 - 손을 떼면 끌어 바뀐 크기를 칸 트리와 견준다.</summary>
    private readonly List<(FrameworkElement Element, SettingsItem Item, bool Horizontal)> _sizers = [];

    private bool IsDesign => Form?.IsDesign == true;

    /// <summary>그린 뿌리 칸.</summary>
    private SettingsItem? Root => Form?.Root.Item;

    // ── 그리기 ───────────────────────────────────────────────────────────

    private void Build(SettingsFormState? form)
    {
        if (_layout is not null)
        {
            _layout.PreviewMouseLeftButtonUp -= OnLayoutMouseUp;
        }

        Unsubscribe();
        ClearDropIndicator();
        _selection = null;
        _sizers.Clear();

        if (form is null)
        {
            _layout = null;
            Content = null;
            return;
        }

        var root = form.Root.Item;
        var design = form.IsDesign;

        var layout = new LayoutControl
        {
            Orientation = root.Orientation == SettingsOrientation.Horizontal ? Orientation.Horizontal : Orientation.Vertical,
            Padding = new Thickness(8),
            ScrollBars = ScrollBars.Auto,
            Tag = root,
            AllowNewItemsDuringCustomization = false,
            AllowAvailableItemsDuringCustomization = false,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        AddChildren(layout, form.Root.Children, root.Orientation == SettingsOrientation.Horizontal, design);

        if (form.Root.Children.Count == 0)
        {
            layout.Children.Add(new LayoutItem
            {
                AddColonToLabel = false,
                Label = "",
                Content = new TextBlock
                {
                    Text = design
                        ? "칸이 없습니다 - 왼쪽 도구 상자에서 칸을 여기로 끌어 오거나 두 번 누르세요."
                        : "칸이 없습니다 - 도구 모음의 [디자인] 을 켜고 칸을 더하세요.",
                    Opacity = 0.7,
                    VerticalAlignment = VerticalAlignment.Center
                }
            });
        }

        layout.PreviewMouseLeftButtonUp += OnLayoutMouseUp;

        _layout = layout;
        Content = layout;

        // 사용자 배치는 컨트롤이 뜬 뒤에 켠다 - 만들 때 켜면 덮개가 자리를 못 잡는다.
        if (design)
        {
            layout.Loaded += (_, _) =>
            {
                layout.IsCustomization = true;
                ShowSelection(SelectedItem);
            };
        }
    }

    /// <summary>
    /// 구역(또는 판)에 칸들을 넣는다. 「나누기」는 미리보기에서 그리지 않고 <b>앞 칸</b>에 크기 조절(LayoutControl 의 <c>AllowHorizontalSizing</c>·<c>AllowVerticalSizing</c>)을 켠다.
    /// </summary>
    /// <remarks>
    /// DevExpress v26 LayoutControl 에는 따로 놓는 LayoutSplitter 요소가 없다(같은 이름은 도킹 쪽) - 칸의 크기 조절을 켜면 그 칸의 오른쪽(가로 구역)·아래쪽(세로 구역)에 막대가 생긴다.
    /// 앞 칸이 없거나(맨 앞) 앞도 나누기면 아무 일도 안 한다. 디자인에서는 자리표시 칸으로 그려 끌어 옮긴다(크기 조절은 안 켠다 - 덮개가 가로챈다).
    /// </remarks>
    private void AddChildren(Panel panel, IEnumerable<SettingsFieldViewModel> children, bool horizontal, bool design)
    {
        FrameworkElement? previous = null;

        foreach (var child in children)
        {
            if (child.Item.Kind == SettingsItemKind.Splitter && !design)
            {
                if (previous is { Tag: SettingsItem sized })
                {
                    if (horizontal) LayoutControl.SetAllowHorizontalSizing(previous, true);
                    else LayoutControl.SetAllowVerticalSizing(previous, true);

                    _sizers.Add((previous, sized, horizontal));
                }

                previous = null;
                continue;
            }

            var element = CreateElement(child, design);
            panel.Children.Add(element);
            previous = child.Item.Kind == SettingsItemKind.Splitter ? null : element;
        }
    }

    private FrameworkElement CreateElement(SettingsFieldViewModel field, bool design)
    {
        var item = field.Item;

        // 나누기(디자인에서만 여기 온다) - 골라 옮기고 지울 수 있게 얇은 자리표시. 미리보기에서는 AddChildren 이 앞 칸에 크기 조절을 켠다.
        if (item.Kind == SettingsItemKind.Splitter)
        {
            return new LayoutItem
            {
                Tag = item,
                AddColonToLabel = false,
                Label = "",
                ToolTip = "나누기 - 미리보기에서 앞 칸 가장자리에 크기 조절 막대가 생깁니다.",
                Content = new TextBlock { Text = "↔ 나누기", Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center }
            };
        }

        if (item.Kind == SettingsItemKind.Group)
        {
            var group = new LayoutGroup
            {
                Tag = item,
                Header = string.IsNullOrWhiteSpace(item.Label) ? null : item.Label,
                Orientation = item.Orientation == SettingsOrientation.Horizontal ? Orientation.Horizontal : Orientation.Vertical,
                View = item.View == SettingsGroupView.Tabs ? LayoutGroupView.Tabs
                    : string.IsNullOrWhiteSpace(item.Label) ? LayoutGroupView.Group
                    : LayoutGroupView.GroupBox,
                ToolTip = string.IsNullOrWhiteSpace(item.Tooltip) ? null : item.Tooltip
            };

            AddChildren(group, field.Children, item.Orientation == SettingsOrientation.Horizontal, design);
            ApplySize(group, item);

            return group;
        }

        var layoutItem = new LayoutItem
        {
            Tag = item,
            Label = item.DisplayLabel,
            AddColonToLabel = false,
            ToolTip = string.IsNullOrWhiteSpace(item.Tooltip) ? null : item.Tooltip
        };

        var editor = CreateEditor(field, layoutItem, design);

        if (item.Kind == SettingsItemKind.List)
        {
            layoutItem.LabelPosition = LayoutItemLabelPosition.Top;
            layoutItem.VerticalAlignment = VerticalAlignment.Stretch;
            layoutItem.MinHeight = 160;
        }

        layoutItem.Content = editor;
        ApplySize(layoutItem, item);

        return layoutItem;
    }

    /// <summary>편집기 하나. 칸 모델이 바뀌면(값·굵게·경고) 편집기와 라벨을 고친다.</summary>
    /// <remarks>
    /// 칸마다 붙던 되돌리기(↺) 단추는 뺐다(사용자, 2026-09-17 "굳이 없어도") - 덮어쓴 칸은 라벨이 굵고, 되돌리기는 도구 줄의 [초기값] 이 층째로 한다.
    /// </remarks>
    private FrameworkElement CreateEditor(SettingsFieldViewModel field, LayoutItem owner, bool design)
    {
        var item = field.Item;
        FrameworkElement editor;
        Action<JsonNode?> push;

        switch (item.Kind)
        {
            case SettingsItemKind.Number:
                {
                    var decimals = Math.Max(0, item.Decimals ?? 0);
                    var spin = new SpinEdit
                    {
                        MinValue = item.Min is { } min ? (decimal)min : null,
                        MaxValue = item.Max is { } max ? (decimal)max : null,
                        Increment = (decimal)(item.Step ?? Math.Pow(10, -decimals)),
                        IsFloatValue = decimals > 0,
                        Mask = "n" + decimals,
                        MaskUseAsDisplayFormat = true,
                        MinWidth = 100
                    };
                    spin.EditValueChanged += (_, _) =>
                    {
                        if (_pushing || spin.EditValue is null) return;
                        var value = Convert.ToDouble(spin.EditValue, CultureInfo.InvariantCulture);
                        Raise(field, decimals == 0 ? JsonValue.Create((long)Math.Round(value)) : JsonValue.Create(Math.Round(value, decimals)));
                    };
                    push = node => spin.EditValue = node is JsonValue v ? (decimal)SettingsValue.ToDouble(v) : 0m;
                    editor = spin;
                    break;
                }
            case SettingsItemKind.Slider:
                {
                    var track = new TrackBarEdit
                    {
                        Minimum = item.Min ?? 0,
                        Maximum = item.Max ?? 100,
                        SmallStep = item.Step ?? 1,
                        LargeStep = (item.Step ?? 1) * 5,
                        MinWidth = 160,
                        TickPlacement = System.Windows.Controls.Primitives.TickPlacement.None
                    };
                    var number = new TextBlock { MinWidth = 36, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                    track.EditValueChanged += (_, _) =>
                    {
                        var value = Math.Round(Convert.ToDouble(track.EditValue ?? 0, CultureInfo.InvariantCulture) / (item.Step ?? 1)) * (item.Step ?? 1);
                        number.Text = value.ToString("0.##", CultureInfo.CurrentCulture);
                        if (_pushing) return;
                        Raise(field, value == Math.Floor(value) ? JsonValue.Create((long)value) : JsonValue.Create(value));
                    };
                    push = node =>
                    {
                        var value = node is JsonValue v ? SettingsValue.ToDouble(v) : 0;
                        track.EditValue = value;
                        number.Text = value.ToString("0.##", CultureInfo.CurrentCulture);
                    };
                    var row = new DockPanel { LastChildFill = true };
                    DockPanel.SetDock(number, System.Windows.Controls.Dock.Right);
                    row.Children.Add(number);
                    row.Children.Add(track);
                    editor = row;
                    break;
                }
            case SettingsItemKind.Check:
                {
                    var check = new CheckEdit { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
                    check.EditValueChanged += (_, _) =>
                    {
                        if (_pushing) return;
                        Raise(field, JsonValue.Create(check.IsChecked == true));
                    };
                    push = node => check.IsChecked = node is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.True;
                    editor = check;
                    break;
                }
            case SettingsItemKind.Combo:
                {
                    var combo = new ComboBoxEdit { IsTextEditable = false, ItemsSource = item.Items ?? [], MinWidth = 120 };
                    combo.SelectedIndexChanged += (_, _) =>
                    {
                        if (_pushing || combo.SelectedItem is not string text) return;
                        Raise(field, JsonValue.Create(text));
                    };
                    push = node => combo.SelectedItem = node is JsonValue v ? v.GetValue<string>() : null;
                    editor = combo;
                    break;
                }
            case SettingsItemKind.List:
                {
                    var (grid, pushList) = CreateList(field, design);
                    push = pushList;
                    editor = grid;
                    break;
                }
            default:
                {
                    var text = new TextEdit { MinWidth = 160 };
                    text.EditValueChanged += (_, _) =>
                    {
                        if (_pushing) return;
                        Raise(field, JsonValue.Create(text.EditValue?.ToString() ?? string.Empty));
                    };
                    push = node => text.EditValue = node is JsonValue v ? v.GetValue<string>() : string.Empty;
                    editor = text;
                    break;
                }
        }

        if (design)
        {
            // 디자인에서는 값을 못 바꾼다 - 덮개가 누름을 먹지만 키보드로 들어오지 않게.
            editor.IsEnabled = false;
        }

        void Update(bool value)
        {
            if (value)
            {
                _pushing = true;

                try
                {
                    // 글을 치던 칸은 덮지 않는다 - 치는 동안 스크립트가 같은 칸에 쓰면 치던 것이 날아간다.
                    if (!editor.IsKeyboardFocusWithin) push(field.Value);
                }
                finally
                {
                    _pushing = false;
                }
            }

            owner.LabelStyle = !design && field.IsOverridden ? BoldLabel : null;
            owner.ToolTip = field.ToolTip;
            owner.Label = field.Label;
        }

        System.ComponentModel.PropertyChangedEventHandler handler = (_, e) =>
        {
            if (e.PropertyName is nameof(SettingsFieldViewModel.Value) or null or "")
                Update(value: true);
            else if (e.PropertyName is nameof(SettingsFieldViewModel.IsOverridden) or nameof(SettingsFieldViewModel.Warning) or nameof(SettingsFieldViewModel.Label))
                Update(value: false);
        };

        field.PropertyChanged += handler;
        _subscriptions.Add((field, handler));
        Update(value: true);

        return editor;
    }

    private static readonly Style BoldLabel = CreateBoldLabel();

    private static Style CreateBoldLabel()
    {
        var style = new Style(typeof(LayoutItemLabel));
        style.Setters.Add(new Setter(System.Windows.Documents.TextElement.FontWeightProperty, FontWeights.Bold));
        style.Seal();
        return style;
    }

    /// <summary>목록 칸 - 표는 <see cref="SettingsListGrid"/>(처음 행 대화 상자와 같다). 행을 더하고 고치고 지우면 통째로 값을 낸다.</summary>
    private (FrameworkElement Grid, Action<JsonNode?> Push) CreateList(SettingsFieldViewModel field, bool design)
    {
        SettingsListGrid? list = null;
        list = new SettingsListGrid(field.Item.Columns ?? [], editable: !design, () =>
        {
            if (_pushing) return;
            Raise(field, list!.Read());
        });

        return (list.Grid, list.Push);
    }

    /// <summary>사람이 값을 바꿨다 - 칸 모델에 쓴다(화면 모델이 저장한다).</summary>
    private void Raise(SettingsFieldViewModel field, JsonNode? value)
    {
        if (IsDesign) return;

        field.Value = value;
    }

    private void Unsubscribe()
    {
        foreach (var (field, handler) in _subscriptions) field.PropertyChanged -= handler;
        _subscriptions.Clear();
    }

    // ── 디자인: 고르기·되읽기 ────────────────────────────────────────────

    private SelectionAdorner? _selection;

    /// <summary>
    /// 고른 칸의 테두리를 그린다. 이 판의 LayoutControl 은 고른 요소 목록을 밖에 열어 두지 않아(26.1 실측: <c>SelectedElements</c> 없음)
    /// 고르기와 표시를 판이 직접 한다.
    /// </summary>
    private void ShowSelection(SettingsItem? item)
    {
        if (_selection is not null)
        {
            AdornerLayer.GetAdornerLayer(_selection.AdornedElement)?.Remove(_selection);
            _selection = null;
        }

        if (_layout is null || !IsDesign || item is null) return;

        var element = Elements(_layout).FirstOrDefault(e => ReferenceEquals(e.Tag, item));

        if (element is null || AdornerLayer.GetAdornerLayer(element) is not { } layer) return;

        _selection = new SelectionAdorner(element);
        layer.Add(_selection);
    }

    /// <summary>
    /// 디자인에서 손을 떼면 - 끌어 옮겼을 수 있으니 먼저 알리고(되읽기), 누른 자리의 칸을 고른다.
    /// </summary>
    /// <remarks>사용자 배치 덮개가 누름을 먹어 칸의 이벤트로는 못 받는다 - 자리로 찾는다(가장 안쪽 칸).</remarks>
    /// <summary>저장된 크기(나누기로 끌어 정한 것)를 건다.</summary>
    private static void ApplySize(FrameworkElement element, SettingsItem item)
    {
        if (item.Width is > 0 and var width) element.Width = width;
        if (item.Height is > 0 and var height) element.Height = height;
    }

    /// <summary>
    /// 미리보기에서 손을 뗐다 - 「나누기」 막대로 앞 칸 크기를 바꿨으면 알린다. LayoutControl 은 끄는 동안 칸의 <c>Width</c>·<c>Height</c> 를 고친다.
    /// </summary>
    private void ReportSizes()
    {
        foreach (var (element, item, horizontal) in _sizers)
        {
            var now = horizontal ? element.Width : element.Height;
            if (double.IsNaN(now) || now <= 0) continue;

            now = Math.Round(now);
            var saved = horizontal ? item.Width : item.Height;
            if (saved is { } before && Math.Abs(before - now) < 0.5) continue;

            var size = horizontal ? new SettingsItemSize(item, now, null) : new SettingsItemSize(item, null, now);
            if (SizeChangedCommand?.CanExecute(size) == true) SizeChangedCommand.Execute(size);
        }
    }

    private void OnLayoutMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_layout is null) return;

        if (!IsDesign)
        {
            // 막대를 놓은 뒤 LayoutControl 이 크기를 마저 적으므로 한 박자 늦게 본다.
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, ReportSizes);
            return;
        }

        // 끌어 옮겼을 수 있다 - 칸 트리에 되읽고 알린다(화면 모델이 바뀌었으면 저장).
        var changed = ReadBack();
        if (LayoutChangedCommand?.CanExecute(changed) == true) LayoutChangedCommand.Execute(changed);

        var hit = HitItem(e.GetPosition(_layout));

        SetCurrentValue(SelectedItemProperty, hit);
    }

    /// <summary>판 좌표의 가장 안쪽 칸(구역 포함). 없으면 null.</summary>
    public SettingsItem? HitItem(Point point) => HitElement(point)?.Element.Tag as SettingsItem;

    // ── 디자인: 도구 상자에서 끌어 놓기 ─────────────────────────────────
    //    도구 상자(SettingsToolboxDragBehavior)가 칸 종류를 DragFormat 으로 싣고 온다. 누름과 같이 사용자 배치 덮개가 가려
    //    칸마다 끌기 이벤트를 달 수 없어, 판이 Preview 끌기 이벤트로 받아 자리를 계산한다(HitItem 과 같은 방식).

    /// <summary>끌어 놓기에 싣는 자료 형식. 값은 <see cref="SettingsItemKind"/>.</summary>
    public const string DragFormat = "Minguk.Tools.SettingsItemKind";

    private DropIndicatorAdorner? _dropIndicator;

    /// <summary>
    /// 판 좌표에 놓으면 어디에 들어가는가. 구역 한가운데면 그 안, 구역 위·아래 끝 띠나 칸이면 그 앞·뒤(구역 방향을 따라 가로면 좌우 반),
    /// 칸이 없는 자리면 맨 끝.
    /// </summary>
    public SettingsDrop DropTargetAt(SettingsItemKind kind, Point point)
    {
        if (_layout is null || HitElement(point) is not { } hit || ReferenceEquals(hit.Element, _layout))
            return new SettingsDrop(kind, null, SettingsDropPlacement.End);

        var item = (SettingsItem)hit.Element.Tag;
        var bounds = hit.Bounds;

        if (item.Kind == SettingsItemKind.Group)
        {
            // 구역 머리·밑 띠(8px)는 구역의 앞·뒤, 나머지는 구역 안.
            const double edge = 8;

            if (point.Y < bounds.Top + edge) return new SettingsDrop(kind, item, SettingsDropPlacement.Before);
            if (point.Y > bounds.Bottom - edge) return new SettingsDrop(kind, item, SettingsDropPlacement.After);

            return new SettingsDrop(kind, item, SettingsDropPlacement.Inside);
        }

        var horizontal = IsInHorizontalGroup(hit.Element);
        var before = horizontal ? point.X < bounds.Left + (bounds.Width / 2) : point.Y < bounds.Top + (bounds.Height / 2);

        return new SettingsDrop(kind, item, before ? SettingsDropPlacement.Before : SettingsDropPlacement.After);
    }

    /// <summary>놓는다 - 끌어 놓기가 끝났을 때, 그리고 검사 하네스가 마우스 없이 부른다.</summary>
    public void Drop(SettingsItemKind kind, Point point)
    {
        ClearDropIndicator();

        if (!IsDesign) return;

        // 놓기 전에 끌어 옮긴 것을 되읽는다 - 화면 모델이 칸을 넣고 양식을 저장하기 전에 트리가 화면과 같아야 한다.
        var changed = ReadBack();
        if (LayoutChangedCommand?.CanExecute(changed) == true) LayoutChangedCommand.Execute(changed);

        var drop = DropTargetAt(kind, point);
        if (DropCommand?.CanExecute(drop) == true) DropCommand.Execute(drop);
    }

    private static bool TryGetKind(DragEventArgs e, out SettingsItemKind kind)
    {
        kind = default;

        if (!e.Data.GetDataPresent(DragFormat) || e.Data.GetData(DragFormat) is not SettingsItemKind found) return false;

        kind = found;
        return true;
    }

    private void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        if (!IsDesign || _layout is null || !TryGetKind(e, out var kind))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Copy;
        e.Handled = true;

        ShowDropIndicator(DropTargetAt(kind, e.GetPosition(_layout)));
    }

    private void OnPreviewDragLeave(object sender, DragEventArgs e)
    {
        // 판 안의 칸 사이를 지날 때도 온다 - 정말 판 밖으로 나갔을 때만 지운다.
        var point = e.GetPosition(this);

        if (point.X < 0 || point.Y < 0 || point.X >= ActualWidth || point.Y >= ActualHeight) ClearDropIndicator();
    }

    private void OnPreviewDrop(object sender, DragEventArgs e)
    {
        if (_layout is null || !TryGetKind(e, out var kind))
        {
            ClearDropIndicator();
            return;
        }

        e.Effects = DragDropEffects.Copy;
        e.Handled = true;

        Drop(kind, e.GetPosition(_layout));
    }

    private void ShowDropIndicator(SettingsDrop drop)
    {
        if (_layout is null) return;

        var element = drop.Anchor is null ? _layout : Elements(_layout).FirstOrDefault(e => ReferenceEquals(e.Tag, drop.Anchor)) ?? (FrameworkElement)_layout;
        var horizontal = drop.Anchor is not null && drop.Placement != SettingsDropPlacement.Inside && IsInHorizontalGroup(element);

        if (_dropIndicator is not null && ReferenceEquals(_dropIndicator.AdornedElement, element)
            && _dropIndicator.Placement == drop.Placement && _dropIndicator.Horizontal == horizontal)
            return;

        ClearDropIndicator();

        if (AdornerLayer.GetAdornerLayer(element) is not { } layer) return;

        _dropIndicator = new DropIndicatorAdorner(element, drop.Placement, horizontal);
        layer.Add(_dropIndicator);
    }

    private void ClearDropIndicator()
    {
        if (_dropIndicator is null) return;

        AdornerLayer.GetAdornerLayer(_dropIndicator.AdornedElement)?.Remove(_dropIndicator);
        _dropIndicator = null;
    }

    /// <summary>이 칸을 든 구역이 가로로 놓는가.</summary>
    private static bool IsInHorizontalGroup(FrameworkElement element)
        => VisualTreeHelper.GetParent(element) is LayoutGroup { Orientation: Orientation.Horizontal };

    /// <summary>놓을 자리 표시 - 앞·뒤는 굵은 선, 안은 점선 상자, 맨 끝은 판 아래 선.</summary>
    private sealed class DropIndicatorAdorner(UIElement adorned, SettingsDropPlacement placement, bool horizontal) : Adorner(adorned)
    {
        private static readonly Brush Accent = Freeze(new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)));
        private static readonly Pen Dashed = Freeze(new Pen(Accent, 2) { DashStyle = DashStyles.Dash });

        public SettingsDropPlacement Placement { get; } = placement;

        public bool Horizontal { get; } = horizontal;

        private static T Freeze<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            var size = AdornedElement.RenderSize;
            const double thick = 3;

            switch (Placement)
            {
                case SettingsDropPlacement.Inside:
                    drawingContext.DrawRectangle(null, Dashed, new Rect(1, 1, Math.Max(0, size.Width - 2), Math.Max(0, size.Height - 2)));
                    break;
                case SettingsDropPlacement.Before when Horizontal:
                    drawingContext.DrawRectangle(Accent, null, new Rect(-thick, 0, thick, size.Height));
                    break;
                case SettingsDropPlacement.After when Horizontal:
                    drawingContext.DrawRectangle(Accent, null, new Rect(size.Width, 0, thick, size.Height));
                    break;
                case SettingsDropPlacement.Before:
                    drawingContext.DrawRectangle(Accent, null, new Rect(0, -thick, size.Width, thick));
                    break;
                default:
                    drawingContext.DrawRectangle(Accent, null, new Rect(0, Math.Max(0, size.Height - thick), size.Width, thick));
                    break;
            }
        }
    }

    private (FrameworkElement Element, Rect Bounds)? HitElement(Point point)
    {
        if (_layout is null) return null;

        FrameworkElement? best = null;
        var bestBounds = Rect.Empty;
        var bestArea = double.MaxValue;

        foreach (var element in Elements(_layout))
        {
            if (element.Tag is not SettingsItem || !element.IsVisible || element.ActualWidth <= 0) continue;

            Rect bounds;

            try
            {
                bounds = element.TransformToAncestor(_layout).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            }
            catch (InvalidOperationException)
            {
                continue; // 탭 안에 숨은 칸은 조상 관계가 끊겨 있다.
            }

            var area = bounds.Width * bounds.Height;

            if (bounds.Contains(point) && area < bestArea)
            {
                best = element;
                bestBounds = bounds;
                bestArea = area;
            }
        }

        return best is null ? null : (best, bestBounds);
    }

    private sealed class SelectionAdorner(UIElement adorned) : Adorner(adorned)
    {
        private static readonly Pen Pen = CreatePen();

        private static Pen CreatePen()
        {
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)), 2);
            pen.Freeze();
            return pen;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            var size = AdornedElement.RenderSize;
            drawingContext.DrawRectangle(null, Pen, new Rect(-1, -1, size.Width + 2, size.Height + 2));
        }
    }

    /// <summary>
    /// 디자인에서 끌어 옮긴 결과를 양식 트리로 되읽는다. 칸 객체는 그대로 쓰고 <c>Children</c> 만 다시 채운다.
    /// </summary>
    /// <returns>순서·묶음이 바뀌었으면 true.</returns>
    private bool ReadBack()
    {
        if (_layout is null || Root is null || !IsDesign) return false;

        var before = Signature(Root);

        Root.Children = ReadChildren(_layout);

        return before != Signature(Root);
    }

    private static List<SettingsItem> ReadChildren(Panel panel)
    {
        var list = new List<SettingsItem>();

        foreach (var child in panel.Children.OfType<FrameworkElement>())
        {
            switch (child)
            {
                case LayoutGroup group:
                    {
                        var item = group.Tag as SettingsItem ?? new SettingsItem { Kind = SettingsItemKind.Group };

                        item.Orientation = group.Orientation == Orientation.Horizontal ? SettingsOrientation.Horizontal : SettingsOrientation.Vertical;
                        item.Children = ReadChildren(group);

                        // LayoutControl 이 스스로 만든 빈 묶음은 버린다.
                        if (group.Tag is null && item.Children.Count == 0) continue;

                        group.Tag = item;
                        list.Add(item);
                        break;
                    }
                case LayoutItem { Tag: SettingsItem item }:
                    list.Add(item);
                    break;
            }
        }

        return list;
    }

    private static string Signature(SettingsItem item)
        => $"{item.Kind}:{item.Name}:{item.Orientation}({string.Join(",", (item.Children ?? []).Select(Signature))})";

    private static IEnumerable<FrameworkElement> Elements(Panel panel)
    {
        foreach (var child in panel.Children.OfType<FrameworkElement>())
        {
            yield return child;

            if (child is Panel inner)
            {
                foreach (var nested in Elements(inner)) yield return nested;
            }
        }
    }
}
