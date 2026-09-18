using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace Minguk.Tools.Input.Hotkeys;

/// <summary>
/// <see cref="ISharedHotkeys"/> 의 구현. 조합 하나에 쥔 화면들의 줄을 두고, 맨 뒤(마지막에 쥐거나 활성화한 것)가 받는다.
/// </summary>
/// <remarks>
/// 시스템 등록(<see cref="IGlobalHotkeyAdapter"/>)은 조합마다 한 번. 줄이 비면 푼다 - 놓아 주지 않으면 그 키가
/// 다른 프로그램에서 잠긴 채로 남는다. 어댑터는 첫 쥠 때 만든다 - HwndSource 는 메시지 루프가 있는 UI 스레드여야
/// 하고, 화면의 OnLoaded 가 거기다.
/// </remarks>
public sealed class SharedHotkeys : ISharedHotkeys
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly object _gate = new();
    private readonly Func<IGlobalHotkeyAdapter> _createAdapter;
    private readonly Dictionary<(Key, ModifierKeys), List<HotkeyClaim>> _claims = [];
    private IGlobalHotkeyAdapter? _adapter;

    /// <param name="createAdapter">시스템 등록 경로. 검증 하네스는 가짜를 꽂아 눌림을 흉내 낸다.</param>
    public SharedHotkeys(Func<IGlobalHotkeyAdapter> createAdapter) => _createAdapter = createAdapter;

    public HotkeyClaim? Claim(Key key, ModifierKeys modifiers, string label, Action action, out string? problem)
    {
        problem = null;

        lock (_gate)
        {
            var combo = (key, modifiers);

            if (!_claims.TryGetValue(combo, out var queue))
            {
                _adapter ??= _createAdapter();

                if (!_adapter.TryRegister(key, modifiers, () => Fire(combo)))
                {
                    problem = $"{label} 를 등록하지 못했습니다 - 다른 프로그램이 쥐고 있거나 Windows 가 예약한 키입니다.";
                    return null;
                }

                queue = [];
                _claims[combo] = queue;
            }

            var claim = new HotkeyClaim(key, modifiers, label, action, Activate, Release);
            queue.Add(claim);

            Logger.Debug($"단축키 {Describe(combo)} 를 쥠: {label} (나눠 쓰는 화면 {queue.Count}개)");
            return claim;
        }
    }

    /// <summary>눌렸다. 줄의 맨 뒤가 받는다. 어댑터가 UI 스레드에서 부르므로 화면을 그대로 만져도 된다.</summary>
    private void Fire((Key, ModifierKeys) combo)
    {
        HotkeyClaim? claim;

        lock (_gate)
        {
            claim = _claims.TryGetValue(combo, out var queue) ? queue.LastOrDefault() : null;
        }

        Logger.Debug($"단축키 {Describe(combo)} 눌림 → {claim?.Label ?? "(받는 화면 없음)"}");
        claim?.Action();
    }

    private void Activate(HotkeyClaim claim)
    {
        lock (_gate)
        {
            if (!_claims.TryGetValue((claim.Key, claim.Modifiers), out var queue) || !queue.Remove(claim)) return;

            queue.Add(claim);
        }
    }

    private void Release(HotkeyClaim claim)
    {
        lock (_gate)
        {
            var combo = (claim.Key, claim.Modifiers);

            if (!_claims.TryGetValue(combo, out var queue) || !queue.Remove(claim)) return;
            if (queue.Count > 0) return;

            _claims.Remove(combo);
            _adapter?.Unregister(claim.Key, claim.Modifiers);

            Logger.Debug($"단축키 {Describe(combo)} 를 놓음: 쥔 화면이 없다");
        }
    }

    private static string Describe((Key Key, ModifierKeys Modifiers) combo)
        => combo.Modifiers == ModifierKeys.None ? combo.Key.ToString() : $"{combo.Modifiers}+{combo.Key}";

    public void Dispose()
    {
        lock (_gate)
        {
            _claims.Clear();
            _adapter?.Dispose();
            _adapter = null;
        }
    }
}
