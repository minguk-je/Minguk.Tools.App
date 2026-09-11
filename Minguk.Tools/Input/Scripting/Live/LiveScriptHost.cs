using System;

using Minguk.Tools.Capture;
using Minguk.Tools.Vision.Ocr;
using Minguk.Tools.Vision.Perception;

namespace Minguk.Tools.Input.Scripting.Live;

/// <summary>
/// 실시간 스크립트가 바깥에서 빌려 쓰는 것들 - 보낼 경로, 대상 창, 눈(인식 허브), 글자 읽기, 출력 칸.
/// </summary>
/// <remarks>
/// API 는 화면을 모른다. 화면(스크립트·플레이)이 제 것을 이렇게 빌려 준다. 검증 하네스는 가짜 어댑터와
/// 가짜 허브를 꽂아 화면 없이 스크립트를 돌린다.
/// </remarks>
public sealed class LiveScriptHost
{
    public required InputService Service { get; init; }

    /// <summary>이 경로는 대상 창이 앞에 있어야 들어가는가(SendInput·Interception). 창 메시지 경로는 아니다.</summary>
    public required bool RequiresForeground { get; init; }

    /// <summary>잡고 있는 대상. 몹 자리를 화면 픽셀로 바꾸고, 앞에 있는지 볼 때 쓴다.</summary>
    public required Func<CaptureTarget?> Target { get; init; }

    public required IPerceptionHub Hub { get; init; }

    /// <summary>글자 읽기 엔진. 없으면 <c>읽기()</c> 가 그렇게 말한다.</summary>
    public Func<IOcrEngine?>? Ocr { get; init; }

    public required Action<string> Print { get; init; }

    public required Action<string, string> Watch { get; init; }

    /// <summary>API 를 한 번 부를 때마다. 호출 로그 칸이 받는다. 없으면 안 남긴다.</summary>
    public Action<ScriptCall>? Trace { get; init; }

    /// <summary>키·버튼을 누르고 있는 시간.</summary>
    public int HoldTimeMs { get; init; } = 30;

    /// <summary>
    /// 조준 배율. 화면 가운데에서 목표까지의 거리(픽셀)에 이것을 곱해 마우스를 움직인다.
    /// </summary>
    /// <remarks>
    /// 게임은 마우스가 움직인 양을 제 감도로 시야 회전에 바꾼다. 픽셀 하나를 옮기는 데 몇 카운트가 드는지는
    /// 게임·감도·해상도마다 달라 사람이 맞춘다. 1.0 에서 시작해, 조준이 모자라면 올리고 넘치면 내린다.
    /// </remarks>
    public double AimScale { get; init; } = 1.0;

    /// <summary>초당 입력 호출 상한. 끝나지 않는 반복문이 입력을 쏟아붓지 않게.</summary>
    public int MaxInputsPerSecond { get; init; } = 30;
}
