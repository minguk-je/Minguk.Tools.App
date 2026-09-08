using System;
using System.Runtime.InteropServices;
using System.Windows;

namespace Minguk.Tools.Capture.Input;

/// <summary>
/// 미리보기에서 일어난 입력을 실제 대상 창으로 흘려보낸다.
///
/// 앞의 셋을 엮는 자리다.
///   <see cref="CaptureTargetBounds"/> 로 대상이 화면 어디에 있는지 구하고,
///   <see cref="PreviewInputMapper"/> 로 누른 자리를 화면 좌표로 바꾸고,
///   <see cref="VirtualInput"/> 으로 그 자리에 실제 입력을 만들어 넣는다.
///
/// 포커스에 대하여
///   SendInput 은 "지금 포커스를 가진 창" 으로 간다. 그래서 입력을 보내기 전에
///   대상 창을 앞으로 가져온다. 대상이 모니터면 그 자리에 있는 창이 알아서 받는다 —
///   클릭 자체가 포커스를 옮기므로 따로 할 일이 없다.
///
/// 안 되는 경우
///   관리자 권한으로 뜬 창에는 일반 권한 프로세스가 입력을 넣을 수 없다(UIPI).
///   그때는 조용히 아무 일도 일어나지 않는다 — 오류도 나지 않는다.
///   그런 창을 다루려면 이 앱도 관리자로 띄워야 한다.
/// </summary>
public sealed class PreviewInputRouter
{
    private readonly Func<CaptureTarget?> _targetProvider;

    /// <param name="targetProvider">지금 캡처 중인 대상. 대상이 바뀌면 다음 호출부터 반영된다.</param>
    /// <param name="inputAdapter">입력을 실제로 만들어 낼 곳.</param>
    public PreviewInputRouter(Func<CaptureTarget?> targetProvider, IInputAdapter inputAdapter)
    {
        _targetProvider = targetProvider;
        InputAdapter = inputAdapter;
    }

    /// <summary>
    /// 입력을 만들어 내는 어댑터. 돌아가는 중에 갈아끼워도 된다 —
    /// 다음 입력부터 바뀐 어댑터로 나간다.
    /// </summary>
    public IInputAdapter InputAdapter { get; set; }

    /// <summary>지금 쓰고 있는 입력 경로 이름. 화면에 보여 주려고 둔다.</summary>
    public string AdapterName => InputAdapter.Name;

    /// <summary>마지막으로 옮긴 화면 좌표. 키 입력은 좌표가 없어서 이걸 기준으로 삼는다.</summary>
    public Point? LastScreenPoint { get; private set; }

    /// <summary>
    /// 미리보기 좌표를 화면 좌표로 바꿔 마우스를 옮긴다.
    /// 대상 위가 아니거나 대상을 못 찾으면 false.
    /// </summary>
    public bool TryMoveMouse(Point pointInControl, Size controlSize, Size sourceSize)
    {
        if (!TryResolveScreenPoint(pointInControl, controlSize, sourceSize, out var screenPoint))
            return false;

        LastScreenPoint = screenPoint;
        InputAdapter.MoveMouseTo((int)Math.Round(screenPoint.X), (int)Math.Round(screenPoint.Y));

        return true;
    }

    /// <summary>그 자리로 옮긴 뒤 버튼을 누른다.</summary>
    public bool TryPressMouse(Point pointInControl, Size controlSize, Size sourceSize, MouseButton button)
    {
        if (!TryMoveMouse(pointInControl, controlSize, sourceSize))
            return false;

        FocusTargetWindow();
        InputAdapter.PressMouseButton(button);

        return true;
    }

    public bool TryReleaseMouse(Point pointInControl, Size controlSize, Size sourceSize, MouseButton button)
    {
        if (!TryMoveMouse(pointInControl, controlSize, sourceSize))
            return false;

        InputAdapter.ReleaseMouseButton(button);
        return true;
    }

    public bool TryScroll(Point pointInControl, Size controlSize, Size sourceSize, int delta)
    {
        if (!TryMoveMouse(pointInControl, controlSize, sourceSize))
            return false;

        InputAdapter.ScrollWheel(delta);
        return true;
    }

    /// <summary>
    /// 키를 보낸다. 좌표가 없으므로 대상 창을 앞으로 가져온 뒤 넣는다.
    /// 대상이 모니터면 그 화면에서 마지막으로 클릭한 창이 받는다.
    /// </summary>
    public void SendKey(ushort virtualKey, bool isKeyUp)
    {
        FocusTargetWindow();

        if (isKeyUp)
            InputAdapter.ReleaseKey(virtualKey);
        else
            InputAdapter.PressKey(virtualKey);
    }

    private bool TryResolveScreenPoint(Point pointInControl, Size controlSize, Size sourceSize, out Point screenPoint)
    {
        screenPoint = default;

        var target = _targetProvider();
        if (target is null)
            return false;

        return CaptureTargetBounds.TryGet(target, out var bounds)
               && PreviewInputMapper.TryMapToScreen(pointInControl, controlSize, sourceSize, bounds, out screenPoint);
    }

    /// <summary>
    /// 대상이 창이면 앞으로 가져온다. 모니터면 할 일이 없다 — 클릭이 알아서 포커스를 옮긴다.
    /// </summary>
    private void FocusTargetWindow()
    {
        var target = _targetProvider();

        if (target is null || target.Kind != CaptureTargetKind.Window || target.Handle == IntPtr.Zero)
            return;

        if (NativeMethods.GetForegroundWindow() == target.Handle)
            return;

        NativeMethods.SetForegroundWindow(target.Handle);
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr windowHandle);
    }
}
