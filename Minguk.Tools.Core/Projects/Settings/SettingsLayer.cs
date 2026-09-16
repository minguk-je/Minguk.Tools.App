using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;

namespace Minguk.Tools.Projects.Settings;

/// <summary>층 - 솔루션 공통인가 프로젝트인가.</summary>
public enum SettingsLayerKind
{
    Solution,

    Project
}

/// <summary>
/// 폴더 하나의 양식·값(<c>settings.form.json</c>·<c>settings.values.json</c>). <b>앱 안에서 폴더마다 한 벌</b>이다(<see cref="For"/>).
/// </summary>
/// <remarks>
/// 한 벌이어야 설정 탭에서 바꾼 값이 도는 스크립트에 곧바로 보이고, 스크립트가 쓴 값이 설정 탭에 곧바로 보인다.
/// 두 프로젝트가 같은 솔루션 층을 나눠 쓴다.
///
/// 잠금은 <see cref="Gate"/> 하나로 모든 층을 막는다 - 합쳐 읽을 때 두 층을 한 번에 봐야 해서다. 드물게 불려 다툼이 없다.
/// 값 쓰기는 300ms 묶어 파일로 낸다. 파일을 밖에서 고치면 다음에 읽을 때(1초에 한 번 본다) 다시 읽는다 - 묶어 둔 쓰기가 없을 때만.
/// </remarks>
public sealed class SettingsLayer
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private static readonly Dictionary<string, SettingsLayer> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>모든 설정 층을 막는 잠금.</summary>
    public static readonly object Gate = new();

    private const int SaveDelayMs = 300;

    private SettingsForm _form = new();
    private SettingsValues _values = new();
    private DateTime _formStamp;
    private DateTime _valuesStamp;
    private long _lastCheck;
    private bool _dirty;
    private readonly Timer _saveTimer;

    private SettingsLayer(string directory)
    {
        Directory = directory;
        _saveTimer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);

        LoadFiles();
    }

    public string Directory { get; }

    public string FormPath => Path.Combine(Directory, SolutionSettingsFiles.FormFile);

    public string ValuesPath => Path.Combine(Directory, SolutionSettingsFiles.ValuesFile);

    /// <summary>값이나 양식이 바뀌었다. 인자는 바뀐 이름(양식이면 null). 아무 스레드에서나 온다.</summary>
    public event EventHandler<string?>? Changed;

    public static SettingsLayer For(string directory)
    {
        var full = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);

        lock (Gate)
        {
            if (!Cache.TryGetValue(full, out var layer))
                Cache[full] = layer = new SettingsLayer(full);

            return layer;
        }
    }

    /// <summary>양식(잠금 안에서 부른다). 고치려면 <see cref="SaveForm"/> 에 새것을 넘긴다.</summary>
    public SettingsForm Form
    {
        get
        {
            RefreshIfChanged();
            return _form;
        }
    }

    public IReadOnlyDictionary<string, JsonNode?> Values
    {
        get
        {
            RefreshIfChanged();
            return _values.Values;
        }
    }

    public bool TryGetValue(string name, out JsonNode? value)
    {
        lock (Gate)
        {
            RefreshIfChanged();
            return _values.Values.TryGetValue(name, out value);
        }
    }

    public void SetValue(string name, JsonNode? value)
    {
        lock (Gate)
        {
            _values.Values[name] = value?.DeepClone();
            MarkDirty();
        }

        Changed?.Invoke(this, name);
    }

    /// <summary>이 층의 값을 뺀다 - 아래 층(솔루션·기본값)이 보이게.</summary>
    public bool RemoveValue(string name)
    {
        bool removed;

        lock (Gate)
        {
            removed = _values.Values.Remove(name);
            if (removed) MarkDirty();
        }

        if (removed) Changed?.Invoke(this, name);

        return removed;
    }

    /// <summary>양식을 바꿔 곧바로 저장한다.</summary>
    public void SaveForm(SettingsForm form)
    {
        lock (Gate)
        {
            _form = form;

            if (form.IsEmpty && !File.Exists(FormPath))
            {
                // 빈 양식 파일을 괜히 만들지 않는다.
            }
            else
            {
                form.Save(FormPath);
                _formStamp = Stamp(FormPath);
            }
        }

        Changed?.Invoke(this, null);
    }

    /// <summary>양식에 없는 이름의 값을 지운다. 지운 이름들.</summary>
    public IReadOnlyList<string> RemoveUnused(Func<string, bool> isDefined)
    {
        var removed = new List<string>();

        lock (Gate)
        {
            foreach (var name in new List<string>(_values.Values.Keys))
            {
                if (isDefined(name)) continue;

                _values.Values.Remove(name);
                removed.Add(name);
            }

            if (removed.Count > 0) MarkDirty();
        }

        if (removed.Count > 0) Changed?.Invoke(this, null);

        return removed;
    }

    /// <summary>묶어 둔 값 쓰기를 지금 낸다.</summary>
    public void Flush()
    {
        lock (Gate)
        {
            if (!_dirty) return;

            try
            {
                _values.Save(ValuesPath);
                _valuesStamp = Stamp(ValuesPath);
                _dirty = false;
            }
            catch (Exception ex)
            {
                // 잠긴 파일이면 다음 쓰기에서 다시 한다. 값은 메모리에 있다.
                Logger.Warn(ex, $"설정 값을 저장하지 못했습니다: {ValuesPath}");
                _saveTimer.Change(SaveDelayMs * 3, Timeout.Infinite);
            }
        }
    }

    /// <summary>파일에서 다시 읽는다. 묶어 둔 쓰기는 버린다.</summary>
    public void Reload()
    {
        lock (Gate)
        {
            _dirty = false;
            LoadFiles();
        }

        Changed?.Invoke(this, null);
    }

    /// <summary>모든 층의 묶어 둔 쓰기를 낸다(앱 끝·실행 끝).</summary>
    public static void FlushAll()
    {
        List<SettingsLayer> layers;

        lock (Gate) layers = [.. Cache.Values];

        foreach (var layer in layers) layer.Flush();
    }

    /// <summary>하네스용 - 한 벌 캐시를 비운다.</summary>
    public static void ResetCache()
    {
        FlushAll();

        lock (Gate) Cache.Clear();
    }

    private void MarkDirty()
    {
        _dirty = true;
        _saveTimer.Change(SaveDelayMs, Timeout.Infinite);
    }

    private void LoadFiles()
    {
        try
        {
            _form = SettingsForm.Load(FormPath);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, $"설정 양식을 읽지 못했습니다: {FormPath}");
            _form = new SettingsForm();
        }

        try
        {
            _values = SettingsValues.Load(ValuesPath);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, $"설정 값을 읽지 못했습니다: {ValuesPath}");
            _values = new SettingsValues();
        }

        _formStamp = Stamp(FormPath);
        _valuesStamp = Stamp(ValuesPath);
    }

    /// <summary>밖에서 파일을 고쳤으면 다시 읽는다. 1초에 한 번만 본다.</summary>
    private void RefreshIfChanged()
    {
        var now = Environment.TickCount64;
        if (now - _lastCheck < 1000) return;

        _lastCheck = now;

        if (_dirty) return;

        if (Stamp(FormPath) != _formStamp || Stamp(ValuesPath) != _valuesStamp)
        {
            Logger.Info($"설정 파일이 밖에서 바뀌어 다시 읽었습니다: {Directory}");
            LoadFiles();
        }
    }

    private static DateTime Stamp(string path) => File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
}
