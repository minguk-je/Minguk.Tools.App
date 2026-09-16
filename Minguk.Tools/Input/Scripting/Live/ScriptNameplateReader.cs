using System;
using System.Collections.Generic;

using Minguk.Tools.Vision.Labeling;

namespace Minguk.Tools.Input.Scripting.Live;

/// <summary>
/// <c>몹.이름표</c> 를 부를 때만 읽는다 - API 가 한 실행에 한 벌 만들어 몹들에 물린다. 같은 사각형은 한 번만 읽는다.
/// </summary>
internal sealed class ScriptNameplateReader(Func<LabelBox, string> read)
{
    private readonly Dictionary<LabelBox, string> _read = [];
    private readonly object _gate = new();

    public string Read(ScriptMob mob)
    {
        lock (_gate)
        {
            if (_read.TryGetValue(mob.Box, out var known)) return known;
        }

        var text = read(mob.Box);

        lock (_gate)
        {
            // 몹 목록은 0.25초마다 새로 온다 - 옛 사각형이 쌓이지 않게 가끔 비운다.
            if (_read.Count > 256) _read.Clear();
            _read[mob.Box] = text;
        }

        return text;
    }
}
