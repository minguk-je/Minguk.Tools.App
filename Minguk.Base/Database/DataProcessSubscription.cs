namespace Minguk.Base.Database;

/// <summary>
/// DataProcess 이벤트 구독의 반복 부분만 걷어낸다.
///
/// 화면마다 아래 6줄짜리 Rx 배선이 이벤트 개수만큼(보통 20번 남짓) 반복된다.
/// <code>
/// Disposables.Add(Observable.FromEventPattern&lt;DataProcess.DataProcessEventHandler, DataProcessEventArgs&gt;(
///         h => MasterDataProcess.OnSave += h,
///         h => MasterDataProcess.OnSave -= h)
///     .Select(x => (x.Sender, x.EventArgs))
///     .Subscribe(onNext: x => { ... }));
/// </code>
///
/// 이 헬퍼를 쓰면 이렇게 된다.
/// <code>
/// Disposables.Add(DataProcessSubscription.On(
///     h => MasterDataProcess.OnSave += h,
///     h => MasterDataProcess.OnSave -= h,
///     e => { ... }));
/// </code>
///
/// ※ 이 헬퍼는 '구독하고 해제하는 것' 외에 아무것도 하지 않는다.
///    무엇을 저장하고 어떤 순서로 처리할지는 화면이 그대로 쥐고 있어야 한다.
///    (try/catch, 로깅, 상태 전환을 여기서 대신 해주지 않는 것도 같은 이유다.)
/// </summary>
public static class DataProcessSubscription
{
    /// <summary>
    /// DataProcess 이벤트 하나를 구독하고, Dispose 하면 해제되는 핸들을 돌려준다.
    /// 보통 화면의 CompositeDisposable 에 그대로 Add 한다.
    /// </summary>
    /// <param name="add">이벤트 추가. <c>h =&gt; MasterDataProcess.OnSave += h</c></param>
    /// <param name="remove">이벤트 제거. <c>h =&gt; MasterDataProcess.OnSave -= h</c></param>
    /// <param name="handler">이벤트 본문. 인자는 <see cref="DataProcessEventArgs"/>.</param>
    public static IDisposable On(
        Action<DataProcess.DataProcessEventHandler> add,
        Action<DataProcess.DataProcessEventHandler> remove,
        Action<DataProcessEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(add);
        ArgumentNullException.ThrowIfNull(remove);
        ArgumentNullException.ThrowIfNull(handler);

        DataProcess.DataProcessEventHandler bridge = (_, e) => handler(e);

        add(bridge);

        return new Unsubscriber(() => remove(bridge));
    }

    /// <summary>
    /// 보내는 쪽(DataProcess)까지 받아야 하는 경우.
    /// 마스터/디테일이 같은 핸들러를 공유할 때 어느 쪽에서 온 이벤트인지 구분할 수 있다.
    /// </summary>
    public static IDisposable On(
        Action<DataProcess.DataProcessEventHandler> add,
        Action<DataProcess.DataProcessEventHandler> remove,
        Action<DataProcess, DataProcessEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(add);
        ArgumentNullException.ThrowIfNull(remove);
        ArgumentNullException.ThrowIfNull(handler);

        DataProcess.DataProcessEventHandler bridge = (process, e) => handler(process, e);

        add(bridge);

        return new Unsubscriber(() => remove(bridge));
    }

    private sealed class Unsubscriber : IDisposable
    {
        private Action? release;

        public Unsubscriber(Action release) => this.release = release;

        public void Dispose()
        {
            var action = release;
            release = null;
            action?.Invoke();
        }
    }
}
