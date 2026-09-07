using System.Text.RegularExpressions;

namespace Minguk.Tools.Source;

/// <summary>
/// DevExpress Docking 의 IDocument.Id 는 XamlName Grammar 를 따라야 한다.
/// (첫 글자는 문자 또는 '_', 이후는 문자/숫자/'_' 만 허용)
/// 네임스페이스가 포함된 뷰 이름('a.b.Views.XView')의 '.' 같은 문자를 '_' 로 치환한다.
/// https://learn.microsoft.com/dotnet/desktop-wpf/xaml-services/xamlname-grammar
/// </summary>
public class CustomID
{
    public string Id { get; private set; }

    public CustomID(string id) => Id = Sanitize(id);

    private static string Sanitize(string id)
    {
        if (string.IsNullOrEmpty(id))
            return "_";

        var sanitized = Regex.Replace(id, "[^A-Za-z0-9_]", "_");

        if (!char.IsLetter(sanitized[0]) && sanitized[0] != '_')
            sanitized = "_" + sanitized;

        return sanitized;
    }

    public override string ToString() => Id;
}
