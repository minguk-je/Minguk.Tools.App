using System;

using Vortice.Direct3D;
using Vortice.Direct3D11;

using Minguk.Tools.Inference;

namespace Minguk.Tools.Tests;

/// <summary>
/// GPU 전처리기를 언제 버려야 하는가(<see cref="FramePreprocessor.Matches"/>).
/// </summary>
/// <remarks>
/// <b>왜 있나</b> - 크기만 보고 판단했더니, 캡처 대상을 모니터에서 게임 창으로 바꾸는 순간 세션이 새 D3D 장치로
/// 다시 만들어지는데도 옛 전처리기를 그대로 써서 매 프레임 NullReferenceException 이 났다(실측 2026-09-13).
/// 화면에는 아무 말도 안 뜨고 몹 찾기만 조용히 멈춰, 사람은 모델을 의심하게 된다("다시 학습해야 하나?").
/// 장치를 진짜로 둘 만들어 견준다 - 포인터 비교라 흉내로는 뜻이 없다.
/// </remarks>
internal static partial class Program
{
    private static void TestPreprocessorReuse()
    {
        var spec = new TensorSpec { Width = 640, Height = 640, Letterbox = false };

        if (!TryCreateDevice(out var first, out var firstContext) || !TryCreateDevice(out var second, out var secondContext))
        {
            Check("전처리기 다시 쓰기 판단", true, "D3D11 장치를 못 만들어 건너뛴다");
            return;
        }

        using (first)
        using (firstContext)
        using (second)
        using (secondContext)
        {
            using var preprocessor = new FramePreprocessor(first!, firstContext!, spec, pipelined: false);

            var sameDevice = preprocessor.Matches(first!, spec);
            var otherDevice = preprocessor.Matches(second!, spec);

            var otherSize = preprocessor.Matches(first!, new TensorSpec { Width = 640, Height = 360, Letterbox = false });
            var otherFit = preprocessor.Matches(first!, new TensorSpec { Width = 640, Height = 640, Letterbox = true });

            Check("장치가 바뀌면 전처리기를 버린다 (크기·방식도 같이 본다)",
                  sameDevice && !otherDevice && !otherSize && !otherFit,
                  $"같은 장치 {sameDevice}, 다른 장치 {otherDevice}, 다른 크기 {otherSize}, 다른 방식 {otherFit}");
        }
    }

    /// <summary>하드웨어가 없으면(원격·가상 머신) WARP 로라도 만든다. 그래도 안 되면 건너뛴다.</summary>
    private static bool TryCreateDevice(out ID3D11Device? device, out ID3D11DeviceContext? context)
    {
        foreach (var driver in new[] { DriverType.Hardware, DriverType.Warp })
        {
            try
            {
                if (D3D11.D3D11CreateDevice(IntPtr.Zero, driver, DeviceCreationFlags.BgraSupport,
                                            [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0],
                                            out device, out context).Success)
                    return true;
            }
            catch (Exception) { }
        }

        device = null;
        context = null;

        return false;
    }
}
