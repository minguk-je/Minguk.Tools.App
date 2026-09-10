namespace Minguk.Tools.Vision.Training;

/// <summary>
/// 학습이 어디까지 왔는지. 글이 아니라 숫자다.
/// </summary>
/// <remarks>
/// 진행 글("Row: 5, Loss: 1.47")만 흘리면 화면은 마지막 한 줄밖에 못 보여 준다. 막대와
/// 꺾은선을 그리려면 바퀴 수와 loss 가 숫자로 와야 한다. 글은 글대로 따로 흘린다.
/// </remarks>
/// <param name="EpochsDone">끝난 바퀴 수. 0 부터 <paramref name="MaxEpoch"/> 까지.</param>
/// <param name="MaxEpoch">돌릴 바퀴 수.</param>
/// <param name="Loss">이번 스텝의 loss. 바퀴 경계 알림에는 없다.</param>
/// <param name="ImagePath">이번 스텝이 본 그림. 바퀴 경계 알림에는 없다.</param>
public readonly record struct TrainingStep(int EpochsDone, int MaxEpoch, double? Loss, string? ImagePath = null)
{
    /// <summary>0~1. 바퀴 안에서는 안 움직인다 - 스텝 수를 미리 모르기 때문이다.</summary>
    public double Fraction => MaxEpoch <= 0 ? 0 : (double)EpochsDone / MaxEpoch;
}
