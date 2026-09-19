using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Threading;

namespace Minguk.Tools.Helper;

/// <summary>
/// .NET 이벤트를 <see cref="IObservable{T}"/> 로 - 화면은 이벤트를 <c>+=</c> 로 걸지 않고 이것으로 구독해 <c>Disposables</c> 에 넣는다(사용자, 2026-09-19).
/// </summary>
/// <remarks>
/// <b>왜</b> - <c>+=</c> 에 람다를 걸면 풀 길이 없고(같은 람다를 다시 만들 수 없다), 푸는 자리가 <c>ReleaseResources</c> 의 <c>-=</c> 와
/// <c>Disposables</c> 둘로 갈려 빠뜨리기 쉬웠다. 구독 하나 = <see cref="IDisposable"/> 하나로 두면 모아 두었다가 한 번에 끊는다.
///
/// 구독자는 이벤트를 올린 스레드에서 불린다(<c>+=</c> 와 같다). 화면 스레드로 넘길 때만 <see cref="ObserveOnUi{T}"/>.
/// 문자열로 이벤트 이름을 받는 <c>FromEventPattern(obj, "Name")</c> 은 쓰지 않는다 - 리플렉션이고 이름이 틀려도 빌드가 통과한다.
/// </remarks>
public static class RxEvents
{
    /// <summary><see cref="EventHandler"/> 이벤트.</summary>
    public static IObservable<EventArgs> From(Action<EventHandler> add, Action<EventHandler> remove)
        => Observable.FromEvent<EventHandler, EventArgs>(handler => (_, e) => handler(e), add, remove);

    /// <summary><see cref="EventHandler{TEventArgs}"/> 이벤트.</summary>
    public static IObservable<TArgs> From<TArgs>(Action<EventHandler<TArgs>> add, Action<EventHandler<TArgs>> remove)
        => Observable.FromEvent<EventHandler<TArgs>, TArgs>(handler => (_, e) => handler(e), add, remove);

    /// <summary>제 대리자 형식을 쓰는 이벤트(<c>FileSystemEventHandler</c>, DevExpress 이벤트 등).</summary>
    public static IObservable<TArgs> From<TDelegate, TArgs>(Func<Action<object?, TArgs>, TDelegate> conversion, Action<TDelegate> add, Action<TDelegate> remove)
        => Observable.FromEvent<TDelegate, TArgs>(
            handler => conversion((_, args) => handler(args)),
            add,
            remove);

    /// <summary>바뀐 속성 이름. <paramref name="name"/> 을 주면 그 속성만.</summary>
    public static IObservable<string?> PropertyChanged(INotifyPropertyChanged source, string? name = null)
    {
        var changes = Observable.FromEvent<PropertyChangedEventHandler, string?>(
            handler => (_, e) => handler(e.PropertyName),
            h => source.PropertyChanged += h,
            h => source.PropertyChanged -= h);

        return name is null ? changes : changes.Where(changed => changed == name);
    }

    /// <summary>컬렉션이 바뀐 것.</summary>
    public static IObservable<NotifyCollectionChangedEventArgs> CollectionChanged(INotifyCollectionChanged source)
        => Observable.FromEvent<NotifyCollectionChangedEventHandler, NotifyCollectionChangedEventArgs>(
            handler => (_, e) => handler(e),
            h => source.CollectionChanged += h,
            h => source.CollectionChanged -= h);

    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>
    /// 구독한다 - <b>받는 쪽이 예외를 던져도 구독이 살아 있다</b>. 이벤트 구독은 <c>Subscribe</c> 가 아니라 이것으로 한다.
    /// </summary>
    /// <remarks>
    /// Rx 의 <c>Subscribe</c> 는 받는 쪽이 한 번 던지면 그 구독을 끊는다(SafeObserver). <c>+=</c> 는 다음 알림도 받았으므로, 그대로 옮기면
    /// 캡처 콜백 한 번의 예외로 미리보기가 영영 멈춘다. 예외는 로그에 남기고 올린 쪽(캡처 스레드·파일 감시 스레드)으로 올려 보내지 않는다.
    /// 화면에 띄울 예외는 받는 쪽이 <c>Guard</c> 로 싼다.
    /// </remarks>
    public static IDisposable Listen<T>(this IObservable<T> source, Action<T> onNext)
        => source.Subscribe(value =>
        {
            try
            {
                onNext(value);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"이벤트를 처리하다 실패했습니다({onNext.Method.DeclaringType?.Name}.{onNext.Method.Name}) - 구독은 그대로 둡니다.");
            }
        });

    /// <summary>
    /// 화면 스레드에서 받는다 - 백그라운드(파일 감시·캡처·학습)에서 오는 알림을 화면에 반영할 때. 손으로 짠 <c>Dispatcher.BeginInvoke</c> 자리다.
    /// </summary>
    public static IObservable<T> ObserveOnUi<T>(this IObservable<T> source)
        => Application.Current?.Dispatcher is { } dispatcher
            ? source.ObserveOn(new SynchronizationContextScheduler(new DispatcherSynchronizationContext(dispatcher)))
            : source;
}
