namespace Minguk.Tools.Input.Adapters;

/// <summary>
/// 아무것도 보내지 않는 경로 - 캡처 대상이 영상 파일일 때 스크립트가 쓴다(<see cref="InputAdapterFactory.CreateSilent"/>).
/// </summary>
/// <remarks>
/// 영상을 틀어 검출·글자 읽기·스크립트 흐름을 시험할 때, 스크립트의 키·클릭·조준이 <b>진짜 화면</b>으로 나가면 안 된다
/// (SendInput 은 앞에 있는 아무 창에나 들어간다). 부른 것은 호출 로그에 남고, 보낸 것으로 친다(true) - 막혔다고 스크립트가 멈추면 흐름을 못 본다.
/// 커서 자리는 모른다(null) - 커서를 대상 안으로 옮기는 코드가 그것을 보고 건너뛴다.
/// </remarks>
public sealed class SilentInputAdapter : IInputAdapter
{
    public string Name => "보내지 않음(영상)";

    public bool IsAvailable => true;

    public string? UnavailableReason => null;

    public bool RequiresForegroundTarget => false;

    public (int X, int Y)? GetCursorPosition() => null;

    public bool MoveMouseTo(int screenX, int screenY) => true;

    public bool MoveMouseBy(int deltaX, int deltaY) => true;

    public bool PressMouseButton(MouseButton button) => true;

    public bool ReleaseMouseButton(MouseButton button) => true;

    public bool ClickMouseButton(MouseButton button) => true;

    public bool ScrollWheel(int delta) => true;

    public bool PressKey(ushort virtualKey) => true;

    public bool ReleaseKey(ushort virtualKey) => true;

    public void Dispose()
    {
    }
}
