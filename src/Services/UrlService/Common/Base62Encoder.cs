namespace UrlService.Common;

/// <summary>
/// Converts a number into a short Base62 string.
/// Base62 uses characters: 0-9, a-z, A-Z (62 characters total)
/// Example: 12345 → "dnh"
/// This is how bit.ly and tinyurl generate short codes!
/// </summary>
public static class Base62Encoder
{
    private const string Characters = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

    public static string Encode(long number)
    {
        if (number == 0)
            return Characters[0].ToString();

        var result = new Stack<char>();

        while (number > 0)
        {
            result.Push(Characters[(int)(number % 62)]);
            number /= 62;
        }

        return new string(result.ToArray());
    }
}