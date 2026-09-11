using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

using DevExpress.Mvvm;

using Minguk.Tools.Capture;
using Minguk.Tools.Markup;
using Minguk.Tools.Vision.Inference;
using Minguk.Tools.Vision.Perception;

namespace Minguk.Tools.ViewModels;

/// <summary>
/// 잡은 화면을 <b>보는</b> 화면들의 바탕 - 몹 찾기(검출·추적)와 글자 읽기(영역 OCR·이름표)를 얹는다.
/// </summary>
/// <remarks>
/// 편집 화면과 플레이 화면이 같이 쓴다. 캡처 화면은 이 계층이 없다 - 담기만 하는 화면에
/// 68MB 모델과 libtorch 의 GPU 메모리를 물릴 이유가 없고, 도구 줄도 그만큼 비워진다.
///
/// 몸통은 <c>.Detect.cs</c>(찾기) 와 <c>.Ocr.cs</c>(글자) 에 있다. 이 파일은 바탕이 열어 둔 자리에
/// 그 둘을 끼우는 일만 한다: 프레임이 오면 찾고 읽고, 미리보기 클릭은 영역 끌기가 먼저 보고,
/// 담을 때는 방금 찾은 것을 라벨로 준다.
/// </remarks>
public abstract partial class RecognizingCaptureViewModelBase : CaptureViewModelBase
{
    /// <summary>학습한 모델로 프레임에서 몹을 찾을지.</summary>
    public bool IsMobDetectionOn
    {
        get => GetProperty(() => IsMobDetectionOn);
        set => SetProperty(() => IsMobDetectionOn, value, OnMobDetectionChanged);
    }

    /// <summary>
    /// 프레임 간 추적을 쓸지. 켜면 두 번 연속 보인 것만 내놓고 잠깐 놓친 것은 이어 준다.
    /// </summary>
    /// <remarks>
    /// 끄고 켜서 견줄 수 있게 토글로 둔다. 기본은 켬 - 한 프레임짜리 헛것이 사라지는 값이
    /// 0.5초 늦게 나타나는 값보다 크다.
    /// </remarks>
    public bool IsTrackingOn
    {
        get => GetProperty(() => IsTrackingOn);
        set => SetProperty(() => IsTrackingOn, value, () => _tracker.Reset());
    }

    /// <summary>이보다 자신 없는 것은 안 보여 준다.</summary>
    public double DetectMinimumScore
    {
        get => GetProperty(() => DetectMinimumScore);
        set => SetProperty(() => DetectMinimumScore, value);
    }

    /// <summary>몇 마리를 몇 ms 에 찾았는지. 실제 속도가 여기 그대로 뜬다.</summary>
    public string? DetectionStatus
    {
        get => GetProperty(() => DetectionStatus);
        set => SetProperty(() => DetectionStatus, value);
    }

    /// <summary>찾은 것들. 미리보기 위에 겹쳐 그린다.</summary>
    public ObservableCollection<PredictedBox> Detections { get; } = [];

    /// <summary>가장 자신 있는 몹을 누른다.</summary>
    public DelegateCommand ClickDetectionCommand { get; private set; }

    /// <summary>인식 허브. 찾은 것과 프레임을 여기 올려 두면 스크립트가 읽어 간다.</summary>
    protected IPerceptionHub Hub { get; } = PerceptionHubFactory.Default;

    /// <summary>잡고 있는지·찾고 있는지·무엇을 잡는지를 허브에 알린다. 시작·중지·몹 찾기 토글 때.</summary>
    protected void PublishPerceptionState() => Hub.PublishState(IsRunning, IsMobDetectionOn, SelectedTarget);

    protected override void OnRunningStateChanged() => PublishPerceptionState();

    protected RecognizingCaptureViewModelBase()
    {
        ClickDetectionCommand = new DelegateCommand(DoClickDetection, () => Detections.Count > 0, false);
    }

    /// <summary>프레임마다 찾고 읽는다. 둘 다 시간이 됐을 때만 백그라운드로 하나 띄우고 바로 돌아온다.</summary>
    protected override void OnFramePixels(CapturedFrameEventArgs e)
    {
        // 0.25초에 한 번만, 앞의 것이 끝났을 때만.
        MaybeDetect(e);

        // 글자 읽기도 같은 규칙 - 0.5초에 한 번, 앞의 것이 끝났을 때만.
        MaybeOcr(e);
    }

    /// <summary>글자 영역을 끄는 중이면 클릭이 아니라 영역의 시작점이다. 게임으로 보내지 않는다.</summary>
    protected override bool TryInterceptPreviewMouseDown(Point pointInControl) => TryBeginOcrRegionPick(pointInControl);

    /// <summary>담을 때 방금 찾은 것을 라벨로 같이 준다. 라벨링 화면은 그리는 곳이 아니라 틀린 것만 고치는 곳이 된다.</summary>
    protected override IReadOnlyList<Detection> DetectionsForLabels => FreshDetections;

    protected override void RestoreSettings()
    {
        base.RestoreSettings();

        // 켜진 채로 복구하지 않는다 - 화면을 열자마자 모델 68MB 를 읽으면 뜨는 것이 느려진다.
        // 화면을 나누기 전 값(캡처 모니터 이름으로 저장된 것)을 처음 한 번 물려받는다.
        DetectMinimumScore = GetSettingOrLegacy(nameof(DetectMinimumScore), 0.5);
        IsTrackingOn = GetSettingOrLegacy(nameof(IsTrackingOn), true);
        RestoreOcrRegion();
        IsNameplateOcrOn = GetSettingOrLegacy(nameof(IsNameplateOcrOn), false);

        var ocrLanguage = GetSettingOrLegacy(nameof(SelectedOcrLanguage), Vision.Ocr.OcrEngineFactory.PreferredLanguage);
        SelectedOcrLanguage = OcrLanguages.Contains(ocrLanguage) ? ocrLanguage : OcrLanguages[0];
    }

    protected override void SaveSettings()
    {
        base.SaveSettings();

        // 문턱은 읽기만 하고 저장을 안 해 슬라이더를 움직여도 다음 실행에 안 남았다. 같이 저장한다.
        SetSetting(nameof(DetectMinimumScore), DetectMinimumScore);
        SetSetting(nameof(IsTrackingOn), IsTrackingOn);
        SaveOcrRegion();
        SetSetting(nameof(IsNameplateOcrOn), IsNameplateOcrOn);
        SetSetting(nameof(SelectedOcrLanguage), SelectedOcrLanguage ?? string.Empty);
    }

    protected override void ReleaseResources()
    {
        // 눈을 감는다. 스크립트가 옛 결과를 읽지 않게.
        Hub.PublishState(false, false, null);

        ReleaseOcr();

        // 모델은 68MB 를 물고 있고 libtorch 는 GPU 메모리를 잡는다. 화면을 닫으면 놓는다.
        ReleaseDetector();

        base.ReleaseResources();
    }
}
