using Minguk.Tools.Vision.HealthBars;
using Minguk.Tools.Vision.Regions;

namespace Minguk.Tools.Input.Scripting.Live;

/// <summary>
/// 자리에 놓인 막대가 몇 할 찼나 - 대상 체력바·마나·게이지. 게임과 상관없이 「찬 칸은 색이 있고 빈 칸은 회색」 으로 잰다(<see cref="BarFill"/>).
/// </summary>
public partial class LiveScriptApi
{
    /// <summary>
    /// 그 이름 자리(칸)의 막대가 몇 할 찼나(0~1). 왼쪽부터 색이 있는 칸이 이어진 길이다.
    /// </summary>
    /// <remarks>
    /// 사용자(2026-09-25) "체력바로 확인해줘" - 대상 이름 글자만 믿고 공격하던 것을, 대상 체력바가 비었는지(몹이 죽었거나 대상이 없다)로도 본다.
    /// 막대가 없는 자리(대상이 없어 게임 화면이 비치는 곳)는 색이 있어 엉뚱한 값이 나올 수 있다 - 이름 같은 다른 표시와 같이 본다.
    /// </remarks>
    public double BarFillAt(string name) => Traced("BarFillAt", Quote(name), () =>
    {
        var book = _host.Regions?.Invoke() ?? throw Guard("영역 목록이 없습니다 - 화면에서 데이터셋 폴더를 골라야 합니다.");
        var found = book.Resolve(name) ?? throw Guard(MissingRegion(book, name));

        _host.Hub.TryGetFrameSize(out var frameWidth, out var frameHeight);

        var area = RegionTargets.Bounds(RegionTargets.Of(found.Region, found.Cell)[0], frameWidth, frameHeight);
        var (pixels, width, height) = ToBgra(CropFor(area));

        return System.Math.Round(BarFill.Measure(pixels, width, height), 3);
    });

    public double 채움(string 이름) => BarFillAt(이름);

    /// <summary>
    /// 그 이름 자리(칸)의 평균 밝기(0~255) - 켜진 단추와 꺼진(어두운) 단추를 가른다.
    /// </summary>
    /// <remarks>
    /// 사용자(2026-09-26) "노란색이 어두워 졌네... 그럼 우측 상단 X 눌러줘" - 재료가 모자라 꺼진 단추도 모양은 같아 본보기(정규화 상호상관)로는
    /// 켜진 것과 못 가르고(밝기에 안 흔들리게 만든 것이라), 채움은 색이 있는지만 봐서 둘 다 찬 것으로 본다.
    /// 아이온2 모노리스 「공명」 단추 실측: 켜짐 평균 (225, 176, 36) → 약 176 · 꺼짐 (111, 86, 16) → 약 87.
    /// </remarks>
    public double BrightnessAt(string name) => Traced("BrightnessAt", Quote(name), () =>
    {
        var book = _host.Regions?.Invoke() ?? throw Guard("영역 목록이 없습니다 - 화면에서 데이터셋 폴더를 골라야 합니다.");
        var found = book.Resolve(name) ?? throw Guard(MissingRegion(book, name));

        _host.Hub.TryGetFrameSize(out var frameWidth, out var frameHeight);

        var area = RegionTargets.Bounds(RegionTargets.Of(found.Region, found.Cell)[0], frameWidth, frameHeight);
        var (pixels, width, height) = ToBgra(CropFor(area));

        if (width <= 0 || height <= 0) return 0;

        double sum = 0;

        for (var i = 0; i + 3 < width * height * 4 && i + 3 < pixels.Length; i += 4)
            sum += (0.114 * pixels[i]) + (0.587 * pixels[i + 1]) + (0.299 * pixels[i + 2]);

        return System.Math.Round(sum / (width * height), 1);
    });

    public double 밝기(string 이름) => BrightnessAt(이름);
}
