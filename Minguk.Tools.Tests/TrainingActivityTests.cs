using System;

using Minguk.Tools.Vision.Training;

namespace Minguk.Tools.Tests;

/// <summary>
/// 학습 중 신호(<see cref="TrainingActivity"/>) - 시작·끝을 알리고 겹쳐도 세며, GPU 리셋 오류를 알아보는가.
/// </summary>
/// <remarks>
/// 2026-09-14 로그: 학습(CUDA)과 검출(DirectML)가 같은 3GB 카드에서 겹쳐 `DXGI_ERROR_DEVICE_REMOVED` → 학습 `cudaErrorUnknown` →
/// 재현율 `887A0007`. 신호가 끝을 못 알리면 검출이 영영 멈추고, 리셋을 못 알아보면 사람은 영문 모를 예외만 본다.
/// </remarks>
internal static partial class Program
{
    private static void TestTrainingActivity()
    {
        var changes = 0;
        EventHandler handler = (_, _) => changes++;

        TrainingActivity.Changed += handler;

        try
        {
            var before = TrainingActivity.IsBusy;
            var outer = TrainingActivity.Begin();
            var inner = TrainingActivity.Begin();
            var busyBoth = TrainingActivity.IsBusy;

            inner.Dispose();
            inner.Dispose();   // 두 번 풀어도 한 번만 센다
            var busyOne = TrainingActivity.IsBusy;

            outer.Dispose();
            var idle = !TrainingActivity.IsBusy;

            Check("학습 중 신호: 겹쳐도 세고, 다 끝나야 멈춤이 풀리며, 두 번 풀어도 한 번만 센다",
                  !before && busyBoth && busyOne && idle && changes == 4,
                  $"전 {before} · 둘 {busyBoth} · 하나 {busyOne} · 끝 {idle} · 알림 {changes}번");
        }
        finally
        {
            TrainingActivity.Changed -= handler;
        }

        var lost = TrainingActivity.IsGpuLost(new InvalidOperationException("wrap", new Exception("HRESULT: [0x887A0005], ApiCode: [DXGI_ERROR_DEVICE_REMOVED/DeviceRemoved]")))
                   && TrainingActivity.IsGpuLost(new Exception("Exception(1) tid(ae8) 887A0007 ..."))
                   && TrainingActivity.IsGpuLost(new InvalidOperationException("YOLO 학습이 실패했습니다(종료 1). Search for `cudaErrorUnknown' in ..."));
        var notLost = !TrainingActivity.IsGpuLost(new InvalidOperationException("YOLO 학습이 실패했습니다(종료 1). ModuleNotFoundError: ultralytics"));

        Check("GPU 리셋 오류(887A0005·887A0007·cudaErrorUnknown)를 알아보고, 다른 실패는 아니라고 한다", lost && notLost, $"리셋 {lost} · 다른 실패 {notLost}");
    }
}
