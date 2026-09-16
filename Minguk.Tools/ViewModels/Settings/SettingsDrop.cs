using Minguk.Tools.Projects.Settings;

namespace Minguk.Tools.ViewModels.Settings;

/// <summary>놓는 자리가 기준 칸의 어디인가.</summary>
public enum SettingsDropPlacement
{
    /// <summary>판 맨 끝(기준 칸 없음).</summary>
    End,

    Before,

    After,

    /// <summary>구역 안 맨 끝.</summary>
    Inside
}

/// <summary>도구 상자에서 놓은 것 - 칸 종류와 자리.</summary>
public sealed record SettingsDrop(SettingsItemKind Kind, SettingsItem? Anchor, SettingsDropPlacement Placement);
