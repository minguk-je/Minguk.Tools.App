using System.Collections;

using DevExpress.Xpf.Grid;

using Minguk.Tools.Vision.Regions;

namespace Minguk.Tools.Markup.Regions;

/// <summary>
/// 영역 패널 트리의 자식 고르기 - 자리 줄 밑에 그 칸들, 칸 줄은 자식이 없다.
/// </summary>
/// <remarks>
/// 자리와 칸은 형식이 달라 <c>ChildNodesPath</c> 하나로는 못 묶는다(칸에는 칸 목록이 없다). 그래서 <c>TreeDerivationMode="ChildNodesSelector"</c>.
/// 돌려주는 것이 <see cref="System.Collections.ObjectModel.ObservableCollection{T}"/> 라 칸을 더하고 지우면 트리가 따라간다.
/// </remarks>
public sealed class RegionTreeChildren : IChildNodesSelector
{
    public IEnumerable? SelectChildren(object item) => item is NamedRegion region ? region.Cells : null;
}
