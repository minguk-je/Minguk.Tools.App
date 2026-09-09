using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Minguk.Tools.Input.Sequencing;

/// <summary>
/// 단계 하나를 적어 둔 것. 실행하는 물건이 아니라 <b>저장·편집되는 데이터</b>다.
///
/// 왜 한 클래스에 모든 칸을 두는가
///   종류마다 클래스를 나누면(TypeStep · MoveStep …) 다형성은 깔끔해지지만, 그리드 한 장에서
///   종류를 바꿔 가며 편집할 수 없다. 종류를 바꾸는 순간 다른 객체가 되어 버리기 때문이다.
///   칸 몇 개가 노는 대신 종류를 콤보로 고르는 쪽이 편집기에 맞다.
///   쓰지 않는 칸은 <see cref="Describe"/> 도 화면도 무시한다.
///
/// 이 형식은 그대로 JSON 이 되어 설정에 들어간다(<see cref="SequencePlan"/>).
/// 칸을 지우거나 이름을 바꾸면 예전에 저장된 시퀀스를 못 읽으므로, 늘리기만 한다.
/// </summary>
public sealed class SequenceStepDefinition : INotifyPropertyChanged
{
    private SequenceStepKind _kind;
    private string? _text;
    private int _x;
    private int _y;
    private int _notches = -1;
    private int _delayMs = 500;
    private MouseButton _button = MouseButton.Left;

    public SequenceStepKind Kind
    {
        get => _kind;
        // 종류가 바뀌면 요약도 통째로 달라진다.
        set => Set(ref _kind, value);
    }

    /// <summary><see cref="SequenceStepKind.Type"/> 이 누를 글자들.</summary>
    public string? Text { get => _text; set => Set(ref _text, value); }

    public int X { get => _x; set => Set(ref _x, value); }

    public int Y { get => _y; set => Set(ref _y, value); }

    /// <summary>굴릴 칸 수. 양수가 위, 음수가 아래.</summary>
    public int Notches { get => _notches; set => Set(ref _notches, value); }

    /// <summary>쉬는 시간.</summary>
    public int DelayMs { get => _delayMs; set => Set(ref _delayMs, value); }

    public MouseButton Button { get => _button; set => Set(ref _button, value); }

    /// <summary>목록에 보여 줄 종류 이름. 저장되는 것은 열거형 이름이고, 이것은 표시용이다.</summary>
    [JsonIgnore]
    public string KindName => SequenceStepKindNames.Of(Kind);

    /// <summary>그리드에 보여 줄 한 줄 요약. 종류가 쓰는 칸만 읽는다.</summary>
    [JsonIgnore]
    public string Summary => Describe();

    public string Describe() => Kind switch
    {
        SequenceStepKind.Type => string.IsNullOrEmpty(Text) ? "글자 (비어 있음)" : $"글자 \"{Text}\"",
        SequenceStepKind.Enter => "Enter",
        SequenceStepKind.ToggleHangul => "한/영",
        SequenceStepKind.Click => Button switch
        {
            MouseButton.Right => "우클릭",
            MouseButton.Middle => "휠클릭",
            _ => "좌클릭"
        },
        SequenceStepKind.MoveTo => $"이동 ({X}, {Y})",
        SequenceStepKind.Scroll => Notches < 0 ? $"휠 아래 {-Notches}칸" : $"휠 위 {Notches}칸",
        SequenceStepKind.Wait => $"{DelayMs}ms 쉬기",
        _ => Kind.ToString()
    };

    public SequenceStepDefinition Clone() => new()
    {
        Kind = Kind, Text = Text, X = X, Y = Y,
        Notches = Notches, DelayMs = DelayMs, Button = Button
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // 어느 칸이 바뀌든 요약은 다시 읽어야 한다.
        if (name != nameof(Summary))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Summary)));

        if (name == nameof(Kind))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(KindName)));
    }
}
