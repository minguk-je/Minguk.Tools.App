using System;
using System.Threading.Tasks;
using Minguk.Tools.Input.Korean;

namespace Minguk.Tools.Input;

/// <summary>
/// 어댑터 위에서 "사람이 치는 것처럼" 입력을 만든다.
///
/// 어댑터와 무엇이 다른가
///   어댑터는 스트로크 하나를 보내는 것까지만 안다. 여기는 그 위의 이야기다 -
///   누르고 얼마나 있다 떼는지, 한글을 어느 자리로 찍는지, 목적지에 정말 도착했는지.
///   경로(SendInput·Interception)가 바뀌어도 이 로직은 그대로다.
///
/// 스캔코드가 필요한 것들
///   글자 입력과 한/영 전환은 <see cref="IScanCodeInput"/> 이 있어야 한다.
///   PostMessage 경로처럼 없는 어댑터를 받으면 <see cref="SupportsTyping"/> 가 false 이고,
///   해당 메서드는 아무것도 하지 않고 false 를 돌려준다.
/// </summary>
public sealed class InputService
{
    private readonly IInputAdapter _adapter;
    private readonly IScanCodeInput? _scanCodes;

    public InputService(IInputAdapter adapter)
    {
        _adapter = adapter;
        _scanCodes = adapter as IScanCodeInput;
    }

    /// <summary>글자 입력과 한/영 전환을 할 수 있는지. 스캔코드를 못 넣는 경로면 false.</summary>
    public bool SupportsTyping => _scanCodes is not null;

    /// <summary>
    /// 한/영 전환을 할 수 있는지.
    /// </summary>
    /// <remarks>
    /// 글자를 넣는 것과 다르다. <see cref="ICharacterInput"/> 는 완성된 음절을 그대로 주므로
    /// IME 를 건드리지 않고도 한글이 들어가지만, IME 의 상태 자체를 바꾸지는 못한다.
    /// 한/영 키는 스캔코드로만 통한다.
    /// </remarks>
    public bool SupportsHangulToggle => SupportsTyping;

    public string AdapterName => _adapter.Name;

    // ─────────────────────────── 대기 시간 ───────────────────────────

    /// <summary>
    /// 대기 시간에 얹을 무작위 편차(±ms). 기본값은 0(편차 없음)이다.
    /// </summary>
    /// <remarks>
    /// 자동화에서는 같은 조건이면 같은 타이밍으로 도는 편이 결과를 재현하고 문제를 좁히기 쉽다.
    /// 값을 주면 매번 새로 뽑아 일정한 기계적 패턴이 줄어든다.
    /// </remarks>
    public int JitterMs { get; set; }

    /// <summary>주어진 대기 시간에 ±<see cref="JitterMs"/> 범위의 편차를 얹는다. 최소 1ms.</summary>
    public int Jitter(int delayMs) => Jitter(delayMs, JitterMs);

    /// <param name="jitterMs">±편차 폭. 마우스 이동의 단계 간격처럼 짧은 대기에는 작게 줘야 한다.</param>
    public int Jitter(int delayMs, int jitterMs)
    {
        if (jitterMs <= 0) return Math.Max(1, delayMs);

        // Random.Shared 는 스레드 안전하므로 백그라운드 스레드에서 그대로 쓴다.
        var offset = Random.Shared.Next(-jitterMs, jitterMs + 1);
        return Math.Max(1, delayMs + offset);
    }

    // ─────────────────────────── 키보드 ───────────────────────────

    /// <summary>스캔코드 하나를 누르고 뗀다.</summary>
    /// <param name="extended">방향키 등 E0 확장 키면 true.</param>
    public async Task<bool> TapKeyAsync(ushort scanCode, int holdTimeMs, bool extended = false)
    {
        if (_scanCodes is null) return false;

        if (!_scanCodes.PressScanCode(scanCode, extended)) return false;

        await Task.Delay(Jitter(holdTimeMs));

        return _scanCodes.ReleaseScanCode(scanCode, extended);
    }

    /// <summary>
    /// 한/영 전환을 한 번 넣는다. 어떤 키가 한/영 인지는 키보드 종류마다 달라
    /// <see cref="KoreanKeyboardInfo.Detect"/> 결과를 넘겨야 한다.
    /// </summary>
    public async Task<bool> ToggleHangulAsync(int holdTimeMs, HangulKeyMode mode)
    {
        if (_scanCodes is null) return false;

        switch (mode)
        {
            case HangulKeyMode.RightAlt:
                return await TapKeyAsync(ScanCodes.RightAlt, holdTimeMs, extended: true);

            case HangulKeyMode.ShiftSpace:
                _scanCodes.PressScanCode(ScanCodes.LeftShift, false);
                _scanCodes.PressScanCode(ScanCodes.Space, false);
                await Task.Delay(Jitter(holdTimeMs));
                _scanCodes.ReleaseScanCode(ScanCodes.Space, false);
                return _scanCodes.ReleaseScanCode(ScanCodes.LeftShift, false);

            default:
                // 0xF2 는 키 업 코드가 없는 "메이크 온리" 키다. 키 다운만 보낸다.
                var sent = _scanCodes.PressScanCode(ScanCodes.Hangul, false);
                await Task.Delay(Jitter(holdTimeMs));
                return sent;
        }
    }

    /// <summary>
    /// 글자 하나를 넣는다. 한글이면 지금 IME 상태를 보고 필요할 때만 한/영 을 누른 뒤
    /// 두벌식 자판 자리에 해당하는 키들을 보낸다.
    /// </summary>
    /// <returns>다룰 수 없는 문자라 아무것도 보내지 않았으면 false.</returns>
    public async Task<bool> TypeCharAsync(char c, int holdTimeMs, HangulKeyMode hangulMode)
    {
        if (_scanCodes is null) return false;

        if (HangulKeyMap.TryGetKeySequence(c, out var keys))
        {
            // 영문 상태로 두벌식 키를 보내면 "dks" 처럼 알파벳이 그대로 찍힌다. 먼저 맞춘다.
            await EnsureHangulModeAsync(wantHangul: true, holdTimeMs, hangulMode);
            await SendKeySequenceAsync(keys, holdTimeMs);
            return true;
        }

        if (!ScanCodes.TryGetKeyStroke(c, out var scanCode, out var needsShift)) return false;

        // IME 상태에 결과가 달라지는 것은 알파벳뿐이다(한글 모드의 q 는 'ㅂ').
        // 숫자·공백·문장부호는 한글 모드에서도 그대로 들어가므로 쓸데없이 전환하지 않는다.
        if (char.IsAsciiLetter(c))
        {
            await EnsureHangulModeAsync(wantHangul: false, holdTimeMs, hangulMode);
        }

        await TapWithShiftAsync(scanCode, needsShift, holdTimeMs);
        return true;
    }

    /// <summary>
    /// 가상 키로 한 글자를 넣는다. 스캔코드를 못 넣는 경로가 쓰는 길이다.
    /// </summary>
    /// <remarks>
    /// 한글은 못 한다 - 자모를 눌러 넣는 것은 대상 IME 가 처리해야 하는데 부친 키 메시지로는
    /// 한/영 전환이 먹지 않는다. 그쪽은 <see cref="TypeCharAsync"/> 의 스캔코드 경로만 된다.
    /// </remarks>
    /// <returns>넣었으면 true, 지금 자판으로 못 넣는 글자면 false.</returns>
    public async Task<bool> TypeCharByVirtualKeyAsync(char c, int holdTimeMs)
    {
        // 글자를 그대로 넣을 수 있으면 그 길이 낫다.
        // 부친 VK_SHIFT 는 대상의 키 상태를 안 바꿔서 "abC!" 가 "abc1" 로 들어간다(실측).
        if (_adapter is ICharacterInput characters)
        {
            if (!characters.SendCharacter(c)) return false;

            await Task.Delay(Jitter(holdTimeMs));
            return true;
        }

        if (!VirtualKeys.TryGetKeyStroke(c, out var virtualKey, out var needsShift)) return false;

        if (needsShift) _adapter.PressKey(VirtualKeys.Shift);

        await TapVirtualKeyAsync(virtualKey, holdTimeMs);

        if (needsShift) _adapter.ReleaseKey(VirtualKeys.Shift);

        return true;
    }

    /// <summary>
    /// 스캔코드 없이 이 글자를 넣을 수 있는지.
    /// </summary>
    /// <remarks>
    /// <see cref="ICharacterInput"/> 가 있으면 <b>한글도 된다.</b> 그 길은 글자를 그대로 주므로
    /// IME 를 거치지 않는다 - 자모를 눌러 조합시키는 것이 아니라 완성된 음절을 넣는 것이다.
    /// 가상 키로만 가야 하는 경우에는 지금 자판으로 낼 수 있는 글자만 된다(한글은 안 된다).
    /// </remarks>
    public bool CanTypeWithoutScanCode(char c)
        => _adapter is ICharacterInput || (!Korean.HangulKeyMap.IsHangul(c) && VirtualKeys.CanType(c));

    /// <summary>가상 키 하나를 눌렀다 뗀다.</summary>
    public async Task<bool> TapVirtualKeyAsync(ushort virtualKey, int holdTimeMs)
    {
        if (!_adapter.PressKey(virtualKey)) return false;

        await Task.Delay(Jitter(holdTimeMs));

        return _adapter.ReleaseKey(virtualKey);
    }

    /// <summary>두벌식 키 문자열을 차례로 누른다. 대문자는 Shift 를 함께 누른다(ㄲ, ㅒ 등).</summary>
    public async Task SendKeySequenceAsync(string keys, int holdTimeMs)
    {
        foreach (var key in keys)
        {
            if (!ScanCodes.TryGetKeyStroke(key, out var scanCode, out var needsShift)) continue;

            await TapWithShiftAsync(scanCode, needsShift, holdTimeMs);
        }
    }

    private async Task TapWithShiftAsync(ushort scanCode, bool needsShift, int holdTimeMs)
    {
        if (_scanCodes is null) return;

        if (needsShift) _scanCodes.PressScanCode(ScanCodes.LeftShift, false);

        await TapKeyAsync(scanCode, holdTimeMs);

        if (needsShift) _scanCodes.ReleaseScanCode(ScanCodes.LeftShift, false);
    }

    /// <summary>
    /// 포커스를 가진 창의 IME 가 원하는 상태가 아니면 한/영 을 한 번 눌러 맞춘다.
    /// </summary>
    /// <remarks>
    /// 상태를 읽지 못하는 창에서는 아무것도 하지 않는다.
    /// 잘못 눌러 반대로 뒤집는 것보다 그대로 두는 편이 낫다.
    /// </remarks>
    /// <returns>원하는 상태가 되었으면 true.</returns>
    public async Task<bool> EnsureHangulModeAsync(bool wantHangul, int holdTimeMs, HangulKeyMode hangulMode)
    {
        if (!KoreanKeyboardInfo.TryGetForegroundHangulMode(out var isHangul)) return false;
        if (isHangul == wantHangul) return true;

        await ToggleHangulAsync(holdTimeMs, hangulMode);

        // IME 가 상태를 바꾸는 데 약간 걸린다.
        await Task.Delay(Jitter(holdTimeMs));

        return KoreanKeyboardInfo.TryGetForegroundHangulMode(out var now) && now == wantHangul;
    }

    // ─────────────────────────── 마우스 ───────────────────────────

    /// <summary>버튼을 <paramref name="holdTimeMs"/> 동안 누른 뒤 뗀다.</summary>
    public async Task<bool> ClickAsync(MouseButton button, int holdTimeMs)
    {
        if (!_adapter.PressMouseButton(button)) return false;

        await Task.Delay(Jitter(holdTimeMs));

        return _adapter.ReleaseMouseButton(button);
    }

    /// <param name="notches">굴릴 칸 수. 양수가 위(앞), 음수가 아래(뒤).</param>
    public bool Scroll(int notches) => _adapter.ScrollWheel(notches * WheelNotch);

    private const int WheelNotch = 120;

    /// <summary>이동 속도(px/초). 이동 거리와 함께 몇 단계로 나눌지를 정한다.</summary>
    public int MoveSpeedPxPerSec { get; set; } = 800;

    /// <summary>
    /// 절대 좌표로 옮긴 뒤 실제 도착 지점을 확인해, 어긋난 만큼 되돌려 보낸다.
    /// </summary>
    /// <remarks>
    /// 정규화 좌표를 픽셀로 되돌리는 Windows 의 반올림 규칙은 문서에 없고 실측해도
    /// 가장자리에서 한 칸씩 어긋난다. 규칙을 역산하는 대신 도착 지점을 읽어 차이만큼
    /// 보정하면 규칙이 무엇이든 정확히 도착한다.
    /// </remarks>
    /// <returns>요청한 좌표에 도착했으면 true. 표시 영역 밖이라 되튕겼으면 false.</returns>
    public async Task<bool> MoveToExactAsync(int x, int y, int settleMs = 12, int attempts = 3)
    {
        _adapter.MoveMouseTo(x, y);

        for (var i = 0; i < attempts; i++)
        {
            await Task.Delay(Math.Max(1, settleMs));

            if (_adapter.GetCursorPosition() is not { } got) return false;

            var errorX = got.X - x;
            var errorY = got.Y - y;
            if (errorX == 0 && errorY == 0) return true;

            // 몇 px 을 넘겨 어긋났다면 반올림이 아니라 표시 영역 밖이라 되튕긴 것이다.
            // 모니터를 어긋나게 배치하면 가상 화면 사각형 안에도 어느 모니터에도 속하지 않는
            // 빈 공간이 생기는데, 커서는 그 자리에 놓일 수 없어 보정해도 소용이 없다.
            if (Math.Abs(errorX) > 4 || Math.Abs(errorY) > 4) return false;

            _adapter.MoveMouseTo(x - errorX, y - errorY);
        }

        await Task.Delay(Math.Max(1, settleMs));

        return _adapter.GetCursorPosition() is { } last && last.X == x && last.Y == y;
    }

    /// <summary>
    /// 목적지까지 ease-in-out 곡선으로 나눠 옮긴다.
    /// </summary>
    /// <remarks>
    /// 매 단계를 절대 좌표로 찍으므로 포인터 가속의 영향을 받지 않는다.
    /// 그러면서도 중간 이동이 실제로 일어나 마우스오버로 열리는 메뉴 같은 UI 도 반응한다.
    /// 한 번에 순간이동시키면 그런 UI 가 뜨지 않는다.
    /// </remarks>
    /// <param name="stepDelayMs">단계 사이 간격. 속도는 <see cref="MoveSpeedPxPerSec"/> 로 조절한다.</param>
    public async Task<bool> MoveSmoothAsync(int targetX, int targetY, int stepDelayMs = 8)
    {
        if (_adapter.GetCursorPosition() is not { } start)
            return await MoveToExactAsync(targetX, targetY);

        stepDelayMs = Math.Max(1, stepDelayMs);

        // 거리 ÷ 속도 = 걸리는 시간 -> 단계 수. 속도를 올리면 단계가 줄어 그만큼 빨라진다.
        var distance = Math.Sqrt(Math.Pow(targetX - start.X, 2) + Math.Pow(targetY - start.Y, 2));
        var durationMs = distance / Math.Max(1, MoveSpeedPxPerSec) * 1000.0;
        var steps = Math.Clamp((int)Math.Round(durationMs / stepDelayMs), 1, 1000);

        for (var i = 1; i <= steps; i++)
        {
            var t = (double)i / steps;
            var eased = (1 - Math.Cos(t * Math.PI)) / 2;   // 0 -> 1, 양 끝이 완만한 곡선

            _adapter.MoveMouseTo(
                start.X + (int)Math.Round((targetX - start.X) * eased),
                start.Y + (int)Math.Round((targetY - start.Y) * eased));

            await Task.Delay(Jitter(stepDelayMs, Math.Min(JitterMs, stepDelayMs)));
        }

        // 마지막 착지만 보정한다. 중간 단계는 1px 어긋나도 눈에 띄지 않는다.
        return await MoveToExactAsync(targetX, targetY);
    }
}
