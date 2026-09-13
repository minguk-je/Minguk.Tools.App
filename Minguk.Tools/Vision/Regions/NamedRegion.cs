using System;
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
/// </remarks>
public sealed class NamedRegion
{
    /// <summary>스크립트가 부를 이름. 빈 이름은 못 만든다.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("width")]
    public double Width { get; set; }

    [JsonPropertyName("height")]
    public double Height { get; set; }

    /// <summary>
    /// 읽기 전에 흰 글자만 남길지(<see cref="Ocr.HudInk"/>).
    /// </summary>
    /// <remarks>
    /// 배경이 밝아졌다 어두워졌다 하는 자리(탄약·체력)는 켜야 읽히고, 늘 어두운 자리(궁극기 고리 안)는
    /// 켜면 오히려 못 읽는다. 어느 쪽인지는 해 보기 전에는 모르므로 사람이 켜고 끈다 - 읽는 쪽은 한 길이
    /// 빈 답이면 다른 길로도 한 번 해 보므로, 틀리게 놓아도 대개는 읽힌다.
    /// </remarks>
    [JsonPropertyName("ink")]
    public bool Ink { get; set; } = true;

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
    public string Describe => $"{Name}  ({X:0.000}, {Y:0.000})  {Width:0.000} x {Height:0.000}{(Ink ? "  · 흰 글자만" : string.Empty)}";

    public override string ToString() => Describe;
}
