using System;
using System.Threading;

namespace Minguk.Tools.Vision.Training;

/// <summary>
/// 앱 안에서 학습이 도는 중인지. 몹 찾기를 켠 화면들이 이것을 보고 학습하는 동안 GPU 모델을 내려놓는다.
/// </summary>
/// <remarks>
/// <b>왜 있나</b>(실측 2026-09-14) - GTX 1060 3GB 한 장에서 스크립트 화면의 캡처(30fps)·몹 찾기(DirectML, 0.1초마다)와
/// YOLO 학습(CUDA)이 같이 돌다가 드라이버가 GPU 를 리셋했다(`DXGI_ERROR_DEVICE_REMOVED` → 학습 `cudaErrorUnknown`).
/// 리셋 뒤에는 그 프로세스의 DirectML 이 계속 실패해(`887A0007`) 앱을 다시 켜야 했다.
/// 화면끼리 서로를 모르므로 정적 신호 하나로 알린다. 학습이 겹쳐 돌 수도 있어 횟수로 센다.
/// </remarks>
public static class TrainingActivity
{
    private static int _count;

    /// <summary>학습이 하나라도 도는 중인가.</summary>
    public static bool IsBusy => Volatile.Read(ref _count) > 0;

    /// <summary>시작·끝. 부르는 스레드에서 온다 - 받는 화면이 UI 스레드로 넘긴다. 정적 이벤트라 받는 쪽이 반드시 푼다.</summary>
    public static event EventHandler? Changed;

    /// <summary>학습을 시작한다고 알린다. 돌려받은 것을 Dispose 하면 끝났다고 알린다(실패·멈춤 포함 - finally 에서).</summary>
    public static IDisposable Begin()
    {
        Interlocked.Increment(ref _count);
        Changed?.Invoke(null, EventArgs.Empty);

        return new Scope();
    }

    private sealed class Scope : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            Interlocked.Decrement(ref _count);
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>
    /// GPU 가 리셋·제거된 오류인가(`DXGI_ERROR_DEVICE_REMOVED` 887A0005 · `DEVICE_RESET` 887A0007 · `DEVICE_HUNG` 887A0006, CUDA 의 알 수 없는 오류).
    /// 이렇게 되면 이 프로세스의 GPU 연결이 다시 살아나지 않아 앱을 다시 켜야 한다.
    /// </summary>
    public static bool IsGpuLost(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
        {
            var text = ex.Message ?? string.Empty;

            if (text.Contains("887A0005", StringComparison.OrdinalIgnoreCase)
                || text.Contains("887A0006", StringComparison.OrdinalIgnoreCase)
                || text.Contains("887A0007", StringComparison.OrdinalIgnoreCase)
                || text.Contains("DEVICE_REMOVED", StringComparison.OrdinalIgnoreCase)
                || text.Contains("DEVICE_RESET", StringComparison.OrdinalIgnoreCase)
                || text.Contains("cudaErrorUnknown", StringComparison.OrdinalIgnoreCase)
                || ex.HResult is unchecked((int)0x887A0005) or unchecked((int)0x887A0006) or unchecked((int)0x887A0007))
                return true;
        }

        return false;
    }

    /// <summary>GPU 가 리셋됐을 때 사람에게 보일 한 줄.</summary>
    public const string GpuLostMessage =
        "GPU 가 리셋됐습니다(드라이버가 장치를 다시 시작함). 이 앱의 GPU 연결은 되살아나지 않으니 앱을 다시 켜세요. " +
        "학습할 때 스크립트·플레이 화면의 캡처·몹 찾기를 같은 카드에서 같이 돌리면 3GB 카드에서 이렇게 됩니다.";
}
