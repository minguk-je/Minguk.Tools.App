"""
내보낸 .onnx 를 DirectML 이 제대로 계산하도록 고친다. 학습 뒤 내보내기와 --import-onnx 사이에 한 번 돌린다.

    python 도구/onnx-DirectML-고치기.py 들어갈것.onnx [나올것.onnx]

무엇을 고치나
-------------
DirectML 실행 공급자는 **MatMul 의 오른쪽이 1차원 벡터**일 때 그 벡터를 통째로 무시하고
행 합계를 돌려준다(실측, onnxruntime-directml 1.24.4 / DirectML 1.15.4).

    MatMul([[0,1,2],[3,4,5]], [1,10,100])  CPU [210, 543]   DML [3, 12]

D-FINE 의 디코더에는 이 모양이 세 군데 있다(distance distribution 을 거리로 되돌리는 'integral').
그래서 **터지지도, 느려지지도 않고 답만 틀린다** - 점수가 0.9 에서 0.06 으로 내려앉아
몹을 한 마리도 못 찾는데 헛것도 안 나와서 "모델이 덜 배웠나" 로 보인다. 여기서 하루를 잃기 쉽다.

고치는 법은 간단하다. 벡터를 [N,1] 짜리 행렬로 바꿔 곱하고 마지막 축을 다시 없앤다.
계산 결과는 같고(검증: CPU 와 소수점까지 일치), 무게도 노드 셋만 는다.

언제 필요 없어지나
------------------
onnxruntime 의 DirectML 쪽이 고쳐지면 그만둬도 된다. 고쳐졌는지는 이것으로 본다:

    Minguk.Tools.Tests.exe --onnx-agree --model=<원본.onnx> --image=<아무 그림.png>
"""

import sys
from pathlib import Path

import numpy as np
import onnx
from onnx import helper, numpy_helper


def fix(model: onnx.ModelProto) -> int:
    """1차원 벡터를 오른쪽에 둔 MatMul 을 2차원으로 바꾼다. 고친 개수를 돌려준다."""
    initializers = {i.name: i for i in model.graph.initializer}
    nodes, made, fixed = [], set(), 0

    for node in model.graph.node:
        vector = node.input[1] if node.op_type == "MatMul" and len(node.input) == 2 else None
        weights = initializers.get(vector) if vector else None

        if weights is None or numpy_helper.to_array(weights).ndim != 1:
            nodes.append(node)
            continue

        values = numpy_helper.to_array(weights)
        column = vector + "_2d"

        # 같은 벡터를 여러 MatMul 이 나눠 쓴다. 2차원 판은 한 번만 만든다.
        if column not in made:
            model.graph.initializer.append(numpy_helper.from_array(values.reshape(-1, 1).copy(), column))
            made.add(column)

        product = node.output[0] + "_2d"
        axes = node.output[0] + "_axes"

        model.graph.initializer.append(numpy_helper.from_array(np.array([-1], dtype=np.int64), axes))
        nodes.append(helper.make_node("MatMul", [node.input[0], column], [product], name=node.name + "_2d"))
        nodes.append(helper.make_node("Squeeze", [product, axes], [node.output[0]], name=node.name + "_squeeze"))
        fixed += 1

    del model.graph.node[:]
    model.graph.node.extend(nodes)

    return fixed


def main() -> int:
    if len(sys.argv) < 2:
        print(__doc__)
        return 1

    source = Path(sys.argv[1])
    target = Path(sys.argv[2]) if len(sys.argv) > 2 else source.with_name(source.stem + "-dml.onnx")

    model = onnx.load(str(source))
    fixed = fix(model)

    onnx.checker.check_model(model)
    onnx.save(model, str(target))

    print(f"1차원 MatMul {fixed}개를 고쳤다 → {target}")
    print("이제 들이면 된다: Minguk.Tools.Tests.exe --import-onnx --model=" + str(target) + " --size=640x640")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
