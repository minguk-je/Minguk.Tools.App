using Minguk.Base.Utilities;

namespace Minguk.Tools.Inference;

/// <summary>
/// DirectML 이 쓸 GPU 번호(DXGI 어댑터 순서) - OCR·검출 엔진이 새로 만들어질 때 이 값을 쓴다.
/// </summary>
/// <remarks>
/// 사용자(2026-09-26) "GPU 2장인데?" - GTX 1060 3GB 두 장. 게임이 첫째(0)를 쓰니 OCR·검출은 둘째(1)로 돌리면 같은 카드를 나눠 쓰지 않는다
/// (한 장에서 게임 + DirectML 이 겹치면 PP-OCRv5 GPU 가 영역 하나에 0.9~1초로 늘어졌다). 스크립트 화면 인식 도구 줄의 「GPU」 콤보로 고르고 앱 전체 설정에 남는다.
/// 이미 만들어진 엔진은 안 바뀐다 - OCR 은 콤보를 바꾸면 버리고 다시 만들고, 검출은 다음에 모델을 올릴 때부터.
/// </remarks>
public static class DmlDevice
{
    /// <summary>앱 전체 설정 키.</summary>
    public const string SettingKey = "Minguk.Tools.Dml.DeviceId";

    private static int? _index;

    /// <summary>쓸 GPU 번호. 처음 읽을 때 설정에서, 없으면 0.</summary>
    public static int Index
    {
        get => _index ??= int.TryParse(AppSettingUtility.Get(SettingKey, "0"), out var saved) && saved >= 0 ? saved : 0;
        set
        {
            _index = value < 0 ? 0 : value;
            AppSettingUtility.Set(SettingKey, _index.Value.ToString());
        }
    }
}
