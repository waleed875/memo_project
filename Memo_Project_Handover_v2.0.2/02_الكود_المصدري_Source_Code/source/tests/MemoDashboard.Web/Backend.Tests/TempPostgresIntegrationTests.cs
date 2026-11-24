using System.Data;
using System.Reflection;
using System.Text.Json;
using Dapper;
using MemoDashboard.Backend.Models;
using MemoDashboard.Backend.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace MemoDashboard.Backend.Tests;

public class TempPostgresIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public TempPostgresIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }
    [Fact]
    public async Task ExportCleanMasterDataSql()
    {
        var csb = new NpgsqlConnectionStringBuilder(GetConnectionString());
        var exportPath1 = @"d:\Memo\Memo 2.0.0\source\database\clean_seed\database_clean_master_data.sql";
        var exportPath2 = @"d:\Memo\Memo 2.0.0\export\database_clean_master_data.sql";

        var pgDump = @"C:\Program Files\PostgreSQL\18\bin\pg_dump.exe";
        if (!File.Exists(pgDump))
        {
            throw new FileNotFoundException("pg_dump not found", pgDump);
        }

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = pgDump,
            Arguments = string.Join(" ", new[]
            {
                $"--host=\"{csb.Host}\"",
                $"--port={csb.Port}",
                $"--username=\"{csb.Username}\"",
                "--no-owner",
                "--no-privileges",
                "--exclude-table-data=\"core.note_attachments\"",
                "--exclude-table-data=\"core.note_create_operations\"",
                "--exclude-table-data=\"core.note_field_values\"",
                "--exclude-table-data=\"core.note_links\"",
                "--exclude-table-data=\"core.note_review_history\"",
                "--exclude-table-data=\"core.note_source_exit_refs\"",
                "--exclude-table-data=\"core.note_source_invoices\"",
                "--exclude-table-data=\"core.note_source_loot\"",
                "--exclude-table-data=\"core.note_sources\"",
                "--exclude-table-data=\"core.notes\"",
                "--exclude-table-data=\"core.duplicate_incidents\"",
                "--exclude-table-data=\"core.duplicate_incidents_archive\"",
                "--exclude-table-data=\"core.audit_logs\"",
                "--exclude-table-data=\"system.backup_history\"",
                "--exclude-table-data=\"audit.logs\"",
                "--exclude-table-data=\"updates.audit_log\"",
                "--exclude-table-data=\"updates.installations\"",
                "--exclude-table-data=\"updates.artifacts\"",
                $"--file=\"{exportPath1}\"",
                csb.Database
            }),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.Environment["PGPASSWORD"] = csb.Password;

        using var process = System.Diagnostics.Process.Start(startInfo)!;
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        _output.WriteLine($"pg_dump exit code: {process.ExitCode}");
        if (!string.IsNullOrWhiteSpace(stderr)) _output.WriteLine($"stderr: {stderr}");
        Assert.Equal(0, process.ExitCode);

        // Copy to export directory
        Directory.CreateDirectory(Path.GetDirectoryName(exportPath2)!);
        File.Copy(exportPath1, exportPath2, overwrite: true);

        var fi = new FileInfo(exportPath1);
        _output.WriteLine($"Generated {exportPath1}: {fi.Length} bytes");
    }

    private static bool UsesExplicitIntegrationConnection =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AUDIT_MEMO_DASHBOARD_INTEGRATION_CONNECTION"));

    private static string GetConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable("AUDIT_MEMO_DASHBOARD_INTEGRATION_CONNECTION");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        // Test-only fallback: reuse the same DPAPI-protected runtime configuration
        // used by Run/F5. The decrypted connection string remains in process memory
        // and is never written to evidence, source or logs.
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AuditMemoSystem",
            "MemoDashboard");
        var paths = new DashboardDataPaths(
            root,
            Path.Combine(root, "keys"),
            Path.Combine(root, "app_settings.json"));
        var protection = DataProtectionProvider.Create(
            new DirectoryInfo(paths.KeysDirectory),
            options =>
            {
                options.SetApplicationName("MemoDashboard");
                if (OperatingSystem.IsWindows()) options.ProtectKeysWithDpapi();
            });
        var configuration = new ConfigurationService(
            paths,
            protection,
            NullLogger<ConfigurationService>.Instance);
        connectionString = configuration.BuildConnectionString();
        Assert.False(
            string.IsNullOrWhiteSpace(connectionString),
            "No integration connection was provided and the protected Run/F5 configuration was unavailable.");
        return connectionString!;
    }
    private static ConfigurationService CreateProtectedConfiguration()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AuditMemoSystem",
            "MemoDashboard");
        var paths = new DashboardDataPaths(
            root,
            Path.Combine(root, "keys"),
            Path.Combine(root, "app_settings.json"));
        var protection = DataProtectionProvider.Create(
            new DirectoryInfo(paths.KeysDirectory),
            options =>
            {
                options.SetApplicationName("MemoDashboard");
                if (OperatingSystem.IsWindows()) options.ProtectKeysWithDpapi();
            });
        return new ConfigurationService(
            paths,
            protection,
            NullLogger<ConfigurationService>.Instance);
    }

    [Fact]
    public async Task ConfiguredDatabaseNumericAuditProducesSanitizedEvidence()
    {
        var periodTo = new DateTime(2026, 8, 3);
        await using var connection = new NpgsqlConnection(GetConnectionString());
        await connection.OpenAsync();

        var parameters = new DynamicParameters();
        parameters.Add("From", (DateTime?)null, DbType.Date);
        parameters.Add("To", periodTo, DbType.Date);
        parameters.Add("CompanyId", (Guid?)null, DbType.Guid);
        parameters.Add("FactoryId", (Guid?)null, DbType.Guid);
        parameters.Add("ProductionLineId", (Guid?)null, DbType.Guid);
        parameters.Add("NoteTypeId", (Guid?)null, DbType.Guid);
        parameters.Add("Status", (string?)null, DbType.String);
        parameters.Add("EntryCutoffDate", (DateTime?)null, DbType.Date);
        parameters.Add("DateBasis", "ArrivalDate", DbType.String);
        parameters.Add("CurrentDate", periodTo, DbType.Date);
        parameters.Add("MinSample", 1, DbType.Int32);
        parameters.Add("PreviousCompanyId", (Guid?)null, DbType.Guid);
        parameters.Add("PreviousFrom", (DateTime?)null, DbType.Date);
        parameters.Add("PreviousTo", (DateTime?)null, DbType.Date);
        parameters.Add("PreviousFactoryId", (Guid?)null, DbType.Guid);
        parameters.Add("PreviousProductionLineId", (Guid?)null, DbType.Guid);
        parameters.Add("PreviousNoteTypeId", (Guid?)null, DbType.Guid);
        parameters.Add("PreviousStatus", (string?)null, DbType.String);
        parameters.Add("PreviousEntryCutoffDate", (DateTime?)null, DbType.Date);

        using var multi = await connection.QueryMultipleAsync(SnapshotSql(), parameters, commandTimeout: 30);
        var sqlSummary = await multi.ReadSingleAsync<DashboardSummary>();
        var sqlCompanyCounts = await multi.ReadSingleAsync<CompanyCountsRow>();
        var sqlDuplicates = (await multi.ReadAsync<DuplicateRow>()).ToList();
        var sqlCompanies = (await multi.ReadAsync<CompanyRow>()).ToList();
        var sqlTimeliness = (await multi.ReadAsync<PerfRow>()).ToList();
        var sqlMonthly = (await multi.ReadAsync<MonthlyRow>()).ToList();
        var sqlStatuses = (await multi.ReadAsync<StatusRow>()).ToList();
        _ = await multi.ReadAsync<RecentRow>();
        var sqlSla = await multi.ReadSingleAsync<SlaRow>();
        var sqlBacklog = (await multi.ReadAsync<BacklogRow>()).ToList();
        var sqlIncoming = (await multi.ReadAsync<IncomingRow>()).ToList();
        var sqlQuality = (await multi.ReadAsync<QualityRow>()).ToList();
        var sqlFieldQuality = (await multi.ReadAsync<FieldQualityRow>()).ToList();
        var sqlTopFields = (await multi.ReadAsync<TopFieldRow>()).ToList();
        var sqlStates = (await multi.ReadAsync<IndicatorRow>()).ToList();
        var sqlFactories = (await multi.ReadAsync<PerfRow>()).ToList();
        var sqlLines = (await multi.ReadAsync<PerfRow>()).ToList();
        var sqlTrends = (await multi.ReadAsync<TrendRow>()).ToList();
        var sqlCompleteness = await multi.ReadSingleAsync<CompletenessRow>();
        var sqlCompanyMetrics = await multi.ReadSingleOrDefaultAsync<CompanyMetrics>();
        _ = await multi.ReadSingleOrDefaultAsync<CompanyMetrics>();

        var service = new DashboardQueryService(
            CreateProtectedConfiguration(),
            NullLogger<DashboardQueryService>.Instance);
        var apiSnapshot = await service.GetSnapshotAsync(new FilterQuery { To = periodTo, DateBasis = "ArrivalDate" }, minSample: 1);

        Assert.True(apiSnapshot.IsHealthy);
        Assert.Equal(sqlSummary.TotalNotes, apiSnapshot.Summary.TotalNotes);
        Assert.Equal(sqlSummary.Approved, apiSnapshot.Summary.Approved);
        Assert.Equal(sqlSummary.PendingReview, apiSnapshot.Summary.PendingReview);
        Assert.Equal(sqlSummary.LateTotal, apiSnapshot.Summary.LateTotal);
        Assert.Equal(sqlCompanyCounts.ActiveCompaniesTotal, apiSnapshot.Summary.ActiveCompaniesTotal);
        Assert.Equal(sqlCompanies.Count, apiSnapshot.Companies.Count);
        Assert.Equal(sqlTimeliness.Count, apiSnapshot.TimelinessByCompany.Count);
        Assert.Equal(sqlTimeliness.Select(item => item.Label).OrderBy(item => item), apiSnapshot.TimelinessByCompany.Select(item => item.Label).OrderBy(item => item));
        foreach (var sqlRow in sqlTimeliness)
        {
            var apiRow = apiSnapshot.TimelinessByCompany.Single(item => item.Label == sqlRow.Label);
            Assert.Equal(sqlRow.TotalNotes, apiRow.TotalNotes);
            Assert.Equal(sqlRow.EligibleCount, apiRow.EligibleCount);
            Assert.Equal(sqlRow.OnTimeCount, apiRow.OnTimeCount);
            Assert.Equal(sqlRow.LateCount, apiRow.LateCount);
        }
        foreach (var sqlRow in sqlMonthly)
        {
            var apiRow = apiSnapshot.MonthlyActivity.Single(item => item.Month == sqlRow.MonthStart.ToString("yyyy-MM"));
            Assert.Equal(sqlRow.Count, apiRow.Count);
        }
        foreach (var sqlRow in sqlStatuses)
        {
            var apiRow = apiSnapshot.StatusDistribution.Single(item => item.Status == sqlRow.Status);
            Assert.Equal(sqlRow.Count, apiRow.Count);
        }
        foreach (var sqlRow in sqlDuplicates)
        {
            var apiRow = apiSnapshot.DuplicateBreakdown.Single(item => item.Classification == sqlRow.Classification);
            Assert.Equal(sqlRow.Count, apiRow.Count);
        }
        foreach (var sqlRow in sqlIncoming)
        {
            var apiRow = apiSnapshot.IncomingVsApproved.Single(item => item.PeriodKey == sqlRow.Label.ToString("yyyy-MM"));
            Assert.Equal(sqlRow.Incoming, apiRow.Incoming);
            Assert.Equal(sqlRow.Approved, apiRow.Approved);
        }
        foreach (var sqlRow in sqlFactories)
        {
            var apiRow = apiSnapshot.PerformanceByFactory.Single(item => item.Label == sqlRow.Label);
            Assert.Equal(sqlRow.TotalNotes, apiRow.TotalNotes);
            Assert.Equal(sqlRow.OnTimeCount, apiRow.OnTimeCount);
            Assert.Equal(sqlRow.LateCount, apiRow.LateCount);
        }
        foreach (var sqlRow in sqlLines)
        {
            var apiRow = apiSnapshot.PerformanceByLine.Single(item => item.Label == sqlRow.Label);
            Assert.Equal(sqlRow.TotalNotes, apiRow.TotalNotes);
            Assert.Equal(sqlRow.OnTimeCount, apiRow.OnTimeCount);
            Assert.Equal(sqlRow.LateCount, apiRow.LateCount);
        }
        foreach (var sqlRow in sqlQuality)
        {
            var apiRow = apiSnapshot.QualityByCompany.Single(item => item.CompanyName == sqlRow.CompanyName);
            Assert.Equal(sqlRow.SampleSize, apiRow.SampleSize);
            Assert.Equal(sqlRow.CorrectCount, apiRow.CorrectCount);
            Assert.Equal(sqlRow.ErrorCount, apiRow.ErrorCount);
        }
        Assert.Equal(sqlDuplicates.Sum(item => item.Count), apiSnapshot.DuplicateBreakdown.Sum(item => item.Count));
        Assert.Equal(sqlTrends.Sum(item => item.CompanyResend), apiSnapshot.DuplicateTrend.Sum(item => item.CompanyResend));
        Assert.Equal(sqlTrends.Sum(item => item.DuplicateNumber), apiSnapshot.DuplicateTrend.Sum(item => item.DuplicateNumber));
        Assert.Equal(sqlMonthly.Sum(item => item.Count), apiSnapshot.MonthlyActivity.Sum(item => item.Count));
        Assert.Equal(sqlStatuses.Sum(item => item.Count), apiSnapshot.StatusDistribution.Sum(item => item.Count));
        Assert.Equal(sqlSla.EligibleNotes, apiSnapshot.Sla.EligibleNotes);
        Assert.Equal(sqlSla.OnTimeNotes, apiSnapshot.Sla.OnTimeNotes);
        Assert.Equal(sqlSla.LateNotes, apiSnapshot.Sla.LateNotes);
        Assert.Equal(sqlCompleteness.MissingWritingDate, apiSnapshot.DataCompleteness.Single(item => item.Code == "missing-writing-date").Value);
        Assert.Equal(sqlCompleteness.MissingFactory, apiSnapshot.DataCompleteness.Single(item => item.Code == "missing-factory").Value);
        Assert.Equal(sqlCompleteness.MissingProductionLine, apiSnapshot.DataCompleteness.Single(item => item.Code == "missing-production-line").Value);

        var zeroCompany = sqlCompanies.FirstOrDefault(item => item.NoteCount == 0)?.CompanyName;
        const string companyMetricSql = """
            SELECT
                total_notes AS "TotalNotes",
                expected_items AS "ExpectedItems",
                reviewed_items AS "ReviewedItems",
                applicable_reviewed_items AS "ApplicableReviewedItems",
                correct_items AS "CorrectItems",
                error_items AS "ErrorItems",
                review_coverage_percentage AS "ReviewCoveragePercentage",
                item_quality_percentage AS "ItemQualityPercentage",
                error_notes AS "ErrorNotes",
                time_eligible_notes AS "TimeEligibleNotes",
                on_time_notes AS "OnTimeNotes",
                late_notes AS "LateNotes",
                time_compliance_percentage AS "TimeCompliancePercentage",
                duplicate_number_incidents AS "DuplicateNumberIncidents",
                company_resend_incidents AS "CompanyResendIncidents",
                duplicate_incidents AS "DuplicateIncidents"
            FROM reporting.fn_company_metrics_snapshot_v3_filtered(
                @CompanyId, @From::date, @To::date, NULL, NULL, NULL, NULL, NULL);
            """;
        var activeCompany = apiSnapshot.Companies.First(item => item.NoteCount > 0);
        var zeroActivityCompany = apiSnapshot.Companies.First(item => item.NoteCount == 0);
        var companyFrom = new DateTime(2026, 7, 1);
        var activeCompanySql = await connection.QuerySingleAsync<CompanyMetrics>(
            companyMetricSql,
            new { CompanyId = activeCompany.Id, From = companyFrom, To = periodTo });
        var zeroCompanySql = await connection.QuerySingleAsync<CompanyMetrics>(
            companyMetricSql,
            new { CompanyId = zeroActivityCompany.Id, From = companyFrom, To = periodTo });
        var activeCompanyApi = await service.GetSnapshotAsync(
            new FilterQuery { CompanyId = activeCompany.Id, From = companyFrom, To = periodTo, DateBasis = "ArrivalDate" },
            minSample: 1);
        var zeroCompanyApi = await service.GetSnapshotAsync(
            new FilterQuery { CompanyId = zeroActivityCompany.Id, From = companyFrom, To = periodTo, DateBasis = "ArrivalDate" },
            minSample: 1);
        Assert.Equal(activeCompanySql.TotalNotes, activeCompanyApi.CompanyMetrics?.TotalNotes);
        Assert.Equal(activeCompanySql.ItemQualityPercentage, activeCompanyApi.CompanyMetrics?.ItemQualityPercentage);
        Assert.Equal(activeCompanySql.TimeCompliancePercentage, activeCompanyApi.CompanyMetrics?.TimeCompliancePercentage);
        Assert.Equal(0, zeroCompanySql.TotalNotes);
        Assert.Equal(0, zeroCompanyApi.Summary.TotalNotes);
        Assert.False(zeroCompanyApi.Views.CompanySummary?.Availability.HasRows ?? true);
        Assert.Equal(activeCompanyApi.CompanyMetrics?.TotalNotes, activeCompanyApi.Views.CompanySummary?.Metrics?.TotalNotes);
        Assert.Equal(activeCompanyApi.PreviousCompanyMetrics?.TotalNotes, activeCompanyApi.Views.CompanySummary?.PreviousMetrics?.TotalNotes);
        // The service auto-derives the previous window from From/To/CompanyId exactly like
        // DashboardQueryService (period length mirrored backwards), so the expectation must be
        // computed from SQL for the same window instead of assuming an empty history.
        var periodDays = (periodTo.Date - companyFrom.Date).Days + 1;
        var expectedPreviousTo = companyFrom.Date.AddDays(-1);
        var expectedPreviousFrom = expectedPreviousTo.AddDays(-periodDays + 1);
        var expectedPreviousSql = await connection.QuerySingleAsync<CompanyMetrics>(
            companyMetricSql,
            new { CompanyId = activeCompany.Id, From = expectedPreviousFrom, To = expectedPreviousTo });
        Assert.Equal(expectedPreviousSql.TotalNotes, activeCompanyApi.PreviousCompanyMetrics?.TotalNotes);
        var comparison = activeCompanyApi.Views.CompanySummary?.Comparison;
        var currentTotal = activeCompanyApi.CompanyMetrics?.TotalNotes ?? 0;
        if (expectedPreviousSql.TotalNotes <= 0 || currentTotal <= 0)
        {
            Assert.Null(comparison);
        }
        else
        {
            Assert.NotNull(comparison);
            Assert.Equal(currentTotal - expectedPreviousSql.TotalNotes, comparison.TotalNotesDelta);
            Assert.Equal(
                DeltaPoints(activeCompanyApi.CompanyMetrics?.ItemQualityPercentage, expectedPreviousSql.ItemQualityPercentage),
                comparison.ItemQualityDeltaPoints);
            Assert.Equal(
                DeltaPoints(activeCompanyApi.CompanyMetrics?.TimeCompliancePercentage, expectedPreviousSql.TimeCompliancePercentage),
                comparison.TimeComplianceDeltaPoints);
        }
        Assert.Null(zeroCompanyApi.Views.CompanySummary?.Comparison);
        var output = new
        {
            source = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AUDIT_MEMO_DASHBOARD_INTEGRATION_CONNECTION"))
                ? "protected Run/F5 runtime configuration"
                : "process-scoped integration connection",
            period = new { from = (DateTime?)null, to = periodTo },
            directSql = new
            {
                summary = sqlSummary,
                companyCounts = sqlCompanyCounts,
                duplicateBreakdown = sqlDuplicates,
                companies = sqlCompanies,
                timelinessByCompany = sqlTimeliness,
                monthly = sqlMonthly,
                statuses = sqlStatuses,
                sla = sqlSla,
                backlog = sqlBacklog,
                incoming = sqlIncoming,
                quality = sqlQuality,
                fieldQuality = sqlFieldQuality,
                topFields = sqlTopFields,
                indicatorStates = sqlStates,
                factories = sqlFactories,
                lines = sqlLines,
                repetitionTrend = sqlTrends,
                completeness = sqlCompleteness,
                companyMetrics = sqlCompanyMetrics
            },
            companyReports = new
            {
                active = new { name = activeCompany.Name, directSql = activeCompanySql, api = activeCompanyApi.Views.CompanySummary },
                zeroActivity = new { name = zeroActivityCompany.Name, directSql = zeroCompanySql, api = zeroCompanyApi.Views.CompanySummary }
            },
            api = new
            {
                summary = apiSnapshot.Summary,
                companies = apiSnapshot.Companies,
                timelinessByCompany = apiSnapshot.TimelinessByCompany,
                monthly = apiSnapshot.MonthlyActivity,
                statuses = apiSnapshot.StatusDistribution,
                sla = apiSnapshot.Sla,
                backlog = apiSnapshot.BacklogByAge,
                incoming = apiSnapshot.IncomingVsApproved,
                quality = apiSnapshot.QualityByCompany,
                fieldQuality = apiSnapshot.FieldQuality,
                topFields = apiSnapshot.TopErrorFields,
                indicatorStates = apiSnapshot.IndicatorStateDistribution,
                factories = apiSnapshot.PerformanceByFactory,
                lines = apiSnapshot.PerformanceByLine,
                repetitionTrend = apiSnapshot.DuplicateTrend,
                completeness = apiSnapshot.DataCompleteness,
                companyMetrics = apiSnapshot.CompanyMetrics,
                views = apiSnapshot.Views
            },
            uiExpressions = new
            {
                executiveTotalNotes = apiSnapshot.Views.ExecutiveSummary.Summary.TotalNotes,
                executiveTimeliness = apiSnapshot.Views.ExecutiveSummary.Sla.OnTimeRate,
                executiveReviewCoverage = apiSnapshot.Views.ExecutiveSummary.Metrics?.ReviewCoveragePercentage,
                executiveQuality = apiSnapshot.Views.ExecutiveSummary.Metrics?.ItemQualityPercentage,
                executiveErrorNotes = apiSnapshot.Views.ExecutiveSummary.Metrics?.ErrorNotes,
                executivePending = apiSnapshot.Views.ExecutiveSummary.Summary.PendingReview,
                executiveLate = apiSnapshot.Views.ExecutiveSummary.Summary.LateTotal,
                executiveRepetition = apiSnapshot.Views.ExecutiveSummary.Summary.CompanyResend + apiSnapshot.Views.ExecutiveSummary.Summary.DuplicateNumbers,
                operationsIncoming = apiSnapshot.Views.OperationsSummary.IncomingVsApproved.Sum(item => item.Incoming),
                operationsApproved = apiSnapshot.Views.OperationsSummary.Summary.Approved,
                operationsIncompleteReview = apiSnapshot.Views.OperationsSummary.Metrics?.IncompleteReviewNotes,
                operationsRepetition = apiSnapshot.Views.OperationsSummary.Metrics?.DuplicateIncidents,
                operationsCompleteness = apiSnapshot.Views.OperationsSummary.DataCompleteness.Sum(item => item.Value),
                zeroActivityCompany = zeroCompany
            }
        };

        var outputPath = Environment.GetEnvironmentVariable("MEMO_DASHBOARD_NUMERIC_AUDIT_OUTPUT");
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
        }

        _output.WriteLine($"DASHBOARD_NUMERIC_AUDIT totalNotes={sqlSummary.TotalNotes}; companies={sqlCompanies.Count}; zeroActivityCompany={zeroCompany ?? "none"}; late={sqlSummary.LateTotal}; duplicates={sqlDuplicates.Sum(item => item.Count)}");
    }

    [Fact]
    public async Task DefaultDashboardRangeIncludesAllExistingNotes()
    {
        await using var connection = new NpgsqlConnection(GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "select count(*) from core.notes where is_deleted = false;", connection);
        var databaseCount = Convert.ToInt64(await command.ExecuteScalarAsync());

        var service = new DashboardQueryService(
            CreateProtectedConfiguration(),
            NullLogger<DashboardQueryService>.Instance);
        var snapshot = await service.GetSnapshotAsync(new FilterQuery(), minSample: 1);

        Assert.True(snapshot.IsHealthy);
        Assert.Null(snapshot.EffectiveFilters.From);
        Assert.Null(snapshot.EffectiveFilters.To);
        Assert.Equal(databaseCount, snapshot.Summary.TotalNotes);
    }
    [Fact]
    public async Task CurrentOperationalRangeMatchesIndependentPostgresCounts()
    {
        var operationalTo = GetOperationalDate();
        var historicalTo = new DateTime(2026, 8, 3);
        await using var connection = new NpgsqlConnection(GetConnectionString());
        await connection.OpenAsync();

        const string sql = """
            SELECT
                COUNT(*) AS "TotalNotes",
                COUNT(*) FILTER (WHERE n.status = 'APPROVED') AS "CurrentApproved",
                COUNT(*) FILTER (WHERE n.status IN ('NEEDS_REVIEW', 'UNDER_REVIEW', 'COMPLETE', 'HAS_ERRORS')) AS "PendingReview",
                COUNT(*) FILTER (WHERE n.status = 'RETURNED_FOR_CORRECTION') AS "ReturnedForCorrection",
                COUNT(*) FILTER (WHERE n.writing_date IS NOT NULL AND n.arrival_at >= n.writing_date
                                  AND (n.arrival_at::date - n.writing_date::date) BETWEEN 0 AND 5) AS "OnTime",
                COUNT(*) FILTER (WHERE n.writing_date IS NOT NULL AND n.arrival_at >= n.writing_date
                                  AND (n.arrival_at::date - n.writing_date::date) > 5) AS "LateTotal"
            FROM core.notes n
            WHERE n.is_deleted = FALSE AND n.arrival_at::date <= @To::date;

            SELECT c.id AS "CompanyId", c.name AS "CompanyName", COUNT(n.id) AS "NoteCount"
            FROM core.companies c
            LEFT JOIN core.notes n ON n.company_id = c.id AND n.is_deleted = FALSE
              AND n.arrival_at::date <= @To::date
            WHERE c.is_active = TRUE AND c.is_deleted = FALSE
            GROUP BY c.id, c.name ORDER BY c.name;

            SELECT n.status AS "Status", COUNT(*) AS "Count"
            FROM core.notes n
            WHERE n.is_deleted = FALSE AND n.arrival_at::date <= @To::date
            GROUP BY n.status ORDER BY n.status;

            SELECT
                COUNT(*) FILTER (WHERE n.writing_date IS NOT NULL AND n.arrival_at >= n.writing_date) AS "EligibleNotes",
                COUNT(*) FILTER (WHERE n.writing_date IS NOT NULL AND n.arrival_at >= n.writing_date
                                  AND (n.arrival_at::date - n.writing_date::date) BETWEEN 0 AND 5) AS "OnTimeNotes",
                COUNT(*) FILTER (WHERE n.writing_date IS NOT NULL AND n.arrival_at >= n.writing_date
                                  AND (n.arrival_at::date - n.writing_date::date) > 5) AS "LateNotes",
                COUNT(*) FILTER (WHERE n.writing_date IS NOT NULL AND n.arrival_at < n.writing_date) AS "InvalidDateOrderNotes"
            FROM core.notes n
            WHERE n.is_deleted = FALSE AND n.arrival_at::date <= @To::date;

            SELECT d.classification AS "Classification", COUNT(*) AS "Count"
            FROM core.duplicate_incidents d
            JOIN core.notes reference_note
              ON reference_note.id = CASE d.classification
                    WHEN 'COMPANY_DUPLICATE_NUMBER' THEN d.created_note_id
                    WHEN 'COMPANY_RESEND' THEN d.matched_note_id
                    ELSE NULL END
            WHERE reference_note.is_deleted = FALSE
              AND reference_note.arrival_at::date <= @To::date
            GROUP BY d.classification ORDER BY d.classification;

            WITH scoped_notes AS (
                SELECT n.id
                FROM core.notes n
                WHERE n.is_deleted = FALSE AND n.arrival_at::date <= @To::date
            ), note_quality AS (
                SELECT n.id,
                       COUNT(f.field_id)::bigint AS expected_items,
                       COUNT(f.field_id) FILTER (WHERE f.is_reviewed)::bigint AS reviewed_items,
                       COUNT(f.field_id) FILTER (WHERE f.is_unreviewed)::bigint AS unreviewed_items,
                       COUNT(f.field_id) FILTER (WHERE f.is_applicable_reviewed)::bigint AS applicable_reviewed_items,
                       COUNT(f.field_id) FILTER (WHERE f.is_correct)::bigint AS correct_items,
                       COUNT(f.field_id) FILTER (WHERE f.is_error)::bigint AS error_items,
                       COUNT(f.field_id) FILTER (WHERE f.is_not_applicable)::bigint AS not_applicable_items
                FROM scoped_notes n
                LEFT JOIN reporting.vw_note_indicator_facts_v3 f ON f.note_id = n.id
                GROUP BY n.id
            )
            SELECT COUNT(*) AS "TotalNotes",
                   COALESCE(SUM(expected_items), 0) AS "ExpectedItems",
                   COALESCE(SUM(reviewed_items), 0) AS "ReviewedItems",
                   COALESCE(SUM(applicable_reviewed_items), 0) AS "ApplicableReviewedItems",
                   COUNT(*) FILTER (WHERE expected_items > 0 AND unreviewed_items = 0
                                    AND error_items = 0 AND applicable_reviewed_items > 0) AS "CorrectItems",
                   COUNT(*) FILTER (WHERE error_items > 0) AS "ErrorItems",
                   COUNT(*) FILTER (WHERE expected_items > 0 AND unreviewed_items = 0
                                    AND error_items = 0 AND applicable_reviewed_items > 0) AS "CleanReviewedNotes",
                   COUNT(*) FILTER (WHERE error_items > 0) AS "ErrorNotes",
                   COUNT(*) FILTER (WHERE unreviewed_items > 0) AS "IncompleteReviewNotes"
            FROM note_quality;

            SELECT to_char(date_trunc('month', reference_note.arrival_at), 'YYYY-MM') AS "Label",
                   COUNT(*) FILTER (WHERE d.classification = 'COMPANY_RESEND') AS "CompanyResend",
                   COUNT(*) FILTER (WHERE d.classification = 'COMPANY_DUPLICATE_NUMBER') AS "DuplicateNumber"
            FROM core.duplicate_incidents d
            JOIN core.notes reference_note
              ON reference_note.id = CASE d.classification
                    WHEN 'COMPANY_DUPLICATE_NUMBER' THEN d.created_note_id
                    WHEN 'COMPANY_RESEND' THEN d.matched_note_id
                    ELSE NULL END
            WHERE reference_note.is_deleted = FALSE
              AND reference_note.arrival_at::date <= @To::date
            GROUP BY date_trunc('month', reference_note.arrival_at)
            ORDER BY "Label";

            SELECT
                COUNT(*) FILTER (WHERE n.arrival_at::date <= @To::date) AS "ByArrival",
                COUNT(*) FILTER (WHERE n.created_at::date <= @To::date) AS "ByCreated",
                COUNT(*) FILTER (WHERE n.arrival_at::date <= @HistoricalTo::date) AS "HistoricalByArrival",
                COUNT(*) FILTER (WHERE n.created_at::date <= @HistoricalTo::date) AS "HistoricalByCreated"
            FROM core.notes n
            WHERE n.is_deleted = FALSE;
            """;

        using var multi = await connection.QueryMultipleAsync(
            sql,
            new { To = operationalTo, HistoricalTo = historicalTo },
            commandTimeout: 30);
        var directSummary = await multi.ReadSingleAsync<IndependentSummaryRow>();
        var directCompanies = (await multi.ReadAsync<IndependentCompanyRow>()).ToList();
        var directStatuses = (await multi.ReadAsync<IndependentStatusRow>()).ToList();
        var directSla = await multi.ReadSingleAsync<IndependentSlaRow>();
        var directDuplicates = (await multi.ReadAsync<IndependentDuplicateRow>()).ToList();
        var directQuality = await multi.ReadSingleAsync<IndependentQualityRow>();
        var directTrend = (await multi.ReadAsync<TrendRow>()).ToList();
        var dateDiagnostics = await multi.ReadSingleAsync<DateDiagnosticRow>();

        var service = new DashboardQueryService(
            CreateProtectedConfiguration(),
            NullLogger<DashboardQueryService>.Instance);
        var apiSnapshot = await service.GetSnapshotAsync(new FilterQuery { To = operationalTo, DateBasis = "ArrivalDate" }, minSample: 1);

        Assert.True(apiSnapshot.IsHealthy);
        Assert.Null(apiSnapshot.EffectiveFilters.From);
        Assert.Equal(operationalTo, apiSnapshot.EffectiveFilters.To!.Value.Date);
        Assert.Equal(directSummary.TotalNotes, apiSnapshot.Summary.TotalNotes);
        Assert.Equal(directSummary.CurrentApproved, apiSnapshot.Summary.Approved);
        Assert.Equal(directSummary.PendingReview, apiSnapshot.Summary.PendingReview);
        Assert.Equal(directSummary.ReturnedForCorrection, apiSnapshot.Summary.ReturnedForCorrection);
        Assert.Equal(directSummary.OnTime, apiSnapshot.Summary.OnTime);
        Assert.Equal(directSummary.LateTotal, apiSnapshot.Summary.LateTotal);

        Assert.Equal(directCompanies.Count, apiSnapshot.Companies.Count);
        Assert.Equal(directCompanies.Count, apiSnapshot.Summary.ActiveCompaniesTotal);
        Assert.Equal(directCompanies.Sum(item => item.NoteCount), apiSnapshot.Summary.TotalNotes);
        foreach (var directCompany in directCompanies)
        {
            var apiCompany = apiSnapshot.Companies.Single(item => item.Id == directCompany.CompanyId);
            Assert.Equal(directCompany.CompanyName, apiCompany.Name);
            Assert.Equal(directCompany.NoteCount, apiCompany.NoteCount);
            Assert.False(long.TryParse(apiCompany.Name, out _));
        }
        Assert.Contains(directCompanies, company => company.NoteCount == 0);

        Assert.Equal(directStatuses.Sum(item => item.Count), apiSnapshot.StatusDistribution.Sum(item => item.Count));
        foreach (var directStatus in directStatuses)
        {
            Assert.Equal(directStatus.Count, apiSnapshot.StatusDistribution.Single(item => item.Status == directStatus.Status).Count);
        }

        Assert.Equal(directSla.EligibleNotes, apiSnapshot.Sla.EligibleNotes);
        Assert.Equal(directSla.OnTimeNotes, apiSnapshot.Sla.OnTimeNotes);
        Assert.Equal(directSla.LateNotes, apiSnapshot.Sla.LateNotes);
        Assert.Equal(directSla.InvalidDateOrderNotes, apiSnapshot.Sla.InvalidDateOrderNotes);

        foreach (var directDuplicate in directDuplicates)
        {
            Assert.Equal(
                directDuplicate.Count,
                apiSnapshot.DuplicateBreakdown.Single(item => item.Classification == directDuplicate.Classification).Count);
        }
        Assert.Equal(directDuplicates.Sum(item => item.Count), apiSnapshot.DuplicateBreakdown.Sum(item => item.Count));
        Assert.Equal(directDuplicates.Sum(item => item.Count), apiSnapshot.DuplicateTrend.Sum(item => item.CompanyResend + item.DuplicateNumber));
        foreach (var directPoint in directTrend)
        {
            var apiPoint = apiSnapshot.DuplicateTrend.Single(item => item.PeriodKey == directPoint.Label);
            Assert.Equal(directPoint.CompanyResend, apiPoint.CompanyResend);
            Assert.Equal(directPoint.DuplicateNumber, apiPoint.DuplicateNumber);
        }

        Assert.Equal(directQuality.TotalNotes, apiSnapshot.QualityByCompany.Sum(item => item.SampleSize));
        Assert.Equal(directQuality.CorrectItems, apiSnapshot.QualityByCompany.Sum(item => item.CorrectCount));
        Assert.Equal(directQuality.ErrorItems, apiSnapshot.QualityByCompany.Sum(item => item.ErrorCount));
        Assert.All(apiSnapshot.PerformanceByFactory, item => Assert.False(long.TryParse(item.Label, out _)));
        Assert.All(apiSnapshot.PerformanceByLine, item => Assert.False(long.TryParse(item.Label, out _)));
        Assert.All(apiSnapshot.TopErrorFields, item => Assert.False(long.TryParse(item.FieldName, out _)));

        _output.WriteLine(
            $"DASHBOARD_CURRENT_PG_EVIDENCE to={operationalTo:yyyy-MM-dd}; directTotal={directSummary.TotalNotes}; apiTotal={apiSnapshot.Summary.TotalNotes}; " +
            $"directCompanies={directCompanies.Count}; zeroCompanies={directCompanies.Count(item => item.NoteCount == 0)}; " +
            $"approved={directSummary.CurrentApproved}; pending={directSummary.PendingReview}; returned={directSummary.ReturnedForCorrection}; " +
            $"eligible={directSla.EligibleNotes}; onTime={directSla.OnTimeNotes}; late={directSla.LateNotes}; invalidDateOrder={directSla.InvalidDateOrderNotes}; " +
            $"duplicateCard={apiSnapshot.DuplicateBreakdown.Sum(item => item.Count)}; duplicateTrend={apiSnapshot.DuplicateTrend.Sum(item => item.CompanyResend + item.DuplicateNumber)}; " +
            $"qualityTotal={directQuality.TotalNotes}; qualityCorrectItems={directQuality.CorrectItems}; qualityErrorItems={directQuality.ErrorItems}; " +
            $"historicalArrival={dateDiagnostics.HistoricalByArrival}; historicalCreated={dateDiagnostics.HistoricalByCreated}; " +
            $"currentArrival={dateDiagnostics.ByArrival}; currentCreated={dateDiagnostics.ByCreated}");

        _output.WriteLine("COMPANIES=" + string.Join(" | ", directCompanies.Select(item => item.CompanyName + ":" + item.NoteCount)));
        _output.WriteLine("STATUSES=" + string.Join(" | ", directStatuses.Select(item => item.Status + ":" + item.Count)));
        _output.WriteLine("DUPLICATES=" + string.Join(" | ", directDuplicates.Select(item => item.Classification + ":" + item.Count)));
    }



    /// <summary>Mirrors DashboardContractBuilder.Delta so comparison assertions stay data-driven.</summary>
    private static double? DeltaPoints(decimal? current, decimal? previous) =>
        current.HasValue && previous.HasValue
            ? Math.Round((double)(current.Value - previous.Value), 2)
            : null;

    private static DateTime GetOperationalDate()
    {        var utcNow = DateTime.UtcNow;
        foreach (var timeZoneId in new[] { "Asia/Damascus", "Syria Standard Time" })
        {
            try
            {
                return TimeZoneInfo.ConvertTimeFromUtc(utcNow, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)).Date;
    

        }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return utcNow.AddHours(3).Date;
    }
    private sealed class DateDiagnosticRow
    {
        public long ByArrival { get; set; }
        public long ByCreated { get; set; }
        public long HistoricalByArrival { get; set; }
        public long HistoricalByCreated { get; set; }
    }

    private static string SnapshotSql()
    {
        var field = typeof(DashboardQueryService).GetField("SnapshotSql", BindingFlags.NonPublic | BindingFlags.Static);
        return (string)field!.GetValue(null)!;
    }

    [Fact]
    public async Task CompanyComparisonUsesSameCurrentAndPreviousMetricsForRealPreviousSample()
    {
        await using var connection = new NpgsqlConnection(GetConnectionString());
        await connection.OpenAsync();

        const string candidateSql = """
            WITH days AS (
                SELECT DISTINCT n.company_id AS "CompanyId", n.arrival_at::date AS "Day"
                FROM core.notes n
                WHERE n.is_deleted = FALSE AND n.arrival_at IS NOT NULL
            )
            SELECT current_day."CompanyId", current_day."Day" AS "CurrentDay"
            FROM days current_day
            JOIN days previous_day
              ON previous_day."CompanyId" = current_day."CompanyId"
             AND previous_day."Day" = current_day."Day" - 1
            ORDER BY current_day."Day" DESC
            LIMIT 1;
            """;
        var candidate = await connection.QuerySingleOrDefaultAsync<PreviousPeriodCandidate>(candidateSql);
        Assert.NotNull(candidate);

        var currentDay = candidate!.CurrentDay.Date;
        var service = new DashboardQueryService(
            CreateProtectedConfiguration(),
            NullLogger<DashboardQueryService>.Instance);
        var snapshot = await service.GetSnapshotAsync(
            new FilterQuery { CompanyId = candidate.CompanyId, From = currentDay, To = currentDay, DateBasis = "ArrivalDate" },
            minSample: 1);

        var current = snapshot.CompanyMetrics;
        var previous = snapshot.PreviousCompanyMetrics;
        var companyContract = snapshot.Views.CompanySummary;
        Assert.NotNull(current);
        Assert.NotNull(previous);
        Assert.NotNull(companyContract);
        Assert.NotNull(companyContract!.Metrics);
        Assert.NotNull(companyContract.PreviousMetrics);
        Assert.NotNull(companyContract.Comparison);
        Assert.True(current!.TotalNotes > 0);
        Assert.True(previous!.TotalNotes > 0);
        Assert.Equal(current.TotalNotes, companyContract.Metrics!.TotalNotes);
        Assert.Equal(previous.TotalNotes, companyContract.PreviousMetrics!.TotalNotes);
        Assert.Equal(current.TotalNotes - previous.TotalNotes, companyContract.Comparison!.TotalNotesDelta);
        Assert.Equal(currentDay.AddDays(-1), companyContract.PreviousPeriod!.From);
        Assert.Equal(currentDay.AddDays(-1), companyContract.PreviousPeriod.To);

        var expectedQualityDelta = current.ItemQualityPercentage.HasValue && previous.ItemQualityPercentage.HasValue
            ? Math.Round((double)(current.ItemQualityPercentage.Value - previous.ItemQualityPercentage.Value), 2)
            : (double?)null;
        var expectedTimeDelta = current.TimeCompliancePercentage.HasValue && previous.TimeCompliancePercentage.HasValue
            ? Math.Round((double)(current.TimeCompliancePercentage.Value - previous.TimeCompliancePercentage.Value), 2)
            : (double?)null;
        Assert.Equal(expectedQualityDelta, companyContract.Comparison.ItemQualityDeltaPoints);
        Assert.Equal(expectedTimeDelta, companyContract.Comparison.TimeComplianceDeltaPoints);
    }

    [Fact]
    public async Task IndependentSqlAcceptanceQueriesMatchApiContract()
    {
        var from = new DateTime(2026, 7, 1);
        var to = new DateTime(2026, 8, 3);
        await using var connection = new NpgsqlConnection(GetConnectionString());
        await connection.OpenAsync();

        const string independentSql = """
            SELECT
                COUNT(*) AS "TotalNotes",
                COUNT(*) FILTER (WHERE n.status = 'APPROVED') AS "CurrentApproved",
                COUNT(*) FILTER (WHERE n.status IN ('NEEDS_REVIEW', 'UNDER_REVIEW', 'COMPLETE', 'HAS_ERRORS')) AS "PendingReview",
                COUNT(*) FILTER (WHERE n.status = 'RETURNED_FOR_CORRECTION') AS "ReturnedForCorrection",
                COUNT(*) FILTER (WHERE n.writing_date IS NOT NULL AND n.arrival_at >= n.writing_date
                                  AND (n.arrival_at::date - n.writing_date::date) BETWEEN 0 AND 5) AS "OnTime",
                COUNT(*) FILTER (WHERE n.writing_date IS NOT NULL AND n.arrival_at >= n.writing_date
                                  AND (n.arrival_at::date - n.writing_date::date) > 5) AS "LateTotal"
            FROM core.notes n
            WHERE n.is_deleted = FALSE AND n.arrival_at >= @From AND n.arrival_at < @To + INTERVAL '1 day';

            SELECT COUNT(*)
            FROM core.notes n
            WHERE n.is_deleted = FALSE AND n.approved_at IS NOT NULL
              AND n.approved_at >= @From AND n.approved_at < @To + INTERVAL '1 day';

            SELECT c.id AS "CompanyId", c.name AS "CompanyName", COUNT(n.id) AS "NoteCount"
            FROM core.companies c
            LEFT JOIN core.notes n ON n.company_id = c.id AND n.is_deleted = FALSE
              AND n.arrival_at >= @From AND n.arrival_at < @To + INTERVAL '1 day'
            WHERE c.is_active = TRUE AND c.is_deleted = FALSE
            GROUP BY c.id, c.name ORDER BY c.name;

            SELECT n.status AS "Status", COUNT(*) AS "Count"
            FROM core.notes n
            WHERE n.is_deleted = FALSE AND n.arrival_at >= @From AND n.arrival_at < @To + INTERVAL '1 day'
            GROUP BY n.status ORDER BY n.status;

            SELECT
                COUNT(*) FILTER (WHERE n.writing_date IS NOT NULL AND n.arrival_at >= n.writing_date) AS "EligibleNotes",
                COUNT(*) FILTER (WHERE n.writing_date IS NOT NULL AND n.arrival_at >= n.writing_date
                                  AND (n.arrival_at::date - n.writing_date::date) BETWEEN 0 AND 5) AS "OnTimeNotes",
                COUNT(*) FILTER (WHERE n.writing_date IS NOT NULL AND n.arrival_at >= n.writing_date
                                  AND (n.arrival_at::date - n.writing_date::date) > 5) AS "LateNotes",
                COUNT(*) FILTER (WHERE n.writing_date IS NOT NULL AND n.arrival_at < n.writing_date) AS "InvalidDateOrderNotes"
            FROM core.notes n
            WHERE n.is_deleted = FALSE AND n.arrival_at >= @From AND n.arrival_at < @To + INTERVAL '1 day';

            SELECT d.classification AS "Classification", COUNT(*) AS "Count"
            FROM core.duplicate_incidents d
            JOIN core.notes reference_note
              ON reference_note.id = CASE d.classification
                    WHEN 'COMPANY_DUPLICATE_NUMBER' THEN d.created_note_id
                    WHEN 'COMPANY_RESEND' THEN d.matched_note_id
                    ELSE NULL END
            WHERE reference_note.is_deleted = FALSE
              AND reference_note.arrival_at >= @From AND reference_note.arrival_at < @To + INTERVAL '1 day'
            GROUP BY d.classification ORDER BY d.classification;

            WITH scoped_notes AS (
                SELECT n.id FROM core.notes n
                WHERE n.is_deleted = FALSE AND n.arrival_at >= @From AND n.arrival_at < @To + INTERVAL '1 day'
            ), note_quality AS (
                SELECT n.id,
                       COUNT(f.field_id)::bigint AS expected_items,
                       COUNT(f.field_id) FILTER (WHERE f.is_reviewed)::bigint AS reviewed_items,
                       COUNT(f.field_id) FILTER (WHERE f.is_unreviewed)::bigint AS unreviewed_items,
                       COUNT(f.field_id) FILTER (WHERE f.is_applicable_reviewed)::bigint AS applicable_reviewed_items,
                       COUNT(f.field_id) FILTER (WHERE f.is_correct)::bigint AS correct_items,
                       COUNT(f.field_id) FILTER (WHERE f.is_error)::bigint AS error_items,
                       COUNT(f.field_id) FILTER (WHERE f.is_not_applicable)::bigint AS not_applicable_items
                FROM scoped_notes n
                LEFT JOIN reporting.vw_note_indicator_facts_v3 f ON f.note_id = n.id
                GROUP BY n.id
            )
            SELECT COUNT(*) AS "TotalNotes",
                   COALESCE(SUM(expected_items), 0) AS "ExpectedItems",
                   COALESCE(SUM(reviewed_items), 0) AS "ReviewedItems",
                   COALESCE(SUM(unreviewed_items), 0) AS "UnreviewedItems",
                   COALESCE(SUM(applicable_reviewed_items), 0) AS "ApplicableReviewedItems",
                   COALESCE(SUM(correct_items), 0) AS "CorrectItems",
                   COALESCE(SUM(error_items), 0) AS "ErrorItems",
                   COALESCE(SUM(not_applicable_items), 0) AS "NotApplicableItems",
                   COUNT(*) FILTER (WHERE expected_items > 0 AND unreviewed_items = 0
                                    AND error_items = 0 AND applicable_reviewed_items > 0) AS "CleanReviewedNotes",
                   COUNT(*) FILTER (WHERE error_items > 0) AS "ErrorNotes",
                   COUNT(*) FILTER (WHERE unreviewed_items > 0) AS "IncompleteReviewNotes"
            FROM note_quality;
            """;

        using var multi = await connection.QueryMultipleAsync(
            independentSql,
            new { From = from, To = to },
            commandTimeout: 30);
        var directSummary = await multi.ReadSingleAsync<IndependentSummaryRow>();
        var directApprovedEvents = await multi.ReadSingleAsync<long>();
        var directCompanies = (await multi.ReadAsync<IndependentCompanyRow>()).ToList();
        var directStatuses = (await multi.ReadAsync<IndependentStatusRow>()).ToList();
        var directSla = await multi.ReadSingleAsync<IndependentSlaRow>();
        var directDuplicates = (await multi.ReadAsync<IndependentDuplicateRow>()).ToList();
        var directQuality = await multi.ReadSingleAsync<IndependentQualityRow>();

        var service = new DashboardQueryService(
            CreateProtectedConfiguration(),
            NullLogger<DashboardQueryService>.Instance);
        var apiSnapshot = await service.GetSnapshotAsync(
            new FilterQuery { From = from, To = to, DateBasis = "ArrivalDate" },
            minSample: 1);
        Assert.True(apiSnapshot.IsHealthy);
        Assert.Equal(directSummary.TotalNotes, apiSnapshot.Summary.TotalNotes);
        Assert.Equal(directSummary.CurrentApproved, apiSnapshot.Summary.Approved);
        Assert.Equal(directSummary.PendingReview, apiSnapshot.Summary.PendingReview);
        Assert.Equal(directSummary.ReturnedForCorrection, apiSnapshot.Summary.ReturnedForCorrection);
        Assert.Equal(directSummary.OnTime, apiSnapshot.Summary.OnTime);
        Assert.Equal(directSummary.LateTotal, apiSnapshot.Summary.LateTotal);
        Assert.Equal(directApprovedEvents, apiSnapshot.IncomingVsApproved.Sum(item => item.Approved));
        Assert.Equal(8, directCompanies.Count);
        Assert.Equal(directCompanies.Count, apiSnapshot.Companies.Count);
        foreach (var directCompany in directCompanies)
        {
            var apiCompany = apiSnapshot.Companies.Single(item => item.Id == directCompany.CompanyId);
            Assert.Equal(directCompany.CompanyName, apiCompany.Name);
            Assert.Equal(directCompany.NoteCount, apiCompany.NoteCount);
            Assert.False(long.TryParse(apiCompany.Name, out _));
        }
        Assert.Equal(directStatuses.Sum(item => item.Count), apiSnapshot.StatusDistribution.Sum(item => item.Count));
        foreach (var directStatus in directStatuses)
        {
            Assert.Equal(directStatus.Count, apiSnapshot.StatusDistribution.Single(item => item.Status == directStatus.Status).Count);
        }
        Assert.Equal(directSla.EligibleNotes, apiSnapshot.Sla.EligibleNotes);
        Assert.Equal(directSla.OnTimeNotes, apiSnapshot.Sla.OnTimeNotes);
        Assert.Equal(directSla.LateNotes, apiSnapshot.Sla.LateNotes);
        Assert.Equal(directSla.InvalidDateOrderNotes, apiSnapshot.Sla.InvalidDateOrderNotes);
        foreach (var directDuplicate in directDuplicates)
        {
            Assert.Equal(directDuplicate.Count, apiSnapshot.DuplicateBreakdown.Single(item => item.Classification == directDuplicate.Classification).Count);
        }
        Assert.Equal(directDuplicates.Sum(item => item.Count), apiSnapshot.DuplicateBreakdown.Sum(item => item.Count));
        Assert.Equal(directDuplicates.Where(item => item.Classification == "COMPANY_RESEND").Sum(item => item.Count), apiSnapshot.Summary.CompanyResend);
        Assert.Equal(directDuplicates.Where(item => item.Classification == "COMPANY_DUPLICATE_NUMBER").Sum(item => item.Count), apiSnapshot.Summary.DuplicateNumbers);
        Assert.Equal(directDuplicates.Sum(item => item.Count), apiSnapshot.DuplicateTrend.Sum(item => item.CompanyResend + item.DuplicateNumber));

        Assert.NotNull(apiSnapshot.CompanyMetrics);
        var metrics = apiSnapshot.CompanyMetrics!;
        Assert.Equal(directQuality.TotalNotes, metrics.TotalNotes);
        Assert.Equal(directQuality.TotalNotes, directSummary.TotalNotes);
        Assert.Equal(directQuality.ExpectedItems, metrics.ExpectedItems);
        Assert.Equal(directQuality.ReviewedItems, metrics.ReviewedItems);
        Assert.Equal(directQuality.UnreviewedItems, metrics.UnreviewedItems);
        Assert.Equal(directQuality.ApplicableReviewedItems, metrics.ApplicableReviewedItems);
        Assert.Equal(directQuality.CorrectItems, metrics.CorrectItems);
        Assert.Equal(directQuality.ErrorItems, metrics.ErrorItems);
        Assert.Equal(directQuality.NotApplicableItems, metrics.NotApplicableItems);
        Assert.Equal(directQuality.CleanReviewedNotes, metrics.CleanReviewedNotes);
        Assert.Equal(directQuality.ErrorNotes, metrics.ErrorNotes);
        Assert.Equal(directQuality.IncompleteReviewNotes, metrics.IncompleteReviewNotes);
        Assert.Equal(directQuality.CleanReviewedNotes + directQuality.ErrorNotes, metrics.QualityEligibleNoteCount);
        Assert.Equal(ToPercentage(directQuality.ReviewedItems, directQuality.ExpectedItems), metrics.ReviewCoveragePercentage);
        Assert.Equal(ToPercentage(directQuality.CorrectItems, directQuality.ApplicableReviewedItems), metrics.ItemQualityPercentage);

        if (UsesExplicitIntegrationConnection)
        {
            // Golden dataset, arrival-scoped 2026-07-01..2026-08-03: N-0001, N-0002, N-0004.
            Assert.Equal(3, directSummary.TotalNotes);
            Assert.Equal(8, directCompanies.Count);
        }
    }
    [Fact]
    public async Task SnapshotSqlRunsViaDapperQueryMultiple()
    {
        await using var connection = new NpgsqlConnection(GetConnectionString());
        await connection.OpenAsync();

        var conn = connection;

        var referenceDate = new DateTime(2026, 8, 3);
        var parameters = new DynamicParameters();
        parameters.Add("From", (DateTime?)null, DbType.Date);
        parameters.Add("To", (DateTime?)null, DbType.Date);
        parameters.Add("CompanyId", (Guid?)null, DbType.Guid);
        parameters.Add("FactoryId", (Guid?)null, DbType.Guid);
        parameters.Add("ProductionLineId", (Guid?)null, DbType.Guid);
        parameters.Add("NoteTypeId", (Guid?)null, DbType.Guid);
        parameters.Add("Status", (string?)null, DbType.String);
        parameters.Add("EntryCutoffDate", (DateTime?)null, DbType.Date);
        parameters.Add("DateBasis", "ArrivalDate", DbType.String);
        parameters.Add("CurrentDate", referenceDate, DbType.Date);
        parameters.Add("MinSample", 1, DbType.Int32);
        parameters.Add("PreviousCompanyId", (Guid?)null, DbType.Guid);
        parameters.Add("PreviousFrom", (DateTime?)null, DbType.Date);
        parameters.Add("PreviousTo", (DateTime?)null, DbType.Date);
        parameters.Add("PreviousFactoryId", (Guid?)null, DbType.Guid);
        parameters.Add("PreviousProductionLineId", (Guid?)null, DbType.Guid);
        parameters.Add("PreviousNoteTypeId", (Guid?)null, DbType.Guid);
        parameters.Add("PreviousStatus", (string?)null, DbType.String);
        parameters.Add("PreviousEntryCutoffDate", (DateTime?)null, DbType.Date);

        using var multi = await conn.QueryMultipleAsync(SnapshotSql(), parameters, commandTimeout: 30);

        var summary = await multi.ReadSingleAsync<DashboardSummary>();
        var companyCounts = await multi.ReadSingleAsync<CompanyCountsRow>();
        var duplicates = (await multi.ReadAsync<DuplicateRow>()).ToList();
        var companies = (await multi.ReadAsync<CompanyRow>()).ToList();
        var timeliness = (await multi.ReadAsync<PerfRow>()).ToList();
        var monthly = (await multi.ReadAsync<MonthlyRow>()).ToList();
        var statuses = (await multi.ReadAsync<StatusRow>()).ToList();
        var recent = (await multi.ReadAsync<RecentRow>()).ToList();
        var sla = await multi.ReadSingleAsync<SlaRow>();
        var backlog = (await multi.ReadAsync<BacklogRow>()).ToList();
        var incoming = (await multi.ReadAsync<IncomingRow>()).ToList();
        var quality = (await multi.ReadAsync<QualityRow>()).ToList();
        var fieldQuality = (await multi.ReadAsync<FieldQualityRow>()).ToList();
        var topFields = (await multi.ReadAsync<TopFieldRow>()).ToList();
        var states = (await multi.ReadAsync<IndicatorRow>()).ToList();
        var perfFactory = (await multi.ReadAsync<PerfRow>()).ToList();
        var perfLine = (await multi.ReadAsync<PerfRow>()).ToList();
        var trends = (await multi.ReadAsync<TrendRow>()).ToList();
        var completeness = await multi.ReadSingleAsync<CompletenessRow>();
        var companyMetrics = await multi.ReadSingleOrDefaultAsync<CompanyMetrics>();
        var previousCompanyMetrics = await multi.ReadSingleOrDefaultAsync<CompanyMetrics>();

        if (!UsesExplicitIntegrationConnection)
        {
            Assert.Equal(companyCounts.ActiveCompaniesTotal, companies.Count);
            Assert.Equal(companies.Count, timeliness.Count);
            Assert.Equal(summary.TotalNotes, monthly.Sum(item => item.Count));
            Assert.Equal(summary.CompanyResend, duplicates.Where(item => item.Classification == "COMPANY_RESEND").Sum(item => item.Count));
            Assert.Equal(summary.DuplicateNumbers, duplicates.Where(item => item.Classification == "COMPANY_DUPLICATE_NUMBER").Sum(item => item.Count));
            Assert.Equal(sla.LateNotes, summary.LateTotal);
            Assert.Equal(sla.OnTimeNotes, summary.OnTime);
            Assert.Equal(incoming.Sum(item => item.Incoming), summary.TotalNotes);
            return;
        }
        Assert.Equal(7, summary.TotalNotes);
        Assert.Equal(1, summary.NotesToday);
        Assert.Equal(6, summary.NotesCurrentMonth);
        Assert.Equal(2, summary.Approved);
        Assert.Equal(3, summary.PendingReview);
        Assert.Equal(1, summary.ReturnedForCorrection);
        Assert.Equal(2, summary.LateTotal);
        Assert.Equal(1, summary.LateSameMonth);
        Assert.Equal(1, summary.LateCrossMonth);
        Assert.Equal(4, summary.OnTime);
        Assert.Equal(8.5, summary.AvgLateDays);
        Assert.Equal(9, summary.MaxLateDays);
        Assert.Equal(new DateTime(2026, 8, 10), summary.LatestArrivalDate);
        Assert.Equal(8, companyCounts.ActiveCompaniesTotal);
        Assert.Equal(2, companyCounts.CompaniesWithNotes);
        var companyResendBreakdownCount = duplicates.Single(d => d.Classification == "COMPANY_RESEND").Count;
        var duplicateNumberBreakdownCount = duplicates.Single(d => d.Classification == "COMPANY_DUPLICATE_NUMBER").Count;
        Assert.Equal(1, companyResendBreakdownCount);
        Assert.Equal(2, duplicateNumberBreakdownCount);
        Assert.Equal(companyResendBreakdownCount, summary.CompanyResend);
        Assert.Equal(duplicateNumberBreakdownCount, summary.DuplicateNumbers);
        // The current contract includes active companies with zero notes.
        Assert.Equal(8, companies.Count);
        var zeroCompany = companies.Single(c => c.CompanyName == "شركة بلا مذكرات");
        Assert.Equal(0, zeroCompany.NoteCount);
        Assert.All(companies, company => Assert.False(long.TryParse(company.CompanyName, out _)));
        Assert.Equal(2, monthly.Count);
        Assert.Equal(new DateTime(2026, 7, 1), monthly[0].MonthStart);
        Assert.Equal(1, monthly[0].Count);
        Assert.Equal(new DateTime(2026, 8, 1), monthly[1].MonthStart);
        Assert.Equal(6, monthly[1].Count);
        Assert.Equal(6, statuses.Count);
        Assert.Equal(7, recent.Count);
        Assert.Equal("N-0003", recent[0].NoteNumber);

        Assert.Equal(6, sla.EligibleNotes);
        Assert.Equal(4, sla.OnTimeNotes);
        Assert.Equal(2, sla.LateNotes);
        Assert.Equal(0, sla.InvalidDateOrderNotes);

        Assert.Equal(3, backlog.Count);
        Assert.Equal(1, backlog.Single(b => b.Bucket == "1-3-days").Count);
        Assert.Equal(1, backlog.Single(b => b.Bucket == "4-7-days").Count);
        Assert.Equal(1, backlog.Single(b => b.Bucket == "gt-7-days").Count);

        Assert.Equal(2, incoming.Count);
        Assert.Equal(new DateTime(2026, 7, 1), incoming[0].Label);
        Assert.Equal(1, incoming[0].Incoming);
        Assert.Equal(new DateTime(2026, 8, 1), incoming[1].Label);
        Assert.Equal(6, incoming[1].Incoming);
        Assert.Equal(2, incoming.Sum(i => i.Approved));

        Assert.Equal(2, quality.Count);
        var amal = quality.Single(q => q.CompanyName == "شركة الأمل");
        Assert.Equal(4, amal.SampleSize);
        Assert.Equal(3, amal.CorrectCount);
        Assert.Equal(1, amal.ErrorCount);
        var nour = quality.Single(q => q.CompanyName == "شركة النور");
        Assert.Equal(3, nour.SampleSize);
        Assert.Equal(1, nour.CorrectCount);
        Assert.Equal(1, nour.ErrorCount);

        Assert.Equal(10, topFields.Count);
        Assert.Contains(topFields, field => field.ErrorCount > 0);

        Assert.Equal(7, states.Count);
        Assert.Equal(1, states.Single(s => s.Code == 5).Value);
        Assert.Equal(1, states.Single(s => s.Code == 6).Value);

        Assert.Equal(2, perfFactory.Count);
        var nourFactory = perfFactory.Single(p => p.Label == "مصنع النور الأول");
        Assert.Equal(3, nourFactory.TotalNotes);
        Assert.Equal(0, nourFactory.LateCount);
        Assert.Equal(3, nourFactory.SampleCount);
        var amalFactory = perfFactory.Single(p => p.Label == "مصنع الأمل الأول");
        Assert.Equal(4, amalFactory.TotalNotes);
        Assert.Equal(2, amalFactory.LateCount);
        Assert.Equal(4, amalFactory.SampleCount);

        var onlyLine = Assert.Single(perfLine);
        Assert.Equal("خط التعبئة 1", onlyLine.Label);
        Assert.Equal(4, onlyLine.TotalNotes);

        Assert.Equal(2, trends.Count);
        Assert.Equal(1, trends.Single(t => t.Label == "2026-07").CompanyResend);
        Assert.Equal(1, trends.Single(t => t.Label == "2026-07").DuplicateNumber);
        Assert.Equal(0, trends[^1].CompanyResend);
        Assert.Equal(1, trends[^1].DuplicateNumber);
        Assert.Equal(companyResendBreakdownCount, trends.Sum(t => t.CompanyResend));
        Assert.Equal(duplicateNumberBreakdownCount, trends.Sum(t => t.DuplicateNumber));
        Assert.All(perfFactory, performance => Assert.False(long.TryParse(performance.Label, out _)));
        Assert.All(topFields, field => Assert.False(long.TryParse(field.FieldName, out _)));

        const string companyMetricEvidenceSql = """
            SELECT
                total_notes AS "TotalNotes",
                item_quality_percentage AS "ItemQualityPercentage",
                time_compliance_percentage AS "TimeCompliancePercentage"
            FROM reporting.fn_company_metrics_snapshot_v3_filtered(
                @CompanyId, @From::date, @To::date, NULL, NULL, NULL, NULL, NULL);
            """;
        var amalId = await connection.QuerySingleAsync<Guid>(
            "SELECT id FROM core.companies WHERE name = 'شركة الأمل' AND is_active = TRUE AND is_deleted = FALSE");
        var currentCompanyMetrics = await connection.QuerySingleAsync<CompanyMetricEvidenceRow>(
            companyMetricEvidenceSql,
            new { CompanyId = amalId, From = new DateTime(2026, 7, 1), To = new DateTime(2026, 8, 3) });
        var previousCompanyEvidence = await connection.QuerySingleOrDefaultAsync<CompanyMetricEvidenceRow>(
            companyMetricEvidenceSql,
            new { CompanyId = amalId, From = new DateTime(2026, 6, 1), To = new DateTime(2026, 6, 30) });
        Assert.Equal(2, currentCompanyMetrics.TotalNotes);
        Assert.NotNull(previousCompanyEvidence);
        Assert.Equal(0, previousCompanyEvidence!.TotalNotes);

        _output.WriteLine(
            $"DASHBOARD_PG_EVIDENCE companies={companies.Count}; zeroCompany={zeroCompany.CompanyName}:{zeroCompany.NoteCount}; " +
            $"totalNotes={summary.TotalNotes}; approved={summary.Approved}; pending={summary.PendingReview}; late={summary.LateTotal}; " +
            $"duplicateCard={summary.DuplicateNumbers}; duplicateBreakdown={duplicateNumberBreakdownCount}; " +
            $"duplicateTrend={trends.Sum(t => t.DuplicateNumber)}; currentCompanyNotes={currentCompanyMetrics.TotalNotes}; " +
            $"previousCompanyNotes={previousCompanyEvidence.TotalNotes}");

        Assert.Equal(1, completeness.MissingWritingDate);
        Assert.Equal(0, completeness.MissingFactory);
        Assert.Equal(3, completeness.MissingProductionLine);
    }

    private static decimal? ToPercentage(long numerator, long denominator)
    {
        return denominator == 0
            ? null
            : decimal.Round(numerator * 100m / denominator, 2);
    }

    private sealed class PreviousPeriodCandidate
    {
        public Guid CompanyId { get; set; }
        public DateTime CurrentDay { get; set; }
    }

    private sealed class IndependentSummaryRow
    {
        public long TotalNotes { get; set; }
        public long CurrentApproved { get; set; }
        public long PendingReview { get; set; }
        public long ReturnedForCorrection { get; set; }
        public long OnTime { get; set; }
        public long LateTotal { get; set; }
    }

    private sealed class IndependentCompanyRow
    {
        public Guid CompanyId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public long NoteCount { get; set; }
    }

    private sealed class IndependentStatusRow
    {
        public string Status { get; set; } = string.Empty;
        public long Count { get; set; }
    }

    private sealed class IndependentSlaRow
    {
        public long EligibleNotes { get; set; }
        public long OnTimeNotes { get; set; }
        public long LateNotes { get; set; }
        public long InvalidDateOrderNotes { get; set; }
    }

    private sealed class IndependentDuplicateRow
    {
        public string Classification { get; set; } = string.Empty;
        public long Count { get; set; }
    }

    private sealed class IndependentQualityRow
    {
        public long TotalNotes { get; set; }
        public long ExpectedItems { get; set; }
        public long ReviewedItems { get; set; }
        public long UnreviewedItems { get; set; }
        public long ApplicableReviewedItems { get; set; }
        public long CorrectItems { get; set; }
        public long ErrorItems { get; set; }
        public long NotApplicableItems { get; set; }
        public long CleanReviewedNotes { get; set; }
        public long ErrorNotes { get; set; }
        public long IncompleteReviewNotes { get; set; }
    }
    private sealed class CompanyCountsRow
    {
        public long ActiveCompaniesTotal { get; set; }
        public long CompaniesWithNotes { get; set; }
    }

    private sealed class DuplicateRow
    {
        public string Classification { get; set; } = string.Empty;
        public long Count { get; set; }
    }

    private sealed class CompanyRow
    {
        public string CompanyName { get; set; } = string.Empty;
        public long NoteCount { get; set; }
    }

    private sealed class MonthlyRow
    {
        public DateTime MonthStart { get; set; }
        public long Count { get; set; }
    }

    private sealed class StatusRow
    {
        public string Status { get; set; } = string.Empty;
        public long Count { get; set; }
    }

    private sealed class RecentRow
    {
        public Guid Id { get; set; }
        public string NoteNumber { get; set; } = string.Empty;
        public string? CompanyName { get; set; }
        public string? FactoryName { get; set; }
        public string? ProductionLineName { get; set; }
        public string? NoteTypeName { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime? WritingDate { get; set; }
        public DateTime? ArrivalAt { get; set; }
    }

    private sealed class SlaRow
    {
        public long EligibleNotes { get; set; }
        public long OnTimeNotes { get; set; }
        public long LateNotes { get; set; }
        public long InvalidDateOrderNotes { get; set; }
    }

    private sealed class BacklogRow
    {
        public string Bucket { get; set; } = string.Empty;
        public long Count { get; set; }
    }

    private sealed class IncomingRow
    {
        public DateTime Label { get; set; }
        public long Incoming { get; set; }
        public long Approved { get; set; }
    }

    private sealed class QualityRow
    {
        public string CompanyName { get; set; } = string.Empty;
        public long SampleSize { get; set; }
        public long CorrectCount { get; set; }
        public long ErrorCount { get; set; }
    }

    private sealed class FieldQualityRow
    {
        public Guid CompanyId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public long TotalFieldCount { get; set; }
        public long CorrectFieldCount { get; set; }
        public long ErrorFieldCount { get; set; }
        public long UnknownFieldCount { get; set; }
    }

    private sealed class TopFieldRow
    {
        public string FieldName { get; set; } = string.Empty;
        public long SampleSize { get; set; }
        public long ErrorCount { get; set; }
    }

    private sealed class IndicatorRow
    {
        public int Code { get; set; }
        public long Value { get; set; }
    }

    private sealed class PerfRow
    {
        public Guid DimensionId { get; set; }
        public string Label { get; set; } = string.Empty;
        public long TotalNotes { get; set; }
        public long EligibleCount { get; set; }
        public long LateCount { get; set; }
        public long OnTimeCount { get; set; }
        public long SampleCount { get; set; }
        public long CorrectCount { get; set; }
    }

    private sealed class TrendRow
    {
        public string Label { get; set; } = string.Empty;
        public long CompanyResend { get; set; }
        public long DuplicateNumber { get; set; }
    }

    private sealed class CompanyMetricEvidenceRow
    {
        public long TotalNotes { get; set; }
        public decimal? ItemQualityPercentage { get; set; }
        public decimal? TimeCompliancePercentage { get; set; }
    }
    private sealed class CompletenessRow
    {
        public long MissingWritingDate { get; set; }
        public long MissingFactory { get; set; }
        public long MissingProductionLine { get; set; }
    }
}


