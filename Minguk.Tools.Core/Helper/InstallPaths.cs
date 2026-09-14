using System;
using System.IO;

namespace Minguk.Tools.Helper;

/// <summary>
/// 설치 자리. 학습·프로젝트 폴더의 <b>기본값</b>을 여기서 잡는다.
/// </summary>
/// <remarks>
/// <b>실행 폴더가 아니라 그 부모다.</b> Velopack 은 업데이트 때 설치 폴더(<c>current</c>)를 새 패키지로 통째로 갈아 끼운다 -
/// 그 안에 둔 것은 업데이트마다 사라진다. 한 칸 위(설치 루트)는 그대로 남는다.
///
/// <code>
/// %LocalAppData%\Minguk.Tools\      ← 설치 루트. 여기 아래를 기본값으로 쓴다
/// ├─ current\                        실행 파일(업데이트 때 통째로 바뀐다)
/// ├─ packages\                       Velopack 이 쓰는 자리
/// ├─ 학습\                           도구 - 파이썬 환경·저장소·가중치
/// └─ 프로젝트\                       내 것 - 사진·라벨·모델
/// </code>
///
/// <b>지울 때는 같이 지워진다</b> - 설치 루트는 Velopack 이 소유해서 제거하면 통째로 없어진다. 사진을 오래 모을 것이면
/// 설정 화면에서 프로젝트 폴더를 다른 드라이브로 옮겨 두는 편이 낫다. 그래서 기본값일 뿐 고정이 아니다.
///
/// 개발 중(bin\x64\Debug\...)에는 그 폴더의 부모가 된다 - 설정에 값이 있으면 그것이 이긴다.
/// </remarks>
public static class InstallPaths
{
    /// <summary>설치 루트. Velopack 이면 <c>current</c> 의 부모, 아니면 실행 폴더의 부모.</summary>
    public static string Root { get; } = Resolve();

    private static string Resolve()
    {
        var baseDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var parent = Path.GetDirectoryName(baseDirectory);

        return string.IsNullOrEmpty(parent) ? baseDirectory : parent;
    }

    /// <summary>설치 루트 아래 폴더 하나.</summary>
    public static string Under(string name) => Path.Combine(Root, name);
}
