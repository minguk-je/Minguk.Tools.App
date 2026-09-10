using System;
using System.IO;

using Minguk.Tools.Helper;

using NLog;

namespace Minguk.Tools.Vision.Training;

/// <summary>
/// ML.NET 검출 학습기가 받아 쓰는 사전학습 가중치를 우리 폴더에도 보관한다.
/// </summary>
/// <remarks>
/// ML.NET 은 첫 학습 때 <c>autoformer_11m_torchsharp.bin</c>(77MB)을 인터넷에서 받아
/// <c>%TEMP%\mlnet\</c> 에 둔다. 그 자리는 바꿀 수 없다(환경 변수를 안 열어 뒀다). 그런데
/// 임시 폴더는 청소 대상이고, 다른 PC 로 옮길 때 <c>%AppData%\Minguk.Tools</c> 만 복사하면
/// 그 파일은 빠진다 - 인터넷이 안 되는 PC 면 첫 학습이 거기서 멈춘다.
///
/// 그래서 학습 뒤에 우리 폴더로 한 벌 챙겨 두고, 학습 전에 임시 폴더에 없으면 거기서
/// 채워 넣는다. 사용자에게 보이는 자리는 <c>%AppData%\Minguk.Tools\mlnet\</c> 하나다.
/// 데이터셋·모델·libtorch·설정과 같은 뿌리라 폴더 하나로 다 옮겨진다.
/// </remarks>
public static class PretrainedWeights
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public const string FileName = "autoformer_11m_torchsharp.bin";

    /// <summary>ML.NET 이 실제로 읽는 자리. 바꿀 수 없다.</summary>
    public static string TempPath => Path.Combine(Path.GetTempPath(), "mlnet", FileName);

    /// <summary>우리가 보관하는 자리. 폴더 하나로 옮길 수 있게 사용자 데이터 뿌리 아래다.</summary>
    public static string KeptPath => Path.Combine(UserDataPaths.Root, "mlnet", FileName);

    /// <summary>임시 폴더에 없으면 보관본으로 채운다. 학습 전에 부른다.</summary>
    public static void SeedTemp()
    {
        try
        {
            if (File.Exists(TempPath) || !File.Exists(KeptPath)) return;

            Directory.CreateDirectory(Path.GetDirectoryName(TempPath)!);
            File.Copy(KeptPath, TempPath);

            Logger.Info($"사전학습 가중치를 보관본에서 채웠다: {TempPath}");
        }
        catch (Exception ex)
        {
            // 못 채워도 학습은 된다 - ML.NET 이 다시 받는다. 여기서 막지 않는다.
            Logger.Warn(ex, "사전학습 가중치를 임시 폴더에 채우지 못했다");
        }
    }

    /// <summary>임시 폴더에 받힌 것을 보관본으로 챙긴다. 학습 뒤에 부른다.</summary>
    public static void Keep()
    {
        try
        {
            if (!File.Exists(TempPath)) return;

            var source = new FileInfo(TempPath);
            var kept = new FileInfo(KeptPath);

            if (kept.Exists && kept.Length == source.Length) return;

            Directory.CreateDirectory(kept.DirectoryName!);
            File.Copy(TempPath, KeptPath, overwrite: true);

            Logger.Info($"사전학습 가중치를 보관했다: {KeptPath} ({source.Length / 1_048_576:N0} MB)");
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "사전학습 가중치를 보관하지 못했다");
        }
    }
}
