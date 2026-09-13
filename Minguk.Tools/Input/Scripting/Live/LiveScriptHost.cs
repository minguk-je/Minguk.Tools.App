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

    /// <summary>
    /// 조준이 스스로 맞춘 배율을 알려 받는 곳. null 이면 배율을 배우지 않는다(주어진 <see cref="AimScale"/> 그대로).
    /// </summary>
    /// <remarks>
    /// 화면은 이것을 받아 "조준 배율(%)" 칸을 고치고 저장한다 - 다음 실행은 맞춘 배율로 시작한다.
    /// </remarks>
    public Action<double>? AimScaleLearned { get; init; }

    /// <summary>
    /// 지금 배율을 스스로 맞추는가. <b>부를 때마다 묻는다</b> - 돌리는 중에 켜고 끄는 것이 바로 먹어야 한다.
    /// </summary>
    /// <remarks>
    /// 시작할 때 한 번만 읽었더니, 끄고 시작한 사람이 도중에 켜도 아무 일도 안 일어났다. 게다가 꺼져 있다는
    /// 말을 어디서도 안 해 줘서 "왜 안 배우지" 로 1,003번 조준을 돌리게 됐다(실측).
    /// 안 주면 배우는 것으로 본다 - 하네스가 이 칸을 모르기 때문이다.
    /// </remarks>
    public Func<bool>? IsAimScaleAuto { get; init; }

    /// <summary>화면 가운데에서 이 거리(px) 안이면 "맞았다" 로 본다.</summary>
    public int AimTolerancePx { get; init; } = 8;

    /// <summary>초당 입력 호출 상한. 끝나지 않는 반복문이 입력을 쏟아붓지 않게.</summary>
    public int MaxInputsPerSecond { get; init; } = 30;
}
