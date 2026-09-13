namespace Minguk.Tools.Markup;

/// <summary>
/// 편집기에 "이 줄로 가라" 는 요청 한 번.
/// </summary>
/// <remarks>
/// 줄 번호(int)를 그대로 묶으면 같은 줄로 두 번 갈 때 값이 안 바뀌어 알림이 안 온다 - 오류 목록에서 같은 줄을 또 눌러도
/// 편집기가 안 움직인다. 요청마다 새 객체라 늘 온다.
/// </remarks>
public sealed record EditorLineRequest(int Line)
{
    // record 의 값 같음을 끈다 - 같은 줄이라도 다른 요청이다.
    public bool Equals(EditorLineRequest? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
}
