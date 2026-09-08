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
///   <see cref="IInputAdapter"/> 로 그 자리에 실제 입력을 만들어 넣는다.
///
/// 포커스에 대하여
///   SendInput 은 "지금 포커스를 가진 창" 으로 간다. 그래서 입력을 보내기 전에
///   대상 창을 앞으로 가져온다. 대상이 모니터면 그 자리에 있는 창이 알아서 받는다 —
///   클릭 자체가 포커스를 옮기므로 따로 할 일이 없다.
///
/// 클릭은 왜 한 번에 보내는가
///   누름과 뗌을 나눠 보내면 뗌이 영영 안 온다. 누르는 순간 진짜 커서가 대상 창 위로
///   옮겨 가 버려서, 사용자가 버튼을 떼는 것을 이 앱이 못 보기 때문이다.
///   그러면 대상 창에서는 버튼이 눌린 채로 남아 화면이 끌려다닌다.
///   그래서 <see cref="TryClickMouse"/> 하나로 누르고 떼는 것까지 끝낸다.
///
/// 안 되는 경우
///   관리자 권한으로 뜬 창에는 일반 권한 프로세스가 입력을 넣을 수 없다(UIPI).
///   그때는 <see cref="InputForwardResult.Blocked"/> 가 돌아온다.
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

    /// <summary>미리보기 좌표로 마우스만 옮긴다.</summary>
    public InputForwardResult TryMoveMouse(Point pointInControl, Size controlSize, Size sourceSize)
    {
        var resolved = TryResolveScreenPoint(pointInControl, controlSize, sourceSize, out var screenPoint);
        if (resolved != InputForwardResult.Sent)
            return resolved;

        LastScreenPoint = screenPoint;

        return MoveTo(screenPoint) ? InputForwardResult.Sent : InputForwardResult.Blocked;
    }

    /// <summary>
    /// 그 자리로 옮긴 뒤 누르고 뗀다. 미리보기에서 쓰는 것은 이것이다.
    /// </summary>
    public InputForwardResult TryClickMouse(Point pointInControl, Size controlSize, Size sourceSize, MouseButton button)
    {
        var resolved = TryResolveScreenPoint(pointInControl, controlSize, sourceSize, out var screenPoint);
        if (resolved != InputForwardResult.Sent)
            return resolved;

        LastScreenPoint = screenPoint;

        // 대상이 이미 그 자리에 드러나 있으면 창을 끌어올리지 않는다.
        // 마우스 클릭은 "커서 아래에 있는 창" 으로 가지 포커스를 따라가지 않기 때문이다.
        // 굳이 올리면 이 앱이 그 뒤로 숨어서 미리보기를 다시 누를 수 없게 된다.
        // 창 메시지를 직접 넣는 경로는 애초에 끌어올릴 이유가 없다.
        if (InputAdapter.RequiresForegroundTarget && !IsTargetUnderPoint(screenPoint))
            FocusTargetWindow();

        if (!MoveTo(screenPoint))
            return InputForwardResult.Blocked;

        return InputAdapter.ClickMouseButton(button) ? InputForwardResult.Sent : InputForwardResult.Blocked;
    }

    public InputForwardResult TryScroll(Point pointInControl, Size controlSize, Size sourceSize, int delta)
    {
        var moved = TryMoveMouse(pointInControl, controlSize, sourceSize);
        if (moved != InputForwardResult.Sent)
            return moved;

        return InputAdapter.ScrollWheel(delta) ? InputForwardResult.Sent : InputForwardResult.Blocked;
    }

    /// <summary>
    /// 키를 보낸다. 좌표가 없으므로 대상 창을 앞으로 가져온 뒤 넣는다.
    /// 대상이 모니터면 그 화면에서 마지막으로 클릭한 창이 받는다.
    /// </summary>
    public InputForwardResult SendKey(ushort virtualKey, bool isKeyUp)
    {
        if (_targetProvider() is null)
            return InputForwardResult.NoTarget;

        // PostMessage 경로는 포커스와 무관하게 대상 창으로 바로 들어간다.
        if (InputAdapter.RequiresForegroundTarget)
            FocusTargetWindow();

        var sent = isKeyUp
            ? InputAdapter.ReleaseKey(virtualKey)
            : InputAdapter.PressKey(virtualKey);

        return sent ? InputForwardResult.Sent : InputForwardResult.Blocked;
    }

    private bool MoveTo(Point screenPoint)
        => InputAdapter.MoveMouseTo((int)Math.Round(screenPoint.X), (int)Math.Round(screenPoint.Y));

    /// <summary>어디를 눌렀는지 화면 좌표로 푼다. 못 풀면 그 이유를 돌려준다.</summary>
    /// <summary>입력을 보내지 않고 좌표만 알고 싶을 때도 쓴다(요소 검사 등).</summary>
    public InputForwardResult TryResolveScreenPoint(
        Point pointInControl,
        Size controlSize,
        Size sourceSize,
        out Point screenPoint)
    {
        screenPoint = default;

        var target = _targetProvider();
        if (target is null)
            return InputForwardResult.NoTarget;

        if (!CaptureTargetBounds.TryGet(target, out var bounds))
            return InputForwardResult.TargetGone;

        return PreviewInputMapper.TryMapToScreen(pointInControl, controlSize, sourceSize, bounds, out screenPoint)
            ? InputForwardResult.Sent
            : InputForwardResult.OutsideImage;
    }

    /// <summary>
    /// 그 자리에 이미 대상이 드러나 있는지.
    ///
    /// 대상이 모니터면 그 화면에 무엇이 있든 그게 받는 게 맞으므로 항상 true 로 본다.
    /// 창이면 그 점 아래에 있는 창의 최상위 조상이 대상인지 확인한다 —
    /// 자식 컨트롤이 잡히므로 조상까지 올라가야 한다.
    /// </summary>
    private bool IsTargetUnderPoint(Point screenPoint)
    {
        var target = _targetProvider();

        if (target is null || target.Kind != CaptureTargetKind.Window || target.Handle == IntPtr.Zero)
            return true;

        var hit = NativeMethods.WindowFromPoint(new NativeMethods.ScreenPoint
        {
            X = (int)Math.Round(screenPoint.X),
            Y = (int)Math.Round(screenPoint.Y)
        });

        if (hit == IntPtr.Zero)
            return false;

        return NativeMethods.GetAncestor(hit, NativeMethods.GA_ROOT) == target.Handle;
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
        public const uint GA_ROOT = 2;

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr windowHandle);

        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(ScreenPoint point);

        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr windowHandle, uint flags);

        [StructLayout(LayoutKind.Sequential)]
        public struct ScreenPoint
        {
            public int X;
            public int Y;
        }
    }
}
