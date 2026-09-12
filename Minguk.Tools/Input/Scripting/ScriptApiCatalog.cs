using System;
using System.Collections.Generic;
using System.Linq;

namespace Minguk.Tools.Input.Scripting;

/// <summary>어느 모드에서 부를 수 있는지.</summary>
public enum ScriptApiMode
{
    /// <summary>계획 모드(적어 두었다 나중에 보냄)와 실시간 모드 둘 다.</summary>
    Both,

    /// <summary>실시간 모드에서만. 화면을 읽거나 곧바로 반응하는 것들.</summary>
    Live
}

/// <summary>스크립트에서 부를 수 있는 것 하나. 영문 이름과 한글 이름은 같은 것을 가리킨다.</summary>
/// <param name="Name">영문 이름. 스크립트에서 이대로 부른다.</param>
/// <param name="Korean">한글 이름. 같은 일을 한다.</param>
/// <param name="Parameters">괄호 안에 적는 것. 없으면 빈 글.</param>
/// <param name="Summary">한 줄 설명. 완성 목록과 도움말에 그대로 뜬다.</param>
/// <param name="Mode">어느 모드에서 되는지.</param>
public sealed record ScriptApiEntry(string Name, string Korean, string Parameters, string Summary, ScriptApiMode Mode = ScriptApiMode.Both)
{
    /// <summary>"Type(text)" 처럼 부르는 모양.</summary>
    public string Signature => $"{Name}({Parameters})";

    /// <summary>"글자(text)".</summary>
    public string KoreanSignature => $"{Korean}({Parameters})";

    /// <summary>완성 목록에 보이는 설명. 실시간 전용이면 앞에 표시한다 - 계획 모드에서 부르면 빨간 줄이 되는 이유를 알 수 있게.</summary>
    public string DisplaySummary => Mode == ScriptApiMode.Live ? $"[실시간] {Summary}" : Summary;
}

/// <summary>
/// 스크립트 API 의 목록표. 이름·한글 이름·인자·설명이 여기 한 곳에 있다.
/// </summary>
/// <remarks>
/// <b>왜 표 하나인가</b> - 같은 이름이 네 군데에 적혀 있었다: <see cref="SequenceScriptApi"/>(실체),
/// 파이썬 엔진이 심는 이름, 자바스크립트 엔진이 심는 이름, 구문 강조(<c>Resource/SequenceScript.*.xshd</c>).
/// 여기에 코드 완성과 도움말까지 더하면 여섯이다. 하나 늘릴 때 어느 하나를 빠뜨리면 그 언어에서만
/// 그 이름이 없거나, 완성에는 뜨는데 부르면 없는 이름이 된다. 표에서 나오게 하면 한 곳만 고친다.
///
/// 리플렉션으로 <see cref="SequenceScriptApi"/> 에서 뽑지 않는 이유는 그쪽에 적어 두었다 - 도우미 메서드를
/// 하나 넣는 순간 스크립트에도 조용히 새 이름이 생긴다. 무엇을 열어 줄지는 정해서 연다.
///
/// 파이썬·자바스크립트 엔진은 이 표의 이름으로 심는다(자바스크립트는 <c>api.이름</c> 을 감싸는 함수를 만든다).
/// 구문 강조는 xshd 파일이라 아직 손으로 맞춘다.
///
/// 계획 모드 API 의 실체는 <see cref="SequenceScriptApi"/>, 실시간 모드는 <c>Live.LiveScriptApi</c> 다.
/// 둘 다 이 표의 이름을 메서드로 갖고 있어야 하고, 검증(<c>--vision</c>)이 그것을 리플렉션으로 센다.
/// </remarks>
public static class ScriptApiCatalog
{
    public static IReadOnlyList<ScriptApiEntry> Entries { get; } =
    [
        // ── 둘 다: 계획 모드에서는 적히고 실시간 모드에서는 곧바로 나간다 ──
        new("Type", "글자", "text", "글자를 하나씩 누른다. 한글·영문·숫자·문장부호."),
        new("TypeLine", "줄입력", "text", "글자를 치고 Enter 까지 누른다."),
        new("Enter", "엔터", "", "Enter 한 번."),
        new("ToggleHangul", "한영", "", "한/영 을 한 번 뒤집는다."),
        new("Click", "클릭", "button 또는 ms", "마우스 버튼 한 번. 클릭() 좌클릭 · 클릭(\"Right\") 우클릭. 실시간 모드에서는 클릭(100) 이면 100ms 누르고 있다가 뗀다 · 클릭(\"Right\", 100)."),
        new("RightClick", "우클릭", "ms", "우클릭 한 번. 실시간 모드에서는 우클릭(100) 이면 100ms 누르고 있다가 뗀다."),
        new("ClickAt", "이동클릭", "x, y, button", "그 화면 좌표(픽셀)로 옮겨 누른다."),
        new("MoveTo", "이동", "x, y", "화면 좌표(픽셀)로 옮긴다."),
        new("Scroll", "휠", "notches", "휠을 굴린다. 양수가 위, 음수가 아래."),
        new("Wait", "쉬기", "milliseconds", "아무것도 보내지 않고 쉰다(ms). 실시간 모드에서는 이 사이에 중지가 먹는다."),

        // ── 실시간만: 화면을 읽거나 흐름을 다룬다 ──
        new("Aim", "조준", "x, y", "그 화면 좌표를 향해 마우스를 움직인 양으로 옮긴다. 커서를 잡는 게임에서 조준할 때(이동()은 안 먹는다). 겨눈 뒤 새 화면이 오기 전에는 다시 안 겨누고 false.", ScriptApiMode.Live),
        new("MoveBy", "상대이동", "dx, dy", "지금 자리에서 이만큼 움직인다.", ScriptApiMode.Live),
        new("MouseDown", "버튼누르기", "button", "마우스 버튼을 누른 채로 둔다. 떼기 전까지.", ScriptApiMode.Live),
        new("MouseUp", "버튼떼기", "button", "누르고 있던 마우스 버튼을 뗀다.", ScriptApiMode.Live),
        new("Drag", "끌기", "x, y, button", "버튼을 누른 채 그 화면 좌표로 끌었다가 뗀다. 커서가 보이는 창(RPG)에서.", ScriptApiMode.Live),
        new("DragBy", "상대끌기", "dx, dy, button", "버튼을 누른 채 이만큼 움직였다가 뗀다. RPG 의 우클릭 카메라 회전 - 상대끌기(200, 0, \"Right\").", ScriptApiMode.Live),
        new("Mobs", "몹들", "", "지금 화면에서 찾은 몹들. 없으면 빈 목록. 몹 찾기가 켜져 있어야 한다.", ScriptApiMode.Live),
        new("NearestMob", "가장가까운몹", "", "화면 가운데에서 가장 가까운 몹. 없으면 null.", ScriptApiMode.Live),
        new("TargetMob", "목표", "", "잡을 때까지 같은 몹만 본다. 놓치면 잠깐 기다렸다 새로 고른다. 몹이 둘일 때 좌우로 왕복하는 것을 막는다.", ScriptApiMode.Live),
        new("ReleaseTarget", "목표풀기", "", "고정한 목표를 놓는다. 잡은 뒤 다음 몹으로 넘어갈 때.", ScriptApiMode.Live),
        new("WaitMob", "몹기다리기", "milliseconds", "몹이 보일 때까지 최대 ms 기다린다. 못 보면 null.", ScriptApiMode.Live),
        new("ReadText", "읽기", "x, y, width, height", "그 자리(0~1 비율)의 글자를 읽는다.", ScriptApiMode.Live),
        new("ReadNumber", "숫자읽기", "x, y, width, height", "그 자리의 글자에서 숫자만 뽑는다. 없으면 null.", ScriptApiMode.Live),
        new("Key", "키", "name", "이름으로 키 한 번. \"F\", \"Space\", \"Enter\", \"Ctrl+Shift+1\".", ScriptApiMode.Live),
        new("KeyDown", "누르기", "name", "키를 누른 채로 둔다. 떼기 전까지.", ScriptApiMode.Live),
        new("KeyUp", "떼기", "name", "누르고 있던 키를 뗀다.", ScriptApiMode.Live),
        new("Walk", "걷기", "keys, milliseconds", "키를 그 시간만큼 누르고 있다가 뗀다. 게임 이동은 이것 - 걷기(\"W\", 500), 대각선은 \"W+A\".", ScriptApiMode.Live),
        new("IsStopped", "중지되었나", "", "중지를 눌렀거나 F9 를 눌렀으면 true. 반복문 조건에 쓴다.", ScriptApiMode.Live),
        new("Print", "출력", "value", "출력 칸에 한 줄 적는다.", ScriptApiMode.Live),
        new("Watch", "보기", "name, value", "이름표를 붙여 최신값을 보여 준다. 같은 이름은 덮어쓴다.", ScriptApiMode.Live),
        new("Stop", "끝", "", "스크립트를 여기서 끝낸다.", ScriptApiMode.Live)
    ];

    /// <summary>계획 모드 스크립트에 심는 이름 전부. 영문 먼저, 한글 나중.</summary>
    public static IReadOnlyList<string> PlanNames { get; } = NamesOf(Entries.Where(e => e.Mode == ScriptApiMode.Both));

    /// <summary>실시간 모드 스크립트에 심는 이름 전부.</summary>
    public static IReadOnlyList<string> LiveNames { get; } = NamesOf(Entries);

    private static IReadOnlyList<string> NamesOf(IEnumerable<ScriptApiEntry> entries)
    {
        var list = entries.ToList();
        return [.. list.Select(e => e.Name), .. list.Select(e => e.Korean)];
    }

    /// <summary>
    /// 앞글자로 고른다. 영문은 대소문자를 안 가린다. 빈 글이면 전부.
    /// 한 항목이 영문·한글 두 이름으로 나오므로 (이름, 항목) 쌍으로 준다.
    /// </summary>
    public static IEnumerable<(string Name, ScriptApiEntry Entry)> Match(string? prefix)
    {
        var p = prefix ?? string.Empty;

        foreach (var entry in Entries)
        {
            if (entry.Name.StartsWith(p, StringComparison.OrdinalIgnoreCase)) yield return (entry.Name, entry);
            if (entry.Korean.StartsWith(p, StringComparison.Ordinal)) yield return (entry.Korean, entry);
        }
    }

    /// <summary>이름으로 항목을 찾는다. 영문·한글 둘 다. 없으면 null.</summary>
    public static ScriptApiEntry? Find(string name)
        => Entries.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase) || e.Korean == name);
}
