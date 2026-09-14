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
/// 그래서 설정도 둘이다 - 환경 메뉴의 "학습 경로"(도구)와 "작업공간"(내 것).
/// </remarks>
public static class ProjectPaths
{
    public const string RootSettingKey = "Vision.ProjectsRoot";

    /// <summary>설치 루트 아래 "프로젝트". 오래 모을 것이면 설정에서 다른 드라이브로 옮기는 편이 낫다 - 앱을 제거하면 설치 루트는 통째로 지워진다.</summary>
    /// <remarks>
    /// 화면에서는 <b>작업공간</b>이라 부른다(사용자, 2026-09-14 - 프로젝트 경로 → Workspace → 작업공간). "프로젝트" 가 솔루션 안의
    /// 모드·스테이지를 뜻하게 되어 "프로젝트 경로" 가 헷갈렸다. 설정 키(<see cref="RootSettingKey"/>)와 이 클래스 이름은 저장값을 잃지 않으려고 그대로 둔다.
    /// 기본 폴더 이름도 작업공간인데, 옛 기본 폴더(설치 폴더/프로젝트)에 이미 무엇이 있으면 그것을 계속 쓴다.
    /// </remarks>
    public static string DefaultRoot
    {
        get
        {
            var legacy = Helper.InstallPaths.Under("프로젝트");
            var workspace = Helper.InstallPaths.Under("작업공간");

            return !Directory.Exists(workspace) && Directory.Exists(legacy) ? legacy : workspace;
        }
    }

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
    /// 프레임 저장 버튼이 떨어뜨리는 그림. Automation 에서 프로젝트를 골랐으면 <b>그 프로젝트의 captures</b> 다.
    /// </summary>
    /// <remarks>
    /// 프로젝트가 없을 때만 옛 자리(<see cref="LegacyCaptures"/>)를 본다. 솔루션으로 옮긴 뒤에도 옛 자리를 보면 새로 저장한 그림이
    /// 사격장이 아니라 작업공간 바로 아래로 갔다(2026-09-14).
    /// </remarks>
    public static string Captures
        => Projects.SolutionWorkspace.StartupDirectory is { } project
            ? Path.Combine(project, "captures")
            : LegacyCaptures;

    /// <summary>
    /// 솔루션을 쓰기 전의 프레임 저장 자리. 옛 자리에 이미 쌓인 것이 있으면 그것을 쓴다 - 말없이 빈 폴더를 보여 주지 않는다.
    /// 옛 자리를 옮기는 이주(<c>SolutionMigration</c>)도 이것을 본다.
    /// </summary>
    public static string LegacyCaptures
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
