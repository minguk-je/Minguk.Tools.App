using System;
using DevExpress.Mvvm;
using DevExpress.Mvvm.UI;
using Minguk.Base;
using Minguk.Base.Enums;
using Minguk.Base.Utilities;

namespace Minguk.Tools.ViewModels;

/// <summary>
/// 서비스 접근 담당.
///
/// DevExpress 의 서비스는 View 의 &lt;dxmvvm:Interaction.Behaviors&gt; 에 선언한 것이
/// ViewModel 로 내려오는 구조다. 화면마다 GetService&lt;T&gt;() 를 다시 쓰는 대신
/// 여기에 이름을 붙여 두고, 화면은 프로퍼티만 쓴다.
///
/// 선언하지 않은 서비스는 null 이 아니라 예외로 터진다. 화면에서 쓰려면
/// View 쪽 Behaviors 에 해당 서비스가 있는지부터 확인할 것.
/// </summary>
public abstract partial class DocumentViewModelBase
{
    // ── DevExpress 서비스 ────────────────────────────────────────────────
    // View 에 필요한 선언:
    //   <dxmvvm:DispatcherService />
    //   <dx:DXMessageBoxService />
    //   <dxmvvm:OpenFileDialogService /> <dxmvvm:SaveFileDialogService />
    //   <dxmvvm:FolderBrowserDialogService />
    //   <dxmvvm:CurrentWindowService />

    protected IDispatcherService DispatcherService => GetService<IDispatcherService>();
    protected IMessageBoxService MessageBoxService => GetService<IMessageBoxService>();
    protected IOpenFileDialogService OpenFileDialogService => GetService<IOpenFileDialogService>();
    protected ISaveFileDialogService SaveFileDialogService => GetService<ISaveFileDialogService>();
    protected IFolderBrowserDialogService FolderBrowserDialogService => GetService<IFolderBrowserDialogService>();
    protected ICurrentWindowService CurrentWindowService => GetService<ICurrentWindowService>();

    // ── XAML 의 실제 컨트롤 잡기 ──────────────────────────────────────────

    /// <summary>
    /// View 에 <c>&lt;dxmvvm:UIObjectService x:Name="GridControlObjectService" /&gt;</c> 를 두고
    /// InitializeControls() 에서 <c>GridControl = FindControl&lt;BaseGridControl&gt;("GridControlObjectService");</c>
    /// 처럼 꺼낸다. 없으면 null 이다 — 화면이 터지지 않고 조용히 비활성으로 남는다.
    /// </summary>
    protected T? FindControl<T>(string objectServiceName) where T : class
        => ServiceContainer.GetService<IUIObjectService>(objectServiceName)?.Object as T;

    // ── 화면별 설정 저장/복구 ─────────────────────────────────────────────
    // 키는 AppSettingUtility 가 "<ViewModel 전체이름>.<keyName>" 으로 만든다.
    // 화면끼리 키가 겹치지 않으므로 이름은 프로퍼티 이름 그대로 쓰면 된다.
    //   RestoreSettings() : Address = GetSetting(nameof(Address), "127.0.0.1");
    //   SaveSettings()    : SetSetting(nameof(Address), Address);

    protected T GetSetting<T>(string keyName, T defaultValue)
        => AppSettingUtility.Get(this, keyName, defaultValue);

    protected void SetSetting(string keyName, object? value)
    {
        if (value is null)
            return;

        AppSettingUtility.Set(this, keyName, value);
    }

    protected bool HasSetting(string keyName) => AppSettingUtility.Exists(this, keyName);

    // ── 대기 표시 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 오래 걸리는 동기 작업을 대기 표시로 감싼다. 예외가 나도 반드시 닫힌다.
    /// UI 스레드를 잡는 작업에만 쓸 것 — await 하는 작업은 이걸로 감싸도 화면이 안 돈다.
    /// </summary>
    protected void UsingWaitSplash(Action action, SplashMessageTypes messageType = SplashMessageTypes.Loading)
    {
        Utility.ShowWaitSplashScreen(messageType);

        try
        {
            action();
        }
        finally
        {
            Utility.CloseSplashScreen();
        }
    }
}
