namespace MdcAi.Extensions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public static class StringExtensions
{
    /// <summary>
    /// Will handle all whitespace chars not only spaces, trim both leading and trailing whitespaces, remove extra whitespaces, 
    /// and all whitespaces are replaced to space char (so we have uniform space separator). And it is fast.
    /// </summary>
    public static string CompactWhitespaces(this string s)
    {
        if (string.IsNullOrEmpty(s))
            return s;

        var sb = new StringBuilder(s);

        CompactWhitespaces(sb);

        return sb.ToString();
    }

    /// <summary>
    /// Will handle all whitespace chars not only spaces, trim both leading and trailing whitespaces, remove extra whitespaces, 
    /// and all whitespaces are replaced to space char (so we have uniform space separator). And it is fast.
    /// </summary>
    public static void CompactWhitespaces(this StringBuilder sb)
    {
        if ((sb?.Length ?? 0) == 0)
            return;

        // set [start] to first not-whitespace char or to sb.Length

        var start = 0;

        while (start < sb.Length)
        {
            if (char.IsWhiteSpace(sb[start]))
                start++;
            else
                break;
        }

        // if [sb] has only whitespaces, then return empty string

        if (start == sb.Length)
        {
            sb.Length = 0;
            return;
        }

        // set [end] to last not-whitespace char

        var end = sb.Length - 1;

        while (end >= 0)
        {
            if (char.IsWhiteSpace(sb[end]))
                end--;
            else
                break;
        }

        // compact string

        var dest = 0;
        var previousIsWhitespace = false;

        for (var i = start; i <= end; i++)
        {
            if (char.IsWhiteSpace(sb[i]))
            {
                if (!previousIsWhitespace)
                {
                    previousIsWhitespace = true;
                    sb[dest] = ' ';
                    dest++;
                }
            }
            else
            {
                previousIsWhitespace = false;
                sb[dest] = sb[i];
                dest++;
            }
        }

        sb.Length = dest;
    }

    public static string Truncate(this string value, int maxLength)
    {
        if (!string.IsNullOrEmpty(value) && value.Length > maxLength)
            return value[..maxLength];
        return value;
    }

    public static string JoinString(this IEnumerable<string> e, string separator) => string.Join(separator, e);

    public static string ToCsv(this IEnumerable<string> e, bool space = true) => JoinString(e, space ? ", " : ",");

    public static bool IsNullOrEmpty(this string s) => string.IsNullOrEmpty(s);

    public static bool IsNullOrWhiteSpace(this string s) => string.IsNullOrWhiteSpace(s);

    public static string NullIfEmpty(this string s) => string.IsNullOrEmpty(s) ? null : s;

    public static string NullIfEmptyOrWhiteSpace(this string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    public static bool IsNumber(this string str) =>
        decimal.TryParse(str, NumberStyles.Any, NumberFormatInfo.InvariantInfo, out _);

    /// <summary>
    /// Returns a decimal if string (trimmed) is numeric, otherwise null. 
    /// </summary>
    public static decimal? ToNumber(this string value)
    {
        if (!decimal.TryParse(value.Trim(), out var number))
            return null;
        return number;
    }
}
