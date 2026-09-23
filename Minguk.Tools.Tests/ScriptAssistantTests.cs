using System;
using System.Linq;
using System.Windows;

using Minguk.Tools.Llm;

namespace Minguk.Tools.Tests;

/// <summary>
/// 스크립트 도우미의 순수한 규칙 - 요청 모양, 답 읽기(상자·이름·동작), 동작마다 적는 코드. 진짜 모델은 <c>--llm --image=</c>.
/// </summary>
/// <remarks>사용자(2026-09-24) "현재 화면 미리보기로 보여주고 오른쪽 위에 버튼 눌러줘. 이러면 스크립트 내용에 순차 적으로 만들어 주는거야".</remarks>
internal static partial class Program
{
    private static void TestScriptAssistant()
    {
        // ── 요청 ──
        var request = ScriptAssistant.Build("qwen2.5vl:3b", "오른쪽 위 설정 버튼 눌러줘", [9, 9], 1024, 576, ["미니맵", "퀘스트"], "키(\"F\");");
        var system = request.Messages[0].Content;
        var user = request.Messages[1];
        var actions = request.Format?["properties"]?["action"]?["enum"]?.AsArray().Select(n => n!.GetValue<string>()).ToArray() ?? [];

        Check("도우미: 요청에 그림 크기·이미 있는 영역·함수 목록(한글 이름)·스크립트 끝·그림이 든다",
              system.Contains("가로 1024, 세로 576") && system.Contains("미니맵, 퀘스트") && system.Contains("그림누르기(resource, [score], [name])")
              && user.Content.Contains("키(\"F\");") && user.Content.Contains("오른쪽 위 설정 버튼") && user.Images?.Count == 1,
              $"system {system.Length}자");

        Check("도우미: 동작은 여섯 가지로 묶는다", actions.SequenceEqual(ScriptAssistant.Actions), string.Join(", ", actions));

        // ── 답 읽기 ──
        var plan = ScriptAssistant.Parse("{\"action\":\"누르기\",\"name\":\"설정 버튼\",\"box\":[960,20,1010,60],\"say\":\"오른쪽 위 설정\"}", 1024, 576);

        Check("도우미: 상자(보낸 그림 픽셀)를 0~1 로 바꾼다",
              plan.Box is { } box && Math.Abs(box.X - (960 / 1024.0)) < 1e-6 && Math.Abs(box.Width - (50 / 1024.0)) < 1e-6 && Math.Abs(box.Y - (20 / 576.0)) < 1e-6,
              $"{plan.Box}");

        var thousand = ScriptAssistant.Parse("{\"action\":\"누르기\",\"name\":\"a\",\"box\":[900,30,950,90],\"say\":\"\"}", 1024, 576, "1000");

        Check("도우미: 0~1000 비율로 내는 모델(boxUnits 1000)도 읽는다", thousand.Box is { } t && Math.Abs(t.X - 0.9) < 1e-6 && Math.Abs(t.Height - 0.06) < 1e-6, $"{thousand.Box}");

        // 상자 없는 누르기·화면 통째 상자는 멈추지 않고 상자만 비워 준다 - 화면이 자리만 다시 묻는다(EnsureBoxAsync).
        var noBox = ScriptAssistant.Parse("{\"action\":\"누르기\",\"name\":\"x\",\"say\":\"\"}", 1024, 576);
        var wholeScreen = ScriptAssistant.Parse("{\"action\":\"누르기\",\"name\":\"x\",\"box\":[0,0,1024,576],\"say\":\"\"}", 1024, 576);
        var located = ScriptAssistant.ParseLocate("[{\"bbox_2d\": [990, 5, 1020, 35], \"label\": \"icon\"}]", 1024, 576);

        Check("도우미: 상자가 없거나 화면 통째면 비워 두고, 자리만 다시 물은 답(bbox_2d, 목록으로 감싼 것도)을 읽는다",
              noBox.Box is null && wholeScreen.Box is null && located is { } l && Math.Abs(l.X - (990 / 1024.0)) < 1e-6,
              $"{located}");

        Check("도우미: 계획 스키마는 action·name·box·say 를 늘 적게 한다(작은 모델이 상자를 빼먹었다)",
              request.Format?["required"]?.AsArray().Select(n => n!.GetValue<string>()).SequenceEqual(["action", "name", "box", "say"]) == true, "");

        var refusals = new[]
        {
            "{\"action\":\"춤추기\",\"say\":\"\"}",                                                // 모르는 동작
            "{\"action\":\"키\",\"say\":\"\"}",                                                    // 키 없음
            "그런 버튼은 안 보입니다"                                                              // JSON 아님
        };
        var refused = refusals.Count(text => { try { ScriptAssistant.Parse(text, 1024, 576); return false; } catch (LlmException) { return true; } });

        Check("도우미: 모르는 동작·키 없는 키·글 답은 받지 않는다", refused == refusals.Length, $"{refused}/{refusals.Length}");

        // ── 코드 ──
        var none = new AssistantPlan("", "", null, "", 0, "", "", "");
        var lines = new[]
        {
            ScriptAssistant.Render(none with { Action = ScriptAssistant.Press, Say = "설정" }, "설정버튼"),
            ScriptAssistant.Render(none with { Action = ScriptAssistant.WaitFor, Ms = 3000 }, "보상창"),
            ScriptAssistant.Render(none with { Action = ScriptAssistant.Key, Key = "F" }, ""),
            ScriptAssistant.Render(none with { Action = ScriptAssistant.Sleep, Ms = 500 }, ""),
            ScriptAssistant.Render(none with { Action = ScriptAssistant.Type, Text = "/초대 \"친구\"" }, "")
        };

        Check("도우미: 동작마다 이미 있는 함수로 적는다(그림누르기·그림있나 반복·키·쉬기·줄입력, 따옴표는 막는다)",
              lines[0] == "그림누르기(\"설정버튼.png\");   // 설정"
              && lines[1] == "for (var 번 = 0; 번 < 30 && !그림있나(\"보상창.png\"); 번++) 쉬기(100);"
              && lines[2] == "키(\"F\");" && lines[3] == "쉬기(500);" && lines[4] == "줄입력(\"/초대 \\\"친구\\\"\");",
              string.Join(" | ", lines));

        // ── 이름 ──
        var taken = new[] { "설정버튼", "설정버튼2" };

        Check("도우미: 이름은 공백·점을 빼고, 겹치면 번호를 붙이고, 비면 「버튼」",
              ScriptAssistant.CleanName("설정 버튼.", n => taken.Contains(n)) == "설정버튼3" && ScriptAssistant.CleanName(" .. ", _ => false) == "버튼",
              ScriptAssistant.CleanName("설정 버튼.", n => taken.Contains(n)));
    }
}
