using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows;

namespace Minguk.Tools.Vision.Regions;

/// <summary>
/// 자리(<see cref="NamedRegion"/>) 안에서 따로 읽을 <b>칸</b>. 스크립트는 <c>읽기("자리.칸")</c> 으로 부른다.
/// </summary>
/// <remarks>
/// <b>왜 있나</b>(사용자, 2026-09-16) - 오버워치 탄약 「17 | 24」 의 구분선을 모델이 빈칸이나 <c>1</c> 로 읽는다. 자리 안에
/// 「현재」·「최대」 칸을 두면 구분선이 칸 밖에 남아 아예 안 들어간다. LayoutControl 의 LayoutGroup 과 그 안 항목 같은 짜임이다.
///
/// <b>좌표는 자리 기준 0~1</b> - 자리를 옮기거나 늘리면 칸이 따라간다. <see cref="X"/>·<see cref="Y"/>·<see cref="Width"/>·<see cref="Height"/> 는
/// <b>돌리기 전</b> 상자이고, <see cref="Angle"/> 만큼 상자 가운데를 중심으로 돈다(사용자 2026-09-16 「대각선 사각」).
/// 회전은 <b>픽셀 공간</b>에서 한다 - 0~1 은 가로세로 배율이 달라 거기서 돌리면 찌그러진다(<see cref="RegionTargets.Bounds"/>).
/// </remarks>
public sealed class RegionCell : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private double _angle;
    private string _lastText = string.Empty;

    /// <summary>자리 안에서 부를 이름. 그 자리 안에서 겹치지 않는다. 점(.)은 못 쓴다.</summary>
    [JsonPropertyName("name")]
    public string Name
    {
        get => _name;
        set => Set(ref _name, value ?? string.Empty);
    }

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("width")]
    public double Width { get; set; }

    [JsonPropertyName("height")]
    public double Height { get; set; }

    /// <summary>시계 방향 각도(도). 0 이면 돌리지 않은 상자.</summary>
    [JsonPropertyName("angle")]
    public double Angle
    {
        get => _angle;
        set => Set(ref _angle, Math.Round(value, 2));
    }

    /// <summary>자리 기준 0~1 상자(돌리기 전). 소수 4자리로 반올림 - 자리와 같다.</summary>
    [JsonIgnore]
    public Rect Rect
    {
        get => new(X, Y, Width, Height);
        set
        {
            X = Math.Round(value.X, 4);
            Y = Math.Round(value.Y, 4);
            Width = Math.Round(value.Width, 4);
            Height = Math.Round(value.Height, 4);
        }
    }

    /// <summary>
    /// 본보기 마스크 - 칸 기준 0~1 다각형(돌리기 전 상자 안). 비었으면(점 셋 미만) 없다. 저장한다.
    /// </summary>
    /// <remarks>
    /// 사용자(2026-09-23) "Adorner 안에 폴리곤 식의 구역을 자유롭게 지정해서 이부분만 매칭" - 「영역 이미지 저장」 이 다각형 밖을 투명으로 칠해 PNG 에 담고,
    /// <c>그림찾기</c>·<c>그림누르기</c>·<c>명중확인</c> 은 투명한 곳을 빼고 견준다(<see cref="Matching.PolygonMask"/>·<see cref="Matching.TemplateMatch"/>).
    /// 둥근 아이콘·뒤가 비치는 버튼·안쪽 그림만 바뀌는 카드용. 글자 읽기는 이것을 안 본다.
    /// 칸 기준이라 칸을 옮기고 늘리고 돌리면 따라간다 - 저장되는 그림이 곧 칸을 세운 그림이기 때문이다.
    /// </remarks>
    [JsonIgnore]
    public IReadOnlyList<Point> Mask
    {
        get => _mask;
        set
        {
            var points = value is null ? [] : value.Select(p => new Point(Math.Round(Math.Clamp(p.X, 0, 1), 4), Math.Round(Math.Clamp(p.Y, 0, 1), 4))).ToArray();

            if (_mask.SequenceEqual(points)) return;

            _mask = points;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Mask)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasMask)));
        }
    }

    private Point[] _mask = [];

    /// <summary>마스크가 있는가(점 셋 이상).</summary>
    [JsonIgnore]
    public bool HasMask => _mask.Length >= 3;

    /// <summary>파일 모양 - <c>"mask": [[x, y], ...]</c>. 없으면 적지 않는다(옛 파일과 같은 모양).</summary>
    [JsonPropertyName("mask")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double[][]? MaskData
    {
        get => HasMask ? [.. _mask.Select(p => new[] { p.X, p.Y })] : null;
        set => Mask = value is null ? [] : [.. value.Where(pair => pair is { Length: >= 2 }).Select(pair => new Point(pair[0], pair[1]))];
    }

    /// <summary>숫자만 보인다 - 이 칸의 읽은 글에서 숫자 덩어리만 남긴다(<see cref="NamedRegion.NumbersOnly"/>). 저장한다.</summary>
    [JsonPropertyName("digits")]
    public bool NumbersOnly
    {
        get => _numbersOnly;
        set => Set(ref _numbersOnly, value);
    }

    private bool _numbersOnly;

    /// <summary>마지막으로 읽은 글(한 줄로). 저장하지 않는다.</summary>
    [JsonIgnore]
    public string LastText
    {
        get => _lastText;
        set => Set(ref _lastText, value ?? string.Empty);
    }

    /// <summary>
    /// 영역 패널 트리의 「계속 읽기」 열 자리 채움. 칸 줄에서는 체크를 숨기고(<see cref="IsRegion"/>) 값도 없다 - 계속 읽기는 자리 단위다.
    /// </summary>
    /// <remarks>자리와 칸이 한 트리의 같은 열에 묶여, 칸에 이 이름이 없으면 바인딩 오류가 난다. 쓰기도 받아야 편집기 묶음이 안 터진다.</remarks>
    [JsonIgnore]
    public bool? KeepReading
    {
        get => null;
        set { }
    }

    /// <summary>트리 줄이 자리인가(칸이면 false). 칸 줄에서 계속 읽기 체크를 숨기는 데 쓴다.</summary>
    [JsonIgnore]
    public bool IsRegion => false;

    public override string ToString() => Name;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
