using System.Text.RegularExpressions;

namespace Minguk.Base.Extension;

public static class StringExtension
{
    /// <summary>
    /// Checks string object's value to array of string values
    /// </summary>
    /// <param name="value"></param>
    /// <param name="stringValues">Array of string values to compare</param>
    /// <param name="trueValue"></param>
    /// <param name="falseValue"></param>
    /// <returns>Return true if any string value matches</returns>
    public static T If<T>(this string value, T trueValue, T falseValue, params string[] stringValues)
    {
        foreach (string otherValue in stringValues)
            if (String.CompareOrdinal(value, otherValue) == 0)
                return trueValue;

        return falseValue;
    }

    /// <summary>
    /// Checks string object's value to array of string values
    /// </summary>
    /// <param name="value"></param>
    /// <param name="stringValues">Array of string values to compare</param>
    /// <returns>Return true if any string value matches</returns>
    public static bool In(this string? value, params string[] stringValues)
    {
        foreach (string? otherValue in stringValues)
            if (String.CompareOrdinal(value, otherValue) == 0)
                return true;

        return false;
    }

    /// <summary>
    /// Checks string object's value to array of string values
    /// </summary>
    /// <param name="value"></param>
    /// <param name="stringValues">Array of string values to compare</param>
    /// <returns>Return true if any string value matches</returns>
    public static bool NotIn(this string value, params string[] stringValues)
    {
        foreach (string otherValue in stringValues)
            if (String.CompareOrdinal(value, otherValue) == 0)
                return false;

        return true;
    }

    /// <summary>
    /// Converts string to enum object
    /// </summary>
    /// <typeparam name="T">Type of enum</typeparam>
    /// <param name="value">String value to convert</param>
    /// <returns>Returns enum object</returns>
    public static T ToEnum<T>(this string value)
        where T : struct
    {
        return (T)System.Enum.Parse(typeof(T), value, true);
    }

    /// <summary>
    /// Returns characters from right of specified length
    /// </summary>
    /// <param name="value">String value</param>
    /// <param name="length">Max number of charaters to return</param>
    /// <returns>Returns string from right</returns>
    public static string Right(this string value, int length)
    {
        return value.Length > length ? value.Substring(value.Length - length) : value;
    }

    public static string Left(this string value)
    {
        return value.IndexOf(" ", StringComparison.Ordinal) > -1
            ? value.Substring(0, value.IndexOf(" ", StringComparison.Ordinal))
            : value;
    }

    /// <summary>
    /// Returns characters from left of specified length
    /// </summary>
    /// <param name="value">String value</param>
    /// <param name="length">Max number of charaters to return</param>
    /// <returns>Returns string from left</returns>
    public static string Left(this string value, int length)
    {
        return value.Length > length ? value.Substring(0, length) : value;
    }

    /// <summary>
    ///  Replaces the format item in a specified System.String with the text equivalent
    ///  of the value of a specified System.Object instance.
    /// </summary>
    /// <param name="value">A composite format string</param>
    /// <param name="arg0">An System.Object to format</param>
    /// <returns>A copy of format in which the first format item has been replaced by the
    /// System.String equivalent of arg0</returns>
    public static string Format(this string value, object arg0)
    {
        return string.Format(value, arg0);
    }

    /// <summary>
    ///  Replaces the format item in a specified System.String with the text equivalent
    ///  of the value of a specified System.Object instance.
    /// </summary>
    /// <param name="value">A composite format string</param>
    /// <param name="args">An System.Object array containing zero or more objects to format.</param>
    /// <returns>A copy of format in which the format items have been replaced by the System.String
    /// equivalent of the corresponding instances of System.Object in args.</returns>
    public static string Format(this string value, params object[] args)
    {
        return string.Format(value, args);
    }

    public static bool Match(this string value, string pattern)
    {
        return Regex.IsMatch(value, pattern);
    }

    // "a string".IsNullOrEmpty() beats string.IsNullOrEmpty("a string")
    public static bool IsNullOrEmpty(this string theString)
    {
        return string.IsNullOrEmpty(theString);
    }

    // "a string".IsNullOrWhiteSpace() beats string.IsNullOrWhiteSpace("a string")
    public static bool IsNullOrWhiteSpace(this string theString)
    {
        return string.IsNullOrWhiteSpace(theString);
    }

    public static string ToLikeString(this string theString, bool start = true, bool end = true)
    {
        if (string.IsNullOrWhiteSpace(theString))
            return "%";
        else
            return (start ? "%" : string.Empty) + theString + (end ? "%" : string.Empty);
    }

    public static string PadBoth(this string str, int length)
    {
        int spaces = length - str.Length;
        int padLeft = spaces / 2 + str.Length;
        return str.PadLeft(padLeft).PadRight(length);
    }

    public static string Center(this string value, int width)
    {
        if (value.Length >= width)
            return value;

        int leftPadding = (width - value.Length) / 2;
        int rightPadding = width - value.Length - leftPadding;

        return new string(' ', leftPadding) + value + new string(' ', rightPadding);
    }
}
