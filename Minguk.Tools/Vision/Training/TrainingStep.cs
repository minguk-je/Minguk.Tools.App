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
/// <param name="Loss">이번 스텝의 loss. 바퀴 경계 알림·묶음 알림에는 없다(꺾은선은 바퀴마다 한 점).</param>
/// <param name="ImagePath">이번 스텝이 본 그림. 바퀴 경계 알림에는 없다.</param>
/// <param name="Batch">
/// 지금 바퀴(<paramref name="EpochsDone"/> + 1번째)에서 끝난 묶음 수. 묶음 알림일 때만 0 보다 크다 - YOLO 가 묶음마다 알린다(2026-09-15).
/// </param>
/// <param name="BatchCount">한 바퀴의 묶음 수. 묶음 알림일 때만.</param>
public readonly record struct TrainingStep(int EpochsDone, int MaxEpoch, double? Loss, string? ImagePath = null, int Batch = 0, int BatchCount = 0)
{
    /// <summary>묶음 알림인가(바퀴 안에서 온 것).</summary>
    public bool IsBatch => Batch > 0 && BatchCount > 0;

    /// <summary>0~1. 묶음 알림이면 바퀴 안에서도 움직인다.</summary>
    public double Fraction => MaxEpoch <= 0
        ? 0
        : IsBatch
            ? System.Math.Min(1, (EpochsDone + ((double)Batch / BatchCount)) / MaxEpoch)
            : (double)EpochsDone / MaxEpoch;
}
