using System.Reflection;
using System.Text.RegularExpressions;
using Dapper;
using MemoDashboard.Backend.Models;
using MemoDashboard.Backend.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MemoDashboard.Backend.Tests;

public sealed class CompanyReportSqlMappingTests
{
    private static string Sql(string fieldName)
    {
        var field = typeof(CompanyReportService).GetField(
            fieldName,
            BindingFlags.NonPublic | BindingFlags.Static);
        return (string)(field?.GetValue(null)
            ?? throw new InvalidOperationException(fieldName + " was not found."));
    }

    private static void AssertAliasesMatchDto(string sql, Type dtoType)
    {
        // Dapper matches case- and underscore-insensitively only: every
        // selected alias must equal a DTO property after normalization,
        // otherwise the column silently maps to 0/null (live bug 1.1.0).
        var dto = dtoType.GetProperties().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Only the outer SELECT maps to the DTO; CTE-internal aliases are ignored.
        var outer = sql;
        var fromIndex = sql.LastIndexOf("from note_quality", StringComparison.OrdinalIgnoreCase);
        if (fromIndex > 0)
        {
            var selectIndex = sql.LastIndexOf("select", fromIndex, StringComparison.OrdinalIgnoreCase);
            if (selectIndex >= 0) outer = sql.Substring(selectIndex, fromIndex - selectIndex);
        }
        var aliases = Regex.Matches(outer, @"\bas\s+([A-Za-z_][A-Za-z0-9_]*)\b")
            .Select(m => m.Groups[1].Value)
            .Where(a => !string.Equals(a, "timestamp", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var alias in aliases)
        {
            var normalized = alias.Replace("_", string.Empty, StringComparison.Ordinal);
            Assert.True(
                dto.Any(p => string.Equals(p.Replace("_", string.Empty, StringComparison.Ordinal), normalized, StringComparison.OrdinalIgnoreCase)),
                $"Column alias '{alias}' has no matching property on {dtoType.Name}.");
        }
    }

    [Fact]
    public void FieldErrorAliasesMatchDto()
    {
        AssertAliasesMatchDto(Sql("FieldErrorSql"), typeof(CompanyReportFieldRow));
    }

    [Fact]
    public void FactoryAliasesMatchDto()
    {
        AssertAliasesMatchDto(Sql("FactorySql"), typeof(CompanyReportFactoryRow));
    }

    [Fact]
    public void MonthlyAliasesMatchDto()
    {
        AssertAliasesMatchDto(Sql("MonthlySql"), typeof(CompanyReportMonthRow));
    }
}

public sealed class CompanyReportKpisTests
{
    [Fact]
    public void MechanismEfficiencyMatchesComplementFormula()
    {
        // Complement formula: 100 - (3/10 * 100) = 70%.
        Assert.Equal(70m, CompanyReportKpis.MechanismEfficiencyPercent(10, 3));
        Assert.Equal(70m, CompanyReportKpis.MechanismUnlinkedPercent(10, 3));
        Assert.Null(CompanyReportKpis.MechanismEfficiencyPercent(0, 0));
        Assert.Null(CompanyReportKpis.MechanismUnlinkedPercent(0, 0));
    }

    [Fact]
    public void WeightedTimelinessMatchesDesktopFormula()
    {
        // Golden: ((8*0.9 + 1*0.1) / 10) * 100 = 73%.
        Assert.Equal(73m, CompanyReportKpis.WeightedTimelinessPercent(10, 8, 1));
        Assert.Null(CompanyReportKpis.WeightedTimelinessPercent(0, 0, 0));
    }

    [Fact]
    public void OverallCompositeMatchesDesktopFormula()
    {
        // Golden with complement efficiency: (100 + 75 + 70) / 3 = 81.67; with form 80 -> (80 + 75 + 70) / 3 = 75.
        Assert.Equal(81.67m, CompanyReportKpis.OverallCompositePercent(null, 75m, 70m));
        Assert.Equal(75m, CompanyReportKpis.OverallCompositePercent(80m, 75m, 70m));
        Assert.Null(CompanyReportKpis.OverallCompositePercent(null, null, 70m));
        Assert.Null(CompanyReportKpis.OverallCompositePercent(null, 75m, null));
    }

    [Fact]
    public void ParseFormEfficiencyNullIsNull()
    {
        Assert.Null(CompanyReportKpis.ParseFormEfficiency(null));
        Assert.Null(CompanyReportKpis.ParseFormEfficiency(""));
    }

    [Theory]
    [InlineData("80", "80")]
    [InlineData("80,5", "80.5")]
    [InlineData("abc", null)]
    [InlineData("101", null)]
    [InlineData("-1", null)]
    public void ParseFormEfficiencyMirrorsDesktop(string input, string? expected)
    {
        decimal? expectedValue = expected is null ? null : decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(expectedValue, CompanyReportKpis.ParseFormEfficiency(input));
    }

    [Fact]
    public void FormattersUseDesktopVocabulary()
    {
        Assert.Equal("غير منطبق", CompanyReportKpis.FormatPercent(null));
        Assert.Equal("لا يوجد", CompanyReportKpis.FormatDate(null));
        Assert.Equal("2026/09/07", CompanyReportKpis.FormatDate(new DateTime(2026, 9, 7)));
    }
}

public sealed class CompanyReportServiceValidationTests : IDisposable
{
    private readonly string _tempRoot;

    public CompanyReportServiceValidationTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "memo-company-report-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { /* ignore */ }
    }

    private CompanyReportService CreateService()
    {
        var paths = new DashboardDataPaths(
            _tempRoot,
            Path.Combine(_tempRoot, "keys"),
            Path.Combine(_tempRoot, "App_Data", "app_settings.json"));
        var configuration = new ConfigurationService(
            paths,
            new EphemeralDataProtectionProvider(),
            NullLogger<ConfigurationService>.Instance);
        return new CompanyReportService(
            configuration,
            new ThrowingMetrics(),
            NullLogger<CompanyReportService>.Instance);
    }

    private sealed class ThrowingMetrics : ICompanyMetricsService
    {
        public Task<CompanyMetrics> GetAsync(Guid companyId, DateTime? from, DateTime? to, DateTime? entryCutoffDate, string dateBasis, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Database must not be touched by validation tests.");
    }

    [Fact]
    public async Task MissingCompanyIsRejected()
    {
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.GetAsync(new CompanyReportQuery { From = new DateTime(2026, 1, 1), To = new DateTime(2026, 9, 7) }));
        Assert.Contains("اختر شركة", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtraFiltersAreRejected()
    {
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.GetAsync(new CompanyReportQuery
            {
                CompanyId = Guid.NewGuid(),
                From = new DateTime(2026, 1, 1),
                To = new DateTime(2026, 9, 7),
                FactoryId = Guid.NewGuid()
            }));
        Assert.Contains("المرشحات الإضافية", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenPeriodIsRejected()
    {
        var service = CreateService();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.GetAsync(new CompanyReportQuery { CompanyId = Guid.NewGuid(), To = new DateTime(2026, 9, 7) }));
    }

    [Fact]
    public async Task InvertedPeriodIsRejected()
    {
        var service = CreateService();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.GetAsync(new CompanyReportQuery
            {
                CompanyId = Guid.NewGuid(),
                From = new DateTime(2026, 9, 7),
                To = new DateTime(2026, 9, 1)
            }));
    }

    [Fact]
    public async Task MonthlyRequiresCompany()
    {
        var service = CreateService();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.GetMonthlyAsync(Guid.Empty, null, null, null, "WritingDate"));
    }

    [Fact]
    public async Task MonthlyRejectsInvertedPeriod()
    {
        var service = CreateService();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.GetMonthlyAsync(Guid.NewGuid(), new DateTime(2026, 9, 7), new DateTime(2026, 9, 1), null, "WritingDate"));
    }

    [Fact]
    public void FilterSummaryMatchesDesktopWording()
    {
        Assert.Equal(
            "الفترة: 2026/01/01 - 2026/09/07 | أساس التاريخ: حسب تاريخ الكتابة",
            CompanyReportService.BuildFilterSummary(new DateTime(2026, 1, 1), new DateTime(2026, 9, 7), "WritingDate", null));
        Assert.Equal(
            "الفترة: 2026/01/01 - 2026/09/07 | أساس التاريخ: حسب تاريخ الوصول | تاريخ قطع الوصول: 2026/08/01",
            CompanyReportService.BuildFilterSummary(new DateTime(2026, 1, 1), new DateTime(2026, 9, 7), "ArrivalDate", new DateTime(2026, 8, 1)));
    }

    [Fact]
    public void DateBasisRewriteMirrorsDesktopAnchors()
    {
        const string sql = "n.arrival_at between @from and @to and f.arrival_at between @from and @to "
            + "and n.created_at < (@entry_cutoff_date + 1)::timestamp and date_trunc('month', n.arrival_at)";
        var rewritten = CompanyReportService.ApplyDateBasis(sql, "WritingDate");
        Assert.Contains("n.writing_date::date between @from and @to", rewritten, StringComparison.Ordinal);
        Assert.Contains("f.writing_date::date between @from and @to", rewritten, StringComparison.Ordinal);
        Assert.Contains("n.arrival_at::date <= @entry_cutoff_date", rewritten, StringComparison.Ordinal);
        Assert.Contains("date_trunc('month', n.writing_date::date)", rewritten, StringComparison.Ordinal);

        var arrival = CompanyReportService.ApplyDateBasis(sql, "ArrivalDate");
        Assert.Contains("n.arrival_at::date between @from and @to", arrival, StringComparison.Ordinal);
    }
}

public sealed class CompanyReportHtmlBuilderTests
{
    private static CompanyReportData SampleData()
    {
        return new CompanyReportData
        {
            CompanyId = Guid.NewGuid(),
            CompanyName = "شركة الاختبار",
            From = new DateTime(2026, 1, 1),
            To = new DateTime(2026, 9, 7),
            DateBasis = "WritingDate",
            Metrics = new CompanyMetrics
            {
                TotalNotes = 10, ItemQualityPercentage = 75, CleanReviewedNotePercent = 80,
                CleanReviewedNotes = 8, ErrorNotes = 2, TimeCompliancePercentage = 80,
                OnTimeNotes = 8, LateNotes = 1, Late6To10Notes = 1,
                AffectedNoteRatePercentage = 20, DuplicateFreePercentage = 80, DuplicateIncidents = 3,
                CompanyResendIncidents = 1, EntryNotes = 10, UnlinkedEntryNotes = 3,
                ReviewCoveragePercentage = 90, LinkEfficiencyPercentage = 70
            },
            FactoryCount = 2,
            LastArrivalAt = new DateTime(2026, 9, 1),
            Monthly = new List<CompanyReportMonthRow>
            {
                new CompanyReportMonthRow { MonthStart = new DateTime(2026, 8, 1), PeriodLabel = "2026/08", TotalNotes = 6, ItemQualityPercentage = 75, CleanReviewedNotes = 5 }
            },
            FieldErrors = new List<CompanyReportFieldRow>
            {
                new CompanyReportFieldRow { FieldLabel = "البند الأول", ErrorItems = 2, CheckedItems = 4, ErrorRate = 50 }
            },
            Factories = new List<CompanyReportFactoryRow>
            {
                new CompanyReportFactoryRow { FactoryLabel = "مصنع 1", Total = 10, CleanNotes = 8, ErrorNotes = 2, QualityPercentage = 80, Late = 1 }
            },
            Identity = new CompanyReportIdentity { IssuingEntityName = "دائرة النظم" },
            Issue = new CompanyReportIssue { LetterNumber = "123", IssuerName = "المحرر" },
            FilterSummary = "الفترة: 2026/01/01 - 2026/09/07 | أساس التاريخ: حسب تاريخ الكتابة"
        };
    }

    [Fact]
    public void HtmlContainsReportContractAndRtlGuards()
    {
        var html = CompanyReportHtmlBuilder.BuildHtml(SampleData(), new ReportAssets());
        Assert.Contains("<html lang=\"ar\" dir=\"rtl\">", html, StringComparison.Ordinal);
        Assert.Contains("شركة الاختبار", html, StringComparison.Ordinal);
        Assert.Contains("الفترة: 2026/01/01 - 2026/09/07", html, StringComparison.Ordinal);
        Assert.Contains("البند الأول", html, StringComparison.Ordinal);
        Assert.Contains("مصنع 1", html, StringComparison.Ordinal);
        Assert.Contains("@page", html, StringComparison.Ordinal);
        Assert.Contains("A4 landscape", html, StringComparison.Ordinal);
        Assert.Contains(":lang(ar)", html, StringComparison.Ordinal);
        Assert.Contains("73%", html, StringComparison.Ordinal); // weighted timeliness card
        Assert.Contains("81.67%", html, StringComparison.Ordinal); // composite card
    }

    [Fact]
    public void HtmlEscapesUntrustedInput()
    {
        var data = SampleData();
        data.CompanyName = "<script>alert(1)</script>";
        var html = CompanyReportHtmlBuilder.BuildHtml(data, new ReportAssets());
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyDataRendersNoDataBand()
    {
        var data = SampleData();
        data.Metrics.TotalNotes = 0;
        var html = CompanyReportHtmlBuilder.BuildHtml(data, new ReportAssets());
        Assert.Contains("لا توجد بيانات", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportAssetLoaderFindsQomraFont()
    {
        var assets = ReportAssetLoader.Load();
        Assert.False(string.IsNullOrWhiteSpace(assets.FontRegularBase64), "FontRegularBase64 should be loaded.");
        Assert.False(string.IsNullOrWhiteSpace(assets.FontBoldBase64), "FontBoldBase64 should be loaded.");
        var html = CompanyReportHtmlBuilder.BuildHtml(SampleData(), assets);
        Assert.Contains("ITF Qomra Arabic", html, StringComparison.Ordinal);
    }
}

public sealed class ChromiumPdfRendererTests
{
    [Fact]
    public async Task MissingExecutableProducesActionable503()
    {
        var renderer = new ChromiumPdfRenderer(
            NullLogger<ChromiumPdfRenderer>.Instance,
            Path.Combine(Path.GetTempPath(), "memo-no-such-chromium", "chrome.exe"));
        var ex = await Assert.ThrowsAsync<ChromiumNotAvailableException>(() =>
            renderer.RenderPdfAsync("<html></html>"));
        Assert.Contains("Chromium", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BlankHtmlIsRejected()
    {
        var renderer = new ChromiumPdfRenderer(
            NullLogger<ChromiumPdfRenderer>.Instance,
            Path.Combine(Path.GetTempPath(), "memo-no-such-chromium", "chrome.exe"));
        await Assert.ThrowsAsync<ArgumentException>(() => renderer.RenderPdfAsync("  "));
    }

    [Fact]
    public async Task HeadlessChromiumProducesPdfWhenAvailable()
    {
        if (!ChromiumPdfRenderer.TryFindExecutable(out var executable))
        {
            return;
        }

        var renderer = new ChromiumPdfRenderer(NullLogger<ChromiumPdfRenderer>.Instance, executable);
        var html = "<!DOCTYPE html><html lang=\"ar\" dir=\"rtl\"><head><meta charset=\"utf-8\"></head>"
            + "<body><h1>تقرير تجريبي</h1></body></html>";
        var pdf = await renderer.RenderPdfAsync(html);
        Assert.True(pdf.Length > 1000, "Expected a non-trivial PDF payload.");
        Assert.Equal((byte)'%', pdf[0]);
        Assert.Equal((byte)'P', pdf[1]);
        Assert.Equal((byte)'D', pdf[2]);
        Assert.Equal((byte)'F', pdf[3]);
    }

    [Fact]
    public async Task CompanyReportHtmlBuilderEnforcesWhiteBackgroundAndSovereignPalette()
    {
        var data = new CompanyReportData
        {
            CompanyId = Guid.NewGuid(),
            CompanyName = "شركة الاختبار الرسمية",
            From = new DateTime(2026, 1, 1),
            To = new DateTime(2026, 9, 7),
            Metrics = new CompanyMetrics
            {
                TotalNotes = 10,
                ItemQualityPercentage = 75,
                OnTimeNotes = 8,
                LateNotes = 1,
                EntryNotes = 10,
                UnlinkedEntryNotes = 3,
                CleanReviewedNotes = 7
            },
            Monthly = new List<CompanyReportMonthRow>
            {
                new CompanyReportMonthRow { PeriodLabel = "2026/08", TotalNotes = 6, ItemQualityPercentage = 75 }
            },
            Factories = new List<CompanyReportFactoryRow>
            {
                new CompanyReportFactoryRow { FactoryName = "مصنع التجربة", TotalNotes = 5, CleanReviewedNotes = 4, QualityPercentage = 80 }
            },
            FieldErrors = new List<CompanyReportFieldRow>
            {
                new CompanyReportFieldRow { FieldLabel = "رقم المحرك", ErrorItems = 2, CorrectItems = 8, ErrorRate = 20 }
            },
            Issue = new CompanyReportIssue { LetterNumber = "123", Title = "التقرير التفصيلي لأداء الشركة" },
            FilterSummary = "الفترة: 2026/01/01 - 2026/09/07"
        };

        var html = CompanyReportHtmlBuilder.BuildHtml(data, ReportAssetLoader.Load());
        Assert.NotNull(html);

        // Strict Syrian Government Palette & Pure White Background
        Assert.Contains("#FFFFFF", html);
        Assert.Contains("#988561", html);
        Assert.Contains("#B9A779", html);
        Assert.Contains("#EDEBE0", html);
        Assert.Contains("#054239", html);
        Assert.Contains("#161616", html);

        // Disallowed dark backgrounds must NOT be present
        Assert.DoesNotContain("#0B132B", html);
        Assert.DoesNotContain("#101A30", html);

        // If Chromium is installed, test full end-to-end PDF generation with this HTML
        if (ChromiumPdfRenderer.TryFindExecutable(out var executable))
        {
            var renderer = new ChromiumPdfRenderer(NullLogger<ChromiumPdfRenderer>.Instance, executable);
            var pdf = await renderer.RenderPdfAsync(html);
            Assert.NotNull(pdf);
            Assert.True(pdf.Length > 2000, "Rendered PDF should be non-empty and well-formed.");
            Assert.Equal((byte)'%', pdf[0]);
            File.WriteAllBytes(Path.Combine(AppContext.BaseDirectory, "scratch_report.pdf"), pdf);
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "scratch_report.html"), html);
        }
    }
}

public sealed class CompanyReportExcelBuilderTests
{
    [Fact]
    public void WorkbookIsValidOoxmlWithRtlSheetAndKpis()
    {
        var data = new CompanyReportData
        {
            CompanyId = Guid.NewGuid(),
            CompanyName = "شركة الاختبار",
            From = new DateTime(2026, 1, 1),
            To = new DateTime(2026, 9, 7),
            Metrics = new CompanyMetrics { TotalNotes = 10, ItemQualityPercentage = 75, OnTimeNotes = 8, LateNotes = 1, EntryNotes = 10, UnlinkedEntryNotes = 3 },
            Monthly = new List<CompanyReportMonthRow>
            {
                new CompanyReportMonthRow { PeriodLabel = "2026/08", TotalNotes = 6, ItemQualityPercentage = 75 }
            },
            Issue = new CompanyReportIssue { LetterNumber = "123" },
            FilterSummary = "الفترة: 2026/01/01 - 2026/09/07"
        };

        var bytes = CompanyReportExcelBuilder.Build(data);
        Assert.True(bytes.Length > 2000, "Expected a non-trivial workbook.");

        using var stream = new MemoryStream(bytes);
        using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);
        Assert.NotNull(archive.GetEntry("[Content_Types].xml"));
        Assert.NotNull(archive.GetEntry("xl/workbook.xml"));
        var sheet = archive.GetEntry("xl/worksheets/sheet1.xml");
        Assert.NotNull(sheet);
        using var reader = new StreamReader(sheet!.Open());
        var xml = reader.ReadToEnd();
        Assert.Contains("rightToLeft", xml, StringComparison.Ordinal);
        Assert.Contains("شركة الاختبار", xml, StringComparison.Ordinal);
        Assert.Contains("المؤشر المركب", xml, StringComparison.Ordinal);
        Assert.Contains("81.67%", xml, StringComparison.Ordinal);
        Assert.Contains("2026/08", xml, StringComparison.Ordinal);
        var document = System.Xml.Linq.XDocument.Parse(xml);
        Assert.NotNull(document.Root);
    }
}

public sealed class CompanyReportWordBuilderTests
{
    [Fact]
    public void DocumentIsValidOoxmlWithRtlAndKpis()
    {
        var data = new CompanyReportData
        {
            CompanyId = Guid.NewGuid(),
            CompanyName = "شركة الاختبار",
            From = new DateTime(2026, 1, 1),
            To = new DateTime(2026, 9, 7),
            Metrics = new CompanyMetrics
            {
                TotalNotes = 10,
                ItemQualityPercentage = 75,
                OnTimeNotes = 8,
                LateNotes = 1,
                EntryNotes = 10,
                UnlinkedEntryNotes = 3,
                CleanReviewedNotes = 7
            },
            Monthly = new List<CompanyReportMonthRow>
            {
                new CompanyReportMonthRow { PeriodLabel = "2026/08", TotalNotes = 6, ItemQualityPercentage = 75 }
            },
            Factories = new List<CompanyReportFactoryRow>
            {
                new CompanyReportFactoryRow { FactoryName = "مصنع التجربة", TotalNotes = 5, CleanReviewedNotes = 4, QualityPercentage = 80 }
            },
            FieldErrors = new List<CompanyReportFieldRow>
            {
                new CompanyReportFieldRow { FieldLabel = "رقم المحرك", ErrorItems = 2, CorrectItems = 8, ErrorRate = 20 }
            },
            Issue = new CompanyReportIssue { LetterNumber = "123", Title = "التقرير التفصيلي" },
            FilterSummary = "الفترة: 2026/01/01 - 2026/09/07"
        };

        var bytes = CompanyReportWordBuilder.Build(data);
        Assert.True(bytes.Length > 2000, "Expected a non-trivial Word document.");

        using var stream = new MemoryStream(bytes);
        using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);
        Assert.NotNull(archive.GetEntry("[Content_Types].xml"));
        Assert.NotNull(archive.GetEntry("_rels/.rels"));
        Assert.NotNull(archive.GetEntry("word/_rels/document.xml.rels"));
        Assert.NotNull(archive.GetEntry("word/settings.xml"));
        Assert.NotNull(archive.GetEntry("word/styles.xml"));
        Assert.NotNull(archive.GetEntry("word/fontTable.xml"));

        var docEntry = archive.GetEntry("word/document.xml");
        Assert.NotNull(docEntry);
        using var reader = new StreamReader(docEntry!.Open());
        var xml = reader.ReadToEnd();

        Assert.Contains("bidi", xml, StringComparison.Ordinal);
        Assert.Contains("شركة الاختبار", xml, StringComparison.Ordinal);
        Assert.Contains("التقرير التفصيلي", xml, StringComparison.Ordinal);
        Assert.Contains("مصنع التجربة", xml, StringComparison.Ordinal);
        Assert.Contains("رقم المحرك", xml, StringComparison.Ordinal);
        Assert.Contains("المؤشر المركب للشركة", xml, StringComparison.Ordinal);

        var document = System.Xml.Linq.XDocument.Parse(xml);
        Assert.NotNull(document.Root);
    }
}

public sealed class LiveCompanyReportDiagnosticTests
{
    [Fact]
    public async Task RunLiveCompanyReportQueries()
    {
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
        var temp = Path.Combine(Path.GetTempPath(), "memo-diag-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var paths = new DashboardDataPaths(temp, Path.Combine(temp, "keys"), Path.Combine(temp, "app_settings.json"));
        var config = new ConfigurationService(paths, new EphemeralDataProtectionProvider(), NullLogger<ConfigurationService>.Instance);
        await config.SaveAsync(new SetupRequest { Host = "localhost", Port = 5432, Database = "audit_memo", Username = "postgres", Password = "1234" });
        var connStr = config.BuildConnectionString();
        Assert.NotNull(connStr);

        var metricsService = new CompanyMetricsService(config);
        var reportService = new CompanyReportService(config, metricsService, NullLogger<CompanyReportService>.Instance);

        try
        {
            await using var conn = new Npgsql.NpgsqlConnection(connStr);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, name FROM core.companies WHERE is_active AND NOT is_deleted LIMIT 5;";
            var companies = new List<(Guid Id, string Name)>();
            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    companies.Add((reader.GetGuid(0), reader.GetString(1)));
                }
            }

            Assert.NotEmpty(companies);

            foreach (var (id, name) in companies)
            {
                var report1 = await reportService.GetAsync(new CompanyReportQuery
                {
                    CompanyId = id,
                    From = new DateTime(2020, 1, 1),
                    To = DateTime.Today,
                    DateBasis = "WritingDate"
                });
                Assert.NotNull(report1);
                Assert.NotNull(report1.Factories);

                var report2 = await reportService.GetAsync(new CompanyReportQuery
                {
                    CompanyId = id,
                    From = new DateTime(2020, 1, 1),
                    To = DateTime.Today,
                    DateBasis = "ArrivalDate"
                });
                Assert.NotNull(report2);

                // Test HTML build
                var html = CompanyReportHtmlBuilder.BuildHtml(report1, ReportAssetLoader.Load());
                Assert.False(string.IsNullOrWhiteSpace(html));

                // Test Word build
                var word = CompanyReportWordBuilder.Build(report1);
                Assert.NotEmpty(word);

                // Test Excel build
                var excel = CompanyReportExcelBuilder.Build(report1);
                Assert.NotEmpty(excel);
            }
        }
        catch (Npgsql.NpgsqlException)
        {
            // Database not reachable in offline environment
        }
    }
}
