using System;
using System.Collections.Generic;
using System.Linq;

namespace Minguk.Tools.Input.Scripting;

/// <summary>도움말 한 줄. 분류로 묶어 보이고, 고르면 아래에 설명·예시가 뜬다.</summary>
/// <param name="Group">분류(그리드 묶음). 앞의 숫자가 순서다.</param>
/// <param name="Name">보이는 이름 - 함수면 "키(name)".</param>
/// <param name="English">영문 이름(같은 일을 한다). 없으면 빈 글.</param>
/// <param name="Summary">설명.</param>
/// <param name="Example">예시 코드(C#). 없으면 빈 글.</param>
public sealed record ScriptHelpRow(string Group, string Name, string English, string Summary, string Example)
{
    public bool HasExample => Example.Length > 0;

    /// <summary>설명 칸 머리 - "키(name) · Key(name)".</summary>
    public string Title => English.Length > 0 ? $"{Name}  ·  {English}" : Name;
}

/// <summary>
/// 스크립트 화면 도움말 패널의 내용 - 기본 사용법 + 부를 수 있는 함수 전부 + 검출의 속성.
/// </summary>
/// <remarks>
/// 함수 줄은 <see cref="ScriptApiCatalog"/> 에서 뽑는다(사용자, 2026-09-15 - "문법 도움말이 있어야"). 이름·설명을 여기 또 적으면
/// 완성 목록과 도움말이 어긋난다. 여기서 더하는 것은 분류와 예시뿐이다. 표에 새 함수를 넣고 분류를 안 적으면 「기타」 로 뜬다.
/// </remarks>
public static class ScriptHelp
{
    private const string Basics = "01. 기본 사용법";
    private const string Keyboard = "02. 키보드";
    private const string Mouse = "03. 마우스";
    private const string Detections = "04. 검출";
    private const string DetectionProperties = "05. 검출의 속성 (검출.이름 처럼)";
    private const string Screen = "06. 화면 글자 읽기";
    private const string Minimap = "07. 미니맵 · 방향";
    private const string Llm = "08. LLM 판단";
    private const string Flow = "09. 흐름 · 출력";
    private const string Resources = "10. 리소스 · 설정";

    /// <summary>
    /// 분류를 안 적은 함수가 모이는 자리. 번호를 크게 두어 어떤 정렬에서도 맨 끝이고, 분류를 하나 더해도 번호가 안 겹친다.
    /// </summary>
    /// <remarks>
    /// 검사(<c>--script-screen</c>)가 이 이름으로 「분류 안 된 함수」를 센다 - 글자를 박아 두면 분류를 늘릴 때 어긋난다
    /// (미니맵 분류를 넣어 리소스가 9번이 되자 검사가 리소스 8줄을 기타로 셌다, 2026-09-23).
    /// </remarks>
    public const string Other = "99. 기타";

    /// <summary>함수 영문 이름 → 분류.</summary>
    private static readonly Dictionary<string, string> GroupOf = new(StringComparer.Ordinal)
    {
        ["Type"] = Keyboard, ["TypeLine"] = Keyboard, ["Enter"] = Keyboard, ["ToggleHangul"] = Keyboard,
        ["Key"] = Keyboard, ["KeyDown"] = Keyboard, ["KeyUp"] = Keyboard, ["Walk"] = Keyboard,

        ["Click"] = Mouse, ["RightClick"] = Mouse, ["ClickAt"] = Mouse, ["MoveTo"] = Mouse, ["Scroll"] = Mouse,
        ["Aim"] = Mouse, ["MoveBy"] = Mouse, ["MoveCursor"] = Mouse, ["SetMouseMode"] = Mouse, ["SetAimZone"] = Mouse, ["SetAimLead"] = Mouse, ["SetAimConfirm"] = Mouse, ["MouseDown"] = Mouse, ["MouseUp"] = Mouse, ["Drag"] = Mouse, ["DragBy"] = Mouse,

        ["Detections"] = Detections, ["NearestDetection"] = Detections, ["TargetDetection"] = Detections, ["ReleaseTarget"] = Detections, ["WaitDetection"] = Detections,

        ["ReadText"] = Screen, ["ReadNumber"] = Screen, ["ReadNumbersAt"] = Screen, ["HasTextAt"] = Screen, ["FindText"] = Screen, ["FindTexts"] = Screen, ["ReadLines"] = Screen, ["RunInBackground"] = Screen, ["BarFillAt"] = Screen, ["BrightnessAt"] = Screen, ["PressText"] = Screen, ["FindImage"] = Screen, ["HasImage"] = Screen, ["ImageScore"] = Screen, ["PressImage"] = Screen, ["PressRegion"] = Screen, ["RegionSpot"] = Screen, ["ScreenSize"] = Screen, ["HitConfirmed"] = Screen, ["HealthBar"] = Detections, ["HitByHealthBar"] = Detections,
        ["Ammo"] = Screen, ["AmmoMax"] = Screen, ["Health"] = Screen, ["HealthMax"] = Screen, ["Ultimate"] = Screen, ["UltimateReady"] = Screen,

        ["Heading"] = Minimap, ["TargetBearing"] = Minimap, ["TargetBearings"] = Minimap, ["BearingTo"] = Minimap,
        ["Face"] = Minimap, ["GoTo"] = Minimap, ["GoToTarget"] = Minimap, ["TurnScale"] = Minimap,
        ["MarkerBearing"] = Minimap, ["MarkerBearings"] = Minimap, ["GoToMarker"] = Minimap, ["WalkToMarker"] = Minimap, ["WalkPathToMarker"] = Minimap, ["IgnoreNearestMarker"] = Minimap,

        ["SituationSummary"] = Llm, ["Judge"] = Llm, ["JudgeScreen"] = Llm, ["Ask"] = Llm, ["AskScreen"] = Llm, ["CorrectJudgement"] = Llm, ["SetJudgeRules"] = Llm,

        ["Wait"] = Flow, ["IsStopped"] = Flow, ["Print"] = Flow, ["Watch"] = Flow, ["Stop"] = Flow, ["RunProject"] = Flow, ["MoveToProject"] = Flow,

        ["ResourcePath"] = Resources, ["ResourceText"] = Resources, ["ResourceBytes"] = Resources, ["PlaySound"] = Resources,
        ["Setting"] = Resources, ["SetSetting"] = Resources, ["SettingList"] = Resources, ["HasSetting"] = Resources
    };

    /// <summary>함수 영문 이름 → 예시.</summary>
    private static readonly Dictionary<string, string> ExampleOf = new(StringComparer.Ordinal)
    {
        ["Heading"] = "출력(방위());   // 0=북 · 90=동 · 180=남 · 270=서",
        ["TargetBearing"] = "var 목표 = 목표방위();\nif (목표 != null) 출력(목표);   // 방위 237도 · 거리 105px · 돌 각 -14도",
        ["TargetBearings"] = "foreach (var 표 in 목표방위들()) 출력(표);",
        ["MarkerBearing"] = "var 몹 = 마커방위(\"선공몹,일반몹\");\nif (몹 != null) 출력(몹);   // 미니맵 빨간·흰 점 가운데 가장 가까운 것 - 방위·거리·돌 각",
        ["MarkerBearings"] = "출력($\"미니맵 선공몹 {마커방위들(\"선공몹\").Count}마리\");",
        ["RunInBackground"] = "따로돌리기(\"줍기\", 200, () =>\n{\n    if (읽기(\"줍기\").Contains(\"줍기\")) 키(\"F\");\n});   // 본 흐름(공격)과 따로 0.2초마다",
        ["ReadLines"] = "foreach (var 줄 in 글줄들(\"시야\")) 출력(줄);   // 한 번 읽고 모든 줄",
        ["FindTexts"] = "foreach (var 곳 in 글자찾기들(\"연구원\")) 출력(곳);   // 대상 칸과 몹 머리 위 이름표 둘 다",
        ["BarFillAt"] = "if (채움(\"대상체력바\") < 0.02) 출력(\"대상 체력바가 비었습니다\");   // 0~1",
        ["BrightnessAt"] = "if (밝기(\"창단추\") < 128) 출력(\"단추가 꺼져 있습니다\");   // 0~255",
        ["IgnoreNearestMarker"] = "마커무시(\"선공몹,일반몹\", 5000);   // 방금 잡은 몹 점(가장 가까운 것)을 5초 동안 뺀다",
        ["WalkToMarker"] = "if (!마커로걷기(\"선공몹,일반몹\", 2000)) 출력(\"미니맵에 몹이 없거나 막혔습니다\");   // 키보드로만 가장 가까운 몹 점 쪽으로 2초",
        ["WalkPathToMarker"] = "if (!길찾아걷기(\"퀘스트,목표\", 3000)) 출력(\"미니맵에 표시가 없거나 막혔습니다\");   // 바닥만 밟아 퀘스트 표시 쪽으로 3초",
        ["GoToMarker"] = "if (!마커로가기(\"선공몹\", 2000)) 출력(\"미니맵에 몹이 없습니다\");   // 가장 가까운 빨간 점 쪽으로 2초",
        ["BearingTo"] = "if (Math.Abs(방위차(90)) > 10) 바라보기(90);",
        ["Face"] = "바라보기(180);   // 남쪽을 보도록 마우스를 돌린다(한 번짜리)",
        ["GoTo"] = "가기(237, 3000);   // 남서쪽으로 3초 걷는다 - 걷는 동안 미니맵을 보며 고친다",
        ["GoToTarget"] = "if (!목표로가기(5000)) 출력(\"미니맵에 목표 표시가 없습니다\");",
        ["TurnScale"] = "출력(회전배율());   // 도/카운트. 모르면 그 자리에서 재서 적어 둔다",

        ["SituationSummary"] = "var 상황 = 상황요약(\"지역, 퀘스트, 체력, 검출\");\n출력(상황);   // 지역: 붉은 가시 왕관섬\\n퀘스트: …\\n검출: 늑대 2(가장 가까운 것 왼쪽 가까이)",
        ["Judge"] = "if (숫자읽기(\"체력\") < 30) { 키(\"F1\"); continue; }   // 분명한 규칙은 스크립트가 먼저\n\nvar 할일 = 판단(상황요약(\"퀘스트, 검출, 가방\"), \"공격, 줍기, 대화, 사냥터로이동\");\nif (할일 == \"공격\") 키(\"1\");",
        ["JudgeScreen"] = "var 창 = 화면판단(\"지금 뜬 창은 무엇인가? 글: \" + 읽기(\"창제목\"), \"퀘스트수락, 보상, 상점, 없음\", \"가운데창\");",
        ["Ask"] = "출력(물어보기(\"이 퀘스트 글을 한 줄로 줄여 줘: \" + 읽기(\"퀘스트\")));",
        ["AskScreen"] = "출력(화면물어보기(\"화면에 몬스터가 있나? 있으면 몇 마리?\"));",
        ["CorrectJudgement"] = "판단고치기(\"체력 90%. 가방 80/80. 몬스터 없음.\", \"귀환\");   // 다음부터 예시로 넣는다",
        ["SetJudgeRules"] = "판단규칙(\"1. 체력 30% 아래면 물약\\n2. 가방이 가득 차면 귀환\");",

        ["Type"] = "글자(\"안녕하세요\");",
        ["TypeLine"] = "줄입력(\"/초대 친구\");",
        ["Key"] = "키(\"R\");\n키(\"Ctrl+Shift+1\");",
        ["KeyDown"] = "누르기(\"Shift\");\n걷기(\"W\", 500);\n떼기(\"Shift\");",
        ["Walk"] = "걷기(\"W\", 500);      // 0.5초 앞으로\n걷기(\"W+A\", 300);    // 대각선",
        ["Click"] = "클릭();\n클릭(\"Right\");\n클릭(100);            // 0.1초 누르고 떼기",
        ["ClickAt"] = "이동클릭(960, 540, \"Left\");",
        ["Aim"] = "var 검출 = 목표();\nif (검출 is not null && 조준(검출))   // 스레드가 계속 따라간다\n    클릭();",
        ["DragBy"] = "상대끌기(200, 0, \"Right\");   // 카메라 오른쪽으로",
        ["Detections"] = "foreach (var 검출 in 검출들())\n    출력(검출);",
        ["NearestDetection"] = "var 검출 = 가장가까운검출();\nif (검출 is not null) 이동(검출.중심x, 검출.중심y);",
        ["TargetDetection"] = "var 검출 = 목표();          // 잡을 때까지 같은 검출\nif (검출 is null) { 쉬기(50); continue; }",
        ["ReleaseTarget"] = "if (쏜발수 >= 15) 목표풀기();",
        ["WaitDetection"] = "var 검출 = 검출기다리기(3000);\nif (검출 is null) 출력(\"3초 동안 못 봤다\");",
        ["ReadText"] = "var 글 = 읽기(\"퀘스트\");            // 영역 패널에서 만든 이름\nvar 글2 = 읽기(0.4, 0.9, 0.2, 0.05); // 비율 자리",
        ["ReadNumber"] = "var 탄약 = 숫자읽기(\"탄약\");\nif (탄약 is not null && 탄약 <= 3) 키(\"R\");",
        ["HasTextAt"] = "while (글자있나(\"재장전중\")) 쉬기(100);",
        ["FindText"] = "if (글자찾기(\"사격장\") is { } 자리)\n    이동클릭(자리.x, 자리.y);",
        ["PressText"] = "if (!글자누르기(\"사격장\")) 출력(\"사격장 단추가 안 보인다\");",
        ["FindImage"] = "if (그림찾기(\"사격장.png\") is { } 자리)\n    출력($\"{자리.x}, {자리.y} 닮음 {자리.닮음:0.00}\");",
        ["HasImage"] = "if (그림있나(\"궁극기준비.png\", 0.9, \"궁극기자리\")) 키(\"Q\");",
        ["ImageScore"] = "출력($\"닮음 {그림닮음(\"사격장.png\"):0.00}\");   // 문턱을 잡을 때",
        ["HitByHealthBar"] = "var 전 = 체력바(검출);\n클릭();\nif (명중했나(검출, 전) == false)   // 0.3초 안에 체력바가 안 줄면 빗나감\n    빗나감++;",
        ["HealthBar"] = "출력($\"체력 {체력바(검출):P0}\");",
        ["HitConfirmed"] = "클릭();\nif (!명중확인(\"히트마커.png\"))   // 0.3초 안에 히트 마커가 안 뜨면 빗나감\n    빗나감++;",
        ["PressImage"] = "if (!그림누르기(\"사격장.png\")) 출력(\"사격장 카드가 안 보인다\");",
        ["PressRegion"] = "영역누르기(\"시작버튼\");            // 영역 패널에서 만든 자리의 가운데를 그냥 누른다\n영역누르기(\"메뉴.닫기\", \"Right\");",
        ["RegionSpot"] = "var 자리 = 영역자리(\"시작버튼\");\n출력($\"{자리.x}, {자리.y} 크기 {자리.너비}x{자리.높이}\");",
        ["ScreenSize"] = "var (너비, 높이) = 화면크기();\nif (너비 != 1920 || 높이 != 1080) { 출력($\"해상도가 {너비}x{높이} 입니다 - 1920x1080 으로 맞추세요\"); 끝(); }",
        ["Wait"] = "쉬기(300);",
        ["IsStopped"] = "while (!중지되었나())\n{\n    // 반복할 일\n    쉬기(10);\n}",
        ["Print"] = "출력($\"탄약 {숫자읽기(\"탄약\")}\");",
        ["Watch"] = "보기(\"쏜 발수\", 쏜발수);",
        ["ResourceText"] = "var 표 = 리소스글(\"검출표.json\");",
        ["Setting"] = "var hp = 설정<int>(\"물약HP\");     // 설정 탭에서 만든 칸\nif (체력() < hp) 키(\"1\");",
        ["SetSetting"] = "설정저장(\"사냥횟수\", 설정<int>(\"사냥횟수\") + 1);",
        ["SettingList"] = "foreach (var 줄 in 설정목록(\"물약목록\"))\n    if (줄.Get<bool>(\"켜기\")) 출력(줄[\"키\"]);",
        ["PlaySound"] = "소리(\"알림.wav\");",
        ["RunProject"] = "프로젝트실행(\"공용설정\");   // 끝나면 다음 줄로 돌아온다\n출력(\"돌아왔다\");",
        ["MoveToProject"] = "if (그림누르기(\"확인.png\"))\n    프로젝트이동(\"사격장\");   // 여기서 끝나고 사격장으로 넘어간다"
    };

    public static IReadOnlyList<ScriptHelpRow> Rows { get; } = Build();

    private static IReadOnlyList<ScriptHelpRow> Build()
    {
        var rows = new List<ScriptHelpRow>
        {
            new(Basics, "문법", "", "C# 스크립트 문법 그대로다 - 변수(var), if · for · foreach · while, 함수(void 이름() { }), 클래스.\n" +
                                    "부르는 이름은 한글과 영문이 같은 일을 한다(키 = Key). 치다가 Ctrl+Space 로 이름을 고를 수 있다.",
                "var 횟수 = 0;\nif (횟수 < 3) 출력(\"세 번 전\");"),
            new(Basics, "시작 파일", "", "실행하면 시작 파일이 돈다 - 솔루션 탐색기에서 굵게 보이는 파일. 바꾸려면 파일에서 오른쪽 버튼 > 시작 파일로 설정.", ""),
            new(Basics, "다른 파일의 함수 부르기", "",
                "같은 프로젝트의 다른 .csx 에 만든 함수·변수는 이름으로 바로 부른다(#load 를 적지 않아도 된다).\n" +
                "다른 파일의 함수 밖 문장은 시작 파일보다 먼저 돈다 - 다른 파일에는 함수만 두고, 흐름은 시작 파일에서 부른다.",
                "// 도우미.csx\nvoid 재장전()\n{\n    키(\"R\");\n    쉬기(300);\n}\n\n// main.csx\n재장전();"),
            new(Basics, "여러 프로젝트가 같이 쓰는 함수", "",
                "솔루션에 공유 프로젝트를 만들고(솔루션 탭 [새 공유 프로젝트]), 쓸 프로젝트의 솔루션 탐색기에서 추가 > 공유 프로젝트 참조. 그 뒤로는 같은 프로젝트의 함수처럼 부른다.", ""),
            new(Basics, "계속 반복하기", "",
                "중지되었나() 를 반복문 조건에 둔다 - 중지나 비상 정지를 누르면 빠져나온다. 반복 안에 쉬기() 를 조금 두면 CPU 를 덜 쓴다.",
                "while (!중지되었나())\n{\n    var 검출 = 목표();\n    if (검출 is null) { 쉬기(50); continue; }\n\n    if (조준(검출)) 클릭();\n}"),
            new(Basics, "실행 · 멈추기", "",
                "F5 실행/계속 · F6 중지 · F10 한 줄씩 · F9 중단점 · Ctrl+Shift+B 빌드(bin 에 .mtsx - 플레이 화면에서 돌린다).\n" +
                "F5·F6·F10 은 게임 창이 앞에 있어도 먹는다. 도는 동안 Pause 는 비상 정지 - 멈추고 누르고 있던 키·버튼을 모두 뗀다.", ""),
            new(Basics, "화면의 영역에 이름 붙이기", "",
                "영역 패널에서 [새 영역] 으로 만들고 이름을 붙인다. 따로 읽을 부분은 [새 구역] 으로 나누고 읽기(\"영역.구역\") 으로 부른다. 미리보기에서 옮기고 손잡이로 크기를 맞춘다(영역 보기 켬 + 입력 전달 끔).\n" +
                "스크립트에서는 그 이름으로 읽는다 - 읽기(\"이름\") · 숫자읽기(\"이름\") · 글자있나(\"이름\").",
                "var 탄약 = 숫자읽기(\"탄약\");"),
            new(Basics, "리소스 파일", "", "프로젝트의 Resources 폴더에 넣은 파일(그림·소리·json)은 이름으로 부른다 - 리소스경로 · 리소스글 · 소리.", "소리(\"알림.wav\");"),
            new(Basics, "파이썬 · 자바스크립트", "", "아래 함수들을 같은 이름으로 부른다. 변수·반복·함수 문법은 그 언어의 것이다(예시는 C#).", "")
        };

        foreach (var entry in ScriptApiCatalog.Entries)
        {
            var group = GroupOf.TryGetValue(entry.Name, out var known) ? known : Other;
            var summary = entry.Mode == ScriptApiMode.Live ? entry.Summary : entry.Summary + "\n(입력 테스트의 계획 모드에서도 된다)";

            rows.Add(new ScriptHelpRow(group, entry.KoreanSignature, entry.Signature, summary, ExampleOf.GetValueOrDefault(entry.Name, string.Empty)));
        }

        rows.AddRange(
        [
            new(DetectionProperties, "검출.이름", "Name", "검출 이름(라벨링에서 붙인 검출 이름).", "if (검출.이름 == \"허수아비\") 클릭();"),
            new(DetectionProperties, "검출.중심x · 검출.중심y", "CenterX · CenterY", "사각형 가운데 - 화면 픽셀이라 이동()·이동클릭() 에 그대로 넣는다.", "이동(검출.중심x, 검출.중심y);"),
            new(DetectionProperties, "검출.머리x · 검출.머리y", "HeadX · HeadY", "머리 자리(위에서 22% 내려온 곳). 조준(검출) 이 겨누는 곳이다.", "출력(검출.머리y);"),
            new(DetectionProperties, "검출.너비 · 검출.높이", "Width · Height", "사각형 크기(픽셀). 크면 가깝다.", "if (검출.높이 > 200) 걷기(\"S\", 300);"),
            new(DetectionProperties, "검출.점수", "Score", "찾은 확신(0~1).", "if (검출.점수 < 0.5) continue;"),
            new(DetectionProperties, "검출.이름표", "Caption", "머리 위 글자 - 부를 때 지금 화면에서 사각형 위를 잘라 읽는다. 이름표가 없는 게임이거나 못 읽으면 빈 글.", "if (검출.이름표.Contains(\"보스\")) 소리(\"경고.wav\");")
        ]);

        return rows.OrderBy(r => r.Group, StringComparer.Ordinal).ToList();
    }
}
