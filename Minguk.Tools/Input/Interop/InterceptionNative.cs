using System;
using System.Runtime.InteropServices;

namespace Minguk.Tools.Input.Interop;

/// <summary>
/// interception.dll (oblitum/Interception) 의 P/Invoke 선언.
///
/// 이 드라이버가 무엇인가
///   키보드·마우스 드라이버 스택 "아래" 에 끼어드는 커널 필터 드라이버다.
///   여기서 만든 입력은 진짜 장치가 보낸 것과 구분되지 않아, RawInput·DirectInput 을 쓰는
///   게임처럼 주입 입력을 걸러내는 대상에도 통한다.
///
/// 대신 필요한 것
///   드라이버 설치와 재부팅이 선행되어야 한다. 설치 전에는 컨텍스트 생성이 실패한다.
///   interception.dll (x64) 이 실행 파일 옆에 있어야 한다.
/// </summary>
public static class InterceptionNative
{
    private const string DllName = "interception.dll";

    // 디바이스 번호: 1~10 이 키보드, 11~20 이 마우스다.
    public const int KeyboardFirst = 1;
    public const int MaxKeyboard = 10;
    public const int MouseFirst = MaxKeyboard + 1;

    // 키 상태 플래그
    public const ushort KeyDown = 0x00;
    public const ushort KeyUp = 0x01;

    /// <summary>E0 확장 키 플래그. 방향키·오른쪽 Alt 등이 넘패드와 구분되는 근거다.</summary>
    public const ushort KeyE0 = 0x02;

    // 마우스 버튼·휠 상태 플래그 (조합 가능)
    public const ushort MouseLeftDown = 0x001;
    public const ushort MouseLeftUp = 0x002;
    public const ushort MouseRightDown = 0x004;
    public const ushort MouseRightUp = 0x008;
    public const ushort MouseMiddleDown = 0x010;
    public const ushort MouseMiddleUp = 0x020;

    /// <summary>세로 휠. 회전량은 Rolling 에 싣는다.</summary>
    public const ushort MouseWheel = 0x400;

    /// <summary>가로 휠.</summary>
    public const ushort MouseHWheel = 0x800;

    // 이동 방식 플래그
    public const ushort MouseMoveRelative = 0x000;
    public const ushort MouseMoveAbsolute = 0x001;

    /// <summary>절대 좌표를 주 모니터가 아니라 가상 화면 전체 기준으로 해석하게 한다.</summary>
    public const ushort MouseVirtualDesktop = 0x002;

    /// <summary>휠 한 칸에 해당하는 회전량.</summary>
    public const short WheelDelta = 120;

    [StructLayout(LayoutKind.Sequential)]
    public struct KeyStroke
    {
        public ushort Code;        // 스캔코드
        public ushort State;       // KeyDown/KeyUp + E0
        public uint Information;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MouseStroke
    {
        public ushort State;       // 버튼 Down/Up, 휠 플래그
        public ushort Flags;       // 이동 방식
        public short Rolling;      // 휠 회전량 (WheelDelta 단위)
        public int X;
        public int Y;
        public uint Information;
    }

    /// <summary>키보드와 마우스가 같은 자리를 쓰는 공용체다. 디바이스 번호로 어느 쪽인지 정해진다.</summary>
    [StructLayout(LayoutKind.Explicit)]
    public struct Stroke
    {
        [FieldOffset(0)]
        public KeyStroke Key;

        [FieldOffset(0)]
        public MouseStroke Mouse;
    }

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr interception_create_context();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void interception_destroy_context(IntPtr context);

    /// <returns>실제로 보낸 스트로크 수.</returns>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int interception_send(IntPtr context, int device, ref Stroke stroke, uint count);
}
