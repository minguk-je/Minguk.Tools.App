using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Microsoft.CodeAnalysis;

namespace Minguk.Tools.Input.Scripting;

/// <summary>
/// <c>#load</c> 이 파일을 찾는 규칙. 디스크를 읽되, 편집기에 열려 있는 글이 있으면 그것을 준다.
/// </summary>
/// <remarks>
/// 기본 규칙(<see cref="SourceFileResolver"/>)은 디스크만 본다. 탭 셋을 고치고 저장 없이 실행하면 사람은 화면의 글이 돈다고 여기는데
/// 옛 파일이 돌아, 방금 고친 함수가 안 바뀐 것처럼 보인다. 찾는 일은 기본 규칙에 맡기고 읽는 것만 가로챈다.
/// </remarks>
public sealed class ScriptSourceResolver : SourceReferenceResolver
{
    private readonly SourceFileResolver _inner;
    private readonly IReadOnlyDictionary<string, string> _openTexts;

    public ScriptSourceResolver(string? baseDirectory, IReadOnlyDictionary<string, string> openTexts)
    {
        _inner = new SourceFileResolver([], baseDirectory);
        _openTexts = openTexts;
    }

    public override string? NormalizePath(string path, string? baseFilePath) => _inner.NormalizePath(path, baseFilePath);

    public override string? ResolveReference(string path, string? baseFilePath) => _inner.ResolveReference(path, baseFilePath);

    public override Stream OpenRead(string resolvedPath)
    {
        foreach (var (path, text) in _openTexts)
        {
            if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(resolvedPath), StringComparison.OrdinalIgnoreCase))
                return new MemoryStream(new UTF8Encoding(false).GetBytes(text));
        }

        return _inner.OpenRead(resolvedPath);
    }

    // 컴파일러가 규칙끼리 같은지 본다(재사용 판단). 열린 글이 다르면 다른 규칙이다 - 참조로만 같다고 한다.
    public override bool Equals(object? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
}
