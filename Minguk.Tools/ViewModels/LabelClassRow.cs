using System.Windows.Media;

using DevExpress.Mvvm;

namespace Minguk.Tools.ViewModels;

/// <summary>
/// 몹 목록(그리드)의 한 줄. 번호·이름·색.
/// </summary>
/// <remarks>
/// <b>왜 문자열 목록을 그대로 안 쓰는가</b>
///
/// 이름과 색은 그리드 안에서 고친다(2026-09-14). 문자열은 값이라 칸에서 고친 것을 되돌려 받을 자리가 없다.
/// 번호(<see cref="Index"/>)는 화면에 안 보이지만 라벨의 몹 번호 그것이라 줄이 들고 있어야 한다.
///
/// <see cref="Name"/>·<see cref="Color"/> 가 바뀌면 화면(ViewModel)이 <c>PropertyChanged</c> 로 받아 파일에 쓴다.
/// 번호는 안 바뀐다 - 라벨에는 이름이 아니라 번호가 들어 있어서다.
/// </remarks>
public sealed class LabelClassRow : BindableBase
{
    public LabelClassRow(int index, string name, Color color)
    {
        Index = index;
        Name = name;
        Color = color;
    }

    /// <summary>몹 번호. 라벨 파일에 들어가는 그 번호다. 화면에는 안 보인다.</summary>
    public int Index { get; }

    /// <summary>몹 이름. 그리드 칸에서 고쳐 쓴다(더블 클릭).</summary>
    public string Name
    {
        get => GetValue<string>();
        set => SetValue(value);
    }

    /// <summary>사각형 색. 처음에는 번호에서 만든 기본 색이고, 그리드 칸에서 고른다.</summary>
    public Color Color
    {
        get => GetValue<Color>();
        set => SetValue(value);
    }
}
