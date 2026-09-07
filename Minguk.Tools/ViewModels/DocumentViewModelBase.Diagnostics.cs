using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Minguk.Base.Views;

namespace Minguk.Tools.ViewModels;

/// <summary>
/// 로깅·예외 담당.
///
/// 화면 코드가 try/catch 로 뒤덮이는 걸 막는다. 사용자가 부른 동작 하나를
/// <see cref="Guard(Action, string?)"/> 로 감싸면, 터졌을 때 NLog 에 남기고
/// ExceptionViewer 로 보여 준 뒤 화면은 살아 있다.
///
///   private void DoRefresh() =&gt; Guard(() =&gt; { ... });
///
/// 감싸지 말아야 할 곳: 실패하면 화면이 의미 없는 초기화 경로.
/// (그쪽은 베이스가 이미 단계별로 감싸고 있다.)
/// </summary>
public abstract partial class DocumentViewModelBase
{
    private string? _ownerViewFullName;

    /// <summary>
    /// 이 화면의 View 전체 타입 이름. "...ViewModels.DashboardViewModel" → "...Views.DashboardView".
    ///
    /// POCO 프록시(ViewModelSource.Create)가 만든 동적 타입이 GetType() 으로 나오므로
    /// 실제 선언 타입까지 거슬러 올라가서 구한다. MessengerUtility 의 수신 대상 이름으로 쓴다.
    /// </summary>
    protected string OwnerViewFullName => _ownerViewFullName ??= ResolveOwnerViewFullName(GetType());

    /// <summary>이 화면 이름으로 찍히는 로거. 프록시가 아니라 실제 ViewModel 이름이 남는다.</summary>
    protected NLog.Logger Logger => _logger ??= NLog.LogManager.GetLogger(ResolveOwnerType(GetType()).FullName ?? nameof(DocumentViewModelBase));
    private NLog.Logger? _logger;

    private static Type ResolveOwnerType(Type type)
    {
        var current = type;

        // 동적 어셈블리(POCO 프록시)를 건너뛰고, 이름이 ViewModel 로 끝나는 실제 타입을 찾는다.
        while (current is not null &&
               (current.Assembly.IsDynamic || !current.Name.EndsWith("ViewModel", StringComparison.Ordinal)))
        {
            current = current.BaseType;
        }

        return current ?? type;
    }

    private static string ResolveOwnerViewFullName(Type type)
    {
        var fullName = ResolveOwnerType(type).FullName ?? string.Empty;

        // 네임스페이스의 "ViewModels" 와 타입 이름의 "ViewModel" 이 한 번에 바뀐다.
        return fullName.Replace("ViewModel", "View");
    }

    // ── 예외 가드 ─────────────────────────────────────────────────────────

    protected void Guard(Action action, [CallerMemberName] string? caller = null)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Report(ex, caller);
        }
    }

    protected TResult? Guard<TResult>(Func<TResult> func, TResult? fallback = default, [CallerMemberName] string? caller = null)
    {
        try
        {
            return func();
        }
        catch (Exception ex)
        {
            Report(ex, caller);
            return fallback;
        }
    }

    protected async Task GuardAsync(Func<Task> action, [CallerMemberName] string? caller = null)
    {
        try
        {
            await action().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Report(ex, caller);
        }
    }

    /// <summary>NLog 에 남기고 ExceptionViewer 로 보여 준다.</summary>
    protected void Report(Exception ex, [CallerMemberName] string? caller = null)
    {
        try
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
        }
        catch
        {
            // 직렬화 자체가 실패하는 예외가 있다(순환 참조 등). 그때는 메시지만 남긴다.
            Logger.Error(ex, caller ?? string.Empty);
        }

        ExceptionViewer.Show(ex, caller);
    }
}
