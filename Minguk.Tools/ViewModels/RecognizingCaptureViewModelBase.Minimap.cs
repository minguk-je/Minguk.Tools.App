using System;
using System.Linq;
using System.Windows.Input;

using DevExpress.Mvvm;

using Minguk.Tools.Vision.Minimap;
using Minguk.Tools.Vision.Regions;

namespace Minguk.Tools.ViewModels;

/// <summary>
/// 미니맵 익히기 - 화살표 본보기와 기준각을 프로젝트에 남긴다. 스크립트의 <c>방위()</c>·<c>가기()</c> 가 이것을 읽는다.
/// </summary>
/// <remarks>
/// 사용자(2026-09-23) "미니맵으로 방향 보면서 처리가 가능해?" - 단추 한 번으로 끝난다. 앱이 색으로 화살표를 뽑고
/// 모양에서 머리 방향까지 가린다(<see cref="MinimapReader"/>). 다만 아이콘에 따라 머리와 꼬리의 무게가 비슷하면
/// 판별이 흔들려(실측: 여유 1.05배인 장면에서 20° 어긋남) <b>여유 배수를 같이 알리고</b> 낮으면 확인하라고 말한다.
/// </remarks>
public abstract partial class RecognizingCaptureViewModelBase
{
    /// <summary>프레임을 기다릴 최대 시간(ms) - 리드백을 켜며 캡처 세션이 다시 만들어지면 1초 넘게 걸린다.</summary>
    private const int MinimapFrameWaitMs = 5000;

    /// <summary>이보다 여유가 작으면 어느 쪽이 머리인지 뚜렷하지 않다 - 사람에게 확인하라고 한다.</summary>
    private const double MinimapHeadMarginWarn = 1.1;

    /// <summary>미니맵 화살표를 익혀 본보기와 기준각을 프로젝트에 남긴다.</summary>
    public ICommand LearnMinimapCommand => new DelegateCommand(DoLearnMinimap);

    private async void DoLearnMinimap() => await GuardAsync(async () =>
    {
        if (!IsRunning)
        {
            StatusText = "먼저 캡처를 시작해 화면을 잡아야 미니맵을 익힐 수 있습니다.";
            Logger.Info("미니맵 익히기 못 함: 캡처가 멈춰 있다");
            return;
        }

        var spec = LoadMinimapSpec();

        if (RegionBook.Resolve(spec.Region) is not { } found)
        {
            StatusText = $"「{spec.Region}」 자리가 없습니다 - 영역 패널에서 [새 영역]으로 미니맵을 둘러 그 이름을 붙이세요.";
            Logger.Info($"미니맵 익히기 못 함: 자리 없음 ({spec.Region})");
            return;
        }

        Hub.WantsFrames = true;

        var target = RegionTargets.Of(found.Region, found.Cell)[0];
        var deadline = Environment.TickCount64 + MinimapFrameWaitMs;
        System.Windows.Media.Imaging.BitmapSource? crop = null;

        while (Environment.TickCount64 < deadline && (!RegionTargets.TryCrop(Hub, target, out crop) || crop is null))
            await System.Threading.Tasks.Task.Delay(50);

        if (crop is null)
        {
            StatusText = "프레임이 안 옵니다 - 캡처가 돌고 있는지, CPU 리드백이 켜져 있는지 보세요.";
            Logger.Warn("미니맵 익히기 못 함: 5초 안에 프레임이 안 왔다");
            return;
        }

        var (pixels, width, height) = MinimapBgra(crop);

        if (MinimapReader.FindArrow(pixels, width, height, spec) is not { } arrow)
        {
            StatusText = $"미니맵 한가운데에서 화살표를 못 찾았습니다 - 「{spec.Region}」 자리가 미니맵에 맞는지, 캐릭터 화살표가 가운데 오는지 보세요.";
            Logger.Info($"미니맵 익히기 못 함: 화살표 없음 ({width}x{height})");
            return;
        }

        // 조각 원본을 남긴다 - 사람이 열어 볼 수 있고, 색 규칙을 고쳐도 다시 뽑을 수 있다.
        var folder = TemplateFolder;

        System.IO.Directory.CreateDirectory(folder);

        var path = System.IO.Path.Combine(folder, MinimapSpec.TemplateName);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();

        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(crop));

        using (var file = System.IO.File.Create(path)) encoder.Save(file);

        spec.HeadingAtTemplate = arrow.HeadAngle;
        spec.Save(System.IO.Path.Combine(RecognitionRoot, MinimapSpec.FileName));

        var where = Compass(arrow.HeadAngle);

        StatusText = arrow.HeadMargin >= MinimapHeadMarginWarn
            ? $"미니맵을 익혔습니다 - 화살표가 {arrow.HeadAngle:0}도({where})를 향한 것으로 봤습니다. 스크립트에서 방위() 로 읽습니다."
            : $"미니맵을 익혔습니다 - 화살표가 {arrow.HeadAngle:0}도({where})를 향한 것 같지만 뚜렷하지 않습니다(여유 {arrow.HeadMargin:0.00}배). "
              + $"스크립트에서 출력(방위()) 을 찍어 보고 다르면 {MinimapSpec.FileName} 의 headingAtTemplate 을 고치세요.";

        Logger.Info($"미니맵 익히기: 화살표 {arrow.Mask.Count}px · 머리 {arrow.HeadAngle:0}도 · 여유 {arrow.HeadMargin:0.00}배 · {path}");

        WarnAboutMarkers(spec, pixels, width, height, arrow);
    });

    /// <summary>
    /// 지금 화면에서 목표 마커로 잡히는 것들을 알린다 - 미니맵에 겹친 시계·나침반이 목표로 읽히는 것을 미리 잡는다.
    /// </summary>
    /// <remarks>
    /// 게임 시계 옆 해 아이콘(☀)은 목표 마커와 색이 거의 같아(실측 2026-09-23) 색으로 못 거른다. 잡힌 자리를
    /// <c>ignore</c> 에 넣을 비율 그대로 찍어 주므로 붙여 넣기만 하면 된다.
    /// </remarks>
    private void WarnAboutMarkers(MinimapSpec spec, byte[] pixels, int width, int height, MinimapArrow arrow)
    {
        var markers = MinimapReader.Markers(pixels, width, height, spec, arrow.CenterX, arrow.CenterY, out var places);

        if (markers.Count == 0) return;

        var boxes = string.Join(", ", places.Select(p =>
            $"[{Math.Max(0, (p.X / (double)width) - 0.06):0.00}, {Math.Max(0, (p.Y / (double)height) - 0.06):0.00}, 0.12, 0.12]"));

        Logger.Info($"미니맵 익히기: 목표 마커로 잡히는 노란 표시 {markers.Count}곳 - ignore 후보 {boxes}");

        StatusText += $" 노란 표시 {markers.Count}곳이 목표 마커로 잡힙니다 - 게임 시계 같은 UI 라면 {MinimapSpec.FileName} 의 ignore 에 {boxes} 를 넣으세요(로그에도 적었습니다).";
    }

    /// <summary>프로젝트의 minimap.json - 없으면 기본값으로 새로 만든다.</summary>
    private MinimapSpec LoadMinimapSpec()
    {
        var path = System.IO.Path.Combine(RecognitionRoot, MinimapSpec.FileName);

        try
        {
            return MinimapSpec.Load(path) ?? new MinimapSpec();
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or System.IO.IOException)
        {
            Logger.Warn(ex, $"미니맵 설정을 못 읽어 기본값으로 익힌다: {path}");

            return new MinimapSpec();
        }
    }

    private static (byte[] Pixels, int Width, int Height) MinimapBgra(System.Windows.Media.Imaging.BitmapSource source)
    {
        var converted = source.Format == System.Windows.Media.PixelFormats.Bgra32
            ? source
            : new System.Windows.Media.Imaging.FormatConvertedBitmap(source, System.Windows.Media.PixelFormats.Bgra32, null, 0);

        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        var pixels = new byte[width * height * 4];

        converted.CopyPixels(pixels, width * 4, 0);

        return (pixels, width, height);
    }

    /// <summary>방위를 여덟 방향 이름으로 - 사람이 미니맵을 보고 맞는지 바로 견줄 수 있게.</summary>
    private static string Compass(double bearing)
    {
        string[] names = ["북", "북동", "동", "남동", "남", "남서", "서", "북서"];

        return names[(int)Math.Round(MinimapReader.Normalize(bearing) / 45) % 8];
    }
}
