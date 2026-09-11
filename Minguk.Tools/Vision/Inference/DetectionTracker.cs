using System;
using System.Collections.Generic;
using System.Linq;

using Minguk.Tools.Vision.Labeling;

namespace Minguk.Tools.Vision.Inference;

/// <summary>
/// 프레임마다 새로 찾은 것을 직전 것과 이어 붙여, 잠깐 나온 헛것은 버리고 잠깐 사라진 몹은 이어 준다.
/// </summary>
/// <remarks>
/// <b>왜 필요한가</b> - 검출기는 매 프레임을 따로 본다. 그래서 한 프레임에만 30%짜리 헛것이
/// 튀고, 몹이 반 프레임 가려지면 사각형이 깜빡인다. 실측(33장 60바퀴)으로 진짜 봇은 80%대가
/// 매 프레임 같은 자리에 나오고, 헛것은 30~50%대가 한두 프레임 나왔다 사라졌다. 이 차이를
/// 쓰는 것이 추적이다. "몹이 있다" 는 판단을 프레임 하나가 아니라 짧은 시간으로 내린다.
///
/// <b>어떻게</b> - 지난 사각형들과 이번 것을 겹침(IoU)으로 짝짓는다. 짝지어진 것은 자리를
/// 부드럽게 옮기고 본 횟수를 올린다. 못 짝지은 지난 것은 놓친 횟수를 올리고, 몇 번 연속
/// 놓치면 버린다. 새로 나온 것은 후보로 두었다가 연속으로 몇 번 보이면 그때 내놓는다.
///
/// <b>값</b> - 0.25초에 한 번 찾으니 확인에 2번(0.5초), 놓쳐도 2번(0.5초)은 이어 준다.
/// 게임 몹이 그 사이 화면에서 사라지는 일은 드물다. 더 길게 잡으면 사라진 몹의 사각형이
/// 남아 그 자리를 누르게 된다.
///
/// 화면과 떼어 순수하게 둔다. 프레임 순서만 넣으면 되므로 `--vision` 이 숫자로 본다.
/// </remarks>
public sealed class DetectionTracker
{
    /// <summary>이만큼 연속으로 보여야 내놓는다.</summary>
    public int MinHits { get; init; } = 2;

    /// <summary>이만큼 연속으로 못 봐도 이어 준다. 넘으면 버린다.</summary>
    public int MaxMisses { get; init; } = 2;

    /// <summary>지난 것과 이만큼 겹치면 같은 몹이다. 검출 짝짓기(0.5)보다 느슨하다 - 몹이 움직인다.</summary>
    public double MatchIou { get; init; } = 0.3;

    /// <summary>
    /// 안 겹쳐도 가운데가 사각형 크기의 이만큼 안에 있으면 같은 몹이다.
    /// </summary>
    /// <remarks>
    /// 640 모델은 한 번에 0.65초라 시선을 돌리면 그 사이 몹이 제 폭보다 더 옮겨 가 겹침이
    /// 0 이 된다. 겹침만 보면 새 몹으로 갈라져, 옛 자리 사각형이 두 프레임 더 남아 유령이
    /// 됐다 - 실제로 봇마다 오른쪽에 86% 짜리가 하나씩 더 붙었다. 가운데 거리로도 잇는다.
    /// </remarks>
    public double MatchDistance { get; init; } = 1.5;

    /// <summary>자리를 옮길 때 새 값의 비중. 1 이면 그대로 튀고 0 이면 안 움직인다.</summary>
    public double Smoothing { get; init; } = 0.6;

    private readonly List<Track> _tracks = [];
    private int _nextId = 1;

    /// <summary>지금 이어지고 있는 것 전부. 후보도 들어 있다.</summary>
    public IReadOnlyList<Track> Tracks => _tracks;

    /// <summary>
    /// 이번 프레임의 검출을 넣고, 내놓을 만한 것(확인된 것)을 돌려준다.
    /// </summary>
    public IReadOnlyList<Detection> Update(IReadOnlyList<Detection> found)
    {
        var usedTracks = new HashSet<Track>();
        var usedFound = new HashSet<int>();

        // 잘 맞는 짝부터 맺는다. 몹 둘이 붙어 있을 때 엉뚱한 쪽으로 이어지는 것을 줄인다.
        // 겹치는 짝(점수 1~2)이 거리로만 이은 짝(0~1)보다 늘 앞선다.
        var pairs = new List<(double Fit, Track Track, int Index)>();

        foreach (var track in _tracks)
        {
            for (var i = 0; i < found.Count; i++)
            {
                if (found[i].ClassId != track.ClassId) continue;

                var iou = DetectionMatch.Iou(track.Box, found[i].Box);
                if (iou >= MatchIou)
                {
                    pairs.Add((1 + iou, track, i));
                    continue;
                }

                var size = Math.Max(track.Box.Width, track.Box.Height);
                var distance = Math.Sqrt(
                    Math.Pow(track.Box.CenterX - found[i].Box.CenterX, 2) +
                    Math.Pow(track.Box.CenterY - found[i].Box.CenterY, 2));

                if (size > 0 && distance <= size * MatchDistance)
                    pairs.Add((1 - (distance / (size * MatchDistance)), track, i));
            }
        }

        foreach (var (_, track, index) in pairs.OrderByDescending(p => p.Fit))
        {
            if (usedTracks.Contains(track) || usedFound.Contains(index)) continue;

            usedTracks.Add(track);
            usedFound.Add(index);
            track.Seen(found[index], Smoothing);
        }

        foreach (var track in _tracks)
        {
            if (!usedTracks.Contains(track)) track.Missed();
        }

        _tracks.RemoveAll(track => track.Misses > MaxMisses);

        for (var i = 0; i < found.Count; i++)
        {
            if (usedFound.Contains(i)) continue;

            _tracks.Add(new Track(_nextId++, found[i]));
        }

        return _tracks
            .Where(track => track.Hits >= MinHits)
            .Select(track => track.ToDetection())
            .ToArray();
    }

    /// <summary>다 잊는다. 대상 창을 바꾸거나 모델을 새로 읽을 때.</summary>
    public void Reset() => _tracks.Clear();

    /// <summary>이어지고 있는 몹 하나.</summary>
    public sealed class Track
    {
        internal Track(int id, Detection first)
        {
            Id = id;
            Label = first.Label;
            ClassId = first.ClassId;
            Box = first.Box;
            Score = first.Score;
            Hits = 1;
        }

        public int Id { get; }
        public string Label { get; }
        public int ClassId { get; }
        public LabelBox Box { get; private set; }

        /// <summary>최근 점수의 이동 평균. 한 프레임 튄 값에 덜 흔들린다.</summary>
        public float Score { get; private set; }

        /// <summary>연속으로 본 횟수.</summary>
        public int Hits { get; private set; }

        /// <summary>연속으로 못 본 횟수. 보면 0 으로 돌아간다.</summary>
        public int Misses { get; private set; }

        internal void Seen(Detection detection, double smoothing)
        {
            var keep = 1 - smoothing;

            Box = new LabelBox
            {
                ClassId = ClassId,
                CenterX = Math.Round((Box.CenterX * keep) + (detection.Box.CenterX * smoothing), LabelBox.Digits),
                CenterY = Math.Round((Box.CenterY * keep) + (detection.Box.CenterY * smoothing), LabelBox.Digits),
                Width = Math.Round((Box.Width * keep) + (detection.Box.Width * smoothing), LabelBox.Digits),
                Height = Math.Round((Box.Height * keep) + (detection.Box.Height * smoothing), LabelBox.Digits)
            };

            Score = (float)((Score * keep) + (detection.Score * smoothing));
            Hits++;
            Misses = 0;
        }

        internal void Missed() => Misses++;

        public Detection ToDetection() => new(Label, Box, Score);
    }
}
