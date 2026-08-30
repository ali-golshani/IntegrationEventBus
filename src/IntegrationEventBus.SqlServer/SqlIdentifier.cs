using System.Text.RegularExpressions;

namespace IntegrationEventBus.SqlServer;

internal static partial class SqlIdentifier
{
    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidIdentifierRegex();

    public static bool IsValid(string value) =>
        !string.IsNullOrWhiteSpace(value) && ValidIdentifierRegex().IsMatch(value);

    public static string Quote(string value)
    {
        if (!IsValid(value))
        {
            throw new ArgumentException("Invalid SQL identifier.", nameof(value));
        }

        return $"[{value}]";
    }
}
