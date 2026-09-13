using System;
using System.Collections.ObjectModel;
using System.IO;

using DevExpress.Mvvm;

using Minguk.Base.Utilities;
using Minguk.Tools.Helper;
using Minguk.Tools.Input.Scripting.Projects;

namespace Minguk.Tools.ViewModels;

/// <summary>
/// 탭 하나에 열린 파일. 글·저장 안 함 표시·중단점·밖에서 고친 것 다시 읽기.
/// </summary>
/// <remarks>
/// 저장 안 한 글은 실행·완성에 그대로 쓰인다(<see cref="ScriptProjectWorkspace.OpenTexts"/>) - 사람은 화면에 보이는 글이 돈다고 여긴다.
/// 밖에서 고친 파일은 여기서 고친 것이 없을 때만 다시 읽는다. 있으면 덮지 않고 알린다(한 파일짜리 편집기와 같은 규칙).
/// </remarks>
public sealed class ScriptDocument : ViewModelBase, IDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly Action<Action> _onUi;
    private FileChangeWatcher? _watcher;
    private bool _loading;

    public ScriptDocument(string path, Action<Action> onUi)
    {
        _onUi = onUi;
        FilePath = Path.GetFullPath(path);

        _loading = true;
        Text = File.Exists(FilePath) ? FileChangeWatcher.ReadShared(FilePath) : string.Empty;
        _loading = false;

        Watch();
    }

    /// <summary>
    /// 편집기가 묶을 공용 설정(색·완성·오류·잠금) - 워크벤치. 탭의 내용 틀은 문서를 데이터 문맥으로 받아서, 화면(조상)까지 거슬러
    /// 올라가 찾으면 창을 떼어 냈을 때(떠 있는 창은 다른 시각 트리다) 끊긴다. 문서가 들고 다니면 어디에 붙어도 닿는다.
    /// </summary>
    public object? Settings { get; init; }

    /// <summary>글이 바뀌었다(사람이 쳤거나 다시 읽었다). 워크벤치가 다시 검사한다.</summary>
    public event EventHandler? TextChanged;

    public string FilePath
    {
        get => GetProperty(() => FilePath);
        private set => SetProperty(() => FilePath, value, () => RaisePropertyChanged(nameof(Title)));
    }

    /// <summary>탭 제목. 저장 안 한 것은 * - VS 와 같다.</summary>
    public string Title => Path.GetFileName(FilePath) + (IsDirty ? " *" : string.Empty);

    public string Text
    {
        get => GetProperty(() => Text) ?? string.Empty;
        set => SetProperty(() => Text, value, () =>
        {
            if (!_loading) IsDirty = true;
            TextChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    public bool IsDirty
    {
        get => GetProperty(() => IsDirty);
        set => SetProperty(() => IsDirty, value, () => RaisePropertyChanged(nameof(Title)));
    }

    /// <summary>이 파일의 중단점(줄 번호). 파일마다 따로 든다.</summary>
    public ObservableCollection<int> Breakpoints { get; } = [];

    /// <summary>캐럿 줄(1부터). 편집기가 넣는다 - 상태 표시줄의 "줄 12 열 5" 와 F9 중단점이 본다.</summary>
    public int CaretLine
    {
        get => GetProperty(() => CaretLine);
        set => SetProperty(() => CaretLine, value);
    }

    public int CaretColumn
    {
        get => GetProperty(() => CaretColumn);
        set => SetProperty(() => CaretColumn, value);
    }

    /// <summary>편집기에 "이 줄로 가라" 는 요청. 같은 줄로 두 번 가도 알림이 오게 매번 새 객체다(오류 목록 더블 클릭).</summary>
    public Markup.EditorLineRequest? LineRequest
    {
        get => GetProperty(() => LineRequest);
        private set => SetProperty(() => LineRequest, value);
    }

    public void GoToLine(int line) => LineRequest = new Markup.EditorLineRequest(line);

    /// <summary>C# 파일인가. 편집기 언어·완성을 고른다.</summary>
    public bool IsSource => ScriptProject.KindOf(FilePath) == ScriptItemKind.Source;

    public void Save()
    {
        ScriptProject.WriteText(FilePath, Text);
        IsDirty = false;

        Logger.Info($"저장했다: {FilePath}");
    }

    /// <summary>파일 자리가 바뀌었다(이름 바꾸기). 감시도 옮긴다.</summary>
    public void MovedTo(string path)
    {
        FilePath = Path.GetFullPath(path);
        Watch();
    }

    private void Watch()
    {
        _watcher?.Dispose();
        _watcher = new FileChangeWatcher(FilePath, _onUi, OnChangedOutside);
    }

    private void OnChangedOutside(string text)
    {
        if (Normalize(text) == Normalize(Text))
        {
            // 우리가 저장해서 온 알림이거나, 밖에서 같은 글로 맞췄다.
            if (IsDirty) IsDirty = false;
            return;
        }

        var name = Path.GetFileName(FilePath);

        if (IsDirty)
        {
            MessengerUtility.SendMainMessage($"{name} 이(가) 밖에서 바뀌었지만 여기서 고친 것이 있어 불러오지 않았습니다. 버리려면 탭을 닫고 다시 여세요.");
            return;
        }

        _loading = true;
        Text = text;
        _loading = false;
        IsDirty = false;

        MessengerUtility.SendMainMessage($"밖에서 바뀐 {name} 을(를) 다시 불러왔습니다.");

        static string Normalize(string value) => value.Replace("\r\n", "\n").TrimEnd();
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;
    }
}
