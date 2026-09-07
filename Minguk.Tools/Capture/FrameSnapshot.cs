using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Minguk.Tools.Capture;

/// <summary>
/// 캡처된 프레임을 파일로 떨어뜨린다. 캡처가 실제로 무엇을 잡고 있는지 눈으로 확인하는 용도.
/// (DirectX 전체화면이 검은 화면으로 잡히는지 아닌지를 이걸로 판별한다.)
/// </summary>
public static class FrameSnapshot
{
    /// <summary>
    /// BGRA 픽셀을 PNG 로 저장한다. 캡처 콜백 스레드에서 불려도 된다 —
    /// BitmapSource 를 Freeze 하므로 UI 스레드 소유가 아니다.
    /// </summary>
    public static void SavePng(CapturedFrameEventArgs frame, string path)
    {
        if (!frame.HasPixels)
            throw new InvalidOperationException("CPU 리드백이 꺼져 있어 저장할 픽셀이 없다.");

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        // RowPitch 는 Width*4 보다 클 수 있다(GPU 정렬). BitmapSource 에 그대로 넘기면 된다.
        var source = BitmapSource.Create(
            frame.Width,
            frame.Height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            frame.PixelData,
            frame.RowPitch * frame.Height,
            frame.RowPitch);

        source.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));

        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
