using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using DevExpress.Mvvm;

using Minguk.Tools.Input.Scripting.Projects;
using Minguk.Tools.ViewModels;

namespace Minguk.Tools.Tests;

/// <summary>
/// 프로젝트 작업 공간(<see cref="ScriptProjectWorkspace"/>) - 화면 없이 탐색기·탭 동작을 본다.
/// 새 프로젝트·새 파일·이름 바꾸기(열린 탭이 따라가는지)·저장 안 한 탭 닫기(취소/예)·기존 파일 넣기(복사)·목록에서 빼기·시작 파일.
/// </summary>
internal static partial class Program
{
    private static void TestScriptWorkspace()
    {
        var folder = Path.Combine(Path.GetTempPath(), "minguk-workspace-" + Guid.NewGuid().ToString("N"));
        var answers = new Queue<MessageResult>();
        var edits = new List<string>();

        var workspace = new ScriptProjectWorkspace(new ScriptProjectWorkspaceHost
        {
            OnUi = action => action(),
            Ask = (_, _) => answers.Count > 0 ? answers.Dequeue() : MessageResult.Yes
        });
        workspace.EditNodeRequested += (_, node) => edits.Add(node.Id);

        try
        {
            // ── 새 프로젝트: 이름 폴더를 만들고 시작 파일을 연다 ──
            var project = workspace.CreateProject(Path.Combine(folder, "사격장.mtsproj"), "출력(1);");

            Check("작업 공간: 새 프로젝트는 이름 폴더에 만들고 시작 파일을 탭으로 연다",
                  File.Exists(Path.Combine(folder, "사격장", "사격장.mtsproj")) && workspace.Documents.Count == 1 &&
                  workspace.ActiveDocument?.Text == "출력(1);" && workspace.Nodes.Any(n => n.Id == "main.csx" && n.IsEntry),
                  $"탭 {workspace.Documents.Count}, 줄 {string.Join(", ", workspace.Nodes.Select(n => n.Id.Length == 0 ? "<뿌리>" : n.Id))}");

            // ── 새 파일: 폴더 안에, 겹치지 않는 이름으로, 이름 칸을 연다 ──
            var sub = workspace.AddFolder(ScriptProjectWorkspace.RootId)!;
            workspace.SelectedNode = sub;
            var file = workspace.AddNewFile(workspace.TargetFolderId())!;

            Check("작업 공간: 새 폴더·새 파일은 고른 폴더에 만들고 이름 칸을 연다",
                  sub.Id == "새 폴더" && file.Id == "새 폴더/새 스크립트.csx" && File.Exists(project.FullPath(file.Id)) &&
                  edits.SequenceEqual(["새 폴더", "새 폴더/새 스크립트.csx"]) && workspace.ActiveDocument?.FilePath == project.FullPath(file.Id),
                  $"폴더 {sub.Id}, 파일 {file.Id}, 이름 칸 {string.Join(", ", edits)}");

            // ── 이름 바꾸기: 확장자를 지워도 붙이고, 열린 탭이 따라간다 ──
            workspace.ActiveDocument!.Text = "int 셋() => 3;";
            file.Name = "계산";

            var renamed = workspace.Nodes.FirstOrDefault(n => n.Id == "새 폴더/계산.csx");
            Check("작업 공간: 이름을 바꾸면 확장자를 지키고 열린 탭(저장 안 한 글)이 새 경로를 따라간다",
                  renamed is not null && File.Exists(project.FullPath("새 폴더/계산.csx")) && !File.Exists(project.FullPath("새 폴더/새 스크립트.csx")) &&
                  workspace.ActiveDocument.FilePath == project.FullPath("새 폴더/계산.csx") && workspace.ActiveDocument.IsDirty,
                  $"줄 {renamed?.Id ?? "없음"}, 탭 {workspace.ActiveDocument.FilePath}, 저장 안 함 {workspace.ActiveDocument.IsDirty}");

            // ── 실행·완성이 저장 안 한 글을 본다 ──
            workspace.SnapshotOpenTexts();
            var unit = workspace.UnitFor(project.EntryPath)!;
            Check("작업 공간: 컴파일 한 벌에 저장 안 한 탭의 글이 들어간다",
                  unit.Sources.Any(s => s.EndsWith("계산.csx")) && unit.OpenTexts.TryGetValue(project.FullPath("새 폴더/계산.csx"), out var open) && open == "int 셋() => 3;",
                  $"소스 {string.Join(", ", unit.Sources.Select(Path.GetFileName))}");

            // ── 저장 안 한 탭 닫기: 취소면 남고, 예면 저장하고 닫는다 ──
            var doc = workspace.ActiveDocument;
            answers.Enqueue(MessageResult.Cancel);
            var cancelled = !workspace.CloseDocument(doc) && workspace.Documents.Contains(doc);

            answers.Enqueue(MessageResult.Yes);
            var closed = workspace.CloseDocument(doc) && !workspace.Documents.Contains(doc) && File.ReadAllText(project.FullPath("새 폴더/계산.csx")) == "int 셋() => 3;";

            Check("작업 공간: 저장 안 한 탭은 닫을 때 묻는다 - 취소면 남고, 예면 저장하고 닫는다", cancelled && closed, $"취소 {cancelled}, 예 {closed}");

            // ── 기존 파일 넣기: 밖의 파일은 복사한다(원본은 그대로) ──
            var outside = Path.Combine(folder, "밖", "적.png");
            Directory.CreateDirectory(Path.GetDirectoryName(outside)!);
            File.WriteAllBytes(outside, [1, 2, 3]);

            workspace.SelectedNode = workspace.Nodes.First(n => n.Id == ScriptProject.ResourceFolder);
            var added = workspace.AddExisting([outside], workspace.TargetFolderId());

            Check("작업 공간: 밖의 파일은 고른 폴더로 복사해 리소스로 넣고 원본은 둔다",
                  added.Count == 1 && added[0].Id == "Resources/적.png" && added[0].Kind == ScriptNodeKind.Resource &&
                  File.Exists(project.FullPath("Resources/적.png")) && File.Exists(outside),
                  added.Count > 0 ? $"{added[0].Id} ({added[0].Kind})" : "안 들어감");

            // ── 시작 파일 바꾸기·목록에서 빼기 ──
            workspace.SetEntry(workspace.Nodes.First(n => n.Id == "새 폴더/계산.csx"));
            var entryMoved = project.Entry == "새 폴더/계산.csx" && workspace.Nodes.Single(n => n.IsEntry).Id == "새 폴더/계산.csx";

            answers.Enqueue(MessageResult.OK);
            var excluded = workspace.Exclude(workspace.Nodes.First(n => n.Id == "Resources/적.png")) &&
                           project.Find("Resources/적.png") is null && File.Exists(project.FullPath("Resources/적.png"));

            Check("작업 공간: 시작 파일을 바꾸고, 프로젝트에서 제외는 목록에서만 뺀다(파일은 남는다)", entryMoved && excluded, $"시작 {project.Entry}, 제외 {excluded}");

            // ── 삭제는 휴지통으로(가짜 휴지통으로 본다 - 실제 휴지통을 채우지 않게) ──
            {
                var recycled = new List<string>();
                var trash = new ScriptProjectWorkspace(new ScriptProjectWorkspaceHost
                {
                    OnUi = action => action(),
                    Ask = (_, _) => MessageResult.OK,
                    Recycler = new FakeRecycler(recycled)
                });

                trash.OpenProject(project.FilePath);
                var target = trash.Nodes.First(n => n.Id == "main.csx");
                trash.Open(target);

                var deleted = trash.Delete(target);

                Check("작업 공간: 삭제는 묻고 탭을 닫고 휴지통으로 보낸 뒤 목록에서 뺀다",
                      deleted && recycled.SequenceEqual([project.FullPath("main.csx")]) && trash.Documents.All(d => !d.FilePath.EndsWith("main.csx")) &&
                      ScriptProject.Load(project.FilePath).Find("main.csx") is null,
                      $"삭제 {deleted}, 휴지통 {string.Join(", ", recycled.Select(Path.GetFileName))}");

                trash.Dispose();
                workspace.CloseProject();
                workspace.OpenProject(project.FilePath);
            }

            // ── 끌어다 놓기: 놓은 폴더로 복사하고 목록에 넣는다 ──
            {
                var loose = Path.Combine(folder, "밖", "데이터.json");
                File.WriteAllText(loose, "{}");

                var inner = Path.Combine(folder, "밖", "소리들");
                Directory.CreateDirectory(inner);
                File.WriteAllBytes(Path.Combine(inner, "알림.wav"), [0]);

                var dropped = workspace.Drop([loose, inner], "새 폴더");

                Check("작업 공간: 끌어다 놓은 파일·폴더는 놓은 폴더로 복사해 목록에 넣고 원본은 둔다",
                      File.Exists(project.FullPath("새 폴더/데이터.json")) && File.Exists(project.FullPath("새 폴더/소리들/알림.wav")) &&
                      workspace.Project!.Find("새 폴더/데이터.json")?.Kind == ScriptItemKind.Resource &&
                      workspace.Nodes.Any(n => n.Id == "새 폴더/소리들" && n.IsFolder) && workspace.Nodes.Any(n => n.Id == "새 폴더/소리들/알림.wav") &&
                      File.Exists(loose) && File.Exists(Path.Combine(inner, "알림.wav")),
                      $"들어간 줄 {string.Join(", ", dropped.Select(n => n.Id))}");

                // 같은 이름을 또 놓으면 덮지 않고 숫자를 붙인다.
                workspace.Drop([loose], "새 폴더");
                Check("작업 공간: 같은 이름을 또 놓으면 덮지 않고 이름을 바꿔 넣는다",
                      File.Exists(project.FullPath("새 폴더/데이터1.json")) && workspace.Project!.Find("새 폴더/데이터1.json") is not null,
                      string.Join(", ", workspace.Nodes.Where(n => n.Id.StartsWith("새 폴더/데이터")).Select(n => n.Id)));
            }

            // ── 다시 열기: 저장한 목록 그대로 ──
            workspace.ActiveDocument?.Save();
            workspace.CloseProject();
            workspace.OpenProject(project.FilePath);

            Check("작업 공간: 닫고 다시 열면 목록·시작 파일이 그대로다",
                  workspace.Project?.Entry == "새 폴더/계산.csx" && workspace.Nodes.Any(n => n.Id == "새 폴더/계산.csx") && workspace.ActiveDocument?.FilePath.EndsWith("계산.csx") == true,
                  $"시작 {workspace.Project?.Entry}, 탭 {workspace.ActiveDocument?.FilePath}");

            // ── 폴더 감시: 밖에서 만든 파일·폴더는 목록에 들어오고, 지우면 빠진다 ──
            {
                bool WaitFor(Func<bool> condition)
                {
                    var deadline = DateTime.UtcNow.AddSeconds(5);
                    while (DateTime.UtcNow < deadline) { if (condition()) return true; System.Threading.Thread.Sleep(50); }
                    return condition();
                }

                var outsideFolder = project.FullPath("밖에서");
                Directory.CreateDirectory(Path.Combine(outsideFolder, "안쪽"));
                File.WriteAllText(Path.Combine(outsideFolder, "도우미.csx"), "int 둘() => 2;");
                File.WriteAllText(project.FullPath("임시.tmp"), "x");

                var appeared = WaitFor(() => workspace.Nodes.Any(n => n.Id == "밖에서/도우미.csx") && workspace.Nodes.Any(n => n.Id == "밖에서/안쪽"));
                var kind = workspace.Project!.Find("밖에서/도우미.csx")?.Kind;
                var ignored = workspace.Project.Find("임시.tmp") is null;

                File.Delete(Path.Combine(outsideFolder, "도우미.csx"));
                var gone = WaitFor(() => workspace.Nodes.All(n => n.Id != "밖에서/도우미.csx"));

                Directory.Delete(outsideFolder, recursive: true);
                var folderGone = WaitFor(() => workspace.Nodes.All(n => !n.Id.StartsWith("밖에서")));

                Check("작업 공간: 폴더를 하위까지 지켜보다 생긴 것은 넣고 지운 것은 빼며, 임시 파일은 무시한다",
                      appeared && kind == ScriptItemKind.Source && ignored && gone && folderGone && ScriptProject.Load(project.FilePath).Find("밖에서/도우미.csx") is null,
                      $"들어옴 {appeared}({kind}), 임시 무시 {ignored}, 파일 빠짐 {gone}, 폴더 빠짐 {folderGone}");
            }

            // ── 같은 프로젝트를 연 두 화면: 한쪽이 목록을 고치고 저장하면 다른 쪽이 따라간다(스크립트 화면 ↔ 플레이 화면) ──
            {
                bool WaitFor(Func<bool> condition)
                {
                    var deadline = DateTime.UtcNow.AddSeconds(5);
                    while (DateTime.UtcNow < deadline) { if (condition()) return true; System.Threading.Thread.Sleep(50); }
                    return condition();
                }

                var other = new ScriptProjectWorkspace(new ScriptProjectWorkspaceHost { OnUi = action => action(), Ask = (_, _) => MessageResult.Yes });
                other.OpenProject(project.FilePath);

                var otherChanged = 0;
                other.Changed += (_, _) => System.Threading.Interlocked.Increment(ref otherChanged);

                // 앞 검사에서 main.csx 는 휴지통으로 갔다 - 새 파일로 본다.
                var fresh = workspace.AddNewFile(ScriptProjectWorkspace.RootId, "새시작")!;
                WaitFor(() => other.Nodes.Any(n => n.Id == fresh.Id));

                // 시작 파일만 바꾸면 폴더에는 아무 파일도 안 생긴다 - 프로젝트 파일 변경으로만 알 수 있다.
                workspace.SetEntry(workspace.Nodes.First(n => n.Id == fresh.Id));
                var entryFollowed = WaitFor(() => other.Project!.Entry == fresh.Id && other.Nodes.Single(n => n.IsEntry).Id == fresh.Id);

                // 목록에 있는 파일의 내용만 바꿔도 다시 검사하라고 알린다.
                var before = otherChanged;
                File.AppendAllText(project.FullPath(fresh.Id), "\n// 밖에서 고침\n");
                var contentNoticed = WaitFor(() => otherChanged > before);

                Check("작업 공간: 같은 프로젝트를 연 다른 화면이 시작 파일 변경을 따라가고, 파일 내용이 바뀌면 다시 검사하라고 알린다",
                      entryFollowed && contentNoticed,
                      $"시작 따라감 {entryFollowed} ({other.Project!.Entry}), 내용 알림 {contentNoticed}");

                other.Dispose();
            }

            // ── 플레이 목록: Workspace/솔루션/프로젝트/bin 의 완성품만, '솔루션 / 프로젝트' 이름으로 ──
            {
                var workspaceRoot = Path.Combine(folder, "Workspace");
                Directory.CreateDirectory(Path.Combine(workspaceRoot, "오버워치", "사격장", "bin"));
                Directory.CreateDirectory(Path.Combine(workspaceRoot, "디아2", "카우방", "bin"));
                File.WriteAllText(Path.Combine(workspaceRoot, "오버워치", "사격장", "bin", "사격장.mtsx"), "");
                File.WriteAllText(Path.Combine(workspaceRoot, "디아2", "카우방", "bin", "카우방.mtsx"), "");
                File.WriteAllText(Path.Combine(workspaceRoot, "오버워치", "사격장", "main.csx"), "");   // 원본 소스는 목록에 안 나와야 한다

                var items = PlayViewModel.ListBuilds(workspaceRoot).ToList();

                Check("플레이 목록: 프로젝트마다 bin 의 완성품만 '솔루션 / 프로젝트' 로, 원본 소스(.csx)는 안 나온다",
                      items.Count == 2 && items.All(i => i.IsCompiled)
                      && items[0].Name == "디아2 / 카우방" && items[1].Name == "오버워치 / 사격장",
                      string.Join(", ", items.Select(i => i.Name)));
            }

            // 진짜 휴지통(ShellFileRecycler)은 여기서 안 본다 - --vision 을 돌릴 때마다 사람의 휴지통에 파일이 쌓인다.
            // 2026-09-13 에 한 번 보내 보고 자리에서 사라지는 것을 확인했다.
        }
        finally
        {
            workspace.Dispose();
            try { Directory.Delete(folder, recursive: true); } catch (Exception) { }
        }
    }

    /// <summary>보낸 경로만 적는다. 파일은 그대로 두지 않고 지운다 - 목록 빼기가 뒤따르는지 보려는 것이라.</summary>
    private sealed class FakeRecycler(List<string> recycled) : Minguk.Tools.Helper.IFileRecycler
    {
        public string Name => "가짜 휴지통";

        public void Recycle(string path)
        {
            recycled.Add(Path.GetFullPath(path));
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
