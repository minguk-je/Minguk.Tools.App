using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Input;

using Minguk.Tools.Capture;
using Minguk.Tools.Capture.Input;
using Minguk.Tools.Input.Sequencing;

namespace Minguk.Tools.Input.Scripting.Live;

/// <summary>실시간 스크립트가 어떻게 끝났는지.</summary>
public enum LiveScriptOutcome
{
    /// <summary>끝까지 돌았거나 아직 도는 중.</summary>
    None,

    /// <summary>중지·F9·<c>끝()</c> 으로 멈췄다. 오류가 아니다.</summary>
    Stopped,

    /// <summary>안전장치가 막았다. 이유는 <see cref="LiveScriptApi.GuardMessage"/>.</summary>
    Guarded
}

/// <summary>
/// 실시간 모드에서 스크립트가 부르는 것들. <b>부르면 곧바로 나간다.</b>
/// </summary>
/// <remarks>
/// 계획 모드(<see cref="SequenceScriptApi"/>)와 같은 이름을 쓴다 - 같은 글을 두 모드에서 돌릴 수 있어야
/// 사람이 두 벌을 배우지 않는다. 거기에 화면을 읽는 것(몹들·읽기)과 흐름(중지되었나·끝)이 더 있다.
/// 이름은 <see cref="ScriptApiCatalog"/> 표에 있고, 검증이 이 클래스에 그 이름이 다 있는지 센다.
///
/// <b>안전장치</b> - 전부 여기서 건다. 엔진이나 화면에 두면 언어마다 다르게 새어 나간다.
///   - 대상 창이 앞에 없으면 입력을 보내지 않고 멈춘다(<see cref="ScriptGuardException"/>). 엉뚱한 창에 타이핑하는 사고.
///   - 초당 입력 상한. 넘으면 기다린다(멈추지 않는다) - 빠른 반복문이 입력을 쏟지 않게.
///   - 모든 호출이 중지 토큰을 본다. <c>쉬기()</c> 도 토큰으로 기다려서 중지가 바로 먹는다.
///   - 눈이 없으면(캡처 안 돎, 몹 찾기 꺼짐) <c>몹들()</c> 은 빈 목록이 아니라 멈추고 이유를 말한다.
///
/// 스크립트 스레드에서 돈다. UI 스레드가 아니라서 입력을 기다려도(await) 화면이 멈추지 않는다.
/// </remarks>
public sealed class LiveScriptApi
{
    private readonly LiveScriptHost _host;
    private readonly CancellationToken _token;
    private readonly HashSet<ushort> _heldKeys = [];
    private readonly Queue<long> _inputTicks = new();
    private readonly object _gate = new();

    public LiveScriptApi(LiveScriptHost host, CancellationToken token)
    {
        _host = host;
        _token = token;
    }

    /// <summary>어떻게 끝났는지. 엔진이 예외를 받은 뒤 이것을 보고 오류인지 정상 종료인지 가른다.</summary>
    public LiveScriptOutcome Outcome { get; private set; }

    /// <summary>안전장치가 막은 이유.</summary>
    public string? GuardMessage { get; private set; }

    // ── 계획 모드와 같은 이름들 - 곧바로 나간다 ─────────────────────────

    public void Type(string text) => Traced("Type", Quote(text), () => Send(new SequenceStepDefinition { Kind = SequenceStepKind.Type, Text = text ?? string.Empty }));

    public void TypeLine(string text)
    {
        Type(text);
        Enter();
    }

    public void Enter() => Traced("Enter", "", () => Send(new SequenceStepDefinition { Kind = SequenceStepKind.Enter }));

    public void ToggleHangul() => Traced("ToggleHangul", "", () => Send(new SequenceStepDefinition { Kind = SequenceStepKind.ToggleHangul }));

    /// <param name="button">비우면 좌클릭. MouseButton.Right · 숫자 · "right" 를 받는다 - 언어마다 넘기는 모양이 다르다.</param>
    public void Click(object? button = null) => Traced("Click", ToButton(button).ToString(), () => Send(new SequenceStepDefinition { Kind = SequenceStepKind.Click, Button = ToButton(button) }));

    public void RightClick() => Click(MouseButton.Right);

    public void ClickAt(int x, int y, object? button = null)
    {
        MoveTo(x, y);
        Click(button);
    }

    public void MoveTo(int x, int y) => Traced("MoveTo", $"{x}, {y}", () => Send(new SequenceStepDefinition { Kind = SequenceStepKind.MoveTo, X = x, Y = y }));

    public void Scroll(int notches) => Traced("Scroll", notches.ToString(), () => Send(new SequenceStepDefinition { Kind = SequenceStepKind.Scroll, Notches = notches }));

    /// <summary>쉰다. 토큰으로 기다리므로 중지가 이 사이에 먹는다. 짧은 것은 로그에 안 남긴다 - 반복문이 초당 수십 줄을 만든다.</summary>
    public void Wait(int milliseconds)
    {
        ThrowIfStopping();

        if (milliseconds <= 0) return;

        if (milliseconds >= 500) _host.Trace?.Invoke(new ScriptCall(DateTime.Now, "Wait", milliseconds.ToString(), "", 0));

        if (_token.WaitHandle.WaitOne(milliseconds)) ThrowIfStopping();
    }

    public void 글자(string text) => Type(text);
    public void 줄입력(string text) => TypeLine(text);
    public void 엔터() => Enter();
    public void 한영() => ToggleHangul();
    public void 클릭(object? button = null) => Click(button);
    public void 우클릭() => RightClick();
    public void 이동(int x, int y) => MoveTo(x, y);
    public void 이동클릭(int x, int y, object? button = null) => ClickAt(x, y, button);
    public void 휠(int notches) => Scroll(notches);
    public void 쉬기(int milliseconds) => Wait(milliseconds);

    // ── 화면 읽기 ────────────────────────────────────────────────────────

    /// <summary>지금 찾은 몹들. 화면 픽셀 자리로.</summary>
    public IReadOnlyList<ScriptMob> Mobs() => Traced("Mobs", "", MobsCore);

    /// <summary>화면 가운데에서 가장 가까운 몹. 없으면 null.</summary>
    public ScriptMob? NearestMob() => Traced("NearestMob", "", NearestMobCore);

    /// <summary>몹이 보일 때까지 최대 ms 기다린다. 50ms 마다 본다. 못 보면 null.</summary>
    public ScriptMob? WaitMob(int milliseconds) => Traced("WaitMob", milliseconds.ToString(), () => WaitMobCore(milliseconds));

    /// <summary>그 자리(0~1 비율)의 글자를 읽는다.</summary>
    public string ReadText(double x, double y, double width, double height)
        => Traced("ReadText", $"{x:0.###}, {y:0.###}, {width:0.###}, {height:0.###}", () => ReadTextCore(x, y, width, height));

    private IReadOnlyList<ScriptMob> MobsCore()
    {
        ThrowIfStopping();

        var hub = _host.Hub;

        if (!hub.IsCapturing) throw Guard("눈이 없습니다 - 화면에서 시작(연결)을 눌러 창을 잡아야 몹을 볼 수 있습니다.");
        if (!hub.IsDetecting) throw Guard("몹 찾기가 꺼져 있습니다 - 화면에서 몹 찾기를 켜세요.");

        var snapshot = hub.Latest;
        if (snapshot is null || snapshot.Found.Count == 0) return [];

        var target = _host.Target();
        if (target is null || !CaptureTargetBounds.TryGet(target, out var bounds)) return [];

        var mobs = new List<ScriptMob>(snapshot.Found.Count);

        for (var i = 0; i < snapshot.Found.Count; i++)
        {
            var d = snapshot.Found[i];
            var center = PreviewInputMapper.MapRatioToScreen(new Point(d.Box.CenterX, d.Box.CenterY), bounds);
            var caption = i < snapshot.Names.Count ? snapshot.Names[i] ?? string.Empty : string.Empty;

            mobs.Add(new ScriptMob(d.Label, d.Score,
                (int)Math.Round(center.X), (int)Math.Round(center.Y),
                (int)Math.Round(d.Box.Width * bounds.Width), (int)Math.Round(d.Box.Height * bounds.Height), caption));
        }

        return mobs;
    }

    private ScriptMob? NearestMobCore()
    {
        var target = _host.Target();
        if (target is null || !CaptureTargetBounds.TryGet(target, out var bounds)) return null;

        var cx = bounds.Left + (bounds.Width / 2);
        var cy = bounds.Top + (bounds.Height / 2);

        return MobsCore().OrderBy(m => ((m.CenterX - cx) * (m.CenterX - cx)) + ((m.CenterY - cy) * (m.CenterY - cy))).FirstOrDefault();
    }

    private ScriptMob? WaitMobCore(int milliseconds)
    {
        var deadline = Environment.TickCount64 + Math.Max(0, milliseconds);

        while (true)
        {
            var nearest = NearestMobCore();
            if (nearest is not null) return nearest;

            if (Environment.TickCount64 >= deadline) return null;

            Wait(50);
        }
    }

    private string ReadTextCore(double x, double y, double width, double height)
    {
        ThrowIfStopping();

        var hub = _host.Hub;
        if (!hub.IsCapturing) throw Guard("눈이 없습니다 - 화면에서 시작(연결)을 눌러 창을 잡아야 글자를 읽을 수 있습니다.");

        var ocr = _host.Ocr?.Invoke() ?? throw Guard("글자 읽기 엔진이 없습니다 - Windows OCR 언어 팩을 확인하세요.");

        // 처음 부를 때 프레임 복사를 켜고, 한 장 들어올 때까지 잠깐 기다린다.
        hub.WantsFrames = true;

        var deadline = Environment.TickCount64 + 1500;
        var region = new Rect(x, y, width, height);
        System.Windows.Media.Imaging.BitmapSource? crop;

        while (!hub.TryCropFrame(region, out crop) || crop is null)
        {
            if (Environment.TickCount64 >= deadline) throw Guard("프레임이 들어오지 않습니다 - 캡처가 돌고 있는지, CPU 리드백이 켜져 있는지 보세요.");
            Wait(50);
        }

        var outcome = ocr.RecognizeAsync(crop, _token).GetAwaiter().GetResult();

        return outcome.Text.Replace(Environment.NewLine, " ").Trim();
    }

    /// <summary>글자에서 숫자만. 없으면 null.</summary>
    public int? ReadNumber(double x, double y, double width, double height)
        => Traced("ReadNumber", $"{x:0.###}, {y:0.###}, {width:0.###}, {height:0.###}", () => ReadNumberCore(x, y, width, height));

    private int? ReadNumberCore(double x, double y, double width, double height)
    {
        var digits = new string(ReadTextCore(x, y, width, height).Where(char.IsDigit).ToArray());

        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    public IReadOnlyList<ScriptMob> 몹들() => Mobs();
    public ScriptMob? 가장가까운몹() => NearestMob();
    public ScriptMob? 몹기다리기(int milliseconds) => WaitMob(milliseconds);
    public string 읽기(double x, double y, double width, double height) => ReadText(x, y, width, height);
    public int? 숫자읽기(double x, double y, double width, double height) => ReadNumber(x, y, width, height);

    // ── 키 ───────────────────────────────────────────────────────────────

    /// <summary>이름으로 키 한 번. "F" · "Space" · "Enter" · "Ctrl+Shift+1".</summary>
    public void Key(string name) => Traced("Key", Quote(name), () => KeyCore(name));

    /// <summary>키를 누른 채로 둔다. 비상 정지가 <see cref="ReleaseAll"/> 로 전부 뗀다.</summary>
    public void KeyDown(string name) => Traced("KeyDown", Quote(name), () => KeyDownCore(name));

    public void KeyUp(string name) => Traced("KeyUp", Quote(name), () => KeyUpCore(name));

    private void KeyCore(string name)
    {
        var (modifiers, key) = ParseKey(name);

        BeforeInput();

        foreach (var m in modifiers) _host.Service.Adapter.PressKey(m);

        try
        {
            _host.Service.Adapter.PressKey(key);
            Thread.Sleep(_host.HoldTimeMs);
            _host.Service.Adapter.ReleaseKey(key);
        }
        finally
        {
            foreach (var m in modifiers.AsEnumerable().Reverse()) _host.Service.Adapter.ReleaseKey(m);
        }
    }

    private void KeyDownCore(string name)
    {
        var (modifiers, key) = ParseKey(name);

        BeforeInput();

        foreach (var m in modifiers) Hold(m);
        Hold(key);
    }

    private void KeyUpCore(string name)
    {
        var (modifiers, key) = ParseKey(name);

        BeforeInput();

        Release(key);
        foreach (var m in modifiers.AsEnumerable().Reverse()) Release(m);
    }

    public void 키(string name) => Key(name);
    public void 누르기(string name) => KeyDown(name);
    public void 떼기(string name) => KeyUp(name);

    /// <summary>누르고 있던 키를 전부 뗀다. 중지·비상 정지 때 부른다 - 누른 채로 멈추면 게임이 계속 달린다.</summary>
    public void ReleaseAll()
    {
        ushort[] held;

        lock (_gate)
        {
            held = [.. _heldKeys];
            _heldKeys.Clear();
        }

        foreach (var key in held)
        {
            try { _host.Service.Adapter.ReleaseKey(key); }
            catch (Exception) { /* 어댑터가 이미 닫혔을 수 있다. 떼려는 시도만 한다. */ }
        }
    }

    // ── 흐름 ─────────────────────────────────────────────────────────────

    public bool IsStopped() => _token.IsCancellationRequested;

    public bool 중지되었나() => IsStopped();

    /// <summary>스크립트를 여기서 끝낸다. 오류가 아니다.</summary>
    public void Stop()
    {
        Outcome = LiveScriptOutcome.Stopped;
        throw new ScriptStoppedException();
    }

    public void 끝() => Stop();

    public void Print(object? value) => _host.Print(value?.ToString() ?? "null");

    public void Watch(string name, object? value) => _host.Watch(name ?? string.Empty, value?.ToString() ?? "null");

    public void 출력(object? value) => Print(value);

    public void 보기(string name, object? value) => Watch(name, value);

    // ── 호출 기록 ────────────────────────────────────────────────────────

    /// <summary>부른 것·인자·결과·걸린 시간을 남긴다. 터지면 그 사연도 남기고 그대로 던진다.</summary>
    private T Traced<T>(string name, string arguments, Func<T> body)
    {
        var watch = Stopwatch.StartNew();

        try
        {
            var result = body();
            _host.Trace?.Invoke(new ScriptCall(DateTime.Now, name, arguments, Describe(result), watch.Elapsed.TotalMilliseconds));
            return result;
        }
        catch (Exception ex)
        {
            _host.Trace?.Invoke(new ScriptCall(DateTime.Now, name, arguments, "! " + ex.Message, watch.Elapsed.TotalMilliseconds));
            throw;
        }
    }

    private void Traced(string name, string arguments, Action body)
        => Traced<object?>(name, arguments, () => { body(); return null; });

    private static string Describe(object? result) => result switch
    {
        null => "",
        string text => Quote(text),
        IReadOnlyList<ScriptMob> mobs => mobs.Count == 0 ? "없음" : $"{mobs.Count}마리: {mobs[0]}",
        _ => result.ToString() ?? ""
    };

    private static string Quote(string? text) => text is null ? "null" : $"\"{text}\"";

    // ── 안쪽 ─────────────────────────────────────────────────────────────

    /// <summary>단계 하나를 계획 모드와 같은 길로 보낸다. 한글 처리·누름 시간이 그쪽과 같아야 한다.</summary>
    private void Send(SequenceStepDefinition definition)
    {
        BeforeInput();

        var sequence = new SequencePlan { Steps = [definition] }.Build(_host.Service, _host.HoldTimeMs);

        foreach (var step in sequence.Steps)
            step.RunAsync(null, _token).GetAwaiter().GetResult();
    }

    /// <summary>입력 전에 늘 거치는 것 - 중지 확인, 앞 창 확인, 초당 상한.</summary>
    private void BeforeInput()
    {
        ThrowIfStopping();

        if (_host.RequiresForeground && _host.Target() is { Kind: CaptureTargetKind.Window, Handle: var handle } && handle != IntPtr.Zero
            && ForegroundWindow.Handle != handle)
            throw Guard("대상 창이 앞에 없어 입력을 보내지 않았습니다. 창을 앞으로 가져오거나 시작 대기를 늘리세요.");

        Throttle();
    }

    /// <summary>초당 상한을 넘으면 넘긴 만큼 기다린다.</summary>
    private void Throttle()
    {
        var limit = Math.Max(1, _host.MaxInputsPerSecond);

        while (true)
        {
            var now = Environment.TickCount64;

            lock (_gate)
            {
                while (_inputTicks.Count > 0 && now - _inputTicks.Peek() > 1000) _inputTicks.Dequeue();

                if (_inputTicks.Count < limit)
                {
                    _inputTicks.Enqueue(now);
                    return;
                }
            }

            Wait(20);
        }
    }

    private void Hold(ushort key)
    {
        lock (_gate) _heldKeys.Add(key);
        _host.Service.Adapter.PressKey(key);
    }

    private void Release(ushort key)
    {
        lock (_gate) _heldKeys.Remove(key);
        _host.Service.Adapter.ReleaseKey(key);
    }

    private void ThrowIfStopping()
    {
        if (!_token.IsCancellationRequested) return;

        Outcome = LiveScriptOutcome.Stopped;
        _token.ThrowIfCancellationRequested();
    }

    private ScriptGuardException Guard(string message)
    {
        Outcome = LiveScriptOutcome.Guarded;
        GuardMessage = message;
        return new ScriptGuardException(message);
    }

    /// <summary>"Ctrl+Shift+F" → 조합키들과 가상 키. 한 글자·숫자·Key 열거형 이름을 받는다.</summary>
    private static (ushort[] Modifiers, ushort Key) ParseKey(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("키 이름이 비어 있습니다.");

        var parts = name.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var modifiers = new List<ushort>();
        ushort? key = null;

        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers.Add(VirtualKeys.Control); continue;
                case "shift": modifiers.Add(VirtualKeys.Shift); continue;
                case "alt": modifiers.Add(VirtualKeys.Alt); continue;
            }

            key = ToVirtualKey(part);
        }

        if (key is null) throw new ArgumentException($"키 이름을 모르겠습니다: \"{name}\". \"F\", \"Space\", \"Enter\", \"Ctrl+1\" 처럼 적으세요.");

        return ([.. modifiers], key.Value);
    }

    private static ushort ToVirtualKey(string part)
    {
        var text = part.Length == 1 && char.IsDigit(part[0]) ? "D" + part : part;

        if (text.Length == 1 && char.IsLetter(text[0])) text = text.ToUpperInvariant();

        if (Enum.TryParse<System.Windows.Input.Key>(text, true, out var wpfKey) && wpfKey != System.Windows.Input.Key.None)
            return (ushort)KeyInterop.VirtualKeyFromKey(wpfKey);

        if (part.Length == 1 && VirtualKeys.TryGetKeyStroke(part[0], out var virtualKey, out _))
            return virtualKey;

        throw new ArgumentException($"키 이름을 모르겠습니다: \"{part}\".");
    }

    /// <summary>언어마다 다르게 넘어오는 버튼 값을 읽는다. 비우면 좌클릭.</summary>
    private static MouseButton ToButton(object? value) => value switch
    {
        null => MouseButton.Left,
        MouseButton button => button,
        int number => (MouseButton)number,
        long number => (MouseButton)number,
        double number => (MouseButton)(int)number,
        string text when Enum.TryParse<MouseButton>(text, true, out var parsed) => parsed,
        _ => MouseButton.Left
    };
}
