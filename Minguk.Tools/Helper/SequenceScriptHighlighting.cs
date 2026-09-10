using System;
using System.Reflection;
using System.Windows.Media;
using System.Xml;
using DevExpress.Xpf.Core;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace Minguk.Tools.Helper;

/// <summary>
/// 시퀀스 스크립트 편집기의 색과 구문 강조 정의를 준다.
/// </summary>
/// <remarks>
/// AvalonEdit 은 순수 WPF 컨트롤이라 경량 테마가 손대지 않는다. 그냥 두면 테마를 바꿔도
/// 이 편집기만 그대로 남는다. 그래서 색을 직접 준다.
///
/// <b>색은 테마 팔레트에서 읽는다</b>
///
/// 경량 테마는 <see cref="LightweightThemeManager.CurrentTheme"/> 에 브러시 사전을 들고 있다.
/// 거기서 <c>Brush.Editor.Background</c> · <c>Brush.Foreground</c> · <c>Brush.Border</c> 를
/// 꺼내면 그 테마가 입력 칸에 실제로 쓰는 색이다.
///
/// 앞서 두 가지를 시도했다가 물렀다.
///   테마 <b>이름</b>으로 밝은 벌·어두운 벌을 가르기 - 팔레트로 만든 테마(VS2019Blue 같은)는
///   이름에 Dark 도 Black 도 없어 밝은 쪽으로 잘못 봤다.
///   XAML 에서 테마 <b>키</b>를 짚기 - 키 이름이 DevExpress 판마다 달라진다
///   (<c>LayoutControlThemeKey</c> 를 짚었다가 MC3074 로 막혔다).
///
/// 강조색(낱말·글자·주석)까지 팔레트에서 뽑을 수는 없다. 테마가 그런 것을 안 들고 있다.
/// 대신 <b>팔레트에서 읽은 바탕색의 밝기</b>로 밝은 벌·어두운 벌을 고른다.
///
/// 정의 자체는 어셈블리에 박혀 있다(<c>Resource/SequenceScript.*.xshd</c>). 파일로 두면
/// 사용자가 지우거나 못 찾는 자리에 놓일 수 있는데, 없으면 편집기가 아무 색도 못 낸다.
/// </remarks>
public static class SequenceScriptHighlighting
{
    private static IHighlightingDefinition? _light;
    private static IHighlightingDefinition? _dark;

    /// <summary>지금 테마에 맞는 정의.</summary>
    public static IHighlightingDefinition Current => IsDarkTheme() ? Dark : Light;

    public static IHighlightingDefinition Light => _light ??= Load("SequenceScript.Light.xshd");

    public static IHighlightingDefinition Dark => _dark ??= Load("SequenceScript.Dark.xshd");

    /// <summary>편집기 바탕. 그 테마가 입력 칸에 쓰는 색이다.</summary>
    public static Brush Background => Palette("Brush.Editor.Background") ?? Fallback("#1E1E1E", "#FFFFFF");

    public static Brush Foreground => Palette("Brush.Foreground") ?? Fallback("#D4D4D4", "#1F2328");

    public static Brush Border => Palette("Brush.Border") ?? Fallback("#3C3C3C", "#D0D7DE");

    /// <summary>
    /// 줄 번호. 팔레트에 따로 없어서 본문과 바탕을 섞어 만든다.
    /// </summary>
    /// <remarks>
    /// 본문과 같은 색이면 눈이 그리로 끌리고, 바탕에 너무 가까우면 안 보인다.
    /// 절반 조금 넘게 바탕 쪽으로 당긴다.
    /// </remarks>
    public static Brush LineNumberForeground
    {
        get
        {
            if (Foreground is SolidColorBrush fore && Background is SolidColorBrush back)
                return Frozen(Blend(fore.Color, back.Color, 0.55));

            return Fallback("#6E7681", "#8C959F");
        }
    }

    /// <summary>
    /// 지금 테마가 어두운 쪽인지.
    /// </summary>
    /// <remarks>
    /// 팔레트에서 읽은 바탕색의 밝기로 가른다. 팔레트를 못 읽는 상황에서만 이름으로 어림한다.
    /// </remarks>
    public static bool IsDarkTheme()
    {
        if (Palette("Brush.Editor.Background") is SolidColorBrush back)
            return Luminance(back.Color) < 0.5;

        return GuessDarkByName();
    }

    /// <summary>
    /// 경량 테마 팔레트에서 브러시를 꺼낸다. 없으면 null.
    /// </summary>
    /// <remarks>
    /// 팔레트에 없는 키를 물어보거나, 아직 테마가 안 정해졌을 수 있다.
    /// 색 하나 못 읽었다고 화면이 안 떠서는 안 되므로 조용히 null 로 돌아간다.
    /// </remarks>
    private static Brush? Palette(string key)
    {
        try
        {
            var palette = LightweightThemeManager.CurrentTheme?.Palette;

            if (palette is not null && palette.Contains(key) && palette[key] is Brush brush) return brush;
        }
        catch (Exception)
        {
            // 테마가 준비되기 전에 물어보면 터질 수 있다. 그때는 기본값으로 간다.
        }

        return null;
    }

    private static Brush Fallback(string dark, string light) => Frozen(GuessDarkByName() ? dark : light);

    /// <remarks>
    /// 팔레트를 못 읽을 때만 쓰는 어림이다. <c>*System</c> 테마는 이름만으로 알 수 없어
    /// Windows 설정(<c>AppsUseLightTheme</c>)을 본다.
    /// </remarks>
    private static bool GuessDarkByName()
    {
        var name = ApplicationThemeHelper.ApplicationThemeName ?? string.Empty;

        if (name.Contains("Dark", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Black", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!name.Contains("System", StringComparison.OrdinalIgnoreCase)) return false;

        try
        {
            var value = Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1);

            return value is int light && light == 0;
        }
        catch (Exception)
        {
            // 못 읽으면 밝은 쪽으로 둔다. 밝은 배경에 어두운 글씨가 그 반대보다 덜 나쁘다.
            return false;
        }
    }

    /// <summary>두 색을 섞는다. <paramref name="ratio"/> 가 1 이면 <paramref name="to"/> 다.</summary>
    private static Color Blend(Color from, Color to, double ratio) => Color.FromRgb(
        (byte)(from.R + (to.R - from.R) * ratio),
        (byte)(from.G + (to.G - from.G) * ratio),
        (byte)(from.B + (to.B - from.B) * ratio));

    /// <summary>사람 눈이 느끼는 밝기. 초록이 가장 밝게 보인다.</summary>
    private static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

    private static Brush Frozen(string hex) => Frozen((Color)ColorConverter.ConvertFromString(hex)!);

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static IHighlightingDefinition Load(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();

        // 리소스 이름은 "<기본네임스페이스>.<폴더>.<파일>" 이다. 폴더 이름을 바꾸면 여기가 깨지므로
        // 통째로 적지 않고 끝만 맞춰 찾는다.
        var name = Array.Find(assembly.GetManifestResourceNames(), n => n.EndsWith(fileName, StringComparison.Ordinal))
                   ?? throw new InvalidOperationException($"구문 강조 정의를 어셈블리에서 찾지 못했다: {fileName}");

        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new XmlTextReader(stream);

        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }
}
