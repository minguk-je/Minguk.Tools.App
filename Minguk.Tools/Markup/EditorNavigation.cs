namespace Minguk.Tools.Markup;

/// <summary>편집기가 "이 파일의 이 줄로 가고 싶다" 고 올리는 것(참조 창 더블 클릭). 파일이 빈 글이면 이 편집기 안이다.</summary>
public sealed record EditorNavigation(string FilePath, int Line);
