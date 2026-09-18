using System;
using System.Collections.Generic;

namespace Minguk.Tools.Vision.Ocr.Paddle;

/// <summary>
/// 줄 그림에서 구분선(외따로 선 머리카락 같은 세로선)을 찾아, 그 양옆을 따로 읽을 가로 범위로 나눈다.
/// </summary>
/// <remarks>
/// <b>왜</b> - 오버워치 탄약 「17 | 24」 의 구분선은 폭 1px 청록 실선이라 인식 모델이 빈칸이나 <c>1</c> 로 읽는다
/// (실측 2026-09-16: 탄약 39장 중 21장이 <c>1724</c>·<c>30140</c>). 가짜 <c>1</c> 은 확률이 0.99 까지 나와 믿음으로 못 거르고,
/// 검출 모델은 줄 전체를 틈 없는 한 덩어리로 본다. 스크립트는 숫자 덩어리(<c>[17, 24]</c>)가 필요하므로 선 양옆을 따로 읽는다.
///
/// <b>안 된 길</b>(모두 같은 39장으로 잼) - 줄을 가로로 늘리기(15~16), 검출 상자 세로 여백 줄이기(11~13), 틈 너비로 가르기
/// (「13|24」 에서 1↔3 틈 6px 와 구분선 양옆 틈 7·8px 가 같다), 밝기 중앙값으로 글자 픽셀 가르기(배경 대각선에 먹혀 26),
/// 사람이 고정 범위를 빼기(구분선이 무기마다 28열/42열로 움직여 28, 지우기만 하면 13).
///
/// <b>무엇을 구분선으로 보나</b> - 세 가지가 다 맞아야 한다.
/// <list type="number">
/// <item><b>능선</b> - 그 열이 양옆 <see cref="Reach"/> 칸보다 <b>둘 다</b> <see cref="Contrast"/> 넘게 밝거나 둘 다 어둡다. 글자 가장자리·배경 대각선은
/// 한쪽만 달라 안 걸리고, 굵은 획은 양옆이 같은 획이라 안 걸린다. 그런 줄이 세로로 줄 높이의 <see cref="TallRatio"/> 넘게 이어지고, 폭이 <see cref="MaxWidth"/>px 이하.</item>
/// <item><b>양옆이 평평</b> - 선이 걸친 줄 높이 안에서, 선에서 <see cref="Near"/>~<see cref="Far"/> 칸 떨어진 곳의 가로 밝기 변화가
/// <see cref="Contrast"/> 넘는 줄이 <see cref="BusyRatio"/> 미만. 한글 <c>ㅣ</c> 옆에는 다른 획이 붙어 있어 여기서 떨어진다
/// (이 조건 없이 자르면 「상제님」 이 「상제 님」 이 되어 이름 37/40 → 29/40). 숫자 <c>1</c> 의 한가운데 열도 여기서 떨어진다.</item>
/// <item><b>양옆에 읽을 것</b> - 줄 끝에서 <see cref="Far"/>px 와 줄 높이의 <see cref="EdgeRatio"/> 중 큰 것 이상 떨어져 있고, 양쪽에 <b>획이 실제로 있다</b>
/// (선이 걸친 높이 안에 가로 밝기 변화가 <see cref="Contrast"/> 넘는 곳). 거리만 보던 때는 줄 맨 앞 <c>1</c> 을 잘랐다 - 줄 상자가 글자에 딱 맞으면
/// <c>1</c> 왼쪽의 빈 여백이 거리를 채워 「17 | 24」 가 <c>'724</c> 로 나왔다(조각 둘레 여백을 덧댄 뒤 드러남, 2026-09-17). Arial <c>1</c> 은 깃발에 획이 있어
/// 그것만으로는 못 막아 <see cref="EdgeRatio"/> 를 0.2 → 0.45(글자 하나 폭쯤)로 올렸다 - 사격장 벤치 93/101 그대로.</item>
/// </list>
///
/// <b>결과</b> - 오버워치 탄약 16 → 30/39, 이름 37/40 · 궁극기 40/40 그대로. 문턱을 20~45 로 바꿔도 28~30.
/// 글꼴 6종 × 11~20px × 밝은/어두운 바탕으로 그린 480줄(<c>Lv 1</c>·<c>I 24</c>·<c>x 1 y</c>·<c>l i l</c>·<c>레벨 1</c> …)에서 진짜 글자를 자른 줄 0,
/// <c>|</c> 가 든 216줄에서 구분선을 찾은 줄 132(못 찾은 것은 주로 11px - 선이 짧다). 구분선이 없으면 줄 전체 하나를 돌려준다 - 가르기 전과 같다.
/// </remarks>
public static class SeparatorSplit
{
    public const double Contrast = 30;
    public const int Reach = 2;
    public const double TallRatio = 0.35;
    public const int MaxWidth = 2;
    public const int Near = 2;
    public const int Far = 6;
    public const double BusyRatio = 0.2;
    public const double EdgeRatio = 0.45;

    /// <summary>따로 읽을 가로 범위들 [Start, End). 구분선 열은 빠진다. 구분선이 없으면 (0, 너비) 하나.</summary>
    public static IReadOnlyList<(int Start, int End)> Split(BgraImage line)
    {
        var width = line.Width;
        var height = line.Height;
        (int Start, int End)[] whole = [(0, width)];

        if (width < (2 * Far) + 3) return whole;

        var luma = new double[width * height];

        for (var i = 0; i < luma.Length; i++)
        {
            var o = i * 4;
            luma[i] = (line.Pixels[o + 2] * 0.299) + (line.Pixels[o + 1] * 0.587) + (line.Pixels[o] * 0.114);
        }

        double L(int x, int y) => luma[(y * width) + x];

        // 열마다 가장 긴 능선 구간(위, 아래).
        var spans = new (int Top, int Bottom)[width];

        for (var x = Reach; x < width - Reach; x++)
        {
            var best = (Top: 0, Bottom: 0);
            var start = -1;

            for (var y = 0; y <= height; y++)
            {
                var isRidge = false;

                if (y < height)
                {
                    var mid = L(x, y);
                    var left = L(x - Reach, y);
                    var right = L(x + Reach, y);

                    isRidge = Math.Min(mid - left, mid - right) > Contrast || Math.Min(left - mid, right - mid) > Contrast;
                }

                if (isRidge && start < 0) start = y;

                if (!isRidge && start >= 0)
                {
                    if (y - start > best.Bottom - best.Top) best = (start, y);
                    start = -1;
                }
            }

            spans[x] = best;
        }

        var need = TallRatio * height;
        var margin = Math.Max(Far, EdgeRatio * height);
        bool IsTall(int x) => spans[x].Bottom - spans[x].Top >= need;

        // [from, to) 열·[top, bottom) 줄 안에 획(가로 밝기 변화)이 있나.
        bool HasInk(int from, int to, int top, int bottom)
        {
            for (var y = top; y < bottom; y++)
                for (var gx = Math.Max(1, from); gx < Math.Min(width, to); gx++)
                    if (Math.Abs(L(gx, y) - L(gx - 1, y)) > Contrast) return true;

            return false;
        }

        var cuts = new List<(int Start, int End)>();

        for (var x = 0; x < width; x++)
        {
            if (!IsTall(x)) continue;

            var s = x;
            while (x + 1 < width && IsTall(x + 1)) x++;
            var e = x + 1;

            if (e - s > MaxWidth || s < margin || width - e < margin) continue;

            var (top, bottom) = spans[s];

            for (var k = s + 1; k < e; k++)
                if (spans[k].Bottom - spans[k].Top > bottom - top) (top, bottom) = spans[k];

            var busy = 0;

            for (var y = top; y < bottom; y++)
            {
                var changed = false;

                for (var gx = s - Far; gx <= s - Near && !changed; gx++)
                    changed = gx >= 1 && Math.Abs(L(gx, y) - L(gx - 1, y)) > Contrast;

                for (var gx = e + Near - 1; gx <= e + Far - 1 && !changed; gx++)
                    changed = Math.Abs(L(gx, y) - L(gx - 1, y)) > Contrast;

                if (changed) busy++;
            }

            if (busy < BusyRatio * (bottom - top) && HasInk(1, s - Near, top, bottom) && HasInk(e + Near, width, top, bottom)) cuts.Add((s, e));
        }

        if (cuts.Count == 0) return whole;

        var parts = new List<(int Start, int End)>(cuts.Count + 1);
        var from = 0;

        foreach (var (s, e) in cuts)
        {
            if (s - from >= 2) parts.Add((from, s));
            from = e;
        }

        if (width - from >= 2) parts.Add((from, width));

        return parts.Count == 0 ? whole : parts;
    }
}
