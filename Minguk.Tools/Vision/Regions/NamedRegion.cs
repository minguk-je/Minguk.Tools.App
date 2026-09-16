using System;
using System.ComponentModel;
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
/// </remarks>
public sealed class NamedRegion : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private bool _ink = true;
    private string _preprocessor = string.Empty;
    private double _shearDegrees;
    private string _language = string.Empty;
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
    /// 읽기 전에 흰 글자만 남길지(<see cref="Ocr.HudInk"/>).
    /// </summary>
    /// <remarks>
    /// 배경이 밝아졌다 어두워졌다 하는 자리(탄약·체력)는 켜야 읽히고, 늘 어두운 자리(궁극기 고리 안)는
    /// 켜면 오히려 못 읽는다. 어느 쪽인지는 해 보기 전에는 모르므로 사람이 켜고 끈다 - 읽는 쪽은 한 길이
    /// 빈 답이면 다른 길로도 한 번 해 보므로, 틀리게 놓아도 대개는 읽힌다.
    /// </remarks>
    [JsonPropertyName("ink")]
    public bool Ink
    {
        get => _ink;
        set => Set(ref _ink, value);
    }

    /// <summary>
    /// 읽기 전 손질 이름(<see cref="Ocr.OcrPreprocessors"/> 의 Id - plain·bright·dark). 비어 있으면 옛 <see cref="Ink"/> 를 따른다.
    /// </summary>
    /// <remarks>
    /// 게임마다 글자가 달라 한 가지 손질로 못 덮는다(실측 2026-09-16: 오버워치 탄약은 "밝은 글자만" 0/12, "그대로 키우기" 10/12).
    /// 자리마다 골라 저장한다 - 화면의 <c>지금 읽기</c> 가 손질을 모두 해 보고 가장 잘 읽은 것을 알려 준다.
    /// </remarks>
    [JsonPropertyName("prep")]
    public string Preprocessor
    {
        get => _preprocessor;
        set => Set(ref _preprocessor, value ?? string.Empty);
    }

    /// <summary>
    /// 이 자리를 읽을 언어 태그("ko"·"en-US"). 비어 있으면 자동 - 숫자는 영문, 안 되면 쓰던 언어로 한 번 더.
    /// </summary>
    /// <remarks>
    /// 같은 숫자라도 언어 팩에 따라 읽히고 안 읽힌다(실측 2026-09-16: 오버워치 영상의 탄약은 ko 가 읽고 en-US 는 빈 글,
    /// 다른 스크린샷은 반대). 「지금 읽기」 가 둘 다 해 보고 잘 읽은 쪽을 여기에 적는다.
    /// </remarks>
    [JsonPropertyName("lang")]
    public string Language
    {
        get => _language;
        set => Set(ref _language, value ?? string.Empty);
    }

    /// <summary>글자가 오른쪽으로 기운 각도(도). 0 이면 손대지 않는다. 이탤릭 HUD 는 10~12도.</summary>
    [JsonPropertyName("shear")]
    public double ShearDegrees
    {
        get => _shearDegrees;
        set => Set(ref _shearDegrees, value);
    }

    /// <summary>
    /// 이 자리를 읽을 때 쓰는 손질과 옵션. 저장된 이름이 없으면 옛 <see cref="Ink"/>(true = 밝은 글자만)를 따른다.
    /// </summary>
    [JsonIgnore]
    public Ocr.IOcrPreprocessor Preprocess
        => Preprocessor.Length > 0
            ? Ocr.OcrPreprocessors.Find(Preprocessor)
            : Ink ? Ocr.OcrPreprocessors.Find("bright") : Ocr.OcrPreprocessors.Default;

    /// <summary>이 자리의 전처리 옵션(기울기). 목표 높이는 공통 기본값.</summary>
    [JsonIgnore]
    public Ocr.OcrPreprocessOptions PreprocessOptions => new(Ocr.OcrPreprocessOptions.DefaultTargetHeight, ShearDegrees);

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

    /// <summary>마지막으로 읽은 글(한 줄로). 저장하지 않는다.</summary>
    [JsonIgnore]
    public string LastText
    {
        get => _lastText;
        set => Set(ref _lastText, value ?? string.Empty);
    }

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

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
