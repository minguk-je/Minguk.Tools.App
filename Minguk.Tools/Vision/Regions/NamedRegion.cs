using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows;

namespace Minguk.Tools.Vision.Regions;

/// <summary>
/// 사람이 화면에서 끌어 만든 <b>이름 붙은 자리</b>. 스크립트가 좌표 대신 이름으로 부른다.
/// </summary>
/// <remarks>
/// <b>왜 이름인가</b> - <c>숫자읽기(0.895, 0.86, 0.095, 0.06)</c> 은 그 네 숫자가 무엇인지 스크립트만 봐서는
/// 알 수 없고, 게임이 바뀌면 스크립트를 다 고쳐야 한다. <c>숫자읽기("탄약")</c> 이면 자리가 바뀌어도
/// 스크립트는 그대로다 - 고치는 것은 화면에서 사각형 하나를 다시 끄는 일이 된다.
///
/// 좌표는 <b>0~1 비율</b>이다. 해상도가 바뀌어도 같은 자리를 가리킨다.
///
/// 옛 "글자 영역"(한 곳만, 설정에 저장)을 여기로 합쳤다(2026-09-15) - 자리마다 <see cref="KeepReading"/> 를 켜면 화면이 계속 읽어
/// <see cref="LastText"/> 에 적는다. 그리드가 칸에서 바로 고치므로 바뀐 것을 알린다(<see cref="INotifyPropertyChanged"/>).
///
/// 자리마다 손질·언어·기울기를 고르던 때(2026-09-16 까지)의 키(<c>ink</c>·<c>prep</c>·<c>lang</c>·<c>shear</c>)는 읽을 때 버린다 -
/// System.Text.Json 은 모르는 키를 건너뛰고, 다음 저장에서 사라진다. 엔진(PP-OCRv5)이 손질 없이 한글·영문·숫자를 읽는다.
/// </remarks>
public sealed class NamedRegion : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private bool _keepReading;
    private string _lastText = string.Empty;

    /// <summary>스크립트가 부를 이름. 빈 이름은 못 만든다.</summary>
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

    /// <summary>
    /// 화면(스크립트·플레이)이 이 자리를 0.5초마다 읽어 <see cref="LastText"/> 에 적을지. 저장한다.
    /// </summary>
    /// <remarks>스크립트의 <c>읽기("이름")</c> 은 이것과 상관없이 부를 때 읽는다 - 이것은 사람이 화면에서 보려는 것이다.</remarks>
    [JsonPropertyName("live")]
    public bool KeepReading
    {
        get => _keepReading;
        set => Set(ref _keepReading, value);
    }

    /// <summary>
    /// 숫자만 보인다 - 이 자리(와 그 칸들)의 읽은 글에서 숫자 덩어리만 남긴다. 저장한다.
    /// </summary>
    /// <remarks>
    /// 사용자(2026-09-18) "숫자읽기인데 다른 문자 들어오면 그리드에도 안 보이게". 탄약처럼 숫자만 뜻이 있는 자리에서 OCR 이 「4|」·「1O」 같은 것을 주면
    /// 스크립트의 <c>숫자읽기</c> 는 알아서 숫자만 뽑는데, 영역 패널의 「읽은 글자」 와 미리보기 겹그림에는 그대로 떠서 헷갈렸다. 켜면 「HP 5 / 9」 는 「5 9」 로, 숫자가 없으면 빈 글.
    /// </remarks>
    [JsonPropertyName("digits")]
    public bool NumbersOnly
    {
        get => _numbersOnly;
        set => Set(ref _numbersOnly, value);
    }

    private bool _numbersOnly;

    /// <summary>화면에 보일 글 - 「숫자만」 이면(자리든 칸이든) 숫자 덩어리만 띄어 잇는다.</summary>
    public static string Shown(NamedRegion region, RegionCell? cell, string text)
        => region.NumbersOnly || cell is { NumbersOnly: true }
            ? string.Join(" ", RegionTargets.NumbersIn(text))
            : text;

    /// <summary>마지막으로 읽은 글(한 줄로). 저장하지 않는다.</summary>
    [JsonIgnore]
    public string LastText
    {
        get => _lastText;
        set => Set(ref _lastText, value ?? string.Empty);
    }

    /// <summary>
    /// 이 자리 안에서 따로 읽을 칸들(<see cref="RegionCell"/>). <b>늘 하나 이상</b> - 새 자리는 자리와 같은 크기의 「전체」 로 시작한다.
    /// </summary>
    /// <remarks>
    /// 읽는 것은 칸들뿐이고, 자리를 부르면 이 순서대로 읽어 잇는다(<see cref="RegionTargets"/>).
    /// 칸을 적기 전의 파일은 <c>cells</c> 가 없어 여기 처음 값(「전체」)이 남는다 - 지금과 똑같이 읽힌다.
    /// </remarks>
    [JsonPropertyName("cells")]
    public ObservableCollection<RegionCell> Cells { get; set; } = [NewWholeCell()];

    /// <summary>새 자리·옛 파일에 붙는 칸 이름. 칸이 하나면 자리 전체를 읽으니 「전체」(사용자, 2026-09-16).</summary>
    public const string DefaultCellName = "전체";

    /// <summary>「전체」 전에 쓰던 기본 칸 이름. 이 이름의 자리 전체 칸 하나뿐이면 읽을 때 「전체」 로 바꾼다.</summary>
    private const string OldDefaultCellName = "칸1";

    private static RegionCell NewWholeCell() => new() { Name = DefaultCellName, Rect = new Rect(0, 0, 1, 1) };

    /// <summary>
    /// 칸이 하나도 없으면(파일에 <c>"cells": []</c>) 자리와 같은 크기의 「전체」 를 붙인다.
    /// 예전 기본 이름 「칸1」 로 저장된 자리 전체 칸 하나(돌리지 않은 것)는 「전체」 로 바꾼다 - 손대지 않은 기본 칸이다.
    /// </summary>
    public void EnsureCells()
    {
        Cells ??= [];

        if (Cells.Count == 0) Cells.Add(NewWholeCell());

        if (Cells is [{ X: 0, Y: 0, Width: 1, Height: 1 } only] && Math.Abs(only.Angle) < 0.01
            && string.Equals(only.Name, OldDefaultCellName, StringComparison.OrdinalIgnoreCase))
            only.Name = DefaultCellName;
    }

    /// <summary>칸의 화면 기준 0~1 상자(돌리기 전). 자리 안 비율을 화면 비율로 바꾼다.</summary>
    public Rect CellRect(RegionCell cell)
        => new(X + (cell.X * Width), Y + (cell.Y * Height), cell.Width * Width, cell.Height * Height);

    /// <summary>이름으로 칸을 찾는다(대소문자 무시). 없으면 null.</summary>
    public RegionCell? FindCell(string name)
        => Cells.FirstOrDefault(c => string.Equals(c.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>트리 줄이 자리인가(칸이면 false). 영역 패널이 칸 줄의 계속 읽기 체크를 숨기는 데 쓴다.</summary>
    [JsonIgnore]
    public bool IsRegion => true;

    /// <summary>사람이 보는 메모. 읽는 데는 안 쓴다.</summary>
    [JsonPropertyName("note")]
    public string Note { get; set; } = string.Empty;

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

    /// <summary>읽을 만한 크기인가. 점짜리는 끌다 만 것이다.</summary>
    [JsonIgnore]
    public bool IsUsable => Name.Length > 0 && Width >= 0.004 && Height >= 0.004;

    /// <summary>화면 목록에 한 줄로 보여 줄 글.</summary>
    [JsonIgnore]
    public string Describe => $"{Name}  ({X:0.000}, {Y:0.000})  {Width:0.000} x {Height:0.000}";

    public override string ToString() => Describe;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
