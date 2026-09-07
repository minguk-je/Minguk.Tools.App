using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Minguk.Base.Extension;

public static class MethodBaseExtension
{
    public static string GetDeclaringName(this MethodBase? methodBase,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        return $"{memberName}[{lineNumber}]";
    }

    public static string GetDeclaringName(this MethodBase methodBase,
        Exception ex,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        var st = new StackTrace(ex, true);
        var frame = st.GetFrame(0);
        var line = frame?.GetFileLineNumber();

        if (line == null)
            return $"{memberName}";
        else
            return $"{memberName}[{line}]";
    }
}
