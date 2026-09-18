using System;
using System.Collections.Generic;
using System.Linq;

using Vortice.Direct3D11;

namespace Minguk.Tools.Capture;

/// <summary>
/// <see cref="ICaptureSessionHub"/> 의 구현. 대상마다 실제 세션 하나와 손잡이 여럿.
/// </summary>
/// <remarks>
/// 프레임 콜백은 캡처 스레드에서 온다. 손잡이 목록은 잠금 아래 배열로 바꿔 끼우고, 콜백은 그 배열을 그대로 돈다 -
/// 콜백 안에서 잠금을 잡으면 화면이 Stop 하는 동안(UI 스레드) 캡처 스레드가 기다리다 프레임이 밀린다.
/// 세션을 만드는 것은 <see cref="ScreenCaptureAdapterFactory"/> 가 아니라 넘겨받은 함수다 - 검증이 가짜 세션을 꽂는다.
/// </remarks>
public sealed class SharedCaptureHub : ICaptureSessionHub
{
    private readonly Func<CaptureTarget, bool, IScreenCaptureAdapter> _create;
    private readonly object _gate = new();
    private readonly Dictionary<string, Shared> _shared = new();

    public SharedCaptureHub(Func<CaptureTarget, bool, IScreenCaptureAdapter> create) => _create = create;

    public IScreenCaptureAdapter Acquire(CaptureTarget target, bool cpuReadback)
    {
        lock (_gate)
        {
            var key = target.Key;

            if (!_shared.TryGetValue(key, out var shared))
            {
                shared = new Shared(this, target);
                _shared[key] = shared;
            }

            var handle = new Handle(shared, cpuReadback);
            shared.Add(handle);

            return handle;
        }
    }

    public int ConsumerCount(CaptureTarget target)
    {
        lock (_gate)
            return _shared.TryGetValue(target.Key, out var shared) ? shared.Count : 0;
    }

    private void Forget(Shared shared)
    {
        lock (_gate)
        {
            var key = shared.Target.Key;

            if (_shared.TryGetValue(key, out var current) && ReferenceEquals(current, shared))
                _shared.Remove(key);
        }
    }

    /// <summary>대상 하나의 실제 세션과 그것을 나눠 쓰는 손잡이들.</summary>
    private sealed class Shared
    {
        private readonly SharedCaptureHub _hub;
        private readonly object _gate = new();
        private Handle[] _handles = [];
        private IScreenCaptureAdapter? _session;
        private bool _sessionHasReadback;

        public Shared(SharedCaptureHub hub, CaptureTarget target)
        {
            _hub = hub;
            Target = target;
        }

        public CaptureTarget Target { get; }

        public int Count => _handles.Length;

        /// <summary>실제 세션. 아직 아무도 시작 안 했으면 null.</summary>
        public IScreenCaptureAdapter? Session => _session;

        public void Add(Handle handle)
        {
            lock (_gate) _handles = [.. _handles, handle];
        }

        public void Remove(Handle handle)
        {
            var last = false;

            lock (_gate)
            {
                _handles = _handles.Where(h => !ReferenceEquals(h, handle)).ToArray();

                if (_handles.Length == 0)
                {
                    DropSession();
                    last = true;
                }
                else
                {
                    Reconcile();
                }
            }

            if (last) _hub.Forget(this);
        }

        /// <summary>손잡이가 Start 했다. 세션이 없으면 만들고, 리드백·fps 를 손잡이들에 맞춘다.</summary>
        public void OnHandleStarted() => Reconcile();

        /// <summary>손잡이가 Stop 했다. 도는 손잡이가 하나도 없으면 세션을 놓는다.</summary>
        public void OnHandleStopped() => Reconcile();

        public void OnFpsChanged() => Reconcile();

        /// <summary>손잡이가 멈춤을 바꿨다. 안쪽 세션이 멈출 수 있으면 true.</summary>
        public bool OnPauseChanged()
        {
            lock (_gate)
            {
                Reconcile();
                return _session is IPausableCapture;
            }
        }

        /// <summary>
        /// 세션 하나를 손잡이들의 요구에 맞춘다 - 도는 손잡이가 없으면 놓고, 있으면 만들고, 리드백이 모자라면
        /// 새로 만들고, fps 는 가장 큰 값으로.
        /// </summary>
        private void Reconcile()
        {
            lock (_gate)
            {
                var running = _handles.Where(h => h.IsRunning).ToArray();

                if (running.Length == 0)
                {
                    DropSession();
                    return;
                }

                var wantsReadback = running.Any(h => h.WantsReadback);
                var fps = running.Max(h => h.TargetFps);

                // 리드백은 세션을 만들 때 정해진다. 없는 세션에 원하는 손잡이가 오면 새로 만든다.
                if (_session is not null && wantsReadback && !_sessionHasReadback)
                {
                    DropSession();
                    Notify("리드백을 켜려고 캡처를 다시 시작합니다 - 다른 화면이 같은 창을 잡고 있습니다.");
                }

                if (_session is null)
                {
                    var session = _hub._create(Target, wantsReadback);
                    session.FrameArrived += OnFrameArrived;
                    session.Notice += OnNotice;
                    session.Ended += OnEnded;
                    session.TargetFps = fps;
                    session.Start();

                    _session = session;
                    _sessionHasReadback = wantsReadback;

                    if (session is IPausableCapture newPausable && running.Any(h => h.WantsPause)) newPausable.TrySetPaused(true);
                    return;
                }

                if (_session.TargetFps != fps) _session.TargetFps = fps;

                if (_session is IPausableCapture pausable) pausable.TrySetPaused(running.Any(h => h.WantsPause));
            }
        }

        private void DropSession()
        {
            var session = _session;
            if (session is null) return;

            _session = null;
            _sessionHasReadback = false;

            session.FrameArrived -= OnFrameArrived;
            session.Notice -= OnNotice;
            session.Ended -= OnEnded;

            try { session.Stop(); }
            catch (Exception) { /* 놓는 길이다. 여기서 터져도 손잡이는 이미 떠났다. */ }

            session.Dispose();
        }

        /// <summary>
        /// 세션이 스스로 멈췄다(대상 창 닫힘). 손잡이를 모두 멈춘 것으로 하고 알린다 - 그래야 화면이 시작/중지 상태를 맞추고 스크립트를 멈춘다.
        /// </summary>
        /// <remarks>세션의 제 이벤트 안에서 불리므로 놓는 것(Dispose)은 밖으로 미룬다 - 제 콜백 안에서 저를 부수지 않게.</remarks>
        private void OnEnded(object? sender, string reason)
        {
            IScreenCaptureAdapter? session;
            Handle[] handles;

            lock (_gate)
            {
                session = _session;
                if (session is null || !ReferenceEquals(session, sender)) return;

                _session = null;
                _sessionHasReadback = false;
                session.FrameArrived -= OnFrameArrived;
                session.Notice -= OnNotice;
                session.Ended -= OnEnded;
                handles = _handles;
            }

            System.Threading.Tasks.Task.Run(() =>
            {
                try { session.Stop(); }
                catch (Exception) { /* 이미 멈춘 세션이다. */ }

                session.Dispose();
            });

            foreach (var handle in handles) handle.RaiseEnded(reason);
        }

        /// <summary>캡처 스레드. 도는 손잡이 전부에 같은 프레임을 준다. 픽셀은 이 콜백이 돌아가면 사라지므로 차례로.</summary>
        private void OnFrameArrived(object? sender, CapturedFrameEventArgs e)
        {
            foreach (var handle in _handles)
                if (handle.IsRunning) handle.RaiseFrame(e);
        }

        private void OnNotice(object? sender, string message) => Notify(message);

        private void Notify(string message)
        {
            foreach (var handle in _handles) handle.RaiseNotice(message);
        }
    }

    /// <summary>화면이 드는 손잡이. 예전 세션과 같은 얼굴이라 화면 코드는 그대로다.</summary>
    private sealed class Handle : IScreenCaptureAdapter, IPausableCapture
    {
        private readonly Shared _shared;
        private int _targetFps = 60;
        private bool _disposed;

        public Handle(Shared shared, bool wantsReadback)
        {
            _shared = shared;
            WantsReadback = wantsReadback;
        }

        public bool WantsReadback { get; }

        /// <summary>이 화면이 멈추기를 원한다. 세션은 하나라 하나라도 원하면 멈춘다(영상만 - <see cref="IPausableCapture"/>).</summary>
        public bool WantsPause { get; private set; }

        public bool TrySetPaused(bool paused)
        {
            WantsPause = paused;
            return _shared.OnPauseChanged();
        }

        public string Name => _shared.Session?.Name is { } inner ? $"{inner} (공유 {_shared.Count})" : "공유 캡처";

        public CaptureTarget Target => _shared.Session?.Target ?? _shared.Target;

        public bool IsRunning { get; private set; }

        public int TargetFps
        {
            get => _targetFps;
            set
            {
                _targetFps = value;
                if (IsRunning) _shared.OnFpsChanged();
            }
        }

        public ID3D11Device? Device => _shared.Session?.Device;

        public ID3D11DeviceContext? Context => _shared.Session?.Context;

        public event EventHandler<CapturedFrameEventArgs>? FrameArrived;

        public event EventHandler<string>? Notice;

        public event EventHandler<string>? Ended;

        public void Start()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Handle));
            if (IsRunning) return;

            IsRunning = true;
            _shared.OnHandleStarted();
        }

        public void Stop()
        {
            if (!IsRunning) return;

            IsRunning = false;
            _shared.OnHandleStopped();
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            IsRunning = false;
            _shared.Remove(this);
        }

        internal void RaiseFrame(CapturedFrameEventArgs e) => FrameArrived?.Invoke(this, e);

        internal void RaiseNotice(string message) => Notice?.Invoke(this, message);

        /// <summary>안쪽 세션이 스스로 멈췄다 - 이 손잡이도 멈춘 것으로 하고 화면에 알린다. 도는 손잡이에게만.</summary>
        internal void RaiseEnded(string reason)
        {
            if (!IsRunning) return;

            IsRunning = false;
            Ended?.Invoke(this, reason);
        }
    }
}
