using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

using Vortice.D3DCompiler;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Minguk.Tools.Inference;

/// <summary>
/// 캡처된 GPU 텍스처를 모델 입력 텐서로 바꾼다. 리사이즈·정규화·축 변환을 GPU 에서 한 번에 한다.
///
/// 왜 이게 필요한가:
///   캡처 프레임을 통째로 CPU 로 내리면 2560×1440 BGRA 가 14.7 MB 다(실측 1.34 ms).
///   모델이 실제로 먹는 건 640×640(4.8 MB)뿐이니, GPU 에서 먼저 줄이고 내린다.
///
///   다만 크기를 줄이는 것만으로는 안 빨라진다. 실제로 재 보면 그렇다:
///     전체 프레임 리드백           1.34 ms  (14.7 MB)
///     텐서 리드백, 그 자리에서 Map  1.25 ms  ( 4.8 MB)  ← 1/3 로 줄였는데 거의 그대로
///     텐서 리드백, 파이프라인       0.60 ms  ( 4.8 MB)  ← 이제야 빨라짐
///
///   리드백 비용을 지배하는 건 대역폭이 아니라 GPU 를 기다리는 동기화다(약 1 ms 고정).
///   그래서 복사와 읽기를 한 프레임 어긋나게 돌린다. 자세한 건 _readback 주석 참조.
///
///   ONNX Runtime 의 C# 바인딩은 D3D12 리소스를 직접 텐서로 받는 길(OrtDmlApi)을 노출하지 않아서,
///   진짜 zero-copy 는 네이티브 shim 없이는 불가능하다. 그건 이 비용이 실제로 문제가 될 때 가서 하면 된다.
/// </summary>
public sealed class FramePreprocessor : IDisposable
{
    /// <summary>
    /// BGRA 텍스처 → 정규화된 텐서 버퍼.
    ///
    /// UNorm 포맷을 샘플하므로 들어오는 값은 이미 0~1 이고, B8G8R8A8 의 채널 순서도
    /// 하드웨어가 풀어 주기 때문에 셰이더에서 .rgb 는 그냥 R,G,B 다. 수동 스위즐이 필요 없다.
    /// </summary>
    private const string ShaderSource = """
        Texture2D<float4>     Source        : register(t0);
        SamplerState          LinearSampler : register(s0);
        RWStructuredBuffer<float> Output    : register(u0);

        cbuffer Params : register(b0)
        {
            uint   OutWidth;
            uint   OutHeight;
            uint   IsNchw;
            uint   UseLetterbox;

            float2 SrcScale;
            float2 SrcOffset;

            float3 Mean;
            float  _pad0;

            float3 InvStd;
            float  _pad1;

            float3 PadColor;
            float  _pad2;
        };

        [numthreads(8, 8, 1)]
        void main(uint3 tid : SV_DispatchThreadID)
        {
            if (tid.x >= OutWidth || tid.y >= OutHeight)
                return;

            float2 uv  = (float2(tid.xy) + 0.5f) / float2(OutWidth, OutHeight);
            float2 src = uv * SrcScale + SrcOffset;

            float3 rgb;
            if (UseLetterbox != 0 && (src.x < 0.0f || src.x > 1.0f || src.y < 0.0f || src.y > 1.0f))
                rgb = PadColor;
            else
                rgb = Source.SampleLevel(LinearSampler, src, 0).rgb;

            rgb = (rgb - Mean) * InvStd;

            uint plane = OutWidth * OutHeight;
            uint idx   = tid.y * OutWidth + tid.x;

            if (IsNchw != 0)
            {
                Output[idx]             = rgb.r;
                Output[plane + idx]     = rgb.g;
                Output[plane * 2 + idx] = rgb.b;
            }
            else
            {
                Output[idx * 3 + 0] = rgb.r;
                Output[idx * 3 + 1] = rgb.g;
                Output[idx * 3 + 2] = rgb.b;
            }
        }
        """;

    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;

    private readonly ID3D11ComputeShader _shader;
    private readonly ID3D11SamplerState _sampler;
    private readonly ID3D11Buffer _constants;

    private readonly ID3D11Buffer _output;
    private readonly ID3D11UnorderedAccessView _outputView;
    // 스테이징 버퍼 두 장을 번갈아 쓴다.
    //
    // 한 장만 쓰면 CopyResource 직후에 Map 을 부르게 되고, 그러면 GPU 가 그 복사를 끝낼 때까지
    // CPU 가 선다. 실측하면 이 대기가 1 ms 안팎이라 전송량(4.8 MB)보다 훨씬 크다.
    // 즉 리드백 비용은 대역폭이 아니라 동기화가 지배한다 — 옮길 양을 줄이는 것만으로는 안 빨라진다.
    //
    // 그래서 이번 프레임은 A 에 복사만 걸어 두고, 지난 프레임에 복사해 둔 B 를 읽는다.
    // 프레임 간격(16 ms)이면 그 복사는 이미 끝나 있으므로 Map 이 서지 않는다.
    // 대신 텐서가 한 프레임 늦는다. 지연이 처리량보다 중요하면 pipelined:false 로 끄면 된다.
    private readonly ID3D11Buffer[] _readback = new ID3D11Buffer[2];
    private readonly bool[] _readbackFilled = new bool[2];
    private readonly bool _pipelined;
    private int _slot;

    private readonly float[] _tensor;

    // 캡처 프레임을 그대로 SRV 로 묶지 않고 우리 텍스처로 한 번 복사한다.
    // 프레임 풀의 텍스처는 수명이 콜백 안으로 한정되고 바인드 플래그도 보장되지 않아서,
    // GPU 안에서 끝나는 복사(수십 마이크로초) 하나로 그 불확실성을 걷어내는 편이 낫다.
    private ID3D11Texture2D? _source;
    private ID3D11ShaderResourceView? _sourceView;
    private int _sourceWidth;
    private int _sourceHeight;

    private bool _disposed;

    /// <param name="pipelined">
    /// true 면 텐서를 한 프레임 늦게 읽어 GPU 대기를 없앤다(처리량 우선).
    /// false 면 그 자리에서 읽어 항상 최신 프레임을 주지만 매 프레임 GPU 를 기다린다(지연 우선).
    /// </param>
    public FramePreprocessor(ID3D11Device device, ID3D11DeviceContext context, TensorSpec spec, bool pipelined = true)
    {
        _device = device;
        _context = context;
        Spec = spec;
        _pipelined = pipelined;

        _tensor = new float[spec.ElementCount];

        var bytecode = Compiler.Compile(ShaderSource, "main", "FramePreprocessor.hlsl", "cs_5_0",
            ShaderFlags.OptimizationLevel3, EffectFlags.None);

        _shader = _device.CreateComputeShader(bytecode.Span, null);

        _sampler = _device.CreateSamplerState(new SamplerDescription(
            Filter.MinMagMipLinear,
            TextureAddressMode.Clamp,
            TextureAddressMode.Clamp,
            TextureAddressMode.Clamp,
            0f,
            1,
            ComparisonFunction.Never,
            0f,
            float.MaxValue));

        _constants = _device.CreateBuffer(new BufferDescription
        {
            ByteWidth = (uint)Marshal.SizeOf<ShaderParams>(),
            BindFlags = BindFlags.ConstantBuffer,
            Usage = ResourceUsage.Dynamic,
            CPUAccessFlags = CpuAccessFlags.Write
        });

        _output = _device.CreateBuffer(new BufferDescription
        {
            ByteWidth = (uint)spec.ByteCount,
            BindFlags = BindFlags.UnorderedAccess,
            Usage = ResourceUsage.Default,
            MiscFlags = ResourceOptionFlags.BufferStructured,
            StructureByteStride = sizeof(float)
        });

        _outputView = _device.CreateUnorderedAccessView(_output, new UnorderedAccessViewDescription(
            _output, Format.Unknown, 0, (uint)spec.ElementCount, BufferUnorderedAccessViewFlags.None));

        for (var i = 0; i < _readback.Length; i++)
        {
            _readback[i] = _device.CreateBuffer(new BufferDescription
            {
                ByteWidth = (uint)spec.ByteCount,
                BindFlags = BindFlags.None,
                Usage = ResourceUsage.Staging,
                CPUAccessFlags = CpuAccessFlags.Read
            });
        }
    }

    public TensorSpec Spec { get; }

    /// <summary>
    /// 마지막으로 읽어 온 텐서. 다음 <see cref="Process"/> 가 덮어쓴다.
    /// 파이프라인 모드에서는 한 프레임 전의 화면이다.
    /// </summary>
    public float[] Tensor => _tensor;

    /// <summary>이것을 만든 D3D11 장치.</summary>
    public ID3D11Device Device => _device;

    /// <summary>
    /// 이 전처리기를 그대로 더 써도 되는가. 아니면 버리고 새로 만들어야 한다.
    /// </summary>
    /// <remarks>
    /// <b>장치가 셋 중 가장 놓치기 쉽다.</b> 캡처 대상을 모니터에서 게임 창으로 바꾸면 세션이 새 D3D 장치로
    /// 다시 만들어지는데, 크기가 같다고 옛 전처리기를 그냥 쓰면 죽은 장치로 텍스처를 만들다
    /// <see cref="NullReferenceException"/> 이 난다 - 그때부터 몹 찾기가 통째로 멈추고 화면에는 아무 말도 안 뜬다
    /// (실측: 0.25초마다 같은 예외가 로그를 도배했다).
    ///
    /// 넣는 방식(레터박스냐)도 본다 - 같은 크기로 모델만 갈아 끼우면 셰이더가 옛 방식으로 남는다.
    ///
    /// 판단을 여기 모아 두는 이유는 부르는 쪽이 셋 중 하나를 빠뜨려도 모르기 때문이다.
    /// </remarks>
    public bool Matches(ID3D11Device device, TensorSpec spec)
        => !_disposed
           && Spec.Width == spec.Width
           && Spec.Height == spec.Height
           && Spec.Letterbox == spec.Letterbox
           && _device.NativePointer == device.NativePointer;

    /// <summary>GPU 전처리(디스패치)에 든 시간.</summary>
    public double LastDispatchMs { get; private set; }

    /// <summary>텐서를 CPU 로 내리는 데 든 시간.</summary>
    public double LastReadbackMs { get; private set; }

    /// <summary>
    /// 캡처 텍스처 한 장을 텐서로 만든다. 캡처 콜백 스레드에서 부르는 것을 전제로 한다.
    /// </summary>
    /// <returns>
    /// <see cref="Tensor"/> 에 쓸 만한 값이 들어갔으면 true.
    /// 파이프라인 모드의 첫 프레임에서는 아직 읽을 게 없어서 false 다.
    /// </returns>
    public bool Process(ID3D11Texture2D frame, int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var t0 = Stopwatch.GetTimestamp();

        EnsureSource(width, height);
        _context.CopyResource(_source!, frame);

        UpdateConstants(width, height);

        _context.CSSetShader(_shader);
        _context.CSSetShaderResource(0, _sourceView);
        _context.CSSetSampler(0, _sampler);
        _context.CSSetConstantBuffer(0, _constants);
        _context.CSSetUnorderedAccessView(0, _outputView, uint.MaxValue);

        _context.Dispatch(DivideUp(Spec.Width, 8), DivideUp(Spec.Height, 8), 1);

        // 다음 프레임에서 캡처 텍스처가 이 슬롯에 묶인 채로 남지 않도록 풀어 준다.
        _context.CSSetShaderResource(0, null);
        _context.CSSetUnorderedAccessView(0, null, uint.MaxValue);

        LastDispatchMs = Elapsed(t0);

        var t1 = Stopwatch.GetTimestamp();

        // 이번 프레임은 현재 슬롯에 복사만 걸어 둔다. 여기서는 GPU 를 기다리지 않는다.
        _context.CopyResource(_readback[_slot], _output);
        _readbackFilled[_slot] = true;

        // 파이프라인이면 지난 프레임 슬롯을, 아니면 방금 건 슬롯을 읽는다.
        var readSlot = _pipelined ? 1 - _slot : _slot;
        _slot = 1 - _slot;

        if (!_readbackFilled[readSlot])
        {
            LastReadbackMs = 0;
            return false;
        }

        var map = _context.Map(_readback[readSlot], 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            Marshal.Copy(map.DataPointer, _tensor, 0, _tensor.Length);
        }
        finally
        {
            _context.Unmap(_readback[readSlot], 0);
        }

        LastReadbackMs = Elapsed(t1);
        return true;
    }

    private void EnsureSource(int width, int height)
    {
        if (_source is not null && _sourceWidth == width && _sourceHeight == height)
            return;

        _sourceView?.Dispose();
        _source?.Dispose();

        _source = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.None
        });

        _sourceView = _device.CreateShaderResourceView(_source, null);

        _sourceWidth = width;
        _sourceHeight = height;

        // 원본 크기가 바뀌면 파이프라인에 남아 있던 지난 프레임은 버린다.
        // (텐서 크기는 그대로지만 내용이 다른 화면이라 섞으면 안 된다.)
        Array.Clear(_readbackFilled);
    }

    private void UpdateConstants(int sourceWidth, int sourceHeight)
    {
        var scaleX = 1f;
        var scaleY = 1f;
        var offsetX = 0f;
        var offsetY = 0f;

        if (Spec.Letterbox)
        {
            // 원본을 비율 그대로 출력 안에 넣었을 때 실제로 차지하는 크기를 구하고,
            // 출력 uv → 원본 uv 로 되돌리는 스케일/오프셋을 만든다.
            var ratio = MathF.Min((float)Spec.Width / sourceWidth, (float)Spec.Height / sourceHeight);
            var fittedWidth = sourceWidth * ratio;
            var fittedHeight = sourceHeight * ratio;
            var padX = (Spec.Width - fittedWidth) * 0.5f;
            var padY = (Spec.Height - fittedHeight) * 0.5f;

            scaleX = Spec.Width / fittedWidth;
            scaleY = Spec.Height / fittedHeight;
            offsetX = -padX / fittedWidth;
            offsetY = -padY / fittedHeight;
        }

        var parameters = new ShaderParams
        {
            OutWidth = (uint)Spec.Width,
            OutHeight = (uint)Spec.Height,
            IsNchw = Spec.Layout == TensorLayout.Nchw ? 1u : 0u,
            UseLetterbox = Spec.Letterbox ? 1u : 0u,

            ScaleX = scaleX,
            ScaleY = scaleY,
            OffsetX = offsetX,
            OffsetY = offsetY,

            MeanR = Spec.Mean.X,
            MeanG = Spec.Mean.Y,
            MeanB = Spec.Mean.Z,

            InvStdR = 1f / Spec.Std.X,
            InvStdG = 1f / Spec.Std.Y,
            InvStdB = 1f / Spec.Std.Z,

            PadR = Spec.PadColor.X,
            PadG = Spec.PadColor.Y,
            PadB = Spec.PadColor.Z
        };

        var map = _context.Map(_constants, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
        try
        {
            Marshal.StructureToPtr(parameters, map.DataPointer, false);
        }
        finally
        {
            _context.Unmap(_constants, 0);
        }
    }

    private static uint DivideUp(int value, int divisor) => (uint)((value + divisor - 1) / divisor);

    private static double Elapsed(long from) => (Stopwatch.GetTimestamp() - from) * 1000d / Stopwatch.Frequency;

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _sourceView?.Dispose();
        _source?.Dispose();
        foreach (var buffer in _readback)
            buffer?.Dispose();
        _outputView.Dispose();
        _output.Dispose();
        _constants.Dispose();
        _sampler.Dispose();
        _shader.Dispose();
    }

    /// <summary>HLSL 의 cbuffer 와 바이트 단위로 같은 모양이어야 한다. float4 경계로 패딩을 맞춰 뒀다.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct ShaderParams
    {
        public uint OutWidth;
        public uint OutHeight;
        public uint IsNchw;
        public uint UseLetterbox;

        public float ScaleX;
        public float ScaleY;
        public float OffsetX;
        public float OffsetY;

        public float MeanR;
        public float MeanG;
        public float MeanB;
        public float Pad0;

        public float InvStdR;
        public float InvStdG;
        public float InvStdB;
        public float Pad1;

        public float PadR;
        public float PadG;
        public float PadB;
        public float Pad2;
    }
}
