using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Minguk.Tools.Input.Scripting;

/// <summary>
/// 파이썬 런타임을 준비한다. 없으면 받아서 푼다.
/// </summary>
/// <remarks>
/// <b>왜 받아 오는가</b>
///
/// Python.NET 은 진짜 CPython 을 껴안는 것이라 디스크에 파이썬이 있어야 한다. 폰트(D2Coding)
/// 처럼 앱에 박아 넣을 수 있는 물건이 아니다 - 폰트는 리소스 한 덩어리지만 파이썬은 파일 트리다.
///
/// 그래서 python.org 가 배포하는 <b>임베더블 배포본</b>을 쓴다. 설치 관리자가 아니라 압축본이라
/// 관리자 권한도, 시스템 등록도 필요 없다. 앱 데이터 아래에 풀어 두고 그것만 본다 -
/// 사용자가 따로 깔아 둔 파이썬을 건드리지 않는다.
///
/// <b>무결성</b>
///
/// python.org 는 배포 페이지에 MD5 만 싣는다. 여기서 기대 해시를 박아 두더라도 그 값은 결국
/// 내가 받은 파일에서 뽑은 것이라 스스로를 검증하는 꼴이 된다. 그래서 기대는 두 가지에 둔다 -
/// <b>고정된 https URL</b>(TLS 가 출처를 보증한다)과 <b>내려받은 크기</b>. 정직하게 말해
/// 서명 검증만큼 강하지 않다.
/// </remarks>
public static class PythonRuntimeInstaller
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>
    /// 쓰는 파이썬 판.
    /// </summary>
    /// <remarks>
    /// 올릴 때 <see cref="ExpectedZipBytes"/> 도 같이 고쳐야 한다. 판마다 크기가 다르다.
    /// Python.NET 이 아직 안 따라간 판을 고르면 초기화에서 터지므로, 올리기 전에 그쪽이
    /// 지원하는지부터 본다.
    /// </remarks>
    public const string Version = "3.12.10";

    /// <summary>이 판의 DLL 이름. Python.NET 에게 이걸 알려 줘야 한다.</summary>
    public const string DllName = "python312.dll";

    private const long ExpectedZipBytes = 11_133_606;

    private static string DownloadUrl
        => $"https://www.python.org/ftp/python/{Version}/python-{Version}-embed-amd64.zip";

    /// <summary>런타임이 풀려 있는 자리.</summary>
    public static string RuntimeDirectory
        => Path.Combine(Helper.UserDataPaths.Root, "Python", Version);

    public static string DllPath => Path.Combine(RuntimeDirectory, DllName);

    /// <summary>이미 받아서 풀어 두었는지.</summary>
    public static bool IsInstalled => File.Exists(DllPath);

    /// <summary>
    /// 없으면 받아서 푼다. 이미 있으면 아무것도 하지 않는다.
    /// </summary>
    /// <remarks>
    /// 풀 때는 임시 폴더에 다 푼 뒤 마지막에 옮긴다. 중간에 끊기면 반쯤 풀린 폴더가 남는데,
    /// 그것을 "설치됨" 으로 보면 다음 실행이 이상하게 터진다.
    /// </remarks>
    public static async Task EnsureInstalledAsync(IProgress<string>? progress = null, CancellationToken token = default)
    {
        if (IsInstalled) return;

        var root = Path.GetDirectoryName(RuntimeDirectory)!;
        Directory.CreateDirectory(root);

        var staging = RuntimeDirectory + ".tmp";
        var zipPath = Path.Combine(root, $"python-{Version}-embed-amd64.zip");

        try
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);

            progress?.Report($"파이썬 {Version} 을 받는 중...");
            await DownloadAsync(zipPath, progress, token);

            progress?.Report("푸는 중...");
            ZipFile.ExtractToDirectory(zipPath, staging, overwriteFiles: true);

            if (!File.Exists(Path.Combine(staging, DllName)))
                throw new InvalidOperationException($"받은 것 안에 {DllName} 이 없다. 판이 바뀌었을 수 있다.");

            EnablePathFile(staging);

            if (Directory.Exists(RuntimeDirectory)) Directory.Delete(RuntimeDirectory, recursive: true);
            Directory.Move(staging, RuntimeDirectory);

            progress?.Report($"파이썬 {Version} 준비됨");
            Logger.Info($"파이썬 {Version} 을 {RuntimeDirectory} 에 설치했다.");
        }
        finally
        {
            // 압축본은 남겨 둘 이유가 없다. 다시 받는 편이 낫다.
            TryDelete(zipPath);
            TryDeleteDirectory(staging);
        }
    }

    private static async Task DownloadAsync(string zipPath, IProgress<string>? progress, CancellationToken token)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

        using var response = await http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? 0;

        if (total > 0 && total != ExpectedZipBytes)
        {
            // 크기가 다르면 그 판이 아니다. 조용히 쓰면 나중에 엉뚱한 데서 터진다.
            throw new InvalidOperationException(
                $"받으려는 파일 크기가 다르다. 기대 {ExpectedZipBytes:N0} / 실제 {total:N0} - 배포본이 바뀌었을 수 있다.");
        }

        await using var source = await response.Content.ReadAsStreamAsync(token);
        await using var target = File.Create(zipPath);

        var buffer = new byte[81920];
        long read = 0;
        int n;

        while ((n = await source.ReadAsync(buffer, token)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, n), token);
            read += n;

            if (total > 0) progress?.Report($"파이썬 {Version} 받는 중... {read * 100 / total}%");
        }
    }

    /// <summary>
    /// <c>._pth</c> 파일의 <c>import site</c> 를 살린다.
    /// </summary>
    /// <remarks>
    /// 임베더블 배포본은 sys.path 를 그 파일로 못박고 site 를 꺼 둔다. 그대로 두면
    /// Python.NET 이 자기를 끼워 넣지 못해 초기화에서 막힌다. 한 줄 주석을 푸는 것으로 끝난다.
    /// </remarks>
    private static void EnablePathFile(string directory)
    {
        var pth = Directory.EnumerateFiles(directory, "python*._pth").FirstOrDefault();
        if (pth is null) return;

        var lines = File.ReadAllLines(pth);

        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim() == "#import site") lines[i] = "import site";
        }

        File.WriteAllLines(pth, lines);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { Logger.Debug(ex, $"임시 파일을 못 지웠다: {path}"); }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (Exception ex) { Logger.Debug(ex, $"임시 폴더를 못 지웠다: {path}"); }
    }
}
