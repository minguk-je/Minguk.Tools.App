namespace Minguk.Tools.Input.Scripting;

/// <summary>
/// 스크립트 언어를 고른다.
/// </summary>
/// <remarks>
/// 새 언어를 붙일 때 여기만 고친다. 화면과 ViewModel 은 <see cref="IScriptEngine"/> 만 안다.
/// </remarks>
public static class ScriptEngineFactory
{
    public static IScriptEngine Create(ScriptLanguage language) => language switch
    {
        ScriptLanguage.Python => new PythonScriptEngine(),
        ScriptLanguage.JavaScript => new JavaScriptEngine(),
        _ => new RoslynScriptEngine()
    };
}
