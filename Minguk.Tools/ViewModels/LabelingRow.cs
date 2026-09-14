using DevExpress.Mvvm;

using Minguk.Tools.Vision.Labeling;

namespace Minguk.Tools.ViewModels;

/// <summary>
/// 그림 목록의 한 줄.
/// </summary>
/// <remarks>
/// <b>왜 <see cref="LabelItem"/> 을 그대로 안 쓰는가</b>
///
/// <see cref="LabelItem"/> 은 값이고 <c>HasLabel</c> 은 그때그때 <c>File.Exists</c> 를 본다.
/// 목록에 그대로 얹으면 화면이 한 번 읽고 그만이라, <b>방금 저장했는데도 표시가 안 켜진다</b>.
/// 실제로 그랬다 - 사각형을 찍고 다음 장으로 넘겨도 앞 줄의 ● 이 그대로 꺼져 있었다.
///
/// 줄마다 컬렉션을 갈아 끼워도 다시 그려지긴 하지만, 그러면 고른 줄을 갈아 끼울 때 선택이
/// 풀리면서 <c>SelectedItem</c> 이 null 로 떨어지고, 그 순간 찍던 사각형이 지워진다.
/// 알림을 줄 자신을 두는 편이 안전하다.
///
/// 세는 일도 여기서 끝난다 - 진행 표시가 목록 길이만큼 디스크를 다시 보지 않아도 된다.
/// </remarks>
public sealed class LabelingRow : BindableBase
{
    public LabelingRow(LabelItem item)
    {
        Item = item;
        HasLabel = item.HasLabel;
    }

    public LabelItem Item { get; }

    public string ImagePath => Item.ImagePath;

    public string LabelPath => Item.LabelPath;

    public string Name => Item.Name;

    /// <summary>이 그림에 사각형이 하나라도 저장돼 있는지.</summary>
    public bool HasLabel
    {
        get => GetValue<bool>();
        set => SetValue(value);
    }

    /// <summary>
    /// 학습 뒤 모델이 이 그림의 라벨을 몇 개 다시 찾았나. "2/2", "1/2 · 헛것 3".
    /// </summary>
    /// <remarks>
    /// loss 는 오차라 낮을수록 좋고 인식률은 높을수록 좋다. 둘을 나란히 두면 loss 를 인식률로
    /// 읽는 일이 생겨("인식률 맞지?") 이쪽은 개수로 적는다. 학습에 쓴 그림이라 외운 것도
    /// 맞은 것으로 센다 - 여기서 못 찾으면 확실히 문제고, 다 찾았다고 새 장면에서도 찾는다는
    /// 뜻은 아니다.
    /// </remarks>
    public string? Recognition
    {
        get => GetValue<string?>();
        set => SetValue(value);
    }

    /// <summary>다 못 찾았거나 헛것이 있으면 참. 목록에서 색으로 가른다.</summary>
    public bool RecognitionIsPoor
    {
        get => GetValue<bool>();
        set => SetValue(value);
    }
}
