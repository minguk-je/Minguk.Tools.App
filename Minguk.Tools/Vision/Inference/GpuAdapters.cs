using System;
using System.Collections.Generic;

using Vortice.DXGI;

namespace Minguk.Tools.Vision.Inference;

/// <summary>「GPU」 콤보 한 줄 - DirectML 번호(DXGI 어댑터 순서)와 이름.</summary>
public sealed record GpuChoice(int Index, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// 이 PC 의 GPU 목록(DXGI 어댑터 순서 = DirectML 번호). 소프트웨어 어댑터(WARP)는 뺀다.
/// </summary>
/// <remarks>사용자(2026-09-26) "GPU 2장인데?" - 게임과 다른 카드로 OCR·검출을 돌리려고 고른다(<see cref="Minguk.Tools.Inference.DmlDevice"/>).</remarks>
public static class GpuAdapters
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    public static IReadOnlyList<GpuChoice> List()
    {
        var result = new List<GpuChoice>();

        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();

            for (uint i = 0; factory.EnumAdapters1(i, out var adapter).Success; i++)
            {
                using (adapter)
                {
                    var description = adapter.Description1;

                    if ((description.Flags & AdapterFlags.Software) != 0) continue;

                    result.Add(new GpuChoice(result.Count, $"{result.Count}: {description.Description.Trim()} ({description.DedicatedVideoMemory / (1024 * 1024 * 1024.0):0.#}GB)"));
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "GPU 목록을 읽지 못했다");
        }

        if (result.Count == 0) result.Add(new GpuChoice(0, "0: 기본 GPU"));

        return result;
    }
}
