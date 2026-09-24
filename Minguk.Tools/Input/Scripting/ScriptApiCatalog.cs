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
        new("Aim", "조준", "검출 | x, y", "검출을 주면 조준 스레드가 8ms 마다 멈추지 않고 머리를 따라가고, 부를 때마다 새 화면을 기다렸다 몸에 들어와 있으면 true(한 화면에 한 번). 좌표를 주면 한 번 움직이고 8px 안이면 true. 커서를 잡는 게임용(이동()은 안 먹는다).", ScriptApiMode.Live),
        new("SetAimZone", "조준범위", "비율", "조준(검출)이 참(쏴도 됨)을 줄 몸 사각형의 안쪽 비율(기본 0.6 = 가운데 60%). 작을수록 가운데서만 쏜다 - 가장자리에서 쏴 빗나가면 줄인다.", ScriptApiMode.Live),
        new("SetAimLead", "앞질러겨누기", "ms", "움직이는 검출을 속도 × ms 만큼 앞서 겨눈다(기본 90). 달리는 봇 뒤를 쏘면 늘리고 앞을 쏘면 줄인다.", ScriptApiMode.Live),
        new("SetAimConfirm", "확인후쏘기", "켬", "조준(검출)이 참을 주려면 마지막 화면에서도 검출이 조준점 근처여야 하는지(기본 true). 끄면 예측만으로 쏜다 - 빠르지만 배율이 틀리면 옆을 쏜다.", ScriptApiMode.Live),
        new("MoveBy", "상대이동", "dx, dy", "지금 자리에서 이만큼 움직인다.", ScriptApiMode.Live),
        new("MoveCursor", "커서이동", "x, y, 허용오차", "커서를 그 화면 좌표까지 실제 커서 자리를 보며 걸어간다. 커서가 보이는 메뉴용(일반 화면). 닿으면 true.", ScriptApiMode.Live),
        new("SetMouseMode", "마우스모드", "\"일반\" | \"조준\"", "마우스 움직임 방식. 조준(기본)=커서를 잡는 게임, 일반=커서가 보이는 메뉴(이동·조준이 커서를 보며 걸어간다). 스크립트 첫머리에서 한 번.", ScriptApiMode.Live),
        new("MouseDown", "버튼누르기", "button", "마우스 버튼을 누른 채로 둔다. 떼기 전까지.", ScriptApiMode.Live),
        new("MouseUp", "버튼떼기", "button", "누르고 있던 마우스 버튼을 뗀다.", ScriptApiMode.Live),
        new("Drag", "끌기", "x, y, button", "버튼을 누른 채 그 화면 좌표로 끌었다가 뗀다. 커서가 보이는 창(RPG)에서.", ScriptApiMode.Live),
        new("DragBy", "상대끌기", "dx, dy, button", "버튼을 누른 채 이만큼 움직였다가 뗀다. RPG 의 우클릭 카메라 회전 - 상대끌기(200, 0, \"Right\").", ScriptApiMode.Live),
        new("Detections", "검출들", "", "지금 화면에서 찾은 검출들. 없으면 빈 목록. 검출이 켜져 있어야 한다.", ScriptApiMode.Live),
        new("NearestDetection", "가장가까운검출", "", "화면 가운데에서 가장 가까운 검출. 없으면 null.", ScriptApiMode.Live),
        new("TargetDetection", "목표", "", "잡을 때까지 같은 검출만 본다. 놓치면 잠깐 기다렸다 새로 고른다. 검출이 둘일 때 좌우로 왕복하는 것을 막는다.", ScriptApiMode.Live),
        new("ReleaseTarget", "목표풀기", "", "고정한 목표를 놓는다. 잡은 뒤 다음 검출로 넘어갈 때.", ScriptApiMode.Live),
        new("WaitDetection", "검출기다리기", "milliseconds", "검출이 보일 때까지 최대 ms 기다린다. 못 보면 null.", ScriptApiMode.Live),
        new("ReadText", "읽기", "x, y, width, height", "그 자리(0~1 비율)의 글자를 읽는다. 읽기(\"탄약\") 으로 화면에서 만들어 둔 영역(구역들을 차례로 이어), 읽기(\"탄약.현재\") 로 구역 하나를 부를 수도 있다.", ScriptApiMode.Live),
        new("ReadNumber", "숫자읽기", "x, y, width, height", "그 자리의 글자에서 숫자만 뽑는다. 숫자읽기(\"탄약.최대\") 처럼 만들어 둔 영역·구역을 부를 수도 있다. 없으면 null.", ScriptApiMode.Live),
        new("Ammo", "탄약", "", "지금 탄약. HUD 에서 읽는다. 못 읽으면 null.", ScriptApiMode.Live),
        new("AmmoMax", "탄약최대", "", "탄약 최대치 - 재장전하면 이만큼 찬다.", ScriptApiMode.Live),
        new("Health", "체력", "", "지금 체력. HUD 에서 읽는다.", ScriptApiMode.Live),
        new("HealthMax", "체력최대", "", "체력 최대치.", ScriptApiMode.Live),
        new("Ultimate", "궁극기", "", "궁극기 충전(%). 다 찼으면 숫자가 없어 null.", ScriptApiMode.Live),
        new("UltimateReady", "궁극기준비", "", "궁극기가 다 찼는가.", ScriptApiMode.Live),
        new("ReadNumbersAt", "숫자들읽기", "name", "이름 붙인 영역에서 읽은 숫자를 구역 순서대로 준다. 「현재」·「최대」 구역이면 [30, 40] - 믿을 범위는 스크립트가 고른다. \"영역.구역\" 이면 그 구역만.", ScriptApiMode.Live),
        new("HasTextAt", "글자있나", "name", "이름 붙인 영역(또는 \"영역.구역\")에 읽을 글자가 있는가. 스크립트 화면의 영역 패널에서 만든 이름.", ScriptApiMode.Live),
        new("FindText", "글자찾기", "text, [name]", "화면 전체(또는 이름 붙인 자리)에서 그 글이 든 낱말·줄을 찾아 자리(x·y 화면 픽셀, 너비·높이, 글)를 준다. 없으면 null. 화면 전체는 6조각으로 읽어 0.3~0.6초 - 메뉴가 떴을 때 부른다.", ScriptApiMode.Live),
        new("PressText", "글자누르기", "text, [name], [button]", "글자찾기로 찾아 그 가운데를 누른다. 찾았으면 true. 게임 메뉴·버튼 - 글자누르기(\"사격장\").", ScriptApiMode.Live),
        new("FindImage", "그림찾기", "resource, [score], [name]", "본보기 그림(프로젝트 Resources 의 PNG - 영역 패널의 「본보기로 저장」)을 화면에서 찾아 자리를 준다. 못 찾으면 null. 그림으로 된 메뉴·버튼용. 밝기가 달라져도 찾지만 크기가 달라지면 못 찾는다. PNG 의 투명한 곳(영역 패널 「마스크」 밖)은 빼고 견준다.", ScriptApiMode.Live),
        new("HasImage", "그림있나", "resource, [score], [name]", "본보기 그림이 화면(또는 이름 붙인 자리)에 있는가. 스킬 아이콘·버프 표시처럼 자리가 고정된 것은 자리를 주면 훨씬 빠르다.", ScriptApiMode.Live),
        new("ImageScore", "그림닮음", "resource, [name]", "본보기가 얼마나 닮았는지(0~1). 못 찾으면 0. 문턱(기본 0.8)을 잡을 때 눈으로 본다.", ScriptApiMode.Live),
        new("Heading", "방위", "", "캐릭터 몸이 향한 방위(도) - 북 0, 시계 방향. 미니맵 화살표를 읽는다. 먼저 도구 줄의 「미니맵 익히기」를 한 번 눌러야 한다.", ScriptApiMode.Live),
        new("TargetBearing", "목표방위", "", "미니맵 목표 마커 가운데 가장 가까운 것 - 방위·거리(px)·돌 각. 없으면 null.", ScriptApiMode.Live),
        new("TargetBearings", "목표방위들", "", "미니맵 목표 마커를 모두, 가까운 것부터.", ScriptApiMode.Live),
        new("BearingTo", "방위차", "bearing", "지금 몸 방향에서 그 방위까지 몇 도 돌아야 하나(−180~180). 양수면 오른쪽.", ScriptApiMode.Live),
        new("Face", "바라보기", "bearing", "그 방위를 보도록 마우스를 가로로 돌린다. 한 번짜리 - 몸은 안 돌아 맞았는지 확인할 수 없다(정확도는 회전 배율에 달렸다).", ScriptApiMode.Live),
        new("GoTo", "가기", "bearing, ms", "그 방위로 걸어간다 - 걷는 동안 미니맵을 보며 고친다. 회전 배율이 좀 틀려도 수렴한다. 자동 이동이 안 되는 곳에서 쓴다.", ScriptApiMode.Live),
        new("GoToTarget", "목표로가기", "ms", "미니맵 목표 마커 쪽으로 걸어간다. 마커가 없으면 걷지 않고 false.", ScriptApiMode.Live),
        new("MarkerBearing", "마커방위", "name", "미니맵의 그 이름 점(minimap.json 의 markers - 기본 「몹」 은 빨간 점) 가운데 가장 가까운 것 - 방위·거리(px)·돌 각. 없으면 null.", ScriptApiMode.Live),
        new("MarkerBearings", "마커방위들", "name", "그 이름 점을 모두, 가까운 것부터.", ScriptApiMode.Live),
        new("WalkToMarker", "마커로걷기", "name, ms", "그 이름 점 가운데 가장 가까운 것 쪽으로 키보드(W·A·S·D 8방향)만으로 걷는다 - 마우스는 안 쓰고 화살표 방위도 안 믿는다. 걸을 때 점이 밀리는 쪽을 보고 방향을 잡는다. 다 왔거나 시간이 다 되면 true, 점이 없거나 막혔으면 false.", ScriptApiMode.Live),
        new("GoToMarker", "마커로가기", "name, ms", "그 이름 점 가운데 가장 가까운 것 쪽으로 걸어간다. 없으면 걷지 않고 false. 주위에 몹이 없을 때 미니맵의 몹 쪽으로 가는 데 쓴다.", ScriptApiMode.Live),
        new("TurnScale", "회전배율", "", "마우스 가로 1 카운트가 몸을 몇 도 돌리나. 모르면 그 자리에서 재서 minimap.json 에 적는다(제자리에서 조금 돌고 걷는다).", ScriptApiMode.Live),
        new("HealthBar", "체력바", "검출", "그 검출 머리 위 체력바가 몇 할 찼는지(0~1). 못 찾으면 null. 쏘기 전·뒤를 견줘 맞았는지 안다.", ScriptApiMode.Live),
        new("HitByHealthBar", "명중했나", "검출, 쏘기전, [ms]", "쏘기 전 체력바(체력바(검출))보다 ms(기본 300) 안에 줄면 true, 그대로면 false, 쏘기 전 값을 모르면 null - var 전 = 체력바(검출); 클릭(); if (명중했나(검출, 전) == false) …", ScriptApiMode.Live),
        new("HitConfirmed", "명중확인", "resource, [ms], [score]", "방금 쏜 것이 맞았는가 - 조준점 둘레에 히트 마커(맞히면 잠깐 뜨는 X)가 ms(기본 300) 안에 뜨면 true. 본보기는 영역 패널의 「연속 저장」 으로 떠서 마커가 찍힌 것을 고른다.", ScriptApiMode.Live),
        new("PressImage", "그림누르기", "resource, [score], [name]", "본보기 그림을 찾아 그 가운데를 누른다. 찾았으면 true - 그림누르기(\"사격장.png\").", ScriptApiMode.Live),
        new("PressRegion", "영역누르기", "name, [button]", "이름 붙인 영역(또는 \"영역.구역\")의 가운데를 그냥 누른다 - 늘 같은 자리에 있는 버튼은 찾을 것 없이 이쪽이 빠르다. 영역누르기(\"시작버튼\")", ScriptApiMode.Live),
        new("RegionSpot", "영역자리", "name", "이름 붙인 영역의 가운데 자리(화면 픽셀)와 크기. 눌러 보기 전에 어디인지 볼 때. var 자리 = 영역자리(\"시작버튼\"); 출력(자리.x);", ScriptApiMode.Live),
        new("SituationSummary", "상황요약", "names", "이름 붙인 자리들을 읽어 「이름: 읽은 글」 줄로 묶는다. 「검출」 을 넣으면 지금 검출을 이름별 수·자리로. 판단(상황요약(\"지역, 퀘스트, 체력, 검출\"), …) 처럼 넘긴다.", ScriptApiMode.Live),
        new("Judge", "판단", "situation, actions", "로컬 LLM(llm.json 의 textModel, 기본 qwen3:8b)이 상황을 보고 행동 목록(쉼표로) 중 하나를 고른다. 목록 밖은 못 고른다. 규칙은 프로젝트의 llm-rules.md. 한 번에 몇 초 - 분명한 규칙은 스크립트로 먼저 처리한다.", ScriptApiMode.Live),
        new("JudgeScreen", "화면판단", "question, actions, [name]", "지금 화면(또는 이름 붙인 자리)을 그림 모델(visionModel)에 보여 주고 행동 목록 중 하나를 고르게 한다. 글자는 OCR 로 읽어 질문에 넣는 편이 정확하다.", ScriptApiMode.Live),
        new("Ask", "물어보기", "question", "글 모델에 자유롭게 묻는다. 답은 글.", ScriptApiMode.Live),
        new("AskScreen", "화면물어보기", "question, [name]", "지금 화면(또는 이름 붙인 자리)을 보여 주고 자유롭게 묻는다. 답은 글.", ScriptApiMode.Live),
        new("CorrectJudgement", "판단고치기", "situation, action", "틀린 판단을 고친다 - 「이 상황이면 이 행동」 을 프로젝트의 llm-examples.jsonl 에 적고, 다음 판단부터 예시로 넣는다.", ScriptApiMode.Live),
        new("SetJudgeRules", "판단규칙", "rules", "이 실행 동안 쓸 게임 규칙(llm-rules.md 대신). 빈 글이면 파일로 돌아간다.", ScriptApiMode.Live),
        new("Key", "키", "name", "이름으로 키 한 번. \"F\", \"Space\", \"Enter\", \"Ctrl+Shift+1\".", ScriptApiMode.Live),
        new("KeyDown", "누르기", "name", "키를 누른 채로 둔다. 떼기 전까지.", ScriptApiMode.Live),
        new("KeyUp", "떼기", "name", "누르고 있던 키를 뗀다.", ScriptApiMode.Live),
        new("Walk", "걷기", "keys, milliseconds", "키를 그 시간만큼 누르고 있다가 뗀다. 게임 이동은 이것 - 걷기(\"W\", 500), 대각선은 \"W+A\".", ScriptApiMode.Live),
        new("IsStopped", "중지되었나", "", "중지를 눌렀거나 비상 정지(Pause)를 눌렀으면 true. 반복문 조건에 쓴다.", ScriptApiMode.Live),
        new("Print", "출력", "value", "출력 칸에 한 줄 적는다.", ScriptApiMode.Live),
        new("Watch", "보기", "name, value", "이름표를 붙여 최신값을 보여 준다. 같은 이름은 덮어쓴다.", ScriptApiMode.Live),
        new("ResourcePath", "리소스경로", "name", "프로젝트 리소스의 전체 경로. \"적.png\" 처럼 Resources 아래 이름도 된다. 없으면 멈춘다.", ScriptApiMode.Live),
        new("ResourceText", "리소스글", "name", "프로젝트 리소스를 글(UTF-8)로 읽는다. json·csv·txt.", ScriptApiMode.Live),
        new("ResourceBytes", "리소스바이트", "name", "프로젝트 리소스를 바이트로 읽는다.", ScriptApiMode.Live),
        new("Setting", "설정", "name", "설정 탭에서 만든 칸의 값. 프로젝트 값 → 솔루션 값 → 기본값 순서로 찾는다. C# 은 Setting<int>(\"물약HP\") 도 된다.", ScriptApiMode.Live),
        new("SetSetting", "설정저장", "name, value", "설정 값을 지금 프로젝트에 쓴다. 설정 탭에도 곧바로 보인다.", ScriptApiMode.Live),
        new("SettingList", "설정목록", "name", "목록 칸의 행들. 행은 row[\"HP\"] 로 읽는다.", ScriptApiMode.Live),
        new("HasSetting", "설정있나", "name", "그 이름의 설정 칸이 있는가.", ScriptApiMode.Live),
        new("PlaySound", "소리", "name","프로젝트 리소스의 wav 를 울린다. 끝나기를 기다리지 않는다.", ScriptApiMode.Live),
        new("RunProject", "프로젝트실행", "name", "같은 솔루션의 옆 프로젝트를 그 줄에서 돌리고 끝나면 다음 줄로 돌아온다(함수 부르기처럼 기다린다). 스크립트 화면은 소스를, 플레이 화면은 빌드된 것(bin\\이름.mtsx)을 돈다. 도는 동안은 그 프로젝트의 모델·자리·리소스, 돌아오면 원래 것으로.", ScriptApiMode.Live),
        new("MoveToProject", "프로젝트이동", "name", "지금 스크립트를 끝내고 그 프로젝트로 넘어간다 - 돌아오지 않고 쌓이지 않는다. 메인화면 → 영웅선택 → 사격장 → 다시 메인화면처럼 화면을 넘어가는 흐름에.", ScriptApiMode.Live),
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
