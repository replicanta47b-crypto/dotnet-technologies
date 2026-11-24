using System.Text;

namespace Disputes.Domain.Common;

/// <summary>
/// Текстове подання перерахувань у snake_case (так вони зберігаються в MongoDB за схемою Лаб. 01:
/// UnderReview ↔ "under_review"). Єдине місце правил — використовують і Application, і Infrastructure.
/// </summary>
public static class EnumText
{
    public static string ToText<TEnum>(TEnum value) where TEnum : struct, Enum
        => ToSnakeCase(value.ToString());

    public static bool TryParse<TEnum>(string? text, out TEnum value) where TEnum : struct, Enum
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        foreach (var candidate in Enum.GetValues<TEnum>())
        {
            if (string.Equals(ToText(candidate), text.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                value = candidate;
                return true;
            }
        }
        return false;
    }

    public static IReadOnlyList<string> Names<TEnum>() where TEnum : struct, Enum
        => Enum.GetValues<TEnum>().Select(v => ToText(v)).ToList();

    private static string ToSnakeCase(string name)
    {
        var sb = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0) sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
