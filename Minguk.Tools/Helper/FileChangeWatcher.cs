using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Minguk.Tools.Helper;

/// <summary>
/// 파일 하나를 지켜보다 밖에서 바뀌면 새 글을 알려 준다.
/// </summary>
/// <remarks>
/// 편집기는 한 번 저장에 알림을 두세 번 내거나 임시 파일에 쓰고 이름을 바꾼다(Changed 가 아니라 Renamed·Created).
/// 첫 알림에 읽으면 반쯤 쓴 파일이거나 잠겨 있다 - 그래서 300ms 묶고, 잠겨 있으면 다섯 번까지 다시 해 본다.
/// 알림은 <paramref name="onUi"/> 로 UI 스레드에서 준다. 받은 쪽이 지금 글과 견줘 갈지 말지 정한다.
/// (ScriptWorkbench 의 한 파일짜리 감시와 같은 규칙이다.)
/// </remarks>
public sealed class FileChangeWatcher : IDisposable
{
    private const int DebounceMs = 300;
    private const int MaxRetries = 5;

    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly string _path;
    private readonly Action<Action> _onUi;
    private readonly Action<string> _changed;
    private readonly FileSystemWatcher? _watcher;
    private readonly Timer _debounce;
    private int _retries;
    private bool _disposed;

    /// <param name="changed">새 글. UI 스레드.</param>
    public FileChangeWatcher(string path, Action<Action> onUi, Action<string> changed)
    {
        _path = Path.GetFullPath(path);
        _onUi = onUi;
        _changed = changed;
        _debounce = new Timer(_ => Read(), null, Timeout.Infinite, Timeout.Infinite);

        var directory = Path.GetDirectoryName(_path);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return;

        try
        {
            _watcher = new FileSystemWatcher(directory, Path.GetFileName(_path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
            };

            _watcher.Changed += OnEvent;
            _watcher.Created += OnEvent;
            _watcher.Renamed += OnEvent;
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, $"파일을 지켜보지 못했다: {_path}");
        }
    }

    private void OnEvent(object sender, FileSystemEventArgs e)
    {
        if (_disposed) return;

        _retries = 0;
        _debounce.Change(DebounceMs, Timeout.Infinite);
    }

    private void Read()
    {
        if (_disposed || !File.Exists(_path)) return;

        try
        {
            var text = ReadShared(_path);
            _onUi(() => { if (!_disposed) _changed(text); });
        }
        catch (IOException ex)
        {
            if (++_retries <= MaxRetries) _debounce.Change(DebounceMs, Timeout.Infinite);
            else Logger.Warn(ex, $"밖에서 바뀐 파일을 읽지 못했다: {_path}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Warn(ex, $"밖에서 바뀐 파일을 읽지 못했다: {_path}");
        }
    }

    /// <summary>쓰는 쪽이 아직 쥐고 있어도 읽을 수 있게 공유를 넓게 연다.</summary>
    public static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        return reader.ReadToEnd();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _watcher?.Dispose();
        _debounce.Dispose();
    }
}
