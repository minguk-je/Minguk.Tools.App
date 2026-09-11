using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

using NLog;

namespace Minguk.Tools.Vision.Training;

/// <summary>NVIDIA 카드 하나. 번호는 PCI 버스 순서(nvidia-smi 와 같다).</summary>
/// <param name="Index">PCI 순서 번호. <c>CUDA_DEVICE_ORDER=PCI_BUS_ID</c> 일 때 CUDA 번호와 같다.</param>
/// <param name="HasDisplay">모니터가 붙어 있는지. 게임과 바탕화면은 이 카드에서 돈다.</param>
/// <param name="Utilization">지금 사용률(%). 못 읽으면 -1.</param>
public readonly record struct GpuInfo(int Index, string Name, string BusId, bool HasDisplay, int Utilization)
{
    public string Describe => $"GPU {Index} - {Name.Replace("NVIDIA ", string.Empty)}"
                              + (HasDisplay ? " (화면)" : string.Empty)
                              + (Utilization >= 0 ? $" {Utilization}%" : string.Empty);
}

/// <summary>
/// NVML 로 NVIDIA 카드들을 읽는다. 어느 카드에 학습을 올릴지 고르는 근거다.
/// </summary>
/// <remarks>
/// <b>왜 NVML 인가</b> - CUDA 를 올리기 전에 알아야 한다(카드 선택은 CUDA 초기화 전에만
/// 먹는다). NVML 은 드라이버와 같이 깔리는 <c>nvml.dll</c> 이라 따로 받을 것이 없고,
/// <c>CUDA_VISIBLE_DEVICES</c> 와 무관하게 카드를 전부 보여 주며, 번호가 PCI 순서라
/// nvidia-smi 와 같다. Windows(WDDM)에서 nvidia-smi 가 못 보여 주는 "어느 카드에 화면이
/// 붙었나" 도 여기서 나온다.
///
/// <b>자동 선택 규칙</b> - 두 장 이상이면 모니터가 안 붙은 카드를 고른다. 게임과 바탕화면은
/// 모니터가 붙은 카드에서 돌기 때문에, 거기 학습을 올리면 게임이 버벅이고 학습도 느려진다.
/// 다 붙었거나 다 안 붙었으면 지금 사용률이 가장 낮은 카드. 한 장이면 그것.
/// </remarks>
public static class GpuProbe
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>카드 전부. NVIDIA 드라이버가 없으면 빈 목록.</summary>
    public static IReadOnlyList<GpuInfo> List()
    {
        var result = new List<GpuInfo>();

        try
        {
            if (nvmlInit_v2() != 0) return result;

            try
            {
                if (nvmlDeviceGetCount_v2(out var count) != 0) return result;

                for (var i = 0u; i < count; i++)
                {
                    if (nvmlDeviceGetHandleByIndex_v2(i, out var handle) != 0) continue;

                    var name = new StringBuilder(96);
                    nvmlDeviceGetName(handle, name, (uint)name.Capacity);

                    var busId = string.Empty;
                    if (nvmlDeviceGetPciInfo_v3(handle, out var pci) == 0) busId = pci.BusIdLegacy ?? string.Empty;

                    var hasDisplay = nvmlDeviceGetDisplayActive(handle, out var display) == 0 && display != 0;
                    var utilization = nvmlDeviceGetUtilizationRates(handle, out var rates) == 0 ? (int)rates.Gpu : -1;

                    result.Add(new GpuInfo((int)i, name.ToString(), busId, hasDisplay, utilization));
                }
            }
            finally
            {
                _ = nvmlShutdown();
            }
        }
        catch (Exception ex)
        {
            // nvml.dll 이 없거나(드라이버 없음) 오래된 드라이버. 자동 선택은 못 하고 CUDA 기본(0번)으로 간다.
            Logger.Debug($"NVML 을 못 읽었다: {ex.Message}");
        }

        return result;
    }

    /// <summary>학습을 올릴 카드를 고른다. 고를 근거가 없으면 null(CUDA 기본).</summary>
    public static GpuInfo? PickForTraining(IReadOnlyList<GpuInfo> gpus)
    {
        if (gpus.Count == 0) return null;
        if (gpus.Count == 1) return gpus[0];

        var headless = gpus.Where(g => !g.HasDisplay).ToList();
        var candidates = headless.Count > 0 ? headless : gpus.ToList();

        return candidates.OrderBy(g => g.Utilization < 0 ? int.MaxValue : g.Utilization).ThenBy(g => g.Index).First();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct NvmlPciInfo
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)] public string BusIdLegacy;
        public uint Domain;
        public uint Bus;
        public uint Device;
        public uint PciDeviceId;
        public uint PciSubSystemId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string BusId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlUtilization
    {
        public uint Gpu;
        public uint Memory;
    }

    [DllImport("nvml.dll")] private static extern int nvmlInit_v2();
    [DllImport("nvml.dll")] private static extern int nvmlShutdown();
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetCount_v2(out uint count);
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);
    [DllImport("nvml.dll", CharSet = CharSet.Ansi)] private static extern int nvmlDeviceGetName(IntPtr device, StringBuilder name, uint length);
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetPciInfo_v3(IntPtr device, out NvmlPciInfo pci);
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetDisplayActive(IntPtr device, out uint isActive);
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out NvmlUtilization utilization);
}
