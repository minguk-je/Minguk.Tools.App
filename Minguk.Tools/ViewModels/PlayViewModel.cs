using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

using DevExpress.Mvvm;
using DevExpress.Mvvm.POCO;

using Minguk.Base.Utilities;
using Minguk.Image;
using Minguk.Tools.Input;
using Minguk.Tools.Input.Hotkeys;
using Minguk.Tools.Input.Scripting;

namespace Minguk.Tools.ViewModels;

/// <summary>스크립트 폴더의 파일 하나(또는 프로젝트·빌드 결과물). 콤보에 이름만 보이고 실제로는 경로를 든다.</summary>
/// <param name="IsProject">스크립트 프로젝트(<c>.mtsproj</c>)인가. 그러면 여러 파일·리소스를 한 벌로 돌린다.</param>
/// <param name="IsCompiled">빌드된 것(<c>.mtsx</c>, IL)인가. 소스가 아니라 로드해서 실행만 한다.</param>
public sealed record ScriptFileItem(string Name, string Path, bool IsProject = false, bool IsCompiled = false)
{
    public override string ToString() => Name;

    public static ScriptFileItem From(string path) => IsProjectPath(path)
        ? new ScriptFileItem($"{System.IO.Path.GetFileNameWithoutExtension(path)} (프로젝트)", path, IsProject: true)
        : ScriptFiles.IsCompiledPath(path)
            ? new ScriptFileItem($"{System.IO.Path.GetFileNameWithoutExtension(path)} (빌드됨)", path, IsCompiled: true)
            : new ScriptFileItem(System.IO.Path.GetFileName(path), path);

    public static bool IsProjectPath(string path)
        => string.Equals(System.IO.Path.GetExtension(path), Input.Scripting.Projects.ScriptProject.Extension, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// 플레이 화면. 게임 창을 연결하고, 미리보기를 켰다 껐다 하고, 저장해 둔 스크립트를 골라 돌린다.
/// <b>편집은 없다</b> - 다른 PC 에서는 이 화면만 연다.
/// </summary>
/// <remarks>
/// 스크립트 화면과 같은 바탕(잡기·보기)과 같은 실행기를 쓰고 껍데기만 다르다. 스크립트 화면에서 되던 것이
/// 여기서 안 되는 일이 없게. 스크립트는 <see cref="ScriptFiles.DefaultDirectory"/> 에서 고른다 -
/// 스크립트 화면이 저장하는 자리다.
///
/// 담기(F8)는 하지 않는다. 여기서 F8 을 쥐고 있으면 같이 열린 캡처 화면의 등록이 실패한다.
/// 대신 F5(1회)·F6(반복/중지)을 쥔다 - 게임이 앞에 있어야 입력이 들어가므로 앱 밖에서 누를 수단이 있어야 한다.
/// </remarks>
public partial class PlayViewModel : RecognizingCaptureViewModelBase
{
    public static PlayViewModel Create() => ViewModelSource.Create(() => new PlayViewModel());

    private readonly List<HotkeyClaim> _hotkeyClaims = [];

    /// <summary>고른 스크립트 문서. 여기서는 읽기만 한다(파일 → 계획 → 틀린 줄).</summary>
    public ScriptWorkbench Script { get; }

    public ScriptPlayer Player { get; }

    /// <summary>실시간 실행에 필요한 것들 - 출력 칸, 비상 정지, API 에 빌려 줄 것.</summary>
    public LiveScriptSession Live { get; }

    /// <summary>스크립트 폴더의 파일들.</summary>
    public ObservableCollection<ScriptFileItem> Scripts { get; } = [];

    public ScriptFileItem? SelectedScript
    {
        get => GetProperty(() => SelectedScript);
        set => SetProperty(() => SelectedScript, value, OnSelectedScriptChanged);
    }

    public DelegateCommand RefreshScriptsCommand { get; }

    public DelegateCommand BrowseScriptCommand { get; }

    /// <summary>담기는 캡처 화면의 일이다. 여기서 F8 을 쥐면 그쪽 등록이 실패한다.</summary>
    protected override bool SupportsCollecting => false;

    public PlayViewModel()
    {
        Caption = "플레이";
        CaptionImage = FreeImage.Instance?.CacheImageSource("axialis/multimedia/16x16/button_green_play.png");

        Script = new ScriptWorkbench(new ScriptWorkbenchHost
        {
            GetSetting = (key, fallback) => GetSetting(key, fallback),
            SetSetting = (key, value) => SetSetting(key, value),
            OnUi = RunOnUi,
            IsLive = true
        });

        Live = new LiveScriptSession(
            () => _inputRouter is null ? null : new InputService(_inputRouter.InputAdapter),
            () => _inputRouter?.InputAdapter.RequiresForegroundTarget ?? true,
            () => SelectedTarget,
            OcrEngineForScripts,
            () => RegionBook,
            ActivateTargetAsync,
            RunOnUi,
            message => RunOnUi(() => StatusText = message));

        Player = new ScriptPlayer(ResolveRun);

        RefreshScriptsCommand = new DelegateCommand(RefreshScripts, () => Player.IsIdle, false);
        BrowseScriptCommand = new DelegateCommand(DoBrowseScript, () => Player.IsIdle, false);

        Player.RunningChanged += (_, _) =>
        {
            RefreshScriptsCommand.RaiseCanExecuteChanged();
            BrowseScriptCommand.RaiseCanExecuteChanged();
        };
    }

    private static void RunOnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }

    private ScriptRunContext? ResolveRun()
    {
        if (SelectedScript is null && string.IsNullOrEmpty(Script.FilePath) && !Script.IsProject)
        {
            MessengerUtility.SendMainMessage("돌릴 스크립트를 먼저 고르세요.");
            return null;
        }

        if (Script.HasError)
        {
            MessengerUtility.SendMainMessage("스크립트에 고칠 줄이 있습니다. 스크립트 화면에서 고치세요.");
            return null;
        }

        return Live.Resolve(Script, Player);
    }

    private async Task ActivateTargetAsync()
    {
        if (_inputRouter?.TryFocusTargetWindow() == true)
            await Task.Delay(ActivationSettleDelayMs);
    }

    // ── 스크립트 고르기 ──────────────────────────────────────────────────

    /// <summary>스크립트 폴더를 다시 훑는다. 고르고 있던 파일이 그대로 있으면 선택을 지킨다.</summary>
    private void RefreshScripts() => Guard(() =>
    {
        var chosen = SelectedScript?.Path;

        Scripts.Clear();

        foreach (var item in ListScripts())
            Scripts.Add(item);

        SelectedScript = Scripts.FirstOrDefault(s => string.Equals(s.Path, chosen, StringComparison.OrdinalIgnoreCase));

        StatusText = Scripts.Count == 0
            ? $"완성품이 없습니다. 빌더의 스크립트 탭에서 빌드(Ctrl+Shift+B)하면 여기 보입니다 (작업공간 {Vision.ProjectPaths.Root})."
            : $"완성품 {Scripts.Count}개 (작업공간 {Vision.ProjectPaths.Root})";
    });

    private static IEnumerable<ScriptFileItem> ListScripts() => ListBuilds(Vision.ProjectPaths.Root);

    /// <summary>
    /// 작업공간 아래 프로젝트마다 빌드한 완성품(<c>작업공간/솔루션/프로젝트/bin/*.mtsx</c>) - <c>오버워치 / 사격장</c> 으로 이름을 붙인다.
    /// </summary>
    /// <remarks>
    /// 플레이는 빌드한 완성품만 돌린다(사용자 결정 2026-09-14). 고치는 동안의 시험은 스크립트 탭 F5 로 한다. 다른 파일은 "열기" 로 고른다.
    /// 완성품은 파일 하나라 따로 모아 두지 않고 프로젝트 폴더의 bin 에 둔다 - 한때 솔루션 안 Player 폴더로 모았다가 되돌렸다.
    /// </remarks>
    public static IEnumerable<ScriptFileItem> ListBuilds(string workspace)
    {
        if (!Directory.Exists(workspace)) return [];

        return Directory.EnumerateDirectories(workspace)
            .SelectMany(solution => Directory.EnumerateDirectories(solution)
                .Select(project => (Solution: Path.GetFileName(solution), Project: Path.GetFileName(project), Bin: Path.Combine(project, "bin"))))
            .Where(item => Directory.Exists(item.Bin))
            .SelectMany(item => Directory.EnumerateFiles(item.Bin, "*" + ScriptFiles.CompiledExtension)
                .Select(path => new ScriptFileItem($"{item.Solution} / {item.Project}", path, IsCompiled: true)))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// 몹 찾기 모델·이름 붙인 자리를 읽을 폴더 - 고른 완성품의 프로젝트 폴더다(<c>사격장/bin/사격장.mtsx</c> 면 <c>사격장</c>).
    /// </summary>
    /// <remarks>
    /// 완성품이 bin 안에 있으면 그 위가 프로젝트 폴더고 모델·영역이 거기 있다. 열기로 딴 데서 고른 .mtsx 면 그 폴더를 본다.
    /// 완성품을 안 골랐으면 바탕 자리(Builder 에서 고른 프로젝트)를 쓴다.
    /// </remarks>
    protected override string RecognitionRoot
    {
        get
        {
            if (SelectedScript is not { IsCompiled: true } compiled || Path.GetDirectoryName(compiled.Path) is not { } folder)
                return base.RecognitionRoot;

            return string.Equals(Path.GetFileName(folder), "bin", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(folder) ?? folder
                : folder;
        }
    }

    /// <summary>
    /// 고른 것을 연다. 프로젝트면 작업 공간으로(여러 파일·리소스를 한 벌로 검사·실행), 파일이면 예전처럼 한 파일짜리로.
    /// </summary>
    private void OnSelectedScriptChanged() => Guard(() =>
    {
        if (SelectedScript is null) return;

        if (SelectedScript.IsCompiled)
        {
            // 빌드된 것: 소스 없이 IL 을 로드해 돌린다. 프로젝트를 열어 두었으면 닫는다.
            Script.Project.CloseProject();
            Script.LoadCompiled(SelectedScript.Path);

            // 이름 붙인 자리는 완성품 옆 regions.json 을 다시 읽는다. 몹 찾기 모델은 켜 둔 채면 자리가 바뀐 것을 보고 곧바로 다시 읽는다.
            LoadRegions();

            StatusText = $"완성품: {SelectedScript.Name}";
            return;
        }

        if (SelectedScript.IsProject)
        {
            Script.Project.OpenProject(SelectedScript.Path);

            var project = Script.Project.Project!;
            var sources = project.Items.Count(i => i.Kind == Input.Scripting.Projects.ScriptItemKind.Source);

            StatusText = string.IsNullOrEmpty(project.Entry)
                ? $"프로젝트 '{project.Name}' - 시작 파일이 없습니다. 스크립트 화면 솔루션 탐색기에서 '시작 파일로 설정' 을 고르세요."
                : $"프로젝트 '{project.Name}' · 시작 {project.Entry} · 소스 {sources}개";
            return;
        }

        // 한 파일짜리로 돌아간다. 열려 있던 프로젝트는 닫는다(여기서는 고친 것이 없어 묻지 않는다).
        Script.Project.CloseProject();
        Script.LoadFile(SelectedScript.Path);
        StatusText = $"스크립트: {SelectedScript.Name}";
    });

    /// <summary>폴더 밖의 파일을 고른다. 목록에 넣고 고른다.</summary>
    private void DoBrowseScript() => Guard(() =>
    {
        var dialog = OpenFileDialogService;

        var extension = Input.Scripting.Projects.ScriptProject.Extension;
        dialog.Filter = $"스크립트 프로젝트 (*{extension})|*{extension}|" + ScriptFiles.OpenFilter(Script.SelectedLanguage);
        dialog.InitialDirectory = ScriptFiles.DefaultDirectory;

        if (!dialog.ShowDialog()) return;

        var path = dialog.File.GetFullName();
        var item = Scripts.FirstOrDefault(s => string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase));

        if (item is null)
        {
            item = ScriptFileItem.From(path);
            Scripts.Add(item);
        }

        SelectedScript = item;
    });

    // ── 단축키 ───────────────────────────────────────────────────────────

    /// <summary>
    /// 게임이 앞에 있을 때도 시작·중지할 수 있게. 입력 자동화 화면과 같은 키다 - 공용 단축키라 같이 열려 있어도
    /// 되고, 마지막에 본 화면이 받는다.
    /// </summary>
    private void RegisterPlayHotkeys() => Guard(() =>
    {
        (string Label, Key Key, Action Action)[] bindings =
        [
            ("F5 1회", Key.F5, () => { if (Player.IsIdle) Player.RunOnce(); }),
            ("F6 반복/중지", Key.F6, Player.ToggleLoop)
        ];

        var live = new List<string>();
        var failed = new List<string>();

        foreach (var (label, key, action) in bindings)
        {
            if (SharedHotkeysFactory.Default.Claim(key, ModifierKeys.None, label, action, out _) is { } claim)
            {
                _hotkeyClaims.Add(claim);
                live.Add(label);
            }
            else failed.Add(label);
        }

        // 도구 줄의 정적 항목은 넘치면 안 보인다. 상태 줄에 적는다.
        StatusText = failed.Count == 0
            ? $"단축키: {string.Join(" · ", live)} · 마지막에 본 화면이 받습니다"
            : $"단축키: {string.Join(" · ", live)}  (등록 실패: {string.Join(", ", failed)} - 다른 프로그램이 쥐고 있습니다)";
    });

    /// <summary>탭이 앞으로 왔다. 이제 F5·F6 은 여기가 받는다.</summary>
    protected override void OnActivated()
    {
        base.OnActivated();
        foreach (var claim in _hotkeyClaims) claim.Activate();
    }

    // ── 생명주기 ─────────────────────────────────────────────────────────

    protected override void RestoreSettings()
    {
        base.RestoreSettings();

        Player.Restore((key, fallback) => GetSetting(key, fallback));

        RefreshScripts();

        var saved = GetSetting(nameof(SelectedScript), string.Empty);

        if (saved.Length > 0 && File.Exists(saved))
        {
            var item = Scripts.FirstOrDefault(s => string.Equals(s.Path, saved, StringComparison.OrdinalIgnoreCase));

            if (item is null)
            {
                item = ScriptFileItem.From(saved);
                Scripts.Add(item);
            }

            SelectedScript = item;
        }
    }

    protected override void SaveSettings()
    {
        base.SaveSettings();

        SetSetting(nameof(SelectedScript), SelectedScript?.Path ?? string.Empty);
        Player.Save((key, value) => SetSetting(key, value));
    }

    protected override void OnLoaded()
    {
        base.OnLoaded();

        RegisterPlayHotkeys();

        _ = Script.PrepareAsync();
    }

    protected override void ReleaseResources()
    {
        Player.Stop();
        Live.Dispose();

        // 놓아 주지 않으면 앱이 살아 있는 동안 그 키가 잠긴 채로 남는다.
        foreach (var claim in _hotkeyClaims) claim.Dispose();
        _hotkeyClaims.Clear();

        Script.Dispose();

        base.ReleaseResources();
    }
}
