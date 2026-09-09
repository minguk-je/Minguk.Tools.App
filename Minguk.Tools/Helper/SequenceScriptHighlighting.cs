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
    public static IHighlightingDefinition Current => IsDarkTheme() ? Dark : Light;

    public static IHighlightingDefinition Light => _light ??= Load("SequenceScript.Light.xshd");

    public static IHighlightingDefinition Dark => _dark ??= Load("SequenceScript.Dark.xshd");

    /// <summary>편집기 바탕색.</summary>
    /// <remarks>
    /// AvalonEdit 은 순수 WPF 컨트롤이라 DevExpress 경량 테마가 손대지 않는다. 그냥 두면
    /// 어두운 테마에서 이 편집기만 흰 판으로 남는다. 그래서 색을 직접 준다.
    /// </remarks>
    public static Brush Background => Frozen(IsDarkTheme() ? "#1E1E1E" : "#FFFFFF");

    public static Brush Foreground => Frozen(IsDarkTheme() ? "#D4D4D4" : "#1F2328");

    /// <summary>줄 번호. 본문보다 흐려야 글을 읽는 데 방해가 안 된다.</summary>
    public static Brush LineNumberForeground => Frozen(IsDarkTheme() ? "#6E7681" : "#8C959F");

    public static Brush Border => Frozen(IsDarkTheme() ? "#3C3C3C" : "#D0D7DE");

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
