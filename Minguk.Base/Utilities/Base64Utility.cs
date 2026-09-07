using System.Text;
using System.Text.RegularExpressions;

namespace Minguk.Base.Utilities;

public static class Base64Utility
{
    public static string Encode(string str)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(str));
    }

    public static string Decode(string str)
    {
        return Encoding.UTF8.GetString(Convert.FromBase64String(str));
    }

    public static bool IsBase64(string base64)
    {
        base64 = base64.Trim();
        return (base64.Length % 4 == 0) && Regex.IsMatch(base64, @"^[a-zA-Z0-9\+/]*={0,3}$", RegexOptions.None);
    }
}
