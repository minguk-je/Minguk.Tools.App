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

    /// <summary>
    /// 줄여서 PNG 로 저장한다. 실제로 저장된 크기를 돌려준다.
    /// </summary>
    /// <remarks>
    /// <b>왜 줄이는가</b>
    ///
    /// 검출망(AutoFormerV2)이 넣은 그림을 줄이지 않고 그대로 본다. 값이 픽셀 수에 거의
    /// 비례해서 1920x1080 한 장이 4.7초다(실측). 320x180 으로 줄이면 220ms 다.
    ///
    /// <b>왜 파일로 떨어뜨리는가</b>
    ///
    /// 저장된 모델 안에 "경로를 받아 그림을 읽는" 단계가 들어 있어서, 추론에 넣으려면
    /// 경로가 있어야 한다. 픽셀을 바로 넘기려면 학습 파이프라인부터 바꾸고 다시 학습해야 한다.
    /// 그럴 만한 값이 아니다 - 실측으로 <b>그림을 읽는 값은 전체의 0%</b> 였다(4.3ms / 4,724ms).
    /// </remarks>
    public static (int Width, int Height) SaveScaledPng(CapturedFrameEventArgs frame, string path, int longestSide)
    {
        if (!frame.HasPixels)
            throw new InvalidOperationException("CPU 리드백이 꺼져 있어 저장할 픽셀이 없다.");

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var source = BitmapSource.Create(
            frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null,
            frame.PixelData, frame.RowPitch * frame.Height, frame.RowPitch);

        source.Freeze();

        var longest = Math.Max(frame.Width, frame.Height);
        var scale = longest > longestSide ? (double)longestSide / longest : 1d;

        BitmapSource scaled = source;

        if (scale < 1d)
        {
            scaled = new TransformedBitmap(source, new ScaleTransform(scale, scale));
            scaled.Freeze();
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(scaled));

        using var stream = File.Create(path);
        encoder.Save(stream);

        return (scaled.PixelWidth, scaled.PixelHeight);
    }
}
