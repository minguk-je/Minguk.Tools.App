using System.IO;

using Minguk.Base.Utilities;

namespace Minguk.Tools.Vision;

/// <summary>
/// 내가 만든 것이 쌓이는 폴더 - 사진·라벨·모델·영역·프레임 저장.
/// </summary>
/// <remarks>
/// <b>왜 학습 환경과 나누나</b>(사용자 결정 2026-09-14) - 학습 폴더(<see cref="Training.TrainingPaths"/>)에 든 것은 <b>도구</b>다.
/// 파이썬 환경 5.5GB, D-FINE 저장소, 사전학습 가중치 - 다른 PC 에서는 복사가 아니라 다시 만들어야 하고, 지워도 다시 받으면 그만이다.
/// 여기 든 것은 <b>내 것</b>이다. 사진 수백 장과 손으로 찍은 라벨은 다시 만들 수 없다.
///
/// 둘을 한 폴더에 두면 "학습 폴더를 지우고 다시 만들라" 는 말이 곧 데이터를 지우라는 말이 되고, 백업 대상도 흐려진다.
/// 그래서 설정도 둘이다 - 설정 화면의 "학습 경로"(도구)와 "프로젝트 경로"(내 것).
/// </remarks>
public static class ProjectPaths
{
    public const string RootSettingKey = "Vision.ProjectsRoot";

    /// <summary>설치 루트 아래 "프로젝트". 오래 모을 것이면 설정에서 다른 드라이브로 옮기는 편이 낫다 - 앱을 제거하면 설치 루트는 통째로 지워진다.</summary>
    public static string DefaultRoot => Helper.InstallPaths.Under("프로젝트");

    /// <summary>프로젝트 폴더. 설정에 없으면 기본값.</summary>
    public static string Root
    {
        get
        {
            var saved = AppSettingUtility.Get(RootSettingKey, DefaultRoot);

            return string.IsNullOrWhiteSpace(saved) ? DefaultRoot : saved;
        }
        set => AppSettingUtility.Set(RootSettingKey, value);
    }

    /// <summary>데이터셋들이 놓이는 자리(게임마다 하나). 지금 어느 것을 쓰는지는 <see cref="Labeling.LabelDataset.ConfiguredRoot"/> 가 정한다.</summary>
    public static string Datasets => Path.Combine(Root, "Datasets");

    /// <summary>
    /// 프레임 저장 버튼이 떨어뜨리는 그림. 옛 자리에 이미 쌓인 것이 있으면 그것을 쓴다 - 말없이 빈 폴더를 보여 주지 않는다.
    /// </summary>
    public static string Captures
    {
        get
        {
            foreach (var legacy in new[]
                     {
                         Path.Combine(Training.TrainingPaths.Root, "captures"),
                         Path.Combine(Helper.UserDataPaths.Root, "captures")
                     })
            {
                if (Directory.Exists(legacy)) return legacy;
            }

            return Path.Combine(Root, "captures");
        }
    }
}
