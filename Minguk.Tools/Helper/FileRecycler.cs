using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Minguk.Tools.Helper;

/// <summary>
/// 파일·폴더를 휴지통으로 보낸다. 영구 삭제가 아니라 되돌릴 수 있다.
/// </summary>
/// <remarks>
/// 솔루션 탐색기의 Delete 가 쓴다(VS 처럼 파일을 지우되, 잘못 눌러도 되찾을 수 있게 - 사용자 결정 2026-09-13).
/// OS 에 닿는 것이라 어댑터 규칙대로 인터페이스·구현·팩터리로 둔다.
/// </remarks>
public interface IFileRecycler
{
    string Name { get; }

    /// <summary>휴지통으로 보낸다. 없는 경로면 아무것도 안 하고 true.</summary>
    /// <exception cref="IOException">보내지 못했다.</exception>
    void Recycle(string path);
}

/// <summary>셸의 파일 작업(SHFileOperationW, FOF_ALLOWUNDO). 탐색기에서 Delete 를 누른 것과 같다.</summary>
public sealed class ShellFileRecycler : IFileRecycler
{
    public string Name => "셸 휴지통";

    public void Recycle(string path)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full) && !Directory.Exists(full)) return;

        var operation = new NativeMethods.SHFILEOPSTRUCTW
        {
            wFunc = NativeMethods.FO_DELETE,
            // 목록은 널 두 개로 끝나야 한다.
            pFrom = full + "\0\0",
            fFlags = NativeMethods.FOF_ALLOWUNDO | NativeMethods.FOF_NOCONFIRMATION | NativeMethods.FOF_SILENT | NativeMethods.FOF_NOERRORUI
        };

        var result = NativeMethods.SHFileOperationW(ref operation);

        if (result != 0 || operation.fAnyOperationsAborted)
            throw new IOException($"휴지통으로 보내지 못했습니다(코드 0x{result:X}): {full}");
    }

    private static class NativeMethods
    {
        public const uint FO_DELETE = 0x0003;
        public const ushort FOF_SILENT = 0x0004;
        public const ushort FOF_NOCONFIRMATION = 0x0010;
        public const ushort FOF_ALLOWUNDO = 0x0040;
        public const ushort FOF_NOERRORUI = 0x0400;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SHFILEOPSTRUCTW
        {
            public IntPtr hwnd;
            public uint wFunc;
            [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
            [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
            public ushort fFlags;
            [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SHFileOperationW(ref SHFILEOPSTRUCTW lpFileOp);
    }
}

public static class FileRecyclerFactory
{
    public static IFileRecycler Create() => new ShellFileRecycler();
}
