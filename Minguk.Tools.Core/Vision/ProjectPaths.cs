using System.IO;
using System.Linq;

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

    // ── 프로젝트 폴더 안 폴더 이름 - 대문자로 시작한다(사용자, 2026-09-15). VS 의 Resources·Properties 와 같이 ──────────────
    // bin 은 VS 관례대로 소문자로 둔다. Windows 는 대소문자를 안 가려 옛 소문자 폴더도 그대로 열린다 - 보이는 이름은 NormalizeFolderCase 가 맞춘다.

    public const string ImagesFolder = "Images";
    public const string LabelsFolder = "Labels";
    public const string CapturesFolder = "Captures";
    public const string RecordingsFolder = "Recordings";
    public const string ResourcesFolder = "Resources";

    /// <summary>
    /// 프로젝트 폴더 안의 옛 소문자 폴더(images·labels·captures·recordings·resources)를 대문자로 시작하게 이름을 바꾼다. 바꾼 개수.
    /// </summary>
    /// <remarks>
    /// Windows 는 대소문자만 다른 이름으로 곧장 옮기지 못할 때가 있어 임시 이름을 거쳐 두 번 옮긴다. 폴더를 쥔 화면이 있으면 실패할 수 있다 -
    /// 그때는 그대로 둔다(안의 파일은 대소문자와 무관하게 열린다). 솔루션 탭이 아래 화면을 닫은 뒤에 부른다.
    /// </remarks>
    public static int NormalizeFolderCase(string projectDirectory)
    {
        if (string.IsNullOrWhiteSpace(projectDirectory) || !Directory.Exists(projectDirectory)) return 0;

        var renamed = 0;

        foreach (var wanted in new[] { ImagesFolder, LabelsFolder, CapturesFolder, RecordingsFolder, ResourcesFolder })
        {
            try
            {
                var existing = new DirectoryInfo(projectDirectory).EnumerateDirectories()
                    .FirstOrDefault(d => string.Equals(d.Name, wanted, System.StringComparison.OrdinalIgnoreCase));

                if (existing is null || existing.Name == wanted) continue;

                var temporary = Path.Combine(projectDirectory, wanted + ".~이름바꾸기");
                Directory.Move(existing.FullName, temporary);
                Directory.Move(temporary, Path.Combine(projectDirectory, wanted));
                renamed++;
            }
            catch (System.Exception)
            {
                // 쥐고 있는 프로그램이 있다 - 다음에 다시 해 본다. 대소문자만 다른 것이라 동작에는 지장이 없다.
            }
        }

        return renamed;
    }

    /// <summary>
    /// 프레임 저장 버튼이 떨어뜨리는 그림. 솔루션 탭에서 프로젝트를 골랐으면 <b>그 프로젝트의 Captures</b> 다.
    /// </summary>
    /// <remarks>
    /// 프로젝트가 없을 때만 옛 자리(<see cref="LegacyCaptures"/>)를 본다. 솔루션으로 옮긴 뒤에도 옛 자리를 보면 새로 저장한 그림이
    /// 사격장이 아니라 작업공간 바로 아래로 갔다(2026-09-14).
    /// </remarks>
    public static string Captures
        => Projects.SolutionWorkspace.StartupDirectory is { } project
            ? Path.Combine(project, CapturesFolder)
            : LegacyCaptures;

    /// <summary>
    /// 화면캡처 녹화(mp4)가 쌓이는 자리 - 고른 프로젝트의 <c>Recordings</c>. 프로젝트가 없으면 작업공간 바로 아래 <c>Recordings</c>.
    /// </summary>
    /// <remarks>1분에 50~100MB 라 사진보다 빨리 찬다. 사진·라벨처럼 프로젝트 것이라 같이 옮기고 백업한다.</remarks>
    public static string Recordings
        => Projects.SolutionWorkspace.StartupDirectory is { } project
            ? Path.Combine(project, RecordingsFolder)
            : Path.Combine(Root, RecordingsFolder);

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

            return Path.Combine(Root, CapturesFolder);
        }
    }
}
