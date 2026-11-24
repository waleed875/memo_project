using System.Globalization;

namespace MemoDashboard.Backend.Services;

/// <summary>
/// C# mirror of CompanyDetailedReport.cs:573-606 formulas (and kpi.ts).
/// All percentages round to 2 decimals; null means "not applicable" (غير منطبق).
/// Decimal-based to match the numeric columns exactly.
/// </summary>
public static class CompanyReportKpis
{
    public static decimal Round2(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>Desktop:573 — manual form efficiency, display only.</summary>
    public static decimal? FormEfficiencyPercent(decimal? formEfficiencyIndex)
        => formEfficiencyIndex is decimal value ? value : null;

    /// <summary>Machinery efficiency indicator (complement of unlinked entries): 100 - (unlinked / total * 100).</summary>
    public static decimal? MechanismEfficiencyPercent(long entryNotes, long unlinkedEntryNotes)
    {
        if (entryNotes <= 0) return null;
        return Round2(100m - (unlinkedEntryNotes * 100m / entryNotes));
    }

    /// <summary>Compatibility mirror returning the machinery efficiency complement.</summary>
    public static decimal? MechanismUnlinkedPercent(long entryNotes, long unlinkedEntryNotes)
        => MechanismEfficiencyPercent(entryNotes, unlinkedEntryNotes);

    /// <summary>Desktop:589 — ((onTime*0.9 + late*0.1) / total) * 100.</summary>
    public static decimal? WeightedTimelinessPercent(long totalNotes, long onTimeNotes, long lateNotes)
    {
        if (totalNotes <= 0) return null;
        return Round2((onTimeNotes * 0.9m + lateNotes * 0.1m) / totalNotes * 100m);
    }

    /// <summary>Desktop:599 — (form[missing=100] + items + mechanismEfficiency) / 3.</summary>
    public static decimal? OverallCompositePercent(decimal? formEfficiency, decimal? itemQuality, decimal? mechanismEfficiency)
    {
        if (itemQuality is null || mechanismEfficiency is null) return null;
        return Round2(((formEfficiency ?? 100m) + itemQuality.Value + mechanismEfficiency.Value) / 3m);
    }

    /// <summary>Mirrors FrmReportParameters.ParsePercent + kpi.ts parseFormEfficiency.</summary>
    public static decimal? ParseFormEfficiency(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var normalized = input.Trim().Replace(',', '.');
        if (!decimal.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return null;
        if (value < 0 || value > 100) return null;
        return value;
    }

    public static string FormatPercent(decimal? value)
        => value is null ? "غير منطبق" : value.Value.ToString("0.##", CultureInfo.InvariantCulture) + "%";

    public static string FormatInt(long value) => value.ToString(CultureInfo.InvariantCulture);

    public static string FormatDays(decimal? value)
        => value is null ? "غير منطبق" : value.Value.ToString("0.#", CultureInfo.InvariantCulture) + " يوم";

    public static string FormatDate(DateTime? value)
        => value is null ? "لا يوجد" : value.Value.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
}
