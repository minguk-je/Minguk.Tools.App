using System;
using System.IO;
using System.Reflection;
using System.Windows.Media;
using System.Xml;
using DevExpress.Xpf.Core;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using Microsoft.Win32;

namespace Minguk.Tools.Helper;

/// <summary>
/// 시퀀스 스크립트 편집기의 구문 강조 정의를 골라 준다.
/// </summary>
/// <remarks>
/// 정의는 어셈블리에 박혀 있다(<c>Resource/SequenceScript.*.xshd</c>). 파일로 두면
/// 사용자가 지우거나 못 찾는 자리에 놓일 수 있는데, 없으면 편집기가 아무 색도 못 낸다.
/// </remarks>
public static class SequenceScriptHighlighting
{
    private static IHighlightingDefinition? _light;
    private static IHighlightingDefinition? _dark;

    /// <summary>지금 테마에 맞는 정의. 테마를 바꾸면 다시 물어야 한다.</summary>
    public static IHighlightingDefinition Current => (_sampledIsDark ?? IsDarkTheme()) ? Dark : Light;

    public static IHighlightingDefinition Light => _light ??= Load("SequenceScript.Light.xshd");

    public static IHighlightingDefinition Dark => _dark ??= Load("SequenceScript.Dark.xshd");

    /// <summary>편집기 바탕색.</summary>
    /// <remarks>
    /// AvalonEdit 은 순수 WPF 컨트롤이라 DevExpress 경량 테마가 손대지 않는다. 그냥 두면
    /// 테마를 바꿔도 이 편집기만 그대로 남는다. 그래서 색을 직접 준다.
    /// 테마 컨트롤에서 잰 색이 있으면 그것을 쓰고(<see cref="SampleFrom"/>), 없으면 기본값이다.
    /// </remarks>
    public static Brush Background => _sampledBackground ?? Frozen(IsDarkTheme() ? "#1E1E1E" : "#FFFFFF");

    public static Brush Foreground => _sampledForeground ?? Frozen(IsDarkTheme() ? "#D4D4D4" : "#1F2328");

    /// <summary>줄 번호. 본문보다 흐려야 글을 읽는 데 방해가 안 된다.</summary>
    public static Brush LineNumberForeground => _sampledLineNumber ?? Frozen(IsDarkTheme() ? "#6E7681" : "#8C959F");

    public static Brush Border => _sampledBorder ?? Frozen(IsDarkTheme() ? "#3C3C3C" : "#D0D7DE");

    private static Brush? _sampledBackground;
    private static Brush? _sampledForeground;
    private static Brush? _sampledLineNumber;
    private static Brush? _sampledBorder;
    private static bool? _sampledIsDark;

    /// <summary>
    /// 테마가 입혀진 컨트롤에서 색을 재 온다.
    /// </summary>
    /// <remarks>
    /// <b>왜 테마 키를 짚지 않는가</b>
    ///
    /// 두 번 데었다. 키 이름은 DevExpress 판마다 달라지고(전에 <c>LayoutControlThemeKey</c> 를
    /// 짚었다가 MC3074 로 막혔다), 팔레트로 만든 테마(파란색 같은)는 <b>이름만으로 밝고 어두움을
    /// 알 수 없다.</b>
    ///
    /// 옆에 있는 <c>dxe:TextEdit</c> 은 이미 그 테마가 입혀져 있다. 거기서 실제로 그려진 색을
    /// 읽으면 어떤 테마든 그대로 따라간다 - 짚을 이름도, 갱신할 표도 없다.
    ///
    /// 강조색(낱말·글자·주석)까지 테마에서 뽑을 수는 없다. 테마는 그런 것을 안 들고 있다.
    /// 대신 <b>잰 바탕색의 밝기</b>로 밝은 벌·어두운 벌을 고른다 - 이름으로 가르는 것보다 낫다.
    /// </remarks>
    public static void SampleFrom(Brush? background, Brush? foreground)
    {
        if (background is not SolidColorBrush back || foreground is not SolidColorBrush fore) return;

        var dark = Luminance(back.Color) < 0.5;

        _sampledBackground = Freeze(new SolidColorBrush(back.Color));
        _sampledForeground = Freeze(new SolidColorBrush(fore.Color));

        // 줄 번호는 본문과 바탕 사이 어디쯤. 본문과 같으면 눈이 그리로 끌린다.
        _sampledLineNumber = Freeze(new SolidColorBrush(Blend(fore.Color, back.Color, 0.55)));

        // 테두리는 바탕에서 살짝 벗어난 정도면 된다.
        _sampledBorder = Freeze(new SolidColorBrush(Blend(back.Color, fore.Color, 0.25)));

        _sampledIsDark = dark;
    }

    /// <summary>두 색을 섞는다. <paramref name="ratio"/> 가 1 이면 <paramref name="to"/> 다.</summary>
    private static Color Blend(Color from, Color to, double ratio) => Color.FromRgb(
        (byte)(from.R + (to.R - from.R) * ratio),
        (byte)(from.G + (to.G - from.G) * ratio),
        (byte)(from.B + (to.B - from.B) * ratio));

    /// <summary>사람 눈이 느끼는 밝기. 초록이 가장 밝게 보인다.</summary>
    private static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

    private static Brush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
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

    /// <summary>
    /// 지금 테마가 어두운 쪽인지.
    /// </summary>
    /// <remarks>
    /// DevExpress 의 <see cref="Theme"/> 에는 밝고 어두움을 알려 주는 것이 없어서 이름으로 가른다.
    /// "System" 이 붙은 테마는 이름만으로 알 수 없고 Windows 설정을 따라가므로 레지스트리를 본다
    /// (<c>AppsUseLightTheme</c> - 0 이면 어두운 쪽).
    /// </remarks>
    /// <remarks>
    /// <see cref="SampleFrom"/> 로 실제 색을 재 두었으면 그쪽이 낫다. 이것은 잴 것이 없을 때
    /// 쓰는 어림이다 - 팔레트로 만든 테마는 이름만으로 알 수 없다.
    /// </remarks>
    public static bool IsDarkTheme()
    {
        var name = ApplicationThemeHelper.ApplicationThemeName ?? string.Empty;

        if (name.Contains("Dark", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Black", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!name.Contains("System", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            var value = Registry.GetValue(
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
}
