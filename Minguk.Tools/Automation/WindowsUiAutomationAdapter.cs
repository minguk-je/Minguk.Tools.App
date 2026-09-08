using System;
using System.Collections.Generic;
using System.Windows.Automation;

namespace Minguk.Tools.Automation;

/// <summary>
/// 윈도우가 기본으로 주는 접근성 API(UI Automation)로 요소를 다룬다.
///
/// NuGet 을 더 물지 않는다. <c>System.Windows.Automation</c> 은 WPF 와 함께 들어온다.
/// FlaUI 같은 라이브러리가 더 편하긴 하지만, 하는 일이 이 정도면 기본 API 로 충분하고
/// 의존성이 하나도 안 늘어난다.
///
/// 예외를 삼키는 이유
///   대상 프로세스가 그새 죽거나 요소가 사라지면 ElementNotAvailableException 이 난다.
///   자동화에서는 흔한 일이라 예외로 화면을 멈추지 않고 "못 찾았다" 로 돌려준다.
/// </summary>
public sealed class WindowsUiAutomationAdapter : IUiAutomationAdapter
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    public string Name => "UI Automation";

    public bool TryGetElementAt(int screenX, int screenY, out UiElement element)
        => TryWrap(() => AutomationElement.FromPoint(new System.Windows.Point(screenX, screenY)), out element);

    public bool TryGetWindowRoot(IntPtr windowHandle, out UiElement element)
    {
        if (windowHandle == IntPtr.Zero)
        {
            element = null!;
            return false;
        }

        return TryWrap(() => AutomationElement.FromHandle(windowHandle), out element);
    }

    public IReadOnlyList<UiElement> GetChildren(UiElement parent)
    {
        var source = AsAutomationElement(parent);
        if (source is null)
            return Array.Empty<UiElement>();

        try
        {
            var found = source.FindAll(TreeScope.Children, Condition.TrueCondition);
            var result = new List<UiElement>(found.Count);

            foreach (AutomationElement child in found)
            {
                if (Describe(child) is { } described)
                    result.Add(described);
            }

            return result;
        }
        catch (Exception ex)
        {
            Logger.Debug($"자식 요소를 읽지 못했다: {ex.Message}");
            return Array.Empty<UiElement>();
        }
    }

    public bool TryFindByAutomationId(UiElement scope, string automationId, out UiElement element)
        => TryFind(scope, new PropertyCondition(AutomationElement.AutomationIdProperty, automationId), out element);

    public bool TryFindByName(UiElement scope, string name, out UiElement element)
        => TryFind(scope, new PropertyCondition(AutomationElement.NameProperty, name), out element);

    /// <summary>
    /// 누른다.
    ///
    /// InvokePattern 이 있으면 그걸 쓴다 — 좌표를 안 거치므로 창이 가려져 있어도 되고
    /// 커서도 안 움직인다. 없으면 토글(체크박스)이나 선택(리스트 항목)으로 물러난다.
    /// </summary>
    public bool Invoke(UiElement element)
    {
        var source = AsAutomationElement(element);
        if (source is null)
            return false;

        try
        {
            if (source.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke))
            {
                ((InvokePattern)invoke).Invoke();
                return true;
            }

            if (source.TryGetCurrentPattern(TogglePattern.Pattern, out var toggle))
            {
                ((TogglePattern)toggle).Toggle();
                return true;
            }

            if (source.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var select))
            {
                ((SelectionItemPattern)select).Select();
                return true;
            }

            Logger.Debug($"누를 수 있는 방법이 없는 요소다: {element}");
            return false;
        }
        catch (Exception ex)
        {
            Logger.Debug($"요소를 누르지 못했다({element}): {ex.Message}");
            return false;
        }
    }

    public bool SetText(UiElement element, string text)
    {
        var source = AsAutomationElement(element);
        if (source is null)
            return false;

        try
        {
            if (!source.TryGetCurrentPattern(ValuePattern.Pattern, out var value))
            {
                Logger.Debug($"값을 넣을 수 없는 요소다: {element}");
                return false;
            }

            var pattern = (ValuePattern)value;
            if (pattern.Current.IsReadOnly)
                return false;

            pattern.SetValue(text);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Debug($"값을 넣지 못했다({element}): {ex.Message}");
            return false;
        }
    }

    public bool TryGetText(UiElement element, out string text)
    {
        text = string.Empty;

        var source = AsAutomationElement(element);
        if (source is null)
            return false;

        try
        {
            if (source.TryGetCurrentPattern(ValuePattern.Pattern, out var value))
            {
                text = ((ValuePattern)value).Current.Value ?? string.Empty;
                return true;
            }

            // 값 패턴이 없으면 이름이 곧 보이는 글자인 경우가 많다(레이블·버튼).
            text = source.Current.Name ?? string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            Logger.Debug($"값을 읽지 못했다({element}): {ex.Message}");
            return false;
        }
    }

    public bool FocusElement(UiElement element)
    {
        var source = AsAutomationElement(element);
        if (source is null)
            return false;

        try
        {
            source.SetFocus();
            return true;
        }
        catch (Exception ex)
        {
            Logger.Debug($"포커스를 주지 못했다({element}): {ex.Message}");
            return false;
        }
    }

    // ── 안쪽 ──────────────────────────────────────────────────────────────

    private bool TryFind(UiElement scope, Condition condition, out UiElement element)
    {
        element = null!;

        var source = AsAutomationElement(scope);
        if (source is null)
            return false;

        try
        {
            var found = source.FindFirst(TreeScope.Descendants, condition);
            if (found is null)
                return false;

            return TryWrap(() => found, out element);
        }
        catch (Exception ex)
        {
            Logger.Debug($"요소를 찾지 못했다: {ex.Message}");
            return false;
        }
    }

    private bool TryWrap(Func<AutomationElement?> resolve, out UiElement element)
    {
        element = null!;

        try
        {
            var source = resolve();
            if (source is null)
                return false;

            var described = Describe(source);
            if (described is null)
                return false;

            element = described;
            return true;
        }
        catch (Exception ex)
        {
            Logger.Debug($"요소를 읽지 못했다: {ex.Message}");
            return false;
        }
    }

    /// <summary>AutomationElement 의 지금 상태를 우리 모델로 옮긴다.</summary>
    private static UiElement? Describe(AutomationElement source)
    {
        try
        {
            var current = source.Current;

            return new UiElement
            {
                Name = current.Name ?? string.Empty,
                AutomationId = current.AutomationId ?? string.Empty,
                ControlType = current.ControlType?.ProgrammaticName?.Replace("ControlType.", string.Empty) ?? "Unknown",
                Bounds = current.BoundingRectangle,
                IsEnabled = current.IsEnabled,
                IsOffscreen = current.IsOffscreen,
                NativeElement = source
            };
        }
        catch
        {
            // 읽는 사이에 사라진 요소다. 자동화에서는 흔한 일이라 조용히 넘긴다.
            return null;
        }
    }

    private static AutomationElement? AsAutomationElement(UiElement? element)
        => element?.NativeElement as AutomationElement;
}
