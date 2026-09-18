using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Vortice.Direct3D11;
using Vortice.DXGI;

using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;

using WinRT;

namespace Minguk.Tools.Capture;

/// <summary>
/// WinRT(Windows.Graphics.Capture) 와 D3D11 사이를 잇는 COM 인터롭.
///
/// WGC 는 C# 프로젝션만으로는 다 못 쓴다. 두 군데서 네이티브로 내려가야 한다.
///   1) 캡처 대상 만들기 — GraphicsCaptureItem 에는 HWND/HMONITOR 를 받는 공개 생성자가 없다.
///      활성화 팩터리를 IGraphicsCaptureItemInterop 으로 QI 해서 CreateForWindow/CreateForMonitor 를 쓴다.
///   2) 프레임에서 텍스처 꺼내기 — IDirect3DSurface 를 IDirect3DDxgiInterfaceAccess 로 QI 해서
///      ID3D11Texture2D 를 얻는다. 이게 GPU 에 이미 올라와 있는 프레임 원본이다(복사 없음).
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
internal static class CaptureInterop
{
    private static readonly Guid GraphicsCaptureItemIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid GraphicsCaptureItemInteropIid = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);

        IntPtr CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid);
    }

    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        IntPtr GetInterface([In] ref Guid iid);
    }

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    [DllImport("combase.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int WindowsCreateString(string sourceString, int length, out IntPtr hstring);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);

    /// <summary>캡처 대상(창 또는 모니터)에 대응하는 GraphicsCaptureItem 을 만든다.</summary>
    public static GraphicsCaptureItem CreateItem(CaptureTarget target)
    {
        var interop = GetItemInterop();
        var iid = GraphicsCaptureItemIid;

        var abi = target.Kind == CaptureTargetKind.Window
            ? interop.CreateForWindow(target.Handle, ref iid)
            : interop.CreateForMonitor(target.Handle, ref iid);

        if (abi == IntPtr.Zero)
            throw new InvalidOperationException($"캡처 대상을 만들지 못했다: {target.Display}");

        try
        {
            return MarshalInspectable<GraphicsCaptureItem>.FromAbi(abi);
        }
        finally
        {
            // FromAbi 가 자기 참조를 따로 잡으므로 CreateForXxx 가 준 참조는 여기서 놓는다.
            Marshal.Release(abi);
        }
    }

    /// <summary>D3D11 디바이스를 WinRT 가 받아 주는 IDirect3DDevice 로 감싼다.</summary>
    public static IDirect3DDevice CreateDirect3DDevice(ID3D11Device device)
    {
        using var dxgiDevice = device.QueryInterface<IDXGIDevice>();

        var hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var abi);
        if (hr < 0)
            Marshal.ThrowExceptionForHR(hr);

        try
        {
            return MarshalInspectable<IDirect3DDevice>.FromAbi(abi);
        }
        finally
        {
            Marshal.Release(abi);
        }
    }

    /// <summary>
    /// 캡처된 프레임의 표면에서 D3D11 텍스처를 꺼낸다. 복사가 아니라 GPU 원본을 가리키는 참조다.
    /// 프레임(<see cref="Direct3D11CaptureFrame"/>)을 Dispose 하고 나면 이 텍스처도 무효다.
    /// </summary>
    public static ID3D11Texture2D GetTexture(IDirect3DSurface surface)
    {
        var access = surface.As<IDirect3DDxgiInterfaceAccess>();
        var iid = typeof(ID3D11Texture2D).GUID;

        var ptr = access.GetInterface(ref iid);
        if (ptr == IntPtr.Zero)
            throw new InvalidOperationException("프레임 표면에서 ID3D11Texture2D 를 얻지 못했다.");

        return new ID3D11Texture2D(ptr);
    }

    /// <summary>
    /// GraphicsCaptureItem 의 활성화 팩터리를 IGraphicsCaptureItemInterop 으로 가져온다.
    ///
    /// CsWinRT 의 내부 헬퍼(WinRT.ActivationFactory 등)는 버전에 따라 접근성이 바뀌어 왔다.
    /// 여기서는 RoGetActivationFactory 를 직접 불러 그 의존을 없앤다.
    /// </summary>
    private static IGraphicsCaptureItemInterop GetItemInterop()
    {
        const string className = "Windows.Graphics.Capture.GraphicsCaptureItem";

        var hr = WindowsCreateString(className, className.Length, out var hstring);
        if (hr < 0)
            Marshal.ThrowExceptionForHR(hr);

        try
        {
            var iid = GraphicsCaptureItemInteropIid;
            hr = RoGetActivationFactory(hstring, ref iid, out var factory);
            if (hr < 0)
                Marshal.ThrowExceptionForHR(hr);

            try
            {
                return (IGraphicsCaptureItemInterop)Marshal.GetTypedObjectForIUnknown(factory, typeof(IGraphicsCaptureItemInterop));
            }
            finally
            {
                Marshal.Release(factory);
            }
        }
        finally
        {
            WindowsDeleteString(hstring);
        }
    }
}
