using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Minguk.Tools.Vision.Labeling;

namespace Minguk.Tools.Vision.Training;

/// <summary>
/// 학습기에게 넘길 한 줄. 그림 하나와 거기 붙은 사각형 전부다.
/// </summary>
/// <remarks>
/// ML.NET 은 <b>공개 프로퍼티</b>를 보고 열을 만든다. 이름이 곧 열 이름이라 함부로 바꾸면
/// <see cref="DetectorTrainer"/> 의 파이프라인과 어긋난다.
/// </remarks>
public sealed class TrainingSample
{
    public string ImagePath { get; set; } = string.Empty;

    /// <summary>몹 이름들. 사각형 하나에 하나씩.</summary>
    public string[] Labels { get; set; } = [];

    /// <summary>
    /// 사각형들을 <c>x1, y1, x2, y2</c> 넷씩 이어 붙인 것.
    /// </summary>
    /// <remarks>
    /// <b>픽셀 좌표다.</b> 우리가 파일에 담는 것은 0~1 인데(<see cref="LabelBox"/>),
    /// AutoFormerV2 는 픽셀을 받는다. 그래서 <see cref="From"/> 이 그림 크기를 곱한다.
    /// 이걸 안 하면 사각형이 전부 왼쪽 위 한 점에 몰려 아무것도 못 배운다.
    /// </remarks>
    public float[] Box { get; set; } = [];

    /// <summary>
    /// 데이터셋 한 줄을 학습기가 받는 모양으로 바꾼다. 사각형이 없으면 null - 학습에 안 넣는다.
    /// </summary>
    /// <remarks>
    /// 사각형이 없는 그림을 넣으면 "여기엔 아무것도 없다" 를 가르치게 된다. 그런데 우리 목록의
    /// "안 찍음" 은 <b>아직 안 본 그림</b>이라는 뜻이지 비었다는 뜻이 아니다. 그래서 뺀다.
    /// </remarks>
    public static TrainingSample? From(LabelItem item, LabelClasses classes)
    {
        var boxes = LabelFile.Load(item.LabelPath);

        if (boxes.Count == 0) return null;

        if (!ImageSize.TryRead(item.ImagePath, out var width, out var height)) return null;

        var labels = new List<string>(boxes.Count);
        var flat = new List<float>(boxes.Count * 4);

        foreach (var box in boxes)
        {
            labels.Add(classes.NameOf(box.ClassId));

            flat.Add((float)(box.Left * width));
            flat.Add((float)(box.Top * height));
            flat.Add((float)(box.Right * width));
            flat.Add((float)(box.Bottom * height));
        }

        return new TrainingSample
        {
            ImagePath = item.ImagePath,
            Labels = labels.ToArray(),
            Box = flat.ToArray()
        };
    }
}

/// <summary>
/// 그림 크기만 알아낸다. 픽셀은 안 읽는다.
/// </summary>
/// <remarks>
/// 0~1 좌표를 픽셀로 바꾸려면 크기가 필요한데, 그것 때문에 수백 장을 통째로 디코딩할 이유가
/// 없다. 머리말 몇 십 바이트만 본다.
/// </remarks>
public static class ImageSize
{
    public static bool TryRead(string path, out int width, out int height)
    {
        width = height = 0;

        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);

            var extension = Path.GetExtension(path).ToLowerInvariant();

            return extension switch
            {
                ".png" => TryReadPng(reader, out width, out height),
                ".jpg" or ".jpeg" => TryReadJpeg(reader, out width, out height),
                _ => false
            };
        }
        catch (Exception)
        {
            // 깨진 파일 하나 때문에 학습 준비가 멈추면 안 된다. 그 장만 빠진다.
            return false;
        }
    }

    /// <summary>PNG 는 쉽다. IHDR 이 반드시 맨 앞이고 자리도 고정이다.</summary>
    private static bool TryReadPng(BinaryReader reader, out int width, out int height)
    {
        width = height = 0;

        var signature = reader.ReadBytes(8);

        if (signature.Length < 8 || signature[0] != 0x89 || signature[1] != 'P') return false;

        reader.ReadBytes(8);   // 길이 4 + "IHDR" 4

        width = BigEndian(reader.ReadBytes(4));
        height = BigEndian(reader.ReadBytes(4));

        return width > 0 && height > 0;
    }

    /// <summary>
    /// JPEG 는 표시(marker)를 따라가야 한다. 크기는 SOF 표시 안에 있다.
    /// </summary>
    private static bool TryReadJpeg(BinaryReader reader, out int width, out int height)
    {
        width = height = 0;

        if (reader.ReadByte() != 0xFF || reader.ReadByte() != 0xD8) return false;

        while (reader.BaseStream.Position < reader.BaseStream.Length - 1)
        {
            if (reader.ReadByte() != 0xFF) continue;

            byte marker;

            // 0xFF 가 여러 번 이어질 수 있다. 채움 바이트다.
            do
            {
                marker = reader.ReadByte();
            }
            while (marker == 0xFF);

            // SOF0~SOF15. 단 0xC4(허프만) · 0xC8 · 0xCC 는 크기가 아니다.
            if (marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC))
            {
                reader.ReadBytes(3);   // 길이 2 + 정밀도 1

                height = (reader.ReadByte() << 8) | reader.ReadByte();
                width = (reader.ReadByte() << 8) | reader.ReadByte();

                return width > 0 && height > 0;
            }

            var length = (reader.ReadByte() << 8) | reader.ReadByte();

            if (length < 2) return false;

            reader.BaseStream.Seek(length - 2, SeekOrigin.Current);
        }

        return false;
    }

    private static int BigEndian(byte[] bytes)
        => bytes.Length < 4 ? 0 : (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];
}
