using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Minguk.Tools.Input.Korean;

namespace Minguk.Tools.Input.Sequencing;

/// <summary>
/// 무엇을 어떤 순서로 보낼지 정해 <see cref="InputStep"/> 목록을 만든다.
///
/// 왜 목록을 미리 만드는가
///   무엇을 보낼지가 실행 시점에 정해지면, 도는 도중에 구성이 바뀌어 중간에 이상한 것이 나간다.
///   시작할 때 한 번 굳혀 두면 그 뒤로는 목록만 돌면 된다.
///   화면에 "이런 순서로 나갑니다" 를 미리 보여 줄 수도 있다.
/// </summary>
public sealed class InputSequence
{
    private readonly InputService _service;
    private readonly List<InputStep> _steps = [];

    /// <param name="service">단계들이 쓸 입력 서비스.</param>
    /// <param name="holdTimeMs">키·버튼을 누르고 있는 시간.</param>
    public InputSequence(InputService service, int holdTimeMs = 50)
    {
        _service = service;
        HoldTimeMs = holdTimeMs;
    }

    public int HoldTimeMs { get; }

    /// <summary>지금까지 담긴 단계들. 이 목록을 <see cref="SequenceRunner"/> 에 넘긴다.</summary>
    public IReadOnlyList<InputStep> Steps => _steps;

    /// <summary>사람이 읽을 순서. "Enter → Q → W → 좌클릭" 처럼 나온다.</summary>
    public string Describe() => _steps.Count == 0 ? "(비어 있음)" : string.Join(" → ", GetSymbols());

    private IEnumerable<string> GetSymbols()
    {
        foreach (var step in _steps) yield return step.Symbol;
    }

    /// <summary>
    /// 글자들을 하나씩 누른다. 다룰 수 없는 문자는 조용히 건너뛴다.
    /// </summary>
    /// <remarks>
    /// 한글은 두벌식 자리로 풀리고, 필요하면 한/영 을 먼저 눌러 IME 를 맞춘다.
    /// 스캔코드를 못 넣는 경로에서는 아무 단계도 담기지 않는다.
    /// </remarks>
    public InputSequence Type(string text, HangulKeyMode hangulMode = HangulKeyMode.HangulScanCode)
    {
        foreach (var ch in text)
        {
            var c = ch;   // 클로저가 반복 변수를 잡지 않도록 복사한다

            if (_service.SupportsTyping)
            {
                if (!CanType(c)) continue;

                _steps.Add(InputStep.Of(Display(c), () => _service.TypeCharAsync(c, HoldTimeMs, hangulMode)));
                continue;
            }

            // 스캔코드를 못 넣는 경로라도 영문·숫자·문장부호는 가상 키로 들어간다.
            // 한글만 못 한다 - 대상 IME 가 부친 키로는 한/영 전환을 받지 않는다.
            if (!_service.CanTypeWithoutScanCode(c)) continue;

            _steps.Add(InputStep.Of(Display(c), () => _service.TypeCharByVirtualKeyAsync(c, HoldTimeMs)));
        }

        return this;
    }

    /// <summary>이 문자를 스캔코드로 보낼 수 있는지. 한글·영숫자·공백·문장부호를 다룬다.</summary>
    public static bool CanType(char c) => HangulKeyMap.IsHangul(c) || ScanCodes.TryGetKeyStroke(c, out _, out _);

    /// <summary>이 문자를 가상 키로 보낼 수 있는지. 한글은 못 한다.</summary>
    public static bool CanTypeByVirtualKey(char c) => !HangulKeyMap.IsHangul(c) && VirtualKeys.CanType(c);

    /// <summary>순서 문구에 보여 줄 글자. 공백은 눈에 보이게 바꾼다.</summary>
    private static string Display(char c) => c == ' ' ? "␣" : c.ToString();

    /// <summary>
    /// Enter 한 번. 스캔코드를 못 넣는 경로에서는 가상 키로 보낸다.
    /// </summary>
    /// <remarks>
    /// 한때 스캔코드가 안 되면 이 단계를 통째로 버렸는데, PostMessage 도 <c>PressKey</c> 는
    /// 할 수 있으므로 보낼 수 있는 것을 버리고 있었던 것이다.
    /// </remarks>
    public InputSequence Enter()
    {
        _steps.Add(_service.SupportsTyping
            ? InputStep.Of("Enter", () => _service.TapKeyAsync(ScanCodes.Enter, HoldTimeMs))
            : InputStep.Of("Enter", () => _service.TapVirtualKeyAsync(VirtualKeys.Enter, HoldTimeMs)));

        return this;
    }

    /// <summary>한/영 을 한 번 누른다.</summary>
    public InputSequence ToggleHangul(HangulKeyMode mode = HangulKeyMode.HangulScanCode)
    {
        if (_service.SupportsTyping)
            _steps.Add(InputStep.Of("한/영", () => _service.ToggleHangulAsync(HoldTimeMs, mode)));

        return this;
    }

    public InputSequence Click(MouseButton button = MouseButton.Left)
    {
        _steps.Add(InputStep.Of(ButtonName(button), () => _service.ClickAsync(button, HoldTimeMs)));
        return this;
    }

    private static string ButtonName(MouseButton button) => button switch
    {
        MouseButton.Right => "우클릭",
        MouseButton.Middle => "휠클릭",
        _ => "좌클릭"
    };

    /// <summary>정해진 좌표로 옮긴다.</summary>
    public InputSequence MoveTo(int x, int y)
    {
        _steps.Add(InputStep.Of($"이동({x},{y})", () => _service.MoveSmoothAsync(x, y)));
        return this;
    }

    /// <summary>
    /// 여러 좌표를 한 단계씩 돌아가며 짚는다. 한 바퀴에 한 곳씩 나아간다.
    /// </summary>
    /// <remarks>
    /// 좌표를 전부 따로 담지 않고 한 단계로 두는 이유는, 반복 시퀀스에서 "한 바퀴에 한 곳"
    /// 이 자연스럽기 때문이다. 네 곳을 담으면 한 바퀴에 네 곳을 다 도는 것과 다르다.
    /// </remarks>
    public InputSequence MoveAround(IReadOnlyList<(int X, int Y)> points)
    {
        if (points.Count == 0) return this;

        var index = 0;

        _steps.Add(new InputStep("이동", async (progress, _) =>
        {
            var (x, y) = points[index];
            index = (index + 1) % points.Count;

            progress?.Report($"이동({x},{y})");
            await _service.MoveSmoothAsync(x, y);
        }));

        return this;
    }

    /// <param name="notches">굴릴 칸 수. 양수가 위, 음수가 아래.</param>
    public InputSequence Scroll(int notches)
    {
        // 휠은 누름·뗌이 없는 단발이라, 다른 단계와 리듬을 맞추려고 보낸 뒤 유지 시간만큼 쉰다.
        _steps.Add(InputStep.Of(notches < 0 ? "휠 아래" : "휠 위", async () =>
        {
            _service.Scroll(notches);
            await Task.Delay(_service.Jitter(HoldTimeMs));
        }));

        return this;
    }

    /// <summary>
    /// 아무것도 보내지 않고 쉰다.
    /// </summary>
    /// <remarks>
    /// 단계 간격과는 다르다. 간격은 모든 단계 사이에 똑같이 들어가지만, 이것은 한 자리에만 넣는다.
    /// 대상 창이 반응할 틈(메뉴가 열리고 나서 고르기 같은 것)을 줘야 할 때 쓴다.
    /// </remarks>
    public InputSequence Wait(int milliseconds)
    {
        var ms = Math.Max(0, milliseconds);
        _steps.Add(InputStep.Of($"{ms}ms 쉬기", () => Task.Delay(_service.Jitter(ms))));
        return this;
    }

    /// <summary>직접 만든 단계를 담는다.</summary>
    public InputSequence Add(InputStep step)
    {
        _steps.Add(step);
        return this;
    }
}
