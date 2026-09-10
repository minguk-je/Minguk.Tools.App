using System;
using System.Media;
using System.Windows.Input;
using Minguk.Tools.Input.Hotkeys;

namespace Minguk.Tools.ViewModels;

/// <summary>
/// 캡처 모니터의 전역 단축키. 게임을 앞에 둔 채로 데이터셋에 담는다.
/// </summary>
/// <remarks>
/// 몹을 모으는 일은 게임 안에서 한다. 몹이 보일 때마다 이 앱으로 넘어와 버튼을 누르고
/// 다시 게임으로 돌아가면, 그 사이에 몹이 움직이거나 사라진다 - "왔다갔다 힘드네" 가 그 말이다.
/// 게임이 앞에 있는 채로 눌리는 키가 있어야 한다.
///
/// 입력 자동화 화면이 F3~F6 을 쥔다. 두 화면이 같이 열려 있을 수 있으므로 겹치지 않는
/// 키를 쓴다. 겹치면 나중에 연 화면의 등록이 실패한다.
/// </remarks>
public partial class CaptureMonitorViewModel
{
    /// <summary>담기 단축키. 화면에 적는 이름과 실제 키를 한 곳에서 맞춘다.</summary>
    public const string CollectHotkeyLabel = "F8";
    private const Key CollectHotkey = Key.F8;

    private IGlobalHotkeyAdapter? _hotkeys;

    /// <summary>
    /// 담기 요청이 어디서 왔는지. 단축키로 담았을 때는 소리로 알린다.
    /// </summary>
    /// <remarks>
    /// 단축키를 누르는 순간 사용자는 게임을 보고 있어서 이 앱의 상태 줄이 안 보인다.
    /// 담겼는지 모르면 몇 번씩 누르게 되고, 그러면 같은 장면이 여러 장 쌓인다.
    /// 버튼으로 담을 때는 상태 줄이 보이니 소리를 내지 않는다.
    /// </remarks>
    private const int CollectFromButton = 1;
    private const int CollectFromHotkey = 2;

    private void RegisterHotkeys()
    {
        try
        {
            _hotkeys = GlobalHotkeyAdapterFactory.Create();

            if (!_hotkeys.TryRegister(CollectHotkey, ModifierKeys.None, CollectByHotkey))
            {
                // 조용히 넘기면 눌러도 아무 일이 없는 이유를 알 수 없다.
                var message = $"단축키 {CollectHotkeyLabel} 를 등록하지 못했습니다 - 다른 프로그램이 쥐고 있거나 Windows 가 예약한 키입니다. 버튼으로 담으세요.";

                StatusText = message;
                Note(message);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "단축키를 걸지 못했다");
        }
    }

    private void ReleaseHotkeys()
    {
        // 놓아 주지 않으면 앱이 살아 있는 동안 그 키가 잠긴 채로 남는다.
        _hotkeys?.Dispose();
        _hotkeys = null;
    }

    /// <summary>
    /// 단축키로 담기. 캡처가 안 돌면 담을 프레임이 없으니 소리로 거절을 알린다.
    /// </summary>
    private void CollectByHotkey()
    {
        if (!IsRunning)
        {
            // 게임을 보고 있어서 글은 안 보인다. 거절음을 내고 돌아와서 볼 수 있게 적어 둔다.
            SystemSounds.Hand.Play();
            StatusText = $"{CollectHotkeyLabel} 를 눌렀지만 캡처가 돌고 있지 않습니다. 시작부터 누르세요.";
            return;
        }

        RequestCollectFrame(CollectFromHotkey);
    }
}
