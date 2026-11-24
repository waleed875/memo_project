using System.Text.RegularExpressions;

namespace MemoUpdateContracts;

/// <summary>A strict Semantic Version used consistently by the publisher and Windows clients.</summary>
public readonly record struct ReleaseVersion(int Major, int Minor, int Patch, string? PreRelease = null)
    : IComparable<ReleaseVersion>
{
    private static readonly Regex Pattern = new(
        "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*))?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool TryParse(string? value, out ReleaseVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var match = Pattern.Match(value.Trim());
        if (!match.Success
            || !int.TryParse(match.Groups[1].Value, out var major)
            || !int.TryParse(match.Groups[2].Value, out var minor)
            || !int.TryParse(match.Groups[3].Value, out var patch)) return false;

        var preRelease = match.Groups[4].Success ? match.Groups[4].Value : null;
        if (preRelease is not null && preRelease.Split('.').Any(IsInvalidNumericIdentifier)) return false;
        version = new ReleaseVersion(major, minor, patch, preRelease);
        return true;
    }

    public static ReleaseVersion Parse(string value) =>
        TryParse(value, out var version)
            ? version
            : throw new FormatException($"Invalid semantic version: {value}");

    public int CompareTo(ReleaseVersion other)
    {
        var comparison = Major.CompareTo(other.Major);
        if (comparison != 0) return comparison;
        comparison = Minor.CompareTo(other.Minor);
        if (comparison != 0) return comparison;
        comparison = Patch.CompareTo(other.Patch);
        if (comparison != 0) return comparison;

        if (PreRelease is null) return other.PreRelease is null ? 0 : 1;
        if (other.PreRelease is null) return -1;
        var left = PreRelease.Split('.');
        var right = other.PreRelease.Split('.');
        for (var index = 0; index < Math.Min(left.Length, right.Length); index++)
        {
            comparison = CompareIdentifier(left[index], right[index]);
            if (comparison != 0) return comparison;
        }
        return left.Length.CompareTo(right.Length);
    }

    public override string ToString() =>
        PreRelease is null ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{PreRelease}";

    private static bool IsInvalidNumericIdentifier(string identifier) =>
        identifier.All(char.IsDigit) && identifier.Length > 1 && identifier[0] == '0';

    private static int CompareIdentifier(string left, string right)
    {
        var leftNumeric = left.All(char.IsDigit);
        var rightNumeric = right.All(char.IsDigit);
        if (leftNumeric && rightNumeric)
        {
            var lengthComparison = left.Length.CompareTo(right.Length);
            return lengthComparison != 0 ? lengthComparison : string.CompareOrdinal(left, right);
        }
        if (leftNumeric != rightNumeric) return leftNumeric ? -1 : 1;
        return string.CompareOrdinal(left, right);
    }
}
