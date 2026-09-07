using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Minguk.Tools.Helper;

/// <summary>
/// 시작 구간을 재는 계측기.
///
/// 표식을 남길 때는 시각만 적어 두고, 첫 화면이 그려질 때 한 번에 뱉는다.
/// 구간마다 로그를 쓰면 그 로깅 비용이 측정값에 섞여서 무엇이 느린지 흐려진다.
///
/// 기준점은 프로세스 시작 시각이다. Main 에 들어오기 전(런타임 기동, 어셈블리 로딩)에
/// 이미 상당한 시간이 지나 있는데, 그것까지 보여야 어디를 손볼지 판단할 수 있다.
/// </summary>
public static class StartupTrace
{
    private static readonly DateTime ProcessStart = Process.GetCurrentProcess().StartTime;
    private static readonly List<(string Name, double Elapsed)> Marks = new();

    private static bool _dumped;

    /// <summary>이 지점까지 걸린 시간을 기록한다.</summary>
    public static void Mark(string name)
    {
        lock (Marks)
        {
            if (!_dumped)
                Marks.Add((name, (DateTime.Now - ProcessStart).TotalMilliseconds));
        }
    }

    /// <summary>모아 둔 표식을 구간별 소요와 함께 한 번만 남긴다.</summary>
    public static void Dump()
    {
        lock (Marks)
        {
            if (_dumped || Marks.Count == 0)
                return;

            _dumped = true;

            var text = new StringBuilder();
            text.AppendLine("시작 구간 (누적 / 구간)");

            double previous = 0;
            foreach (var (name, elapsed) in Marks)
            {
                text.AppendLine($"  {elapsed,8:n0}ms  {elapsed - previous,7:n0}ms   {name}");
                previous = elapsed;
            }

            NLog.LogManager.GetCurrentClassLogger().Debug(text.ToString().TrimEnd());
        }
    }
}
