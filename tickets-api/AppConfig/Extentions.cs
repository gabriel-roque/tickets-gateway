using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TicketsApi.AppConfig;

public static class Extentions
{
    public static string ToUpperFirst(this string text, bool lower = true)
    {
        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(lower ? text.ToLower() : text);
    }

    public static string ToUpperAllFirst(this string text, bool lower = true)
    {
        return Thread.CurrentThread.CurrentCulture.TextInfo.ToTitleCase(lower ? text.ToLower() : text);
    }
    
    public static string GetNumbersForQuery(this string str)
    {
        var chars = str?.Where(c => char.IsDigit(c) || c == '\\' ||c == '%');

        return chars?.Aggregate("", (current, c) => current + c);
    }
    
    public static string GetNumbers(this string str)
    {
        var chars = str?.Where(c => char.IsDigit(c));

        return chars?.Aggregate("", (current, c) => current + c);
    }
    
    public static string RemoveDiacritics(this string stIn)
    {
        if (stIn is null) return "";

        var stFormD = stIn.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();

        foreach (var t in stFormD)
        {
            var uc = CharUnicodeInfo.GetUnicodeCategory(t);
            if (uc != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(t);
            }
        }

        return (sb.ToString().Normalize(NormalizationForm.FormC));
    }
    
    public static string RemoveSpecialCharacters(this string input)
    {
        if (input is null) return null;

        input = RemoveDiacritics(input);

        input = input.Replace('-', ' ')
            .Replace('º', ' ')
            .Replace(',', ' ')
            .Replace('ª', ' ')
            .Replace('.', ' ');

        input = Regex.Replace(input, @"[^\p{L}\p{Nd}.º %\u00BA/\\]+", " ");

        return input.Trim();
    }

    public static string RemoveNumberSpecialCharacters(this string input)
    {
        if (string.IsNullOrEmpty(input.GetNumbers())) return "Sn";

        input = RemoveDiacritics(input);

        input = input.Replace('-', ' ')
            .Replace('º', ' ')
            .Replace('.', ' ')
            .Replace('/', ' ');

        input = Regex.Replace(input, @"[^\p{L}\p{Nd} ]+", "");

        return input.Trim();
    }
}