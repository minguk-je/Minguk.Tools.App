using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Minguk.Tools.Input.Sequencing;

/// <summary>
/// 적어 둔 단계들의 묶음. 저장되는 단위이자, 실행할 <see cref="InputSequence"/> 를 찍어내는 틀이다.
///
/// 왜 <see cref="InputSequence"/> 와 따로 두는가
///   <see cref="InputSequence"/> 는 <see cref="InputService"/> 를 물고 있어서 경로를 고르기 전에는
///   만들 수도 없고, 델리게이트라서 저장할 수도 없다. 계획은 경로와 무관하게 적어 두고,
///   돌릴 때 그때의 서비스로 <see cref="Build"/> 한다. 경로를 바꿔도 계획은 그대로다.
/// </summary>
public sealed class SequencePlan
{
    public List<SequenceStepDefinition> Steps { get; init; } = [];

    /// <summary>
    /// 지금 서비스로 실행 가능한 시퀀스를 만든다.
    /// </summary>
    /// <remarks>
    /// 글자를 못 넣는 경로(PostMessage 의 스캔코드처럼)에서는 <see cref="InputSequence"/> 가
    /// 해당 단계를 조용히 빼므로, 여기서 걸러 낼 것이 없다. 결과가 비면 부르는 쪽이 알아챈다.
    /// </remarks>
    public InputSequence Build(InputService service, int holdTimeMs)
    {
        var sequence = new InputSequence(service, holdTimeMs);

        foreach (var step in Steps)
        {
            switch (step.Kind)
            {
                case SequenceStepKind.Type: sequence.Type(step.Text ?? string.Empty); break;
                case SequenceStepKind.Enter: sequence.Enter(); break;
                case SequenceStepKind.ToggleHangul: sequence.ToggleHangul(); break;
                case SequenceStepKind.Click: sequence.Click(step.Button); break;
                case SequenceStepKind.MoveTo: sequence.MoveTo(step.X, step.Y); break;
                case SequenceStepKind.Scroll: sequence.Scroll(step.Notches); break;
                case SequenceStepKind.Wait: sequence.Wait(step.DelayMs); break;
            }
        }

        return sequence;
    }

    // 종류를 숫자로 적으면 나중에 열거형 순서를 바꿨을 때 저장된 것이 다른 뜻이 된다.
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = false
    };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>
    /// 저장된 것을 되읽는다. 비었거나 깨졌으면 <see cref="CreateDefault"/> 를 준다.
    /// </summary>
    /// <remarks>
    /// 설정 파일은 사람이 손댈 수 있고 형식도 버전에 따라 달라진다. 여기서 터지면
    /// 화면이 아예 안 뜨므로, 못 읽으면 기본값으로 돌아가고 그 사실만 알린다.
    /// </remarks>
    public static SequencePlan FromJson(string? json, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(json)) return CreateDefault();

        try
        {
            var plan = JsonSerializer.Deserialize<SequencePlan>(json, Options);
            return plan is { Steps.Count: > 0 } ? plan : CreateDefault();
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return CreateDefault();
        }
    }

    /// <summary>처음 열었을 때 보여 줄 것. 무엇을 하는 화면인지 한눈에 보이는 최소 구성이다.</summary>
    public static SequencePlan CreateDefault() => new()
    {
        Steps =
        [
            new SequenceStepDefinition { Kind = SequenceStepKind.Type, Text = "안녕하세요" },
            new SequenceStepDefinition { Kind = SequenceStepKind.Enter }
        ]
    };
}
