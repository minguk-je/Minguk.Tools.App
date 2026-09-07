namespace Minguk.Base.Extension;

public static class ExceptionExtension
{
    public static int LineNumber(this Exception ex)
    {
        int n;
        int i = ex.StackTrace!.LastIndexOf(" ", StringComparison.Ordinal);
        if (i > -1)
        {
            string s = ex.StackTrace.Substring(i + 1);
            if (int.TryParse(s, out n))
                return n;
        }
        return -1;
    }
}
