using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

using Minguk.Tools.Vision.Minimap;
using Minguk.Tools.Vision.Regions;

namespace Minguk.Tools.Input.Scripting.Live;

/// <summary>
/// 미니맵으로 방향 보기 - 몸이 어디를 향했는지, 목표가 어느 쪽인지, 그쪽으로 돌고 걷기.
/// </summary>
/// <remarks>
/// 사용자(2026-09-23) "미니맵으로 방향 보면서 처리가 가능해?" - 게임에 자동 이동이 있지만 「자동 이동할 수 없는
/// 퀘스트」가 있다. 읽는 규칙과 실측은 <see cref="MinimapReader"/> 와 <c>docs/superpowers/specs/2026-09-23-미니맵-방향-design.md</c>.
///
/// <b>화살표는 몸 방향이지 카메라 방향이 아니다</b>. 그래서 <c>바라보기</c> 는 <b>열린 고리</b>다 - 마우스를 돌려도
/// 화살표가 안 돌아 맞았는지 확인할 길이 없고, 정확도가 회전 배율에 달렸다. <c>가기</c> 는 걸으면서 몸이 카메라를
/// 따라 도는 것을 보고 고치므로 배율이 틀려도 수렴한다 - <b>실제로 쓸 것은 이쪽</b>이다.
/// </remarks>
public partial class LiveScriptApi
{
    /// <summary>이 각(도) 안에 들면 다 돌았다고 본다 - 방위를 재는 정밀도(±1~2°)와 걷는 흔들림을 생각한 값.</summary>
    private const double BearingTolerance = 6;

    /// <summary>걸으면서 방위를 다시 보는 주기(ms). 더 짧으면 프레임을 기다리느라 헛돈다.</summary>
    private const int GoToCheckMs = 120;

    /// <summary>회전 배율을 잴 때 한 번에 보낼 카운트. 너무 크면 180° 를 넘어 어느 쪽으로 돌았는지 헷갈린다.</summary>
    private const int TurnProbeCounts = 150;

    /// <summary>걷기를 멈춘 뒤 미니맵이 따라올 때까지 기다리는 시간(ms).</summary>
    /// <remarks>
    /// 걷자마자 읽으면 <b>아직 돌기 전 화면</b>이 온다 - 입력이 게임에 오르고, 게임이 미니맵을 다시 그리고, 그것이
    /// 캡처로 돌아오기까지 걸린다(조준 쪽 실측 지연 110ms 안팎). 실제 게임에서 배율 재기가 세 번 잇따라 실패하고
    /// 네 번째에 겨우 표본 하나를 얻었다(사용자 로그 2026-09-23 17:11~17:12) - 기다림이 없던 탓이다.
    /// </remarks>
    private const int TurnSettleMs = 250;

    // ── 읽기 ────────────────────────────────────────────────────────────

    /// <summary>캐릭터 몸이 향한 방위(도) - 북 0, 시계 방향. 미니맵을 못 읽으면 멈추고 이유를 말한다.</summary>
    public double Heading() => Traced("Heading", "", HeadingCore);

    /// <summary>미니맵 목표 마커 가운데 가장 가까운 것. 없으면 null.</summary>
    public ScriptBearing? TargetBearing() => Traced("TargetBearing", "", () => TargetBearingCore());

    /// <summary>미니맵 목표 마커를 모두 - 가까운 것부터.</summary>
    public IReadOnlyList<ScriptBearing> TargetBearings() => Traced("TargetBearings", "", () => AllTargetBearings());

    /// <summary>지금 몸 방향에서 그 방위까지 몇 도 돌아야 하나(−180~180). 양수면 오른쪽.</summary>
    public double BearingTo(double bearing) => Traced("BearingTo", bearing.ToString("0.#"), () => MinimapReader.Difference(HeadingCore(), bearing));

    // ── 돌기·가기 ───────────────────────────────────────────────────────

    /// <summary>
    /// 그 방위를 보도록 마우스를 가로로 돌린다. <b>한 번짜리 열린 고리</b> - 몸은 안 돌아 확인할 수 없다.
    /// </summary>
    public void Face(double bearing) => Traced("Face", bearing.ToString("0.#"), () => FaceCore(bearing));

    /// <summary>
    /// 그 방위로 걸어간다 - 걷는 동안 미니맵을 보며 고친다. 회전 배율이 틀려도 수렴한다.
    /// </summary>
    public void GoTo(double bearing, int milliseconds) => Traced("GoTo", $"{bearing:0.#}, {milliseconds}", () => GoToCore(bearing, milliseconds));

    /// <summary>
    /// 미니맵 목표 마커 쪽으로 걸어간다. 마커가 없으면 걷지 않고 false.
    /// </summary>
    public bool GoToTarget(int milliseconds) => Traced("GoToTarget", milliseconds.ToString(), () =>
    {
        if (TargetBearingCore() is not { } target) return false;

        GoToCore(target.Bearing, milliseconds);
        return true;
    });

    /// <summary>
    /// 마우스 가로 1 카운트가 몸을 몇 도 돌리나. 아직 모르면 <b>그 자리에서 재서</b> 프로젝트에 적는다(몇 초).
    /// </summary>
    public double TurnScale() => Traced("TurnScale", "", TurnScaleCore);

    public double 방위() => Heading();

    public ScriptBearing? 목표방위() => TargetBearing();

    public IReadOnlyList<ScriptBearing> 목표방위들() => TargetBearings();

    public double 방위차(double 방위) => BearingTo(방위);

    public void 바라보기(double 방위) => Face(방위);

    public void 가기(double 방위, int 밀리초) => GoTo(방위, 밀리초);

    public bool 목표로가기(int 밀리초) => GoToTarget(밀리초);

    public double 회전배율() => TurnScale();

    // ── 속 ──────────────────────────────────────────────────────────────

    private double HeadingCore()
    {
        var spec = MinimapSpecOrThrow();
        var template = MinimapTemplateOrThrow(spec);
        var (pixels, width, height) = MinimapPixels(spec);

        return MinimapReader.Heading(pixels, width, height, spec, template, spec.HeadingAtTemplate)
               ?? throw Guard($"미니맵에서 화살표를 못 찾았습니다 - 「{spec.Region}」 자리가 미니맵에 맞는지, 화면이 가려지지 않았는지 보세요.");
    }

    private ScriptBearing? TargetBearingCore() => AllTargetBearings().FirstOrDefault();

    private IReadOnlyList<ScriptBearing> AllTargetBearings()
    {
        var spec = MinimapSpecOrThrow();
        var template = MinimapTemplateOrThrow(spec);
        var (pixels, width, height) = MinimapPixels(spec);

        if (MinimapReader.FindArrow(pixels, width, height, spec) is not { } arrow)
            throw Guard($"미니맵에서 화살표를 못 찾았습니다 - 「{spec.Region}」 자리가 미니맵에 맞는지 보세요.");

        var heading = MinimapReader.Normalize(spec.HeadingAtTemplate + MinimapReader.BestRotation(template.Mask, arrow.Mask));

        return MinimapReader.Markers(pixels, width, height, spec, arrow.CenterX, arrow.CenterY)
            .Select(m => new ScriptBearing(m.Bearing, m.Distance, MinimapReader.Difference(heading, m.Bearing)))
            .ToArray();
    }

    private void FaceCore(double bearing)
    {
        var turn = MinimapReader.Difference(HeadingCore(), bearing);
        var scale = TurnScaleCore();

        if (Math.Abs(turn) <= BearingTolerance) return;

        MoveBy((int)Math.Round(turn / scale), 0);
    }

    /// <summary>
    /// 걸으면서 방위를 맞춘다 - 키를 누른 채로 <see cref="GoToCheckMs"/> 마다 미니맵을 보고 어긋난 만큼 마우스를 돌린다.
    /// </summary>
    /// <remarks>
    /// 키를 조각조각 눌렀다 떼면 툭툭 걷는 꼴이 되고 게임에 따라 가속이 안 붙는다 - 누른 채로 고친다.
    /// 중간에 터져도 <c>finally</c> 가 뗀다(<see cref="Walk"/> 와 같은 규칙).
    /// </remarks>
    private void GoToCore(double bearing, int milliseconds)
    {
        if (milliseconds <= 0) return;

        var scale = TurnScaleCore();
        var key = ToVirtualKey("W");
        var deadline = Environment.TickCount64 + milliseconds;

        BeforeInput();

        try
        {
            Hold(key);

            while (Environment.TickCount64 < deadline)
            {
                Wait((int)Math.Min(GoToCheckMs, deadline - Environment.TickCount64));

                if (Environment.TickCount64 >= deadline) break;

                var turn = MinimapReader.Difference(HeadingCore(), bearing);

                if (Math.Abs(turn) <= BearingTolerance) continue;

                MoveBy((int)Math.Round(turn / scale), 0);
            }
        }
        finally
        {
            Release(key);
        }
    }

    /// <summary>
    /// 회전 배율을 잰다 - 마우스를 돌리고 <b>짧게 걸어</b> 몸을 카메라에 맞춘 뒤 방위가 얼마나 달라졌는지 본다.
    /// </summary>
    /// <remarks>
    /// 마우스만 돌려서는 못 잰다 - 화살표는 몸 방향이라 안 돈다(실측 2026-09-23). 그래서 걷기가 한 단계 낀다.
    /// 벽에 막히면 몸이 안 돌아 표본이 0 에 가깝게 나온다 - 그런 것은 버리고, 조준 배율과 같이 <b>가운뎃값</b>을 쓴다.
    /// 한 번에 많이 돌리면 180° 를 넘어 어느 쪽으로 돌았는지 헷갈린다 - 작게 던져 보고 결과를 보며 크기를 맞춘다.
    /// </remarks>
    private double TurnScaleCore()
    {
        var spec = MinimapSpecOrThrow();

        if (spec.DegreesPerCount > 0) return spec.DegreesPerCount;

        Print("회전 배율을 모릅니다 - 지금 재겠습니다(제자리에서 조금 돌고 걷습니다).");

        var samples = new List<double>();
        var tries = new List<string>();
        var counts = TurnProbeCounts;

        for (var attempt = 0; attempt < 12 && samples.Count < 5; attempt++)
        {
            var sent = attempt % 2 == 0 ? counts : -counts;
            var before = HeadingCore();

            MoveBy(sent, 0);
            WalkCore("W", 300);

            // 걷기가 끝나고 미니맵이 따라올 때까지 기다린다 - 이것이 없으면 돌기 전 화면을 읽는다.
            Wait(TurnSettleMs);

            var moved = MinimapReader.Difference(before, HeadingCore());

            tries.Add($"{sent}카운트→{moved:+0;-0;0}도");

            // 너무 적게 돌았으면 벽에 막혔거나 카운트가 작다. 너무 많으면 다음엔 작게 던진다.
            if (Math.Abs(moved) < 4) { counts = Math.Min(600, counts * 2); continue; }
            if (Math.Abs(moved) > 110) { counts = Math.Max(40, counts / 2); continue; }
            if (Math.Sign(moved) != Math.Sign(sent)) continue;

            samples.Add(Math.Abs(moved / (double)sent));
        }

        if (samples.Count == 0)
            throw Guard($"회전 배율을 재지 못했습니다({string.Join(" · ", tries)}) - 0도 가까이만 나오면 벽에 막혀 몸이 안 도는 것이니 트인 곳에서 다시 해 보세요. " +
                        "아는 값이 있으면 minimap.json 의 degreesPerCount 에 적으면 됩니다.");

        samples.Sort();

        var scale = samples[samples.Count / 2];

        spec.DegreesPerCount = scale;
        SaveMinimapSpec(spec);
        Print(samples.Count >= 3
            ? $"회전 배율 {scale:0.0000}도/카운트 - 표본 {samples.Count}개의 가운뎃값으로 {MinimapSpec.FileName} 에 적었습니다."
            : $"회전 배율 {scale:0.0000}도/카운트 - 표본이 {samples.Count}개뿐이라 정확하지 않을 수 있습니다({string.Join(" · ", tries)}). " +
              $"트인 곳에서 {MinimapSpec.FileName} 의 degreesPerCount 를 0 으로 지우고 다시 재면 좋습니다.");

        return scale;
    }

    // ── 설정·본보기 ─────────────────────────────────────────────────────

    private MinimapSpec? _minimapSpec;
    private DateTime _minimapSpecStamp;
    private MinimapArrow? _minimapTemplate;
    private DateTime _minimapTemplateStamp;

    private string MinimapRootOrThrow()
        => _host.ResourceRoot
           ?? throw Guard("미니맵은 프로젝트로 돌릴 때만 씁니다 - 자리·본보기를 프로젝트 폴더에서 읽습니다.");

    private MinimapSpec MinimapSpecOrThrow()
    {
        var path = System.IO.Path.Combine(MinimapRootOrThrow(), MinimapSpec.FileName);

        if (!System.IO.File.Exists(path))
            throw Guard($"프로젝트 폴더에 {MinimapSpec.FileName} 이 없습니다 - 캡처를 켜고 도구 줄의 「미니맵 익히기」를 한 번 누르세요. " +
                        "그 전에 영역 패널에서 미니맵을 둘러 「미니맵」 이라는 이름의 자리를 만들어야 합니다.");

        var stamp = System.IO.File.GetLastWriteTimeUtc(path);

        if (_minimapSpec is null || stamp != _minimapSpecStamp)
        {
            try
            {
                _minimapSpec = MinimapSpec.Load(path);
                _minimapSpecStamp = stamp;
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or System.IO.IOException)
            {
                Logger.Warn(ex, $"미니맵 설정을 못 읽었다: {path}");
                throw Guard($"{MinimapSpec.FileName} 을 읽지 못했습니다 - 형식을 보세요.");
            }
        }

        return _minimapSpec!;
    }

    private void SaveMinimapSpec(MinimapSpec spec)
    {
        var path = System.IO.Path.Combine(MinimapRootOrThrow(), MinimapSpec.FileName);

        try
        {
            spec.Save(path);
            _minimapSpecStamp = System.IO.File.GetLastWriteTimeUtc(path);
        }
        catch (System.IO.IOException ex)
        {
            Logger.Warn(ex, $"미니맵 설정을 못 적었다: {path}");
        }
    }

    private MinimapArrow MinimapTemplateOrThrow(MinimapSpec spec)
    {
        var path = System.IO.Path.Combine(MinimapRootOrThrow(), "Resources", MinimapSpec.TemplateName);

        if (!System.IO.File.Exists(path))
            throw Guard($"본보기 화살표({MinimapSpec.TemplateName})가 없습니다 - 캡처를 켜고 도구 줄의 「미니맵 익히기」를 한 번 누르세요.");

        var stamp = System.IO.File.GetLastWriteTimeUtc(path);

        if (_minimapTemplate is null || stamp != _minimapTemplateStamp)
        {
            var frame = System.Windows.Media.Imaging.BitmapFrame.Create(
                new Uri(path), System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            var (pixels, width, height) = ToBgra(frame);

            _minimapTemplate = MinimapReader.FindArrow(pixels, width, height, spec)
                               ?? throw Guard($"본보기 화살표({MinimapSpec.TemplateName})에서 화살표를 못 찾았습니다 - 「미니맵 익히기」를 다시 누르세요.");
            _minimapTemplateStamp = stamp;
        }

        return _minimapTemplate!;
    }

    /// <summary>미니맵 자리의 지금 조각(BGRA32).</summary>
    private (byte[] Pixels, int Width, int Height) MinimapPixels(MinimapSpec spec)
    {
        var book = _host.Regions?.Invoke() ?? throw Guard("영역 목록이 없습니다 - 화면에서 데이터셋 폴더를 골라야 합니다.");
        var found = book.Resolve(spec.Region) ?? throw Guard(MissingRegion(book, spec.Region));

        _host.Hub.TryGetFrameSize(out var frameWidth, out var frameHeight);

        var area = RegionTargets.Bounds(RegionTargets.Of(found.Region, found.Cell)[0], frameWidth, frameHeight);

        return ToBgra(CropFor(area));
    }

    private static (byte[] Pixels, int Width, int Height) ToBgra(System.Windows.Media.Imaging.BitmapSource source)
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
}
