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

/// <summary>스크립트 폴더의 파일 하나. 콤보에 이름만 보이고 실제로는 경로를 든다.</summary>
public sealed record ScriptFileItem(string Name, string Path)
{
    public override string ToString() => Name;
}

/// <summary>
/// 플레이 화면. 게임 창을 연결하고, 미리보기를 켰다 껐다 하고, 저장해 둔 스크립트를 골라 돌린다.
/// <b>편집은 없다</b> - 다른 PC 에서는 이 화면만 연다.
/// </summary>
/// <remarks>
/// 편집 화면과 같은 바탕(잡기·보기)과 같은 실행기를 쓰고 껍데기만 다르다. 편집에서 되던 것이
/// 여기서 안 되는 일이 없게. 스크립트는 <see cref="ScriptFiles.DefaultDirectory"/> 에서 고른다 -
/// 편집 화면이 저장하는 자리다.
///
/// 담기(F8)는 하지 않는다. 여기서 F8 을 쥐고 있으면 같이 열린 캡처 화면의 등록이 실패한다.
/// 대신 F5(1회)·F6(반복/중지)을 쥔다 - 게임이 앞에 있어야 입력이 들어가므로 앱 밖에서 누를 수단이 있어야 한다.
/// </remarks>
public partial class PlayViewModel : RecognizingCaptureViewModelBase
{
    public static PlayViewModel Create() => ViewModelSource.Create(() => new PlayViewModel());

    private IGlobalHotkeyAdapter? _playHotkeys;

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
        if (SelectedScript is null && string.IsNullOrEmpty(Script.FilePath))
        {
            MessengerUtility.SendMainMessage("돌릴 스크립트를 먼저 고르세요.");
            return null;
        }

        if (Script.HasError)
        {
            MessengerUtility.SendMainMessage("스크립트에 고칠 줄이 있습니다. 편집 화면에서 고치세요.");
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
            ? $"스크립트가 없습니다. 편집 화면에서 저장하면 여기 보입니다 ({ScriptFiles.DefaultDirectory})."
            : $"스크립트 {Scripts.Count}개 ({ScriptFiles.DefaultDirectory})";
    });

    private static IEnumerable<ScriptFileItem> ListScripts()
    {
        var folder = ScriptFiles.DefaultDirectory;

        if (!Directory.Exists(folder)) yield break;

        foreach (var path in Directory.EnumerateFiles(folder).Where(p => ScriptFiles.FromPath(p) is not null).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            yield return new ScriptFileItem(Path.GetFileName(path), path);
    }

    private void OnSelectedScriptChanged() => Guard(() =>
    {
        if (SelectedScript is null) return;

        Script.LoadFile(SelectedScript.Path);
        StatusText = $"스크립트: {SelectedScript.Name}";
    });

    /// <summary>폴더 밖의 파일을 고른다. 목록에 넣고 고른다.</summary>
    private void DoBrowseScript() => Guard(() =>
    {
        var dialog = OpenFileDialogService;

        dialog.Filter = ScriptFiles.OpenFilter(Script.SelectedLanguage);
        dialog.InitialDirectory = ScriptFiles.DefaultDirectory;

        if (!dialog.ShowDialog()) return;

        var path = dialog.File.GetFullName();
        var item = Scripts.FirstOrDefault(s => string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase));

        if (item is null)
        {
            item = new ScriptFileItem(Path.GetFileName(path), path);
            Scripts.Add(item);
        }

        SelectedScript = item;
    });

    // ── 단축키 ───────────────────────────────────────────────────────────

    /// <summary>
    /// 게임이 앞에 있을 때도 시작·중지할 수 있게. 입력 자동화 화면과 같은 키다 - 둘이 같이 열려 있으면
    /// 나중에 연 쪽이 실패하고 그 사실이 상태에 적힌다.
    /// </summary>
    private void RegisterPlayHotkeys() => Guard(() =>
    {
        _playHotkeys = GlobalHotkeyAdapterFactory.Create();

        (string Label, Key Key, Action Action)[] bindings =
        [
            ("F5 1회", Key.F5, () => { if (Player.IsIdle) Player.RunOnce(); }),
            ("F6 반복/중지", Key.F6, Player.ToggleLoop)
        ];

        var live = new List<string>();
        var failed = new List<string>();

        foreach (var (label, key, action) in bindings)
        {
            if (_playHotkeys.TryRegister(key, ModifierKeys.None, action)) live.Add(label);
            else failed.Add(label);
        }

        // 도구 줄의 정적 항목은 넘치면 안 보인다. 상태 줄에 적는다.
        StatusText = failed.Count == 0
            ? $"단축키: {string.Join(" · ", live)}"
            : $"단축키: {string.Join(" · ", live)}  (등록 실패: {string.Join(", ", failed)} - 다른 화면이나 프로그램이 쥐고 있습니다)";
    });

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
                item = new ScriptFileItem(Path.GetFileName(saved), saved);
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
        _playHotkeys?.Dispose();
        _playHotkeys = null;

        Script.Dispose();

        base.ReleaseResources();
    }
}
