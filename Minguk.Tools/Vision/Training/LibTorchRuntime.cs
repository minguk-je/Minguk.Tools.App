using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Minguk.Tools.Vision.Training;

/// <summary>어느 것을 받을지.</summary>
public enum LibTorchFlavor
{
    /// <summary>GPU. NVIDIA 카드가 있어야 한다.</summary>
    Cuda,

    /// <summary>CPU. 받는 양은 훨씬 적지만 <b>학습에는 못 쓴다</b>.</summary>
    Cpu
}

/// <summary>
/// libtorch 를 준비한다. 없으면 받아서 푼다.
/// </summary>
/// <remarks>
/// <b>왜 NuGet 으로 참조하지 않는가</b>
///
/// <c>TorchSharp-cuda-windows</c> 를 참조하면 <b>빌드 출력이 3.6GB</b> 가 된다
/// (<c>torch_cuda.dll</c> 863MB · <c>cudnn_cnn_infer64_8.dll</c> 578MB ·
/// <c>cublasLt64_12.dll</c> 514MB …). CPU 판만 해도 274MB 다. 라벨링까지만 쓰는 사람에게
/// 그것을 지우게 할 이유가 없다. 그래서 <b>학습을 누를 때</b> 받는다 - 파이썬을 처음 고를 때
/// 받는 것(<see cref="Input.Scripting.PythonRuntimeInstaller"/>)과 같은 방식이다.
///
/// csproj 에는 관리 어셈블리인 <c>TorchSharp</c> 만 넣는다. 거기 딸려오는
/// <c>LibTorchSharp.dll</c>(1.9MB)이 우리 쪽 껍데기라 반드시 같이 나가야 한다.
///
/// <b>왜 pytorch.org 인가</b>
///
/// NuGet 의 libtorch 는 패키지 크기 제한 때문에 part1~part9 로 쪼개져 있어 런타임에 다시
/// 붙이는 것이 번거롭다. pytorch.org 는 같은 것을 zip 하나로 준다.
///
/// <b>무결성</b>
///
/// pytorch.org 도 해시를 따로 싣지 않는다. 기대는 두 가지에 둔다 - <b>고정된 https URL</b>
/// (TLS 가 출처를 보증한다)과 <b>내려받은 크기</b>. 서명 검증만큼 강하지 않다.
/// </remarks>
public static class LibTorchRuntime
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>
    /// 쓰는 libtorch 판.
    /// </summary>
    /// <remarks>
    /// <b>TorchSharp 0.102.7 이 요구하는 판과 같아야 한다.</b> 어긋나면 진입점을 못 찾아
    /// 알아보기 힘든 오류로 터진다. TorchSharp 를 올릴 때 여기도 같이 본다.
    /// </remarks>
    public const string Version = "2.2.1";

    private static readonly object Gate = new();
    private static bool _loaded;

    /// <summary>받은 것을 두는 자리.</summary>
    /// <remarks>
    /// 실행 폴더에 두면 안 된다. Velopack 이 업데이트 때 설치 폴더를 통째로 갈아 끼우므로
    /// 2GB 를 판마다 다시 받게 된다. <see cref="Helper.UserDataPaths"/> 와 같은 이유다.
    /// </remarks>
    public static string RootDirectory => Path.Combine(Helper.UserDataPaths.Root, "libtorch", Version);

    public static string LibraryDirectory(LibTorchFlavor flavor)
        => Path.Combine(RootDirectory, flavor == LibTorchFlavor.Cuda ? "cuda" : "cpu", "libtorch", "lib");

    /// <summary>이 판이 있는지 알아보는 데 쓰는 파일. 이것이 있으면 다 풀린 것으로 본다.</summary>
    private const string Marker = "torch_cpu.dll";

    public static bool IsInstalled(LibTorchFlavor flavor)
        => File.Exists(Path.Combine(LibraryDirectory(flavor), Marker));

    /// <summary>이미 받아 둔 것 중 나은 쪽. 없으면 null.</summary>
    public static LibTorchFlavor? Installed
        => IsInstalled(LibTorchFlavor.Cuda) ? LibTorchFlavor.Cuda
            : IsInstalled(LibTorchFlavor.Cpu) ? LibTorchFlavor.Cpu
            : null;

    private static string DownloadUrl(LibTorchFlavor flavor) => flavor == LibTorchFlavor.Cuda
        ? $"https://download.pytorch.org/libtorch/cu121/libtorch-win-shared-with-deps-{Version}%2Bcu121.zip"
        : $"https://download.pytorch.org/libtorch/cpu/libtorch-win-shared-with-deps-{Version}%2Bcpu.zip";

    /// <summary>
    /// 받을 것의 크기. 판을 올리면 같이 고쳐야 한다.
    /// </summary>
    /// <remarks>실측한 값이다(2026-09-10).</remarks>
    private static long ExpectedZipBytes(LibTorchFlavor flavor)
        => flavor == LibTorchFlavor.Cuda ? 2_405_745_550L : 186_213_305L;

    public static string DescribeSize(LibTorchFlavor flavor)
        => $"{ExpectedZipBytes(flavor) / 1_048_576:N0} MB";

    /// <summary>
    /// 없으면 받아서 푼다. 이미 있으면 아무것도 하지 않는다.
    /// </summary>
    /// <remarks>
    /// 임시 폴더에 다 푼 뒤 마지막에 옮긴다. 2GB 를 받는 중에 끊기면 반쯤 풀린 폴더가 남는데,
    /// 그것을 "설치됨" 으로 보면 다음에 알아보기 힘든 곳에서 터진다.
    /// </remarks>
    public static async Task EnsureInstalledAsync(LibTorchFlavor flavor,
                                                  IProgress<string>? progress = null,
                                                  CancellationToken token = default)
    {
        if (IsInstalled(flavor)) return;

        var target = Path.GetDirectoryName(LibraryDirectory(flavor))!;   // ...\cuda\libtorch
        var parent = Path.GetDirectoryName(target)!;                     // ...\cuda

        Directory.CreateDirectory(parent);

        var staging = parent + ".tmp";
        var zipPath = Path.Combine(parent, $"libtorch-{Version}.zip");

        try
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);

            await DownloadAsync(flavor, zipPath, progress, token);

            progress?.Report("푸는 중... (2GB 를 풀면 몇 분 걸립니다)");

            // 압축 풀기는 동기 API 뿐이라 스레드풀로 보낸다. 안 그러면 화면이 몇 분 멈춘다.
            await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, staging, overwriteFiles: true), token);

            var extracted = Path.Combine(staging, "libtorch", "lib", Marker);

            if (!File.Exists(extracted))
                throw new InvalidOperationException($"받은 것 안에 {Marker} 이 없다. 배포본이 바뀌었을 수 있다.");

            if (Directory.Exists(parent)) Directory.Delete(parent, recursive: true);
            Directory.Move(staging, parent);

            progress?.Report($"libtorch {Version} 준비됨");
            Logger.Info($"libtorch {Version} ({flavor}) 를 {parent} 에 설치했다.");
        }
        finally
        {
            // 2GB 짜리 압축본을 남겨 둘 이유가 없다.
            TryDelete(zipPath);
            TryDeleteDirectory(staging);
        }
    }

    private static async Task DownloadAsync(LibTorchFlavor flavor, string zipPath,
                                            IProgress<string>? progress, CancellationToken token)
    {
        // 2GB 라 기본 100초로는 어림도 없다.
        using var http = new HttpClient { Timeout = TimeSpan.FromHours(2) };

        using var response = await http.GetAsync(DownloadUrl(flavor), HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? 0;
        var expected = ExpectedZipBytes(flavor);

        if (total > 0 && total != expected)
        {
            throw new InvalidOperationException(
                $"받으려는 파일 크기가 다르다. 기대 {expected:N0} / 실제 {total:N0} - 배포본이 바뀌었을 수 있다.");
        }

        await using var source = await response.Content.ReadAsStreamAsync(token);
        await using var target = File.Create(zipPath);

        var buffer = new byte[1024 * 1024];
        long read = 0;
        var lastReported = -1L;
        int n;

        while ((n = await source.ReadAsync(buffer, token)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, n), token);
            read += n;

            if (total <= 0) continue;

            // 1% 마다만 알린다. 2GB 를 1MB 씩 읽으면 2000번이라 그때마다 알리면 화면이 밀린다.
            var percent = read * 100 / total;

            if (percent == lastReported) continue;

            lastReported = percent;
            progress?.Report($"libtorch 받는 중... {percent}% ({read / 1_048_576:N0} / {total / 1_048_576:N0} MB)");
        }
    }

    /// <summary>
    /// 받아 둔 것을 이 프로세스에 물린다. 한 번만 돈다.
    /// </summary>
    /// <remarks>
    /// TorchSharp 는 제 옆이나 NuGet 폴더에서 네이티브를 찾는다. 다른 자리에 둔 것을 쓰게 하려면
    /// <b>전체 경로로 먼저 올려 주는 것</b>이 전부다 - 이미 로드된 모듈은 로더가 다시 찾지 않는다.
    /// <c>AddDllDirectory</c> 는 필요 없다(실측: 그것이 실패해도 로드는 됐다).
    ///
    /// 한 번 올린 네이티브는 프로세스가 살아 있는 동안 못 내린다. 그래서 CPU 와 CUDA 를
    /// 오가려면 앱을 다시 켜야 한다.
    /// </remarks>
    public static void Load(LibTorchFlavor flavor)
    {
        lock (Gate)
        {
            if (_loaded) return;

            var directory = LibraryDirectory(flavor);

            if (!IsInstalled(flavor))
                throw new InvalidOperationException($"libtorch 를 아직 받지 않았다: {directory}");

            // 순서가 있다. c10 -> torch_cpu -> torch_cuda -> torch 로, 기대는 쪽을 먼저 올린다.
            foreach (var name in new[] { "c10.dll", "torch_cpu.dll", "c10_cuda.dll", "torch_cuda.dll", "torch.dll" })
            {
                var path = Path.Combine(directory, name);

                if (!File.Exists(path)) continue;   // CPU 판에는 cuda 것이 없다

                try
                {
                    NativeLibrary.Load(path);
                }
                catch (Exception ex)
                {
                    // 하나 못 올렸다고 멈추지 않는다. 곁다리인 것도 있고, 정말 필요한 것이면
                    // 뒤이어 torch 를 부를 때 더 또렷한 오류로 터진다.
                    Logger.Warn(ex, $"libtorch 를 못 올렸다: {name}");
                }
            }

            _loaded = true;

            Logger.Info($"libtorch {Version} ({flavor}) 를 {directory} 에서 올렸다.");
        }
    }

    /// <summary>
    /// CUDA 를 쓸 수 있는 NVIDIA 드라이버가 깔려 있는지.
    /// </summary>
    /// <remarks>
    /// libtorch 를 받기 <b>전에</b> 알아야 한다 - CUDA 판은 2.2GB 고 CPU 판은 177MB 인데,
    /// 카드가 없는 PC 에 2.2GB 를 받게 할 수는 없다. torch.cuda.is_available() 은
    /// libtorch 를 올린 뒤에야 부를 수 있어서 여기 쓸 수 없다.
    ///
    /// 그래서 드라이버가 까는 <c>nvcuda.dll</c> 이 있는지로 본다. 카드가 꽂혀 있는지가 아니라
    /// <b>CUDA 를 쓸 준비가 됐는지</b>를 보는 것이라 우리가 알고 싶은 것에 더 가깝다.
    /// </remarks>
    public static bool HasCudaDriver
    {
        get
        {
            try
            {
                if (!NativeLibrary.TryLoad("nvcuda.dll", out var handle)) return false;

                // 올린 것을 그대로 둔다. 곧 libtorch 가 어차피 쓸 것이고, 내리면 다시 올릴 때
                // 드라이버 초기화를 또 하게 된다.
                _ = handle;

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>이 PC 에 맞는 것. 카드가 없으면 CPU 판을 가리키지만 학습에는 못 쓴다.</summary>
    public static LibTorchFlavor Recommended => HasCudaDriver ? LibTorchFlavor.Cuda : LibTorchFlavor.Cpu;

    /// <summary>이 프로세스가 이미 libtorch 를 물었는지.</summary>
    public static bool IsLoaded
    {
        get { lock (Gate) return _loaded; }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, $"임시 파일을 못 지웠다: {path}");
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, $"임시 폴더를 못 지웠다: {path}");
        }
    }
}
