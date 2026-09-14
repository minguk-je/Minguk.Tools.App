using System.IO;

using Minguk.Base.Utilities;

namespace Minguk.Tools.Vision.Training;

/// <summary>
/// 학습에 얽힌 것이 모여 있는 폴더. 설정 하나로 자리를 옮긴다.
/// </summary>
/// <remarks>
/// <b>왜 한 폴더인가</b>(사용자 결정 2026-09-14) - 예전에는 YOLO 환경이 <c>%LOCALAPPDATA%</c>, D-FINE 저장소가 다른 드라이브,
/// 가중치가 또 다른 자리였다. 어디에 무엇이 있는지 사람이 외워야 했고, 다른 PC 로 옮길 때 빠뜨리기 쉬웠다.
/// 이제 <see cref="Root"/> 아래에 다 둔다 - 자리를 옮기려면 설정 한 줄만 고친다.
///
/// <code>
/// &lt;Root&gt;\
/// ├─ yolo-venv\        YOLO 학습용 파이썬(torch cu126 + ultralytics, 약 2.5GB)
/// ├─ dfine-venv\       D-FINE 학습용 파이썬(3.12 · torch cu124, 약 3GB)
/// ├─ D-FINE\           D-FINE 저장소(학습 코드 + 우리 설정)
/// ├─ dfine_n_coco.pth  D-FINE 사전학습 가중치 15MB
/// └─ runs\             학습 결과·로그(yolo11n\weights\best.onnx 등)
/// </code>
///
/// <b>여기 든 것은 도구뿐이다</b> - 사진·라벨·모델은 프로젝트 폴더(<see cref="Minguk.Tools.Vision.ProjectPaths"/>)에 있다. 설정을 둘로 나눈 이유는
/// 그쪽에 적어 두었다.
///
/// <b>가상환경은 옮길 수 없다</b> - <c>pyvenv.cfg</c> 에 그 PC 의 파이썬 경로가 박힌다. 다른 PC 에서는 복사가 아니라
/// <c>도구\학습-환경-준비.ps1</c> 로 다시 만든다. 그래서 이 폴더에서 PC 를 옮길 때 챙길 것은 사실상 가중치 하나다.
/// </remarks>
public static class TrainingPaths
{
    public const string RootSettingKey = "Vision.TrainingRoot";

    /// <summary>설치 루트 아래 "학습". 업데이트해도 남는 자리다(<see cref="Helper.InstallPaths"/>).</summary>
    public static string DefaultRoot => Helper.InstallPaths.Under("학습");

    /// <summary>학습 폴더. 설정에 없으면 기본값.</summary>
    public static string Root
    {
        get
        {
            var saved = AppSettingUtility.Get(RootSettingKey, DefaultRoot);

            return string.IsNullOrWhiteSpace(saved) ? DefaultRoot : saved;
        }
        set => AppSettingUtility.Set(RootSettingKey, value);
    }

    /// <summary>YOLO 학습용 파이썬. 없으면 옛 자리(%LOCALAPPDATA%)도 본다 - 2.5GB 를 다시 받게 하지 않는다.</summary>
    public static string YoloPython
    {
        get
        {
            var now = Path.Combine(Root, "yolo-venv", "Scripts", "python.exe");

            if (File.Exists(now)) return now;

            var legacy = Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "Minguk.Tools", "yolo-venv", "Scripts", "python.exe");

            return File.Exists(legacy) ? legacy : now;
        }
    }

    /// <summary>D-FINE 학습용 파이썬. 옛 이름(.venv)도 본다.</summary>
    public static string DFinePython
    {
        get
        {
            var now = Path.Combine(Root, "dfine-venv", "Scripts", "python.exe");

            if (File.Exists(now)) return now;

            var legacy = Path.Combine(Root, ".venv", "Scripts", "python.exe");

            return File.Exists(legacy) ? legacy : now;
        }
    }

    public static string DFineRepository => Path.Combine(Root, "D-FINE");

    public static string DFineWeights => Path.Combine(Root, "dfine_n_coco.pth");

    /// <summary>학습 결과와 로그. 옛 자리(%AppData%\Minguk.Tools\yolo-runs)에 있던 것도 그대로 두면 된다 - 새 학습부터 여기 쌓인다.</summary>
    public static string Runs => Path.Combine(Root, "runs");

}
