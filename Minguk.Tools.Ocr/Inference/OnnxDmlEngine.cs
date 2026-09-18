using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using Microsoft.ML.OnnxRuntime;

namespace Minguk.Tools.Inference;

/// <summary>
/// ONNX Runtime + DirectML 실행 공급자. GPU 벤더를 가리지 않는다(NVIDIA·AMD·Intel·내장 GPU 모두).
///
/// zero-copy 가 아닌 이유:
///   DirectML EP 에 D3D12 리소스를 직접 텐서로 물리려면 OrtDmlApi 의
///   CreateGPUAllocationFromD3DResource 가 필요한데, ONNX Runtime 의 C# 바인딩은
///   AppendExecutionProvider_DML 하나만 노출하고 그 EP 전용 API 는 내주지 않는다.
///   그래서 입력은 CPU 텐서로 넘긴다 — 대신 <see cref="FramePreprocessor"/> 가
///   GPU 에서 미리 모델 크기로 줄여 놓기 때문에, 옮기는 양이 프레임 전체가 아니라 텐서 하나다.
///
///   나중에 이 업로드마저 문제가 되면 네이티브 C++ shim 을 두고 그쪽에서 OrtDmlApi 를 쓰면 된다.
///   추론 자체가 보통 수~수십 ms 라 그 전에 병목이 될 일은 거의 없다.
/// </summary>
public sealed class OnnxDmlEngine : IDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly InferenceSession _session;
    private readonly SessionOptions _options;
    private readonly RunOptions _runOptions;
    private readonly string[] _inputNames;
    private readonly string[] _outputNames;

    private bool _disposed;

    /// <param name="useGpu">
    /// 거짓이면 CPU 로 돈다. 느리지만 <b>답을 맞춰 보는 잣대</b>가 된다 - GPU 쪽이 이상한 값을 내놓을 때
    /// 모델이 잘못된 것인지 실행 공급자가 잘못된 것인지 이것으로 가른다.
    /// </param>
    public OnnxDmlEngine(string modelPath, int deviceId = 0, bool useGpu = true)
    {
        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"모델 파일이 없다: {modelPath}", modelPath);

        _options = new SessionOptions();

        if (useGpu)
        {
            // DirectML EP 의 요구 조건이다. 둘 다 끄지 않으면 세션 생성이나 실행에서 터진다.
            _options.EnableMemoryPattern = false;
            _options.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
            _options.AppendExecutionProvider_DML(deviceId);
        }

        _session = new InferenceSession(modelPath, _options);
        _runOptions = new RunOptions();

        _inputNames = _session.InputNames.ToArray();
        _outputNames = _session.OutputNames.ToArray();

        ModelPath = modelPath;

        Logger.Info($"ONNX 모델 로드: {Path.GetFileName(modelPath)} " +
                    $"(입력 {string.Join(", ", _inputNames)} / 출력 {string.Join(", ", _outputNames)})");
    }

    public string ModelPath { get; }

    public IReadOnlyList<string> InputNames => _inputNames;

    public IReadOnlyList<string> OutputNames => _outputNames;

    public double LastInferenceMs { get; private set; }

    /// <summary>
    /// 모델이 선언한 입력 모양에서 <see cref="TensorSpec"/> 을 뽑아 낸다.
    /// 동적 차원(-1)이 섞여 있으면 크기를 알 수 없으므로 null 을 준다 — 그때는 호출자가 직접 정해야 한다.
    /// 정규화 값은 모델 파일에 안 적혀 있으니 여기서 알 수 없다. 기본값(0~1)으로 두고 필요하면 바꿀 것.
    /// </summary>
    public TensorSpec? TryDeriveInputSpec()
    {
        if (_inputNames.Length == 0)
            return null;

        var metadata = _session.InputMetadata[_inputNames[0]];
        if (!metadata.IsTensor || metadata.Dimensions is not { Length: 4 } dims)
            return null;

        // [1, 3, H, W] 면 NCHW, [1, H, W, 3] 이면 NHWC.
        if (dims[1] == 3 && dims[2] > 0 && dims[3] > 0)
            return new TensorSpec { Width = dims[3], Height = dims[2], Layout = TensorLayout.Nchw };

        if (dims[3] == 3 && dims[1] > 0 && dims[2] > 0)
            return new TensorSpec { Width = dims[2], Height = dims[1], Layout = TensorLayout.Nhwc };

        return null;
    }

    /// <summary>
    /// 텐서를 넣고 추론한다. 반환된 값들은 호출자가 Dispose 해야 한다.
    /// </summary>
    /// <param name="sizes">
    /// 입력이 둘인 모델(D-FINE·RT-DETR 내보내기)의 둘째 입력. 후처리를 그래프에 넣어 내보내면 "원본 크기" 를 같이 받아
    /// 사각형을 그 크기의 픽셀로 돌려준다. 우리는 레터박스한 칸을 원본이라 일러 주고 되돌리기는 우리가 한다.
    /// 순서는 <b>(너비, 높이)</b> - 후처리가 <c>repeat(1,2)</c> 로 [w,h,w,h] 를 만들어 [x1,y1,x2,y2] 에 곱한다.
    /// </param>
    public IDisposableReadOnlyCollection<OrtValue> Run(float[] tensor, long[] shape, long[]? sizes = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_inputNames.Length > 2)
            throw new NotSupportedException($"입력이 {_inputNames.Length} 개인 모델이다. 하나나 둘만 지원한다.");

        if (_inputNames.Length == 2 && sizes is null)
            throw new NotSupportedException($"이 모델은 둘째 입력({_inputNames[1]})이 필요하다 - 크기를 같이 줘야 한다.");

        var t0 = Stopwatch.GetTimestamp();

        using var input = OrtValue.CreateTensorValueFromMemory(tensor, shape);
        using var sizeInput = sizes is null ? null : OrtValue.CreateTensorValueFromMemory(sizes, [1, sizes.Length]);

        var result = _session.Run(_runOptions, _inputNames, sizeInput is null ? [input] : [input, sizeInput], _outputNames);

        LastInferenceMs = (Stopwatch.GetTimestamp() - t0) * 1000d / Stopwatch.Frequency;

        return result;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _runOptions.Dispose();
        _session.Dispose();
        _options.Dispose();
    }
}
