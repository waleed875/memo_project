using System.Data;
using Dapper;
using MemoDashboard.Backend.Models;
using Npgsql;

namespace MemoDashboard.Backend.Services;

public sealed class DashboardQueryService
{
    private readonly ConfigurationService _configuration;
    private readonly ILogger<DashboardQueryService> _logger;

    private const string FiltersNotes = """
            AND (@From IS NULL OR n.arrival_at >= @From)
            AND (@To IS NULL OR n.arrival_at < @To + 1)
            AND (@CompanyId IS NULL OR n.company_id = @CompanyId)
            AND (@FactoryId IS NULL OR n.factory_id = @FactoryId)
            AND (@ProductionLineId IS NULL OR n.production_line_id = @ProductionLineId)
            AND (@NoteTypeId IS NULL OR n.note_type_id = @NoteTypeId)
            AND (@Status IS NULL OR n.status = @Status)
            AND (@EntryCutoffDate IS NULL OR n.created_at < @EntryCutoffDate + 1)
        """;

    private const string FiltersNotesEntity = """
            AND (@CompanyId IS NULL OR n.company_id = @CompanyId)
            AND (@FactoryId IS NULL OR n.factory_id = @FactoryId)
            AND (@ProductionLineId IS NULL OR n.production_line_id = @ProductionLineId)
            AND (@NoteTypeId IS NULL OR n.note_type_id = @NoteTypeId)
            AND (@Status IS NULL OR n.status = @Status)
            AND (@EntryCutoffDate IS NULL OR n.created_at < @EntryCutoffDate + 1)
        """;

    private const string FiltersDuplicates = """
            AND reference_note.is_deleted = FALSE
            AND (@From IS NULL OR reference_note.arrival_at >= @From)
            AND (@To IS NULL OR reference_note.arrival_at < @To + 1)
            AND (@CompanyId IS NULL OR reference_note.company_id = @CompanyId)
            AND (@FactoryId IS NULL OR reference_note.factory_id = @FactoryId)
            AND (@ProductionLineId IS NULL OR reference_note.production_line_id = @ProductionLineId)
            AND (@NoteTypeId IS NULL OR reference_note.note_type_id = @NoteTypeId)
            AND (@Status IS NULL OR reference_note.status = @Status)
            AND (@EntryCutoffDate IS NULL OR reference_note.created_at < @EntryCutoffDate + 1)
        """;

    private const string SnapshotSql = """
        SELECT
            COUNT(*) AS TotalNotes,
            COUNT(*) FILTER (WHERE arrival_at::date = @CurrentDate) AS NotesToday,
            COUNT(*) FILTER (WHERE date_trunc('month', arrival_at) = date_trunc('month', @CurrentDate)) AS NotesCurrentMonth,
            COUNT(*) FILTER (WHERE status = 'APPROVED') AS Approved,
            COUNT(*) FILTER (WHERE status IN ('NEEDS_REVIEW', 'UNDER_REVIEW', 'COMPLETE', 'HAS_ERRORS')) AS PendingReview,
            COUNT(*) FILTER (WHERE status = 'RETURNED_FOR_CORRECTION') AS ReturnedForCorrection,            (
                SELECT COUNT(*)
                FROM core.duplicate_incidents d
                JOIN core.notes reference_note ON reference_note.id = CASE d.classification
                    WHEN 'COMPANY_DUPLICATE_NUMBER' THEN d.created_note_id
                    WHEN 'COMPANY_RESEND' THEN d.matched_note_id
                    ELSE NULL
                END
                WHERE d.classification = 'COMPANY_RESEND'
                    AND reference_note.is_deleted = FALSE
                    AND (@From IS NULL OR reference_note.arrival_at >= @From)
                    AND (@To IS NULL OR reference_note.arrival_at < @To + 1)
                    AND (@CompanyId IS NULL OR reference_note.company_id = @CompanyId)
                    AND (@FactoryId IS NULL OR reference_note.factory_id = @FactoryId)
                    AND (@ProductionLineId IS NULL OR reference_note.production_line_id = @ProductionLineId)
                    AND (@NoteTypeId IS NULL OR reference_note.note_type_id = @NoteTypeId)
                    AND (@Status IS NULL OR reference_note.status = @Status)
                    AND (@EntryCutoffDate IS NULL OR reference_note.created_at < @EntryCutoffDate + 1)
            ) AS CompanyResend,
            (
                SELECT COUNT(*)
                FROM core.duplicate_incidents d
                JOIN core.notes reference_note ON reference_note.id = CASE d.classification
                    WHEN 'COMPANY_DUPLICATE_NUMBER' THEN d.created_note_id
                    WHEN 'COMPANY_RESEND' THEN d.matched_note_id
                    ELSE NULL
                END
                WHERE d.classification = 'COMPANY_DUPLICATE_NUMBER'
                    AND reference_note.is_deleted = FALSE
                    AND (@From IS NULL OR reference_note.arrival_at >= @From)
                    AND (@To IS NULL OR reference_note.arrival_at < @To + 1)
                    AND (@CompanyId IS NULL OR reference_note.company_id = @CompanyId)
                    AND (@FactoryId IS NULL OR reference_note.factory_id = @FactoryId)
                    AND (@ProductionLineId IS NULL OR reference_note.production_line_id = @ProductionLineId)
                    AND (@NoteTypeId IS NULL OR reference_note.note_type_id = @NoteTypeId)
                    AND (@Status IS NULL OR reference_note.status = @Status)
                    AND (@EntryCutoffDate IS NULL OR reference_note.created_at < @EntryCutoffDate + 1)
            ) AS DuplicateNumbers,
            (
                SELECT COUNT(DISTINCT reference_note.id)
                FROM core.duplicate_incidents d
                JOIN core.notes reference_note ON reference_note.id = CASE d.classification
                    WHEN 'COMPANY_DUPLICATE_NUMBER' THEN d.created_note_id
                    WHEN 'COMPANY_RESEND' THEN d.matched_note_id
                    ELSE NULL
                END
                WHERE d.classification IN ('COMPANY_RESEND', 'COMPANY_DUPLICATE_NUMBER')
                    AND reference_note.is_deleted = FALSE
                    AND (@From IS NULL OR reference_note.arrival_at >= @From)
                    AND (@To IS NULL OR reference_note.arrival_at < @To + 1)
                    AND (@CompanyId IS NULL OR reference_note.company_id = @CompanyId)
                    AND (@FactoryId IS NULL OR reference_note.factory_id = @FactoryId)
                    AND (@ProductionLineId IS NULL OR reference_note.production_line_id = @ProductionLineId)
                    AND (@NoteTypeId IS NULL OR reference_note.note_type_id = @NoteTypeId)
                    AND (@Status IS NULL OR reference_note.status = @Status)
                    AND (@EntryCutoffDate IS NULL OR reference_note.arrival_at::date <= @EntryCutoffDate)
            ) AS DuplicateAffectedNotes,
            COUNT(*) FILTER (WHERE writing_date IS NOT NULL AND (arrival_at::date - writing_date::date) > 5) AS LateTotal,
            COUNT(*) FILTER (WHERE writing_date IS NOT NULL AND (arrival_at::date - writing_date::date) > 5
                             AND date_trunc('month', writing_date) = date_trunc('month', arrival_at)) AS LateSameMonth,
            COUNT(*) FILTER (WHERE writing_date IS NOT NULL AND (arrival_at::date - writing_date::date) > 5
                             AND date_trunc('month', writing_date) <> date_trunc('month', arrival_at)) AS LateCrossMonth,
            COUNT(*) FILTER (WHERE writing_date IS NOT NULL
                             AND (arrival_at::date - writing_date::date) BETWEEN 0 AND 5) AS OnTime,
            ROUND(AVG(arrival_at::date - writing_date::date) FILTER (
                WHERE writing_date IS NOT NULL AND (arrival_at::date - writing_date::date) > 5), 1)
                ::double precision AS AvgLateDays,
            MAX(arrival_at::date - writing_date::date) FILTER (
                WHERE writing_date IS NOT NULL AND (arrival_at::date - writing_date::date) > 5)
                ::double precision AS MaxLateDays,
            MAX(arrival_at) AS LatestArrivalDate
        FROM core.notes n
        WHERE n.is_deleted = FALSE    AND (@From IS NULL OR n.arrival_at >= @From)
            AND (@To IS NULL OR n.arrival_at < @To + 1)
            AND (@CompanyId IS NULL OR n.company_id = @CompanyId)
            AND (@FactoryId IS NULL OR n.factory_id = @FactoryId)
            AND (@ProductionLineId IS NULL OR n.production_line_id = @ProductionLineId)
            AND (@NoteTypeId IS NULL OR n.note_type_id = @NoteTypeId)
            AND (@Status IS NULL OR n.status = @Status)
            AND (@EntryCutoffDate IS NULL OR n.created_at < @EntryCutoffDate + 1);
        SELECT
            (
                SELECT COUNT(*)
                FROM core.companies c
                WHERE c.is_active = TRUE AND c.is_deleted = FALSE
            ) AS ActiveCompaniesTotal,
            (
                SELECT COUNT(DISTINCT c.id)
                FROM core.companies c
                JOIN core.notes n ON n.company_id = c.id AND n.is_deleted = FALSE
                WHERE c.is_active = TRUE AND c.is_deleted = FALSE
                    AND (@From IS NULL OR n.arrival_at >= @From)
                    AND (@To IS NULL OR n.arrival_at < @To + 1)
                    AND (@CompanyId IS NULL OR n.company_id = @CompanyId)
                    AND (@FactoryId IS NULL OR n.factory_id = @FactoryId)
                    AND (@ProductionLineId IS NULL OR n.production_line_id = @ProductionLineId)
                    AND (@NoteTypeId IS NULL OR n.note_type_id = @NoteTypeId)
                    AND (@Status IS NULL OR n.status = @Status)
                    AND (@EntryCutoffDate IS NULL OR n.created_at < @EntryCutoffDate + 1)
            ) AS CompaniesWithNotes;
        SELECT d.classification AS Classification, COUNT(*) AS Count
        FROM core.duplicate_incidents d
        JOIN core.notes reference_note ON reference_note.id = CASE d.classification
            WHEN 'COMPANY_DUPLICATE_NUMBER' THEN d.created_note_id
            WHEN 'COMPANY_RESEND' THEN d.matched_note_id
            ELSE NULL
        END
        WHERE 1 = 1    AND reference_note.is_deleted = FALSE
            AND (@From IS NULL OR reference_note.arrival_at >= @From)
            AND (@To IS NULL OR reference_note.arrival_at < @To + 1)
            AND (@CompanyId IS NULL OR reference_note.company_id = @CompanyId)
            AND (@FactoryId IS NULL OR reference_note.factory_id = @FactoryId)
            AND (@ProductionLineId IS NULL OR reference_note.production_line_id = @ProductionLineId)
            AND (@NoteTypeId IS NULL OR reference_note.note_type_id = @NoteTypeId)
            AND (@Status IS NULL OR reference_note.status = @Status)
            AND (@EntryCutoffDate IS NULL OR reference_note.created_at < @EntryCutoffDate + 1)GROUP BY d.classification;
        
        SELECT c.id AS CompanyId, c.name AS CompanyName, COUNT(n.id) AS NoteCount
        FROM core.companies c
        LEFT JOIN core.notes n ON n.company_id = c.id
            AND n.is_deleted = FALSE
            AND (@From IS NULL OR n.arrival_at >= @From)
            AND (@To IS NULL OR n.arrival_at < @To + 1)
            AND (@FactoryId IS NULL OR n.factory_id = @FactoryId)
            AND (@ProductionLineId IS NULL OR n.production_line_id = @ProductionLineId)
            AND (@NoteTypeId IS NULL OR n.note_type_id = @NoteTypeId)
            AND (@Status IS NULL OR n.status = @Status)
            AND (@EntryCutoffDate IS NULL OR n.created_at < @EntryCutoffDate + 1)
        WHERE c.is_active = TRUE AND c.is_deleted = FALSE
            AND (@CompanyId IS NULL OR c.id = @CompanyId)
        GROUP BY c.id, c.name
        ORDER BY NoteCount DESC, c.name;        
        SELECT c.id AS DimensionId,
               c.name AS Label,
               COUNT(n.id) AS TotalNotes,
               COUNT(n.id) FILTER (WHERE n.writing_date IS NOT NULL AND n.arrival_at >= n.writing_date) AS EligibleCount,
               COUNT(n.id) FILTER (WHERE n.writing_date IS NOT NULL AND n.arrival_at >= n.writing_date
                                    AND (n.arrival_at::date - n.writing_date::date) > 5) AS LateCount,
               COUNT(n.id) FILTER (WHERE n.writing_date IS NOT NULL AND n.arrival_at >= n.writing_date
                                    AND (n.arrival_at::date - n.writing_date::date) BETWEEN 0 AND 5) AS OnTimeCount,
               0 AS SampleCount,
               0 AS CorrectCount,
               0 AS ErrorCount,
               0 AS QualityEligibleCount
        FROM core.companies c
        LEFT JOIN core.notes n ON n.company_id = c.id
            AND n.is_deleted = FALSE
            AND (@From IS NULL OR n.arrival_at >= @From)
            AND (@To IS NULL OR n.arrival_at < @To + 1)
            AND (@FactoryId IS NULL OR n.factory_id = @FactoryId)
            AND (@ProductionLineId IS NULL OR n.production_line_id = @ProductionLineId)
            AND (@NoteTypeId IS NULL OR n.note_type_id = @NoteTypeId)
            AND (@Status IS NULL OR n.status = @Status)
            AND (@EntryCutoffDate IS NULL OR n.created_at < @EntryCutoffDate + 1)
        WHERE c.is_active = TRUE AND c.is_deleted = FALSE
            AND (@CompanyId IS NULL OR c.id = @CompanyId)
        GROUP BY c.id, c.name
        ORDER BY TotalNotes DESC, c.name;
        
        SELECT date_trunc('month', arrival_at)::date AS MonthStart, COUNT(*) AS Count
        FROM core.notes n
        WHERE n.is_deleted = FALSE
    AND (@From IS NULL OR n.arrival_at >= @From)
            AND (@To IS NULL OR n.arrival_at < @To + 1)
            AND (@CompanyId IS NULL OR n.company_id = @CompanyId)
            AND (@FactoryId IS NULL OR n.factory_id = @FactoryId)
            AND (@ProductionLineId IS NULL OR n.production_line_id = @ProductionLineId)
            AND (@NoteTypeId IS NULL OR n.note_type_id = @NoteTypeId)
            AND (@Status IS NULL OR n.status = @Status)
            AND (@EntryCutoffDate IS NULL OR n.created_at < @EntryCutoffDate + 1)GROUP BY 1
        ORDER BY 1;
        
        SELECT status AS Status, COUNT(*) AS Count
        FROM core.notes n
        WHERE n.is_deleted = FALSE    AND (@From IS NULL OR n.arrival_at >= @From)
            AND (@To IS NULL OR n.arrival_at < @To + 1)
            AND (@CompanyId IS NULL OR n.company_id = @CompanyId)
            AND (@FactoryId IS NULL OR n.factory_id = @FactoryId)
            AND (@ProductionLineId IS NULL OR n.production_line_id = @ProductionLineId)
            AND (@NoteTypeId IS NULL OR n.note_type_id = @NoteTypeId)
            AND (@Status IS NULL OR n.status = @Status)
            AND (@EntryCutoffDate IS NULL OR n.created_at < @EntryCutoffDate + 1)GROUP BY status
        ORDER BY Count DESC;
        
        SELECT n.id AS Id,
               n.note_number::text AS NoteNumber,
               c.name AS CompanyName,
               f.name AS FactoryName,
               pl.name AS ProductionLineName,
               nt.name_ar AS NoteTypeName,
               n.status AS Status,
               n.writing_date AS WritingDate,
               n.arrival_at AS ArrivalAt
        FROM core.notes n
        LEFT JOIN core.companies c ON c.id = n.company_id
        LEFT JOIN core.factories f ON f.id = n.factory_id
        LEFT JOIN core.production_lines pl ON pl.id = n.production_line_id
        LEFT JOIN core.note_types nt ON nt.id = n.note_type_id
        WHERE n.is_deleted = FALSE    AND (@From IS NULL OR n.arrival_at >= @From)
            AND (@To IS NULL OR n.arrival_at < @To + 1)
            AND (@CompanyId IS NULL OR n.company_id = @CompanyId)
            AND (@FactoryId IS NULL OR n.factory_id = @FactoryId)
            AND (@ProductionLineId IS NULL OR n.production_line_id = @ProductionLineId)
            AND (@NoteTypeId IS NULL OR n.note_type_id = @NoteTypeId)
            AND (@Status IS NULL OR n.status = @Status)
            AND (@EntryCutoffDate IS NULL OR n.created_at < @EntryCutoffDate + 1)ORDER BY n.arrival_at DESC, n.created_at DESC
        LIMIT 10;
        
        SELECT
            COUNT(*) FILTER (WHERE n.writing_date IS NOT NULL AND n.arrival_at >= n.writing_date) AS EligibleNotes,
            COUNT(*) FILTER (WHERE n.writing_date IS NOT NULL AND n.arrival_at >= n.writing_date
                             AND (n.arrival_at::date - n.writing_date::date) <= 5) AS OnTimeNotes,
            COUNT(*) FILTER (WHERE n.writing_date IS NOT NULL AND n.arrival_at >= n.writing_date
                             AND (n.arrival_at::date - n.writing_date::date) > 5) AS LateNotes,
            COUNT(*) FILTER (WHERE n.writing_date IS NOT NULL AND n.arrival_at < n.writing_date) AS InvalidDateOrderNotes
        FROM core.notes n
        WHERE n.is_deleted = FALSE    AND (@From IS NULL OR n.arrival_at >= @From)
            AND (@To IS NULL OR n.arrival_at < @To + 1)
            AND (@CompanyId IS NULL OR n.company_id = @CompanyId)
            AND (@FactoryId IS NULL OR n.factory_id = @FactoryId)
            AND (@ProductionLineId IS NULL OR n.production_line_id = @ProductionLineId)
            AND (@NoteTypeId IS NULL OR n.note_type_id = @NoteTypeId)
            AND (@Status IS NULL OR n.status = @Status)
            AND (@EntryCutoffDate IS NULL OR n.created_at < @EntryCutoffDate + 1);
        SELECT CASE
                WHEN (@CurrentDate - base.AnchorDate) < 1 THEN 'lt-1-day'
                WHEN (@CurrentDate - base.AnchorDate) <= 3 THEN '1-3-days'
                WHEN (@CurrentDate - base.AnchorDate) <= 7 THEN '4-7-days'
                ELSE 'gt-7-days'
               END AS Bucket,
               COUNT(*) AS Count
        FROM (
            SELECT COALESCE(n.review_started_at, n.created_at, n.arrival_at)::date AS AnchorDate
            FROM core.notes n
            WHERE n.is_deleted = FALSE
              AND n.status IN ('COMPLETE', 'HAS_ERRORS', 'NEEDS_REVIEW', 'UNDER_REVIEW')    AND (@From IS NULL OR n.arrival_at >= @From)
            AND (@To IS NULL OR n.arrival_at < @To + 1)
            AND (@CompanyId IS NULL OR n.company_id = @CompanyId)
            AND (@FactoryId IS NULL OR n.factory_id = @FactoryId)
            AND (@ProductionLineId IS NULL OR n.production_line_id = @ProductionLineId)
            AND (@NoteTypeId IS NULL OR n.note_type_id = @NoteTypeId)
            AND (@Status IS NULL OR n.status = @Status)
            AND (@EntryCutoffDate IS NULL OR n.created_at < @EntryCutoffDate + 1)) base
        GROUP BY 1;
        
        WITH incoming AS (
            SELECT date_trunc('month', n.arrival_at)::date AS Label,
                   COUNT(*) AS Incoming
            FROM core.notes n
            WHERE n.is_deleted = FALSE
              AND n.arrival_at IS NOT NULL
              AND (@From IS NULL OR n.arrival_at >= @From)
              AND (@To IS NULL OR n.arrival_at < @To + 1)
              AND (@CompanyId IS NULL OR n.company_id = @CompanyId)
              AND (@FactoryId IS NULL OR n.factory_id = @FactoryId)
              AND (@ProductionLineId IS NULL OR n.production_line_id = @ProductionLineId)
              AND (@NoteTypeId IS NULL OR n.note_type_id = @NoteTypeId)
              AND (@Status IS NULL OR n.status = @Status)
              AND (@EntryCutoffDate IS NULL OR n.created_at < @EntryCutoffDate + 1)
            GROUP BY 1
        ),
        approved AS (
            SELECT date_trunc('month', n.approved_at)::date AS Label,
                   COUNT(*) AS Approved
            FROM core.notes n
            WHERE n.is_deleted = FALSE
              AND n.approved_at IS NOT NULL
              AND (@From IS NULL OR n.approved_at >= @From)
              AND (@To IS NULL OR n.approved_at < @To + 1)
              AND (@CompanyId IS NULL OR n.company_id = @CompanyId)
              AND (@FactoryId IS NULL OR n.factory_id = @FactoryId)
              AND (@ProductionLineId IS NULL OR n.production_line_id = @ProductionLineId)
              AND (@NoteTypeId IS NULL OR n.note_type_id = @NoteTypeId)
              AND (@Status IS NULL OR n.status = @Status)
              AND (@EntryCutoffDate IS NULL OR n.created_at < @EntryCutoffDate + 1)
            GROUP BY 1
        )
        SELECT COALESCE(incoming.Label, approved.Label) AS Label,
               COALESCE(incoming.Incoming, 0) AS Incoming,
               COALESCE(approved.Approved, 0) AS Approved
        FROM incoming
        FULL OUTER JOIN approved ON approved.Label = incoming.Label
        ORDER BY 1;
        

        WITH note_quality AS (
            SELECT f.note_id,
                   f.company_id,
                   count(*)::bigint AS expected_items,
                   count(*) FILTER (WHERE f.is_unreviewed)::bigint AS unreviewed_items,
                   count(*) FILTER (WHERE f.is_error)::bigint AS error_items,
                   count(*) FILTER (WHERE f.is_applicable_reviewed)::bigint AS applicable_reviewed_items,
                   count(*) FILTER (WHERE f.is_not_applicable)::bigint AS not_applicable_items
            FROM reporting.vw_note_indicator_facts_v3 f
            JOIN core.notes n ON n.id = f.note_id AND n.is_deleted = FALSE
            WHERE (@From IS NULL OR n.arrival_at >= @From)
              AND (@To IS NULL OR n.arrival_at < @To + 1)
              AND (@CompanyId IS NULL OR n.company_id = @CompanyId)
              AND (@FactoryId IS NULL OR n.factory_id = @FactoryId)
              AND (@ProductionLineId IS NULL OR n.production_line_id = @ProductionLineId)
              AND (@NoteTypeId IS NULL OR n.note_type_id = @NoteTypeId)
              AND (@Status IS NULL OR n.status = @Status)
            AND (@EntryCutoffDate IS NULL OR n.created_at < @EntryCutoffDate + 1)
            GROUP BY f.note_id, f.company_id
        )
        SELECT c.id AS CompanyId,
               c.name AS CompanyName,
               COUNT(*) AS SampleSize,
               COUNT(*) FILTER (
                   WHERE q.error_items = 0
                     AND q.unreviewed_items = 0
                     AND q.applicable_reviewed_items > 0) AS CorrectCount,
               COUNT(*) FILTER (WHERE q.error_items > 0) AS ErrorCount,
               COUNT(*) FILTER (
                   WHERE q.error_items > 0
                      OR (q.error_items = 0 AND q.unreviewed_items = 0 AND q.applicable_reviewed_items > 0)) AS QualityEligibleCount,
               COUNT(*) FILTER (
                   WHERE q.error_items = 0 AND q.unreviewed_items = 0
                     AND q.applicable_reviewed_items = 0
                     AND q.not_applicable_items = q.expected_items) AS AllNotApplicableCount
        FROM note_quality q
        JOIN core.companies c ON c.id = q.company_id
        GROUP BY c.id, c.name
        ORDER BY ErrorCount DESC, SampleSize DESC
        ;
        
        SELECT f.company_id AS CompanyId,
               c.name AS CompanyName,
               COUNT(*) FILTER (WHERE f.is_applicable_reviewed) AS TotalFieldCount,
               COUNT(*) FILTER (WHERE f.is_applicable_reviewed AND f.is_correct) AS CorrectFieldCount,
               COUNT(*) FILTER (WHERE f.is_applicable_reviewed AND f.is_error) AS ErrorFieldCount,
               COUNT(*) FILTER (WHERE f.is_unreviewed) AS UnknownFieldCount
        FROM reporting.vw_note_indicator_facts_v3 f
        JOIN core.notes n ON n.id = f.note_id AND n.is_deleted = FALSE
        JOIN core.companies c ON c.id = f.company_id
        WHERE (@From IS NULL OR n.arrival_at >= @From)
          AND (@To IS NULL OR n.arrival_at < @To + 1)
          AND (@CompanyId IS NULL OR n.company_id = @CompanyId)
          AND (@FactoryId IS NULL OR n.factory_id = @FactoryId)
          AND (@ProductionLineId IS NULL OR n.production_line_id = @ProductionLineId)
          AND (@NoteTypeId IS NULL OR n.note_type_id = @NoteTypeId)
          AND (@Status IS NULL OR n.status = @Status)
          AND (@EntryCutoffDate IS NULL OR n.created_at < @EntryCutoffDate + 1)
        GROUP BY f.company_id, c.name
        ORDER BY ErrorFieldCount DESC, TotalFieldCount DESC;
        SELECT f.field_id AS FieldId,
               f.field_name AS FieldName,
               COUNT(*) FILTER (WHERE f.is_applicable_reviewed) AS SampleSize,
               COUNT(*) FILTER (WHERE f.is_applicable_reviewed AND f.is_error) AS ErrorCount
        FROM reporting.vw_note_indicator_facts_v3 f
        JOIN core.notes n ON n.id = f.note_id AND n.is_deleted = FALSE
        WHERE (@From IS NULL OR n.arrival_at >= @From)
          AND (@To IS NULL OR n.arrival_at < @To + 1)
          AND (@CompanyId IS NULL OR n.company_id = @CompanyId)
          AND (@FactoryId IS NULL OR n.factory_id = @FactoryId)
          AND (@ProductionLineId IS NULL OR n.production_line_id = @ProductionLineId)
          AND (@NoteTypeId IS NULL OR n.note_type_id = @NoteTypeId)
          AND (@Status IS NULL OR n.status = @Status)
          AND (@EntryCutoffDate IS NULL OR n.created_at < @EntryCutoffDate + 1)
        GROUP BY f.field_id, f.field_name
        HAVING COUNT(*) FILTER (WHERE f.is_applicable_reviewed) >= @MinSample
        ORDER BY ErrorCount DESC, SampleSize DESC
        LIMIT 10;
        
        SELECT f.effective_field_value AS Code, COUNT(*) AS Value
        FROM reporting.vw_note_indicator_facts_v3 f
        JOIN core.notes n ON n.id = f.note_id AND n.is_deleted = FALSE
        WHERE (@From IS NULL OR n.arrival_at >= @From)
          AND (@To IS NULL OR n.arrival_at < @To + 1)
          AND (@CompanyId IS NULL OR n.company_id = @CompanyId)
          AND (@FactoryId IS NULL OR n.factory_id = @FactoryId)
          AND (@ProductionLineId IS NULL OR n.production_line_id = @ProductionLineId)
          AND (@NoteTypeId IS NULL OR n.note_type_id = @NoteTypeId)
          AND (@Status IS NULL OR n.status = @Status)
          AND (@EntryCutoffDate IS NULL OR n.created_at < @EntryCutoffDate + 1)
        GROUP BY f.effective_field_value
        ORDER BY f.effective_field_value;
        
        WITH note_quality AS (
            SELECT f.note_id,
                   count(*) FILTER (WHERE f.is_error)::int AS error_count,
                   count(*) FILTER (WHERE f.is_unreviewed)::int AS unreviewed_count,
                   count(*) FILTER (WHERE f.is_applicable_reviewed)::int AS applicable_count
            FROM reporting.vw_note_indicator_facts_v3 f
            GROUP BY f.note_id
        ),
        quality AS (
            SELECT n.factory_id AS FactoryId,
                   COUNT(*) AS SampleCount,
                   COUNT(*) FILTER (
                       WHERE q.error_count = 0
                         AND q.unreviewed_count = 0
                         AND q.applicable_count > 0) AS CorrectCount,
                   COUNT(*) FILTER (WHERE q.error_count > 0) AS ErrorCount,
                   COUNT(*) FILTER (
                       WHERE q.error_count > 0
                          OR (q.error_count = 0 AND q.unreviewed_count = 0 AND q.applicable_count > 0)) AS QualityEligibleCount
            FROM core.notes n
            JOIN note_quality q ON q.note_id = n.id
            WHERE n.is_deleted = FALSE
              AND (@From IS NULL OR n.arrival_at >= @From)
              AND (@To IS NULL OR n.arrival_at < @To + 1)
              AND (@CompanyId IS NULL OR n.company_id = @CompanyId)
              AND (@FactoryId IS NULL OR n.factory_id = @FactoryId)
              AND (@ProductionLineId IS NULL OR n.production_line_id = @ProductionLineId)
              AND (@NoteTypeId IS NULL OR n.note_type_id = @NoteTypeId)
              AND (@Status IS NULL OR n.status = @Status)
            AND (@EntryCutoffDate IS NULL OR n.created_at < @EntryCutoffDate + 1)
            GROUP BY n.factory_id
        )
        SELECT f.id AS DimensionId,
               f.name AS Label,
               COUNT(n.id) AS TotalNotes,
               COUNT(*) FILTER (WHERE n.writing_date IS NOT NULL AND n.arrival_at >= n.writing_date) AS EligibleCount,
               COUNT(*) FILTER (WHERE n.writing_date IS NOT NULL AND n.arrival_at >= n.writing_date
                                AND (n.arrival_at::date - n.writing_date::date) > 5) AS LateCount,
                COUNT(*) FILTER (WHERE n.writing_date IS NOT NULL AND n.arrival_at >= n.writing_date
                                 AND (n.arrival_at::date - n.writing_date::date) BETWEEN 0 AND 5) AS OnTimeCount,
               COALESCE(q.SampleCount, 0) AS SampleCount,
               COALESCE(q.CorrectCount, 0) AS CorrectCount,
                COALESCE(q.ErrorCount, 0) AS ErrorCount,
                COALESCE(q.QualityEligibleCount, 0) AS QualityEligibleCount
        FROM core.notes n
        JOIN core.factories f ON f.id = n.factory_id AND f.is_deleted = FALSE
        LEFT JOIN quality q ON q.FactoryId = f.id
        WHERE n.is_deleted = FALSE
          AND n.factory_id IS NOT NULL    AND (@From IS NULL OR n.arrival_at >= @From)
            AND (@To IS NULL OR n.arrival_at < @To + 1)
            AND (@CompanyId IS NULL OR n.company_id = @CompanyId)
            AND (@FactoryId IS NULL OR n.factory_id = @FactoryId)
            AND (@ProductionLineId IS NULL OR n.production_line_id = @ProductionLineId)
            AND (@NoteTypeId IS NULL OR n.note_type_id = @NoteTypeId)
            AND (@Status IS NULL OR n.status = @Status)
            AND (@EntryCutoffDate IS NULL OR n.created_at < @EntryCutoffDate + 1)GROUP BY f.id, f.name, q.SampleCount, q.CorrectCount, q.ErrorCount, q.QualityEligibleCount
        ORDER BY TotalNotes DESC
        LIMIT 10;
        
        WITH note_quality AS (
            SELECT f.note_id,
                   count(*) FILTER (WHERE f.is_error)::int AS error_count,
                   count(*) FILTER (WHERE f.is_unreviewed)::int AS unreviewed_count,
                   count(*) FILTER (WHERE f.is_applicable_reviewed)::int AS applicable_count
            FROM reporting.vw_note_indicator_facts_v3 f
            GROUP BY f.note_id
        ),
        quality AS (
            SELECT n.production_line_id AS LineId,
                   COUNT(*) AS SampleCount,
                   COUNT(*) FILTER (
                       WHERE q.error_count = 0
                         AND q.unreviewed_count = 0
                         AND q.applicable_count > 0) AS CorrectCount,
                   COUNT(*) FILTER (WHERE q.error_count > 0) AS ErrorCount,
                   COUNT(*) FILTER (
                       WHERE q.error_count > 0
                          OR (q.error_count = 0 AND q.unreviewed_count = 0 AND q.applicable_count > 0)) AS QualityEligibleCount
            FROM core.notes n
            JOIN note_quality q ON q.note_id = n.id
            WHERE n.is_deleted = FALSE
              AND (@From IS NULL OR n.arrival_at >= @From)
              AND (@To IS NULL OR n.arrival_at < @To + 1)
              AND (@CompanyId IS NULL OR n.company_id = @CompanyId)
              AND (@FactoryId IS NULL OR n.factory_id = @FactoryId)
              AND (@ProductionLineId IS NULL OR n.production_line_id = @ProductionLineId)
              AND (@NoteTypeId IS NULL OR n.note_type_id = @NoteTypeId)
              AND (@Status IS NULL OR n.status = @Status)
            AND (@EntryCutoffDate IS NULL OR n.created_at < @EntryCutoffDate + 1)
            GROUP BY n.production_line_id
        )
        SELECT pl.id AS DimensionId,
               pl.name AS Label,
               COUNT(n.id) AS TotalNotes,
               COUNT(*) FILTER (WHERE n.writing_date IS NOT NULL AND n.arrival_at >= n.writing_date) AS EligibleCount,
               COUNT(*) FILTER (WHERE n.writing_date IS NOT NULL AND n.arrival_at >= n.writing_date
                                AND (n.arrival_at::date - n.writing_date::date) > 5) AS LateCount,
                COUNT(*) FILTER (WHERE n.writing_date IS NOT NULL AND n.arrival_at >= n.writing_date
                                 AND (n.arrival_at::date - n.writing_date::date) BETWEEN 0 AND 5) AS OnTimeCount,
               COALESCE(q.SampleCount, 0) AS SampleCount,
               COALESCE(q.CorrectCount, 0) AS CorrectCount,
                COALESCE(q.ErrorCount, 0) AS ErrorCount,
                COALESCE(q.QualityEligibleCount, 0) AS QualityEligibleCount
        FROM core.notes n
        JOIN core.production_lines pl ON pl.id = n.production_line_id AND pl.is_deleted = FALSE
        LEFT JOIN quality q ON q.LineId = pl.id
        WHERE n.is_deleted = FALSE
          AND n.production_line_id IS NOT NULL    AND (@From IS NULL OR n.arrival_at >= @From)
            AND (@To IS NULL OR n.arrival_at < @To + 1)
            AND (@CompanyId IS NULL OR n.company_id = @CompanyId)
            AND (@FactoryId IS NULL OR n.factory_id = @FactoryId)
            AND (@ProductionLineId IS NULL OR n.production_line_id = @ProductionLineId)
            AND (@NoteTypeId IS NULL OR n.note_type_id = @NoteTypeId)
            AND (@Status IS NULL OR n.status = @Status)
            AND (@EntryCutoffDate IS NULL OR n.created_at < @EntryCutoffDate + 1)GROUP BY pl.id, pl.name, q.SampleCount, q.CorrectCount, q.ErrorCount, q.QualityEligibleCount
        ORDER BY TotalNotes DESC
        LIMIT 10;
        
        SELECT to_char(date_trunc('month', reference_note.arrival_at), 'YYYY-MM') AS Label,
               COUNT(*) FILTER (WHERE d.classification = 'COMPANY_RESEND') AS CompanyResend,
               COUNT(*) FILTER (WHERE d.classification = 'COMPANY_DUPLICATE_NUMBER') AS DuplicateNumber
        FROM core.duplicate_incidents d
        JOIN core.notes reference_note ON reference_note.id = CASE d.classification
            WHEN 'COMPANY_DUPLICATE_NUMBER' THEN d.created_note_id
            WHEN 'COMPANY_RESEND' THEN d.matched_note_id
            ELSE NULL
        END
        WHERE 1 = 1    AND reference_note.is_deleted = FALSE
            AND (@From IS NULL OR reference_note.arrival_at >= @From)
            AND (@To IS NULL OR reference_note.arrival_at < @To + 1)
            AND (@CompanyId IS NULL OR reference_note.company_id = @CompanyId)
            AND (@FactoryId IS NULL OR reference_note.factory_id = @FactoryId)
            AND (@ProductionLineId IS NULL OR reference_note.production_line_id = @ProductionLineId)
            AND (@NoteTypeId IS NULL OR reference_note.note_type_id = @NoteTypeId)
            AND (@Status IS NULL OR reference_note.status = @Status)
            AND (@EntryCutoffDate IS NULL OR reference_note.created_at < @EntryCutoffDate + 1)GROUP BY 1
        ORDER BY 1;
        
        SELECT
            COUNT(*) FILTER (WHERE n.writing_date IS NULL) AS MissingWritingDate,
            COUNT(*) FILTER (WHERE n.factory_id IS NULL) AS MissingFactory,
            COUNT(*) FILTER (WHERE n.production_line_id IS NULL) AS MissingProductionLine
        FROM core.notes n
        WHERE n.is_deleted = FALSE    AND (@From IS NULL OR n.arrival_at >= @From)
            AND (@To IS NULL OR n.arrival_at < @To + 1)
            AND (@CompanyId IS NULL OR n.company_id = @CompanyId)
            AND (@FactoryId IS NULL OR n.factory_id = @FactoryId)
            AND (@ProductionLineId IS NULL OR n.production_line_id = @ProductionLineId)
            AND (@NoteTypeId IS NULL OR n.note_type_id = @NoteTypeId)
            AND (@Status IS NULL OR n.status = @Status)
            AND (@EntryCutoffDate IS NULL OR n.created_at < @EntryCutoffDate + 1);

        -- Dapper maps the embedded company metrics directly to CompanyMetrics.
        -- Keep the database contract's snake_case names explicit here so the
        -- snapshot does not silently deserialize every KPI to its default value.
        SELECT
            total_notes AS "TotalNotes",
            expected_items AS "ExpectedItems",
            reviewed_items AS "ReviewedItems",
            unreviewed_items AS "UnreviewedItems",
            applicable_reviewed_items AS "ApplicableReviewedItems",
            correct_items AS "CorrectItems",
            error_items AS "ErrorItems",
            not_applicable_items AS "NotApplicableItems",
            review_coverage_percentage AS "ReviewCoveragePercentage",
            item_quality_percentage AS "ItemQualityPercentage",
            errors_per_100_checked AS "ErrorsPer100Checked",
            quality_eligible_note_count AS "QualityEligibleNoteCount",
            clean_reviewed_notes AS "CleanReviewedNotes",
            error_notes AS "ErrorNotes",
            incomplete_review_notes AS "IncompleteReviewNotes",
            all_not_applicable_note_count AS "AllNotApplicableNoteCount",
            no_expected_items_note_count AS "NoExpectedItemsNoteCount",
            clean_reviewed_note_percent AS "CleanReviewedNotePercent",
            time_eligible_notes AS "TimeEligibleNotes",
            on_time_notes AS "OnTimeNotes",
            late_notes AS "LateNotes",
            late_6_to_10_notes AS "Late6To10Notes",
            late_11_to_20_notes AS "Late11To20Notes",
            late_over_20_notes AS "LateOver20Notes",
            missing_writing_date_notes AS "MissingWritingDateNotes",
            invalid_date_order_notes AS "InvalidDateOrderNotes",
            time_compliance_percentage AS "TimeCompliancePercentage",
            average_late_days AS "AverageLateDays",
            median_late_days AS "MedianLateDays",
            p90_late_days AS "P90LateDays",
            maximum_late_days AS "MaximumLateDays",
            average_excess_delay_days AS "AverageExcessDelayDays",
            duplicate_number_incidents AS "DuplicateNumberIncidents",
            company_resend_incidents AS "CompanyResendIncidents",
            duplicate_incidents AS "DuplicateIncidents",
            affected_notes AS "AffectedNotes",
            incidents_per_100_notes AS "IncidentsPer100Notes",
            affected_note_rate_percentage AS "AffectedNoteRatePercentage",
            duplicate_free_percentage AS "DuplicateFreePercentage",
            entry_notes AS "EntryNotes",
            linked_entry_notes AS "LinkedEntryNotes",
            unlinked_entry_notes AS "UnlinkedEntryNotes",
            link_efficiency_percentage AS "LinkEfficiencyPercentage"
        FROM reporting.fn_company_metrics_snapshot_v4_filtered(
            @CompanyId, @From, @To, @FactoryId, @ProductionLineId, @NoteTypeId, @Status, @EntryCutoffDate, @DateBasis);
        SELECT
            total_notes AS "TotalNotes",
            expected_items AS "ExpectedItems",
            reviewed_items AS "ReviewedItems",
            unreviewed_items AS "UnreviewedItems",
            applicable_reviewed_items AS "ApplicableReviewedItems",
            correct_items AS "CorrectItems",
            error_items AS "ErrorItems",
            not_applicable_items AS "NotApplicableItems",
            review_coverage_percentage AS "ReviewCoveragePercentage",
            item_quality_percentage AS "ItemQualityPercentage",
            errors_per_100_checked AS "ErrorsPer100Checked",
            quality_eligible_note_count AS "QualityEligibleNoteCount",
            clean_reviewed_notes AS "CleanReviewedNotes",
            error_notes AS "ErrorNotes",
            incomplete_review_notes AS "IncompleteReviewNotes",
            all_not_applicable_note_count AS "AllNotApplicableNoteCount",
            no_expected_items_note_count AS "NoExpectedItemsNoteCount",
            clean_reviewed_note_percent AS "CleanReviewedNotePercent",
            time_eligible_notes AS "TimeEligibleNotes",
            on_time_notes AS "OnTimeNotes",
            late_notes AS "LateNotes",
            late_6_to_10_notes AS "Late6To10Notes",
            late_11_to_20_notes AS "Late11To20Notes",
            late_over_20_notes AS "LateOver20Notes",
            missing_writing_date_notes AS "MissingWritingDateNotes",
            invalid_date_order_notes AS "InvalidDateOrderNotes",
            time_compliance_percentage AS "TimeCompliancePercentage",
            average_late_days AS "AverageLateDays",
            median_late_days AS "MedianLateDays",
            p90_late_days AS "P90LateDays",
            maximum_late_days AS "MaximumLateDays",
            average_excess_delay_days AS "AverageExcessDelayDays",
            duplicate_number_incidents AS "DuplicateNumberIncidents",
            company_resend_incidents AS "CompanyResendIncidents",
            duplicate_incidents AS "DuplicateIncidents",
            affected_notes AS "AffectedNotes",
            incidents_per_100_notes AS "IncidentsPer100Notes",
            affected_note_rate_percentage AS "AffectedNoteRatePercentage",
            duplicate_free_percentage AS "DuplicateFreePercentage",
            entry_notes AS "EntryNotes",
            linked_entry_notes AS "LinkedEntryNotes",
            unlinked_entry_notes AS "UnlinkedEntryNotes",
            link_efficiency_percentage AS "LinkEfficiencyPercentage"
        FROM reporting.fn_company_metrics_snapshot_v4_filtered(
            @PreviousCompanyId, @PreviousFrom, @PreviousTo, @PreviousFactoryId, @PreviousProductionLineId, @PreviousNoteTypeId, @PreviousStatus, @PreviousEntryCutoffDate, @DateBasis)
        WHERE @PreviousCompanyId IS NOT NULL AND @PreviousFrom IS NOT NULL;
    """;

    private const string LookupsSql = """
        SELECT id AS Id, name AS Name
        FROM core.companies
        WHERE is_active = TRUE AND is_deleted = FALSE
        ORDER BY name;

        SELECT id AS Id, company_id AS CompanyId, name AS Name
        FROM core.factories
        WHERE is_active = TRUE AND is_deleted = FALSE
        ORDER BY name;

        SELECT id AS Id, factory_id AS FactoryId, name AS Name
        FROM core.production_lines
        WHERE is_active = TRUE AND is_deleted = FALSE
        ORDER BY name;

        SELECT id AS Id, code AS Code, name_ar AS NameAr
        FROM core.note_types
        WHERE is_active = TRUE AND is_deleted = FALSE
        ORDER BY name_ar;
        """;

    private static string BuildSnapshotSql(string dateBasis)
    {
        var useWritingDate = string.Equals(dateBasis, "WritingDate", StringComparison.Ordinal);
        var notePeriod = useWritingDate
            ? "n.writing_date::date"
            : "n.arrival_at::date";
        var referencePeriod = useWritingDate
            ? "reference_note.writing_date::date"
            : "reference_note.arrival_at::date";
        var unaliasedPeriod = useWritingDate
            ? "writing_date::date"
            : "arrival_at::date";

        return SnapshotSql
            .Replace("n.arrival_at >= @From", $"{notePeriod} >= @From", StringComparison.Ordinal)
            .Replace("n.arrival_at < @To + 1", $"{notePeriod} <= @To", StringComparison.Ordinal)
            .Replace("reference_note.arrival_at >= @From", $"{referencePeriod} >= @From", StringComparison.Ordinal)
            .Replace("reference_note.arrival_at < @To + 1", $"{referencePeriod} <= @To", StringComparison.Ordinal)
            .Replace("n.created_at < @EntryCutoffDate + 1", "n.arrival_at::date <= @EntryCutoffDate", StringComparison.Ordinal)
            .Replace("reference_note.created_at < @EntryCutoffDate + 1", "reference_note.arrival_at::date <= @EntryCutoffDate", StringComparison.Ordinal)
            .Replace("arrival_at::date = @CurrentDate", $"{unaliasedPeriod} = @CurrentDate", StringComparison.Ordinal)
            .Replace("date_trunc('month', arrival_at)", $"date_trunc('month', {unaliasedPeriod})", StringComparison.Ordinal)
            .Replace("date_trunc('month', n.arrival_at)", $"date_trunc('month', {notePeriod})", StringComparison.Ordinal)
            .Replace("date_trunc('month', reference_note.arrival_at)", $"date_trunc('month', {referencePeriod})", StringComparison.Ordinal);
    }

    public DashboardQueryService(ConfigurationService configuration, ILogger<DashboardQueryService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<DashboardSnapshot> GetSnapshotAsync(
        FilterQuery? filter,
        int minSample = 10,
        CancellationToken cancellationToken = default)
    {
        var businessNow = GetBusinessNow();
        var requested = filter ?? new FilterQuery();
        var effectiveFrom = requested.From?.Date;
        var effectiveTo = requested.To?.Date;
        if (effectiveFrom.HasValue && effectiveTo.HasValue && effectiveFrom.Value > effectiveTo.Value)
        {
            throw new ArgumentException("يجب ألا يكون تاريخ البداية بعد تاريخ النهاية.", nameof(filter));
        }

        var effectiveFilter = new FilterQuery
        {
            From = effectiveFrom,
            To = effectiveTo,
            DateBasis = string.Equals(requested.DateBasis, "ArrivalDate", StringComparison.OrdinalIgnoreCase)
                ? "ArrivalDate"
                : "WritingDate",
            CompanyId = requested.CompanyId,
            FactoryId = requested.FactoryId,
            ProductionLineId = requested.ProductionLineId,
            NoteTypeId = requested.NoteTypeId,
            Status = requested.Status,
            EntryCutoffDate = requested.EntryCutoffDate?.Date
        };
        var previousFrom = (DateTime?)null;
        var previousTo = (DateTime?)null;
        var previousEntryCutoffDate = (DateTime?)null;
        var previousCompanyId = (Guid?)null;
        if (effectiveFrom.HasValue && effectiveTo.HasValue && effectiveFilter.CompanyId.HasValue)
        {
            var periodDays = (effectiveTo.Value.Date - effectiveFrom.Value.Date).Days + 1;
            previousTo = effectiveFrom.Value.Date.AddDays(-1);
            previousFrom = previousTo.Value.AddDays(-periodDays + 1);
            previousEntryCutoffDate = effectiveFilter.EntryCutoffDate?.Date.AddDays(-periodDays);
            previousCompanyId = effectiveFilter.CompanyId;
        }
        var snapshot = new DashboardSnapshot
        {
            ServerTime = businessNow,
            GeneratedAt = businessNow,
            MinSample = Math.Max(1, minSample),
            EffectiveFilters = new DashboardEffectiveFilters
            {
                From = effectiveFrom,
                To = effectiveTo,
                DateBasis = effectiveFilter.DateBasis,
                CompanyId = effectiveFilter.CompanyId,
                FactoryId = effectiveFilter.FactoryId,
                ProductionLineId = effectiveFilter.ProductionLineId,
                NoteTypeId = effectiveFilter.NoteTypeId,
                Status = effectiveFilter.Status,
                EntryCutoffDate = effectiveFilter.EntryCutoffDate
            },
            PreviousPeriod = previousFrom.HasValue
                ? new DashboardPeriod { From = previousFrom.Value, To = previousTo!.Value, DateBasis = effectiveFilter.DateBasis, EntryCutoffDate = previousEntryCutoffDate }
                : null
        };
        var connectionString = _configuration.BuildConnectionString();
        if (connectionString is null)
        {
            snapshot.IsHealthy = false;
            snapshot.ErrorMessage = "لم يتم إعداد الاتصال بقاعدة البيانات بعد. يرجى إكمال الإعداد أولاً.";
            return snapshot;
        }

        try
        {
            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition("SET TRANSACTION READ ONLY;", transaction: transaction, cancellationToken: cancellationToken));

            var parameters = new DynamicParameters();
            parameters.Add("From", effectiveFilter.From, DbType.Date);
            parameters.Add("To", effectiveFilter.To, DbType.Date);
            parameters.Add("DateBasis", effectiveFilter.DateBasis, DbType.String);
            parameters.Add("CompanyId", effectiveFilter.CompanyId, DbType.Guid);
            parameters.Add("FactoryId", effectiveFilter.FactoryId, DbType.Guid);
            parameters.Add("ProductionLineId", effectiveFilter.ProductionLineId, DbType.Guid);
            parameters.Add("NoteTypeId", effectiveFilter.NoteTypeId, DbType.Guid);
            parameters.Add("Status", effectiveFilter.Status, DbType.String);
            parameters.Add("EntryCutoffDate", effectiveFilter.EntryCutoffDate, DbType.Date);
            var currentDate = effectiveTo ?? businessNow.Date;
            parameters.Add("CurrentDate", currentDate, DbType.Date);
            parameters.Add("MinSample", snapshot.MinSample, DbType.Int32);

            parameters.Add("PreviousCompanyId", previousCompanyId, DbType.Guid);
            parameters.Add("PreviousFrom", previousFrom, DbType.Date);
            parameters.Add("PreviousTo", previousTo, DbType.Date);
            parameters.Add("PreviousFactoryId", effectiveFilter.FactoryId, DbType.Guid);
            parameters.Add("PreviousProductionLineId", effectiveFilter.ProductionLineId, DbType.Guid);
            parameters.Add("PreviousNoteTypeId", effectiveFilter.NoteTypeId, DbType.Guid);
            parameters.Add("PreviousStatus", effectiveFilter.Status, DbType.String);
            parameters.Add("PreviousEntryCutoffDate", previousEntryCutoffDate, DbType.Date);

            var snapshotCommand = new CommandDefinition(
                BuildSnapshotSql(effectiveFilter.DateBasis), parameters, transaction: transaction, commandTimeout: 30,
                cancellationToken: cancellationToken);
            using var multi = await connection.QueryMultipleAsync(snapshotCommand);

            snapshot.Summary = await multi.ReadSingleAsync<DashboardSummary>();
            snapshot.Summary.DuplicateIncidentCount = snapshot.Summary.CompanyResend + snapshot.Summary.DuplicateNumbers;
            snapshot.Summary.NonDuplicateNotes = Math.Max(0, snapshot.Summary.TotalNotes - snapshot.Summary.DuplicateAffectedNotes);
            var companyCounts = await multi.ReadSingleAsync<CompanyCountsRaw>();
            snapshot.Summary.ActiveCompaniesTotal = companyCounts.ActiveCompaniesTotal;
            snapshot.Summary.CompaniesWithNotes = companyCounts.CompaniesWithNotes;

            var duplicates = await multi.ReadAsync<DuplicateRaw>();
            snapshot.DuplicateBreakdown = duplicates
                .Select(d => new DuplicateBreakdownItem
                {
                    Classification = d.Classification,
                    Label = StatusTranslations.ClassificationLabel(d.Classification),
                    Count = d.Count
                })
                .OrderByDescending(d => d.Count)
                .ToList();

            var topCompanies = await multi.ReadAsync<CompanyTopRaw>();
            snapshot.Companies = topCompanies
                .Select(c => new CompanyTopItem { Id = c.CompanyId, Name = c.CompanyName, NoteCount = c.NoteCount })
                .ToList();

            var companyTimeliness = await multi.ReadAsync<PerfRaw>();
            snapshot.TimelinessByCompany = companyTimeliness.Select(BuildPerformance).ToList();

            var monthly = await multi.ReadAsync<MonthlyRaw>();
            snapshot.MonthlyActivity = BuildMonthlyActivity(monthly, effectiveFrom, effectiveTo);

            var statuses = await multi.ReadAsync<StatusRaw>();
            snapshot.StatusDistribution = statuses
                .Select(s => new StatusDistributionItem
                {
                    Status = s.Status,
                    StatusLabel = StatusTranslations.StatusLabel(s.Status),
                    Count = s.Count
                })
                .OrderByDescending(s => s.Count)
                .ToList();

            var recent = await multi.ReadAsync<RecentNoteRaw>();
            snapshot.RecentNotes = recent
                .Select(r => new RecentNoteItem
                {
                    Id = r.Id,
                    NoteNumber = r.NoteNumber,
                    CompanyName = r.CompanyName,
                    FactoryName = r.FactoryName,
                    ProductionLineName = r.ProductionLineName,
                    NoteTypeName = r.NoteTypeName,
                    Status = r.Status,
                    StatusLabel = StatusTranslations.StatusLabel(r.Status),
                    WritingDate = r.WritingDate,
                    ArrivalAt = r.ArrivalAt,
                    IsLate = DelayCalculator.IsLate(r.WritingDate, r.ArrivalAt.GetValueOrDefault())
                })
                .ToList();

            var slaRows = (await multi.ReadAsync<SlaRaw>()).ToList();
            if (slaRows.Count != 1)
            {
                _logger.LogError("Dashboard snapshot result-set contract failed for SLA: expected exactly one row, received {RowCount}.", slaRows.Count);
                throw new InvalidOperationException($"Dashboard snapshot SLA result set expected one row, received {slaRows.Count}.");
            }
            var slaRaw = slaRows[0];
            snapshot.Sla = MetricsCalculator.Sla(
                slaRaw.EligibleNotes, slaRaw.OnTimeNotes, slaRaw.LateNotes, slaRaw.InvalidDateOrderNotes);

            var backlog = await multi.ReadAsync<BacklogRaw>();
            snapshot.BacklogByAge = backlog
                .Select(b => new BacklogAgeItem
                {
                    Code = b.Bucket,
                    Label = BacklogBucketLabel(b.Bucket),
                    Value = b.Count
                })
                .OrderBy(b => BucketOrder(b.Code))
                .ToList();

            var incoming = await multi.ReadAsync<IncomingRaw>();
            snapshot.IncomingVsApproved = incoming
                .Select(i => new IncomingVsApprovedItem
                {
                    PeriodKey = i.Label.ToString("yyyy-MM"),
                    PeriodLabel = StatusTranslations.MonthName(i.Label.Month) + " " + i.Label.Year,
                    Label = StatusTranslations.MonthName(i.Label.Month) + " " + i.Label.Year,
                    Incoming = i.Incoming,
                    Approved = i.Approved
                })
                .ToList();

            var quality = await multi.ReadAsync<QualityRaw>();
            var fieldQualityRows = (await multi.ReadAsync<FieldQualityRaw>()).ToList();
            var fieldQuality = fieldQualityRows.ToDictionary(item => item.CompanyId);
            var totalFieldCount = fieldQualityRows.Sum(item => item.TotalFieldCount);
            var correctFieldCount = fieldQualityRows.Sum(item => item.CorrectFieldCount);
            snapshot.FieldQuality = new FieldQualitySummary
            {
                TotalFieldCount = totalFieldCount,
                CorrectFieldCount = correctFieldCount,
                ErrorFieldCount = fieldQualityRows.Sum(item => item.ErrorFieldCount),
                UnknownFieldCount = fieldQualityRows.Sum(item => item.UnknownFieldCount),
                AccuracyRate = MetricsCalculator.Rate(correctFieldCount, totalFieldCount)
            };
            snapshot.QualityByCompany = quality
                .Select(q =>
                {
                    var accuracy = MetricsCalculator.Rate(q.CorrectCount, q.QualityEligibleCount);
                    var error = MetricsCalculator.Rate(q.ErrorCount, q.QualityEligibleCount);
                    fieldQuality.TryGetValue(q.CompanyId, out var fields);
                    var totalFields = fields?.TotalFieldCount ?? 0;
                    var correctFields = fields?.CorrectFieldCount ?? 0;
                    return new QualityByCompanyItem
                    {
                        CompanyId = q.CompanyId,
                        CompanyName = q.CompanyName,
                        Label = q.CompanyName,
                        SampleSize = q.SampleSize,
                        CorrectCount = q.CorrectCount,
                        ErrorCount = q.ErrorCount,
                        QualityEligibleCount = q.QualityEligibleCount,
                        AllNotApplicableCount = q.AllNotApplicableCount,
                        AccuracyRate = accuracy,
                        ErrorRate = error,
                        TotalFieldCount = totalFields,
                        CorrectFieldCount = correctFields,
                        ErrorFieldCount = fields?.ErrorFieldCount ?? 0,
                        UnknownFieldCount = fields?.UnknownFieldCount ?? 0,
                        FieldAccuracyRate = MetricsCalculator.Rate(correctFields, totalFields)
                    };
                })
                .ToList();

            var topFields = await multi.ReadAsync<TopFieldRaw>();
            snapshot.TopErrorFields = topFields
                .Select(f =>
                {
                    var (_, error) = MetricsCalculator.QualityRates(f.SampleSize, 0, f.ErrorCount);
                    return new TopErrorFieldItem
                    {
                        FieldId = f.FieldId,
                        FieldName = f.FieldName,
                        Label = f.FieldName,
                        SampleSize = f.SampleSize,
                        ErrorCount = f.ErrorCount,
                        ErrorRate = error
                    };
                })
                .ToList();

            var states = await multi.ReadAsync<IndicatorRaw>();
            snapshot.IndicatorStateDistribution = states
                .Select(s => new IndicatorStateItem { Code = s.Code, StateLabel = StatusTranslations.IndicatorStateLabel(s.Code), Value = s.Value })
                .ToList();

            var perfFactory = await multi.ReadAsync<PerfRaw>();
            snapshot.PerformanceByFactory = perfFactory
                .Select(p => BuildPerformance(p))
                .ToList();

            var perfLine = await multi.ReadAsync<PerfRaw>();
            snapshot.PerformanceByLine = perfLine
                .Select(p => BuildPerformance(p))
                .ToList();

            var trends = await multi.ReadAsync<TrendRaw>();
            snapshot.DuplicateTrend = BuildDuplicateTrend(trends, effectiveFrom, effectiveTo);

            var completeness = await multi.ReadSingleAsync<CompletenessRaw>();
            snapshot.DataCompleteness = new List<DataCompletenessItem>
            {
                new() { Code = "missing-writing-date", Label = "دون تاريخ كتابة", Value = completeness.MissingWritingDate },
                new() { Code = "missing-factory", Label = "دون مصنع", Value = completeness.MissingFactory },
                new() { Code = "missing-production-line", Label = "دون خط إنتاج", Value = completeness.MissingProductionLine }
            };

            snapshot.CompanyMetrics = await multi.ReadSingleOrDefaultAsync<CompanyMetrics>();
            snapshot.PreviousCompanyMetrics = await multi.ReadSingleOrDefaultAsync<CompanyMetrics>();
            snapshot.Views = DashboardContractBuilder.Build(snapshot);
            await transaction.CommitAsync(cancellationToken);

            snapshot.IsHealthy = true;
            return snapshot;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "فشل جلب بيانات لوحة المتابعة؛ سيُعاد الخطأ إلى endpoint لتصنيفه.");
            throw;
        }
    }

    public async Task<DashboardLookups> GetLookupsAsync(CancellationToken cancellationToken = default)
    {
        var connectionString = _configuration.BuildConnectionString()
            ?? throw new InvalidOperationException("لم يتم إعداد الاتصال بقاعدة البيانات بعد.");

        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var lookupsCommand = new CommandDefinition(
            LookupsSql, commandTimeout: 30,
            cancellationToken: cancellationToken);
        using var multi = await connection.QueryMultipleAsync(lookupsCommand);

        var companies = (await multi.ReadAsync<CompanyLookupRaw>())
            .Select(c => new CompanyLookup { Id = c.Id, Name = c.Name })
            .ToList();

        var factories = (await multi.ReadAsync<FactoryLookupRaw>())
            .Select(f => new FactoryLookup { Id = f.Id, CompanyId = f.CompanyId, Name = f.Name })
            .ToList();

        var productionLines = (await multi.ReadAsync<ProductionLineLookupRaw>())
            .Select(pl => new ProductionLineLookup { Id = pl.Id, FactoryId = pl.FactoryId, Name = pl.Name })
            .ToList();

        var noteTypes = (await multi.ReadAsync<NoteTypeLookupRaw>())
            .Select(t => new NoteTypeLookup { Id = t.Id, Code = t.Code, NameAr = t.NameAr })
            .ToList();

        return new DashboardLookups
        {
            Companies = companies,
            Factories = factories,
            ProductionLines = productionLines,
            NoteTypes = noteTypes
        };
    }

    public async Task<ReviewQueueResult> GetReviewQueueAsync(
        FilterQuery? filter,
        int limit = 50,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var requested = filter ?? new FilterQuery();
        var effectiveFrom = requested.From?.Date;
        var effectiveTo = requested.To?.Date;
        if (effectiveFrom.HasValue && effectiveTo.HasValue && effectiveFrom.Value > effectiveTo.Value)
        {
            throw new ArgumentException("يجب ألا يكون تاريخ البداية بعد تاريخ النهاية.", nameof(filter));
        }

        var dateBasis = string.Equals(requested.DateBasis, "ArrivalDate", StringComparison.OrdinalIgnoreCase)
            ? "ArrivalDate"
            : "WritingDate";
        var periodColumn = string.Equals(dateBasis, "WritingDate", StringComparison.Ordinal)
            ? "n.writing_date::date"
            : "n.arrival_at::date";
        var safeLimit = Math.Clamp(limit, 1, 200);
        var safeOffset = Math.Max(0, offset);
        var businessNow = GetBusinessNow().Date;

        var connectionString = _configuration.BuildConnectionString()
            ?? throw new InvalidOperationException("لم يتم إعداد الاتصال بقاعدة البيانات بعد.");

        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var parameters = new DynamicParameters();
        parameters.Add("From", effectiveFrom, DbType.Date);
        parameters.Add("To", effectiveTo, DbType.Date);
        parameters.Add("CompanyId", requested.CompanyId, DbType.Guid);
        parameters.Add("FactoryId", requested.FactoryId, DbType.Guid);
        parameters.Add("ProductionLineId", requested.ProductionLineId, DbType.Guid);
        parameters.Add("NoteTypeId", requested.NoteTypeId, DbType.Guid);
        parameters.Add("EntryCutoffDate", requested.EntryCutoffDate?.Date, DbType.Date);
        parameters.Add("Limit", safeLimit, DbType.Int32);
        parameters.Add("Offset", safeOffset, DbType.Int32);

        // المصدر المباشر core.notes (قرار المالك) — لا يعتمد على ReportService.
        // الحالات ثابتة NEEDS_REVIEW/UNDER_REVIEW؛ فلتر Status العام يُتجاهل هنا عمداً.
        var whereClause = $"""
            WHERE n.is_deleted = FALSE
              AND n.status IN ('NEEDS_REVIEW', 'UNDER_REVIEW')
              AND (@From IS NULL OR {periodColumn} >= @From)
              AND (@To IS NULL OR {periodColumn} <= @To)
              AND (@CompanyId IS NULL OR n.company_id = @CompanyId)
              AND (@FactoryId IS NULL OR n.factory_id = @FactoryId)
              AND (@ProductionLineId IS NULL OR n.production_line_id = @ProductionLineId)
              AND (@NoteTypeId IS NULL OR n.note_type_id = @NoteTypeId)
              AND (@EntryCutoffDate IS NULL OR n.arrival_at::date <= @EntryCutoffDate)
            """;

        var total = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(
                $"SELECT COUNT(*) FROM core.notes n {whereClause};",
                parameters, commandTimeout: 30, cancellationToken: cancellationToken));

        var rows = await connection.QueryAsync<ReviewQueueRaw>(
            new CommandDefinition(
                $"""
                 SELECT n.id AS Id,
                        n.note_number::text AS NoteNumber,
                        n.company_id AS CompanyId,
                        c.name AS CompanyName,
                        f.name AS FactoryName,
                        pl.name AS ProductionLineName,
                        nt.name_ar AS NoteTypeName,
                        n.status AS Status,
                        n.writing_date AS WritingDate,
                        n.arrival_at AS ArrivalAt,
                        n.created_at AS CreatedAt
                 FROM core.notes n
                 LEFT JOIN core.companies c ON c.id = n.company_id
                 LEFT JOIN core.factories f ON f.id = n.factory_id
                 LEFT JOIN core.production_lines pl ON pl.id = n.production_line_id
                 LEFT JOIN core.note_types nt ON nt.id = n.note_type_id
                 {whereClause}
                 ORDER BY n.arrival_at DESC NULLS LAST, n.created_at DESC NULLS LAST
                 LIMIT @Limit OFFSET @Offset;
                 """,
                parameters, commandTimeout: 30, cancellationToken: cancellationToken));

        var items = rows.Select(r =>
        {
            var anchor = (r.ArrivalAt ?? r.CreatedAt)?.Date;
            int? daysInQueue = anchor.HasValue ? Math.Max(0, (businessNow - anchor.Value).Days) : null;
            return new ReviewQueueItem
            {
                Id = r.Id,
                NoteNumber = r.NoteNumber,
                CompanyId = r.CompanyId,
                CompanyName = r.CompanyName,
                FactoryName = r.FactoryName,
                ProductionLineName = r.ProductionLineName,
                NoteTypeName = r.NoteTypeName,
                Status = r.Status,
                StatusLabel = StatusTranslations.StatusLabel(r.Status),
                WritingDate = r.WritingDate,
                ArrivalAt = r.ArrivalAt,
                CreatedAt = r.CreatedAt,
                DaysInQueue = daysInQueue,
                IsLate = DelayCalculator.IsLate(r.WritingDate, r.ArrivalAt.GetValueOrDefault())
            };
        }).ToList();

        return new ReviewQueueResult
        {
            Items = items,
            Total = total,
            Limit = safeLimit,
            Offset = safeOffset
        };
    }

    private static PerformanceItem BuildPerformance(PerfRaw raw)
    {
        var accuracy = MetricsCalculator.Rate(raw.CorrectCount, raw.QualityEligibleCount);
        return new PerformanceItem
        {
            DimensionId = raw.DimensionId,
            Label = raw.Label,
            TotalNotes = raw.TotalNotes,
            EligibleCount = raw.EligibleCount,
            LateCount = raw.LateCount,
            OnTimeCount = raw.OnTimeCount,
            SampleCount = raw.SampleCount,
            CorrectCount = raw.CorrectCount,
            ErrorCount = raw.ErrorCount,
            QualityEligibleCount = raw.QualityEligibleCount,
            LateRate = MetricsCalculator.Rate(raw.LateCount, raw.EligibleCount),
            AccuracyRate = accuracy
        };
    }

    private static List<DuplicateTrendItem> BuildDuplicateTrend(
        IEnumerable<TrendRaw> rows,
        DateTime? from,
        DateTime? to)
    {
        var byMonth = rows
            .GroupBy(r => r.Label, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        if (!from.HasValue || !to.HasValue)
        {
            return byMonth
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new DuplicateTrendItem
                {
                    PeriodKey = pair.Key,
                    PeriodLabel = FormatMonthLabel(pair.Key),
                    Label = FormatMonthLabel(pair.Key),
                    CompanyResend = pair.Value.CompanyResend,
                    DuplicateNumber = pair.Value.DuplicateNumber
                })
                .ToList();
        }

        var result = new List<DuplicateTrendItem>();
        var month = new DateTime(from.Value.Year, from.Value.Month, 1);
        var lastMonth = new DateTime(to.Value.Year, to.Value.Month, 1);
        for (; month <= lastMonth; month = month.AddMonths(1))
        {
            var key = month.ToString("yyyy-MM");
            result.Add(byMonth.TryGetValue(key, out var row)
                ? new DuplicateTrendItem
                {
                    PeriodKey = key,
                    PeriodLabel = FormatMonthLabel(key),
                    Label = FormatMonthLabel(key),
                    CompanyResend = row.CompanyResend,
                    DuplicateNumber = row.DuplicateNumber
                }
                : new DuplicateTrendItem { PeriodKey = key,
                    PeriodLabel = FormatMonthLabel(key),
                    Label = FormatMonthLabel(key), CompanyResend = 0, DuplicateNumber = 0 });
        }

        return result;
    }
    private static string FormatMonthLabel(string periodKey)
    {
        if (!DateTime.TryParseExact(periodKey, "yyyy-MM", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var month))
        {
            return periodKey;
        }
        return StatusTranslations.MonthName(month.Month) + " " + month.Year;
    }

    private static int BucketOrder(string code) => code switch
    {
        "lt-1-day" => 0,
        "1-3-days" => 1,
        "4-7-days" => 2,
        _ => 3
    };

    private static string BacklogBucketLabel(string code) => code switch
    {
        "lt-1-day" => "أقل من يوم",
        "1-3-days" => "1-3 أيام",
        "4-7-days" => "4-7 أيام",
        _ => "أكثر من 7 أيام"
    };

    private static List<MonthlyActivityItem> BuildMonthlyActivity(
        IEnumerable<MonthlyRaw> rows,
        DateTime? from,
        DateTime? to)
    {
        var byMonth = rows
            .GroupBy(r => r.MonthStart.Date)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Count));

        if (!from.HasValue || !to.HasValue)
        {
            return byMonth
                .OrderBy(pair => pair.Key)
                .Select(pair => new MonthlyActivityItem
                {
                    Month = pair.Key.ToString("yyyy-MM"),
                    MonthLabel = StatusTranslations.MonthName(pair.Key.Month) + " " + pair.Key.Year,
                    Count = pair.Value
                })
                .ToList();
        }

        var result = new List<MonthlyActivityItem>();
        var monthStart = new DateTime(from.Value.Year, from.Value.Month, 1);
        var lastMonth = new DateTime(to.Value.Year, to.Value.Month, 1);
        for (; monthStart <= lastMonth; monthStart = monthStart.AddMonths(1))
        {
            var count = byMonth.TryGetValue(monthStart, out var value) ? value : 0;
            result.Add(new MonthlyActivityItem
            {
                Month = monthStart.ToString("yyyy-MM"),
                MonthLabel = StatusTranslations.MonthName(monthStart.Month) + " " + monthStart.Year,
                Count = count
            });
        }

        return result;
    }
    internal static DateTime GetBusinessNow()
    {
        var utcNow = DateTime.UtcNow;
        foreach (var timeZoneId in new[] { "Asia/Damascus", "Syria Standard Time" })
        {
            try
            {
                return TimeZoneInfo.ConvertTimeFromUtc(
                    utcNow,
                    TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));
            }
            catch (TimeZoneNotFoundException)
            {
                // Try the platform-specific identifier below.
            }
            catch (InvalidTimeZoneException)
            {
                // Try the platform-specific identifier below.
            }
        }

        // Syria has used UTC+03:00 year-round since late 2022. This fallback keeps
        // daily dashboard boundaries stable if the host lacks time-zone data.
        return utcNow.AddHours(3);
    }

    private sealed class CompanyCountsRaw
    {
        public long ActiveCompaniesTotal { get; set; }

        public long CompaniesWithNotes { get; set; }
    }

    private sealed class DuplicateRaw
    {
        public string Classification { get; set; } = string.Empty;

        public long Count { get; set; }
    }

    private sealed class CompanyTopRaw
    {
        public Guid CompanyId { get; set; }

        public string CompanyName { get; set; } = string.Empty;

        public long NoteCount { get; set; }
    }

    private sealed class MonthlyRaw
    {
        public DateTime MonthStart { get; set; }

        public long Count { get; set; }
    }

    private sealed class StatusRaw
    {
        public string Status { get; set; } = string.Empty;

        public long Count { get; set; }
    }

    private sealed class RecentNoteRaw
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

    private sealed class ReviewQueueRaw
    {
        public Guid Id { get; set; }

        public string NoteNumber { get; set; } = string.Empty;

        public Guid? CompanyId { get; set; }

        public string? CompanyName { get; set; }

        public string? FactoryName { get; set; }

        public string? ProductionLineName { get; set; }

        public string? NoteTypeName { get; set; }

        public string Status { get; set; } = string.Empty;

        public DateTime? WritingDate { get; set; }

        public DateTime? ArrivalAt { get; set; }

        public DateTime? CreatedAt { get; set; }
    }

    private sealed class SlaRaw
    {
        public long EligibleNotes { get; set; }

        public long OnTimeNotes { get; set; }

        public long LateNotes { get; set; }

        public long InvalidDateOrderNotes { get; set; }
    }

    private sealed class BacklogRaw
    {
        public string Bucket { get; set; } = string.Empty;

        public long Count { get; set; }
    }

    private sealed class IncomingRaw
    {
        public DateTime Label { get; set; }

        public long Incoming { get; set; }

        public long Approved { get; set; }
    }

    private sealed class QualityRaw
    {
        public Guid CompanyId { get; set; }

        public string CompanyName { get; set; } = string.Empty;

        public long SampleSize { get; set; }

        public long CorrectCount { get; set; }

        public long ErrorCount { get; set; }

        public long QualityEligibleCount { get; set; }

        public long AllNotApplicableCount { get; set; }
    }

    private sealed class FieldQualityRaw
    {
        public Guid CompanyId { get; set; }

        public string CompanyName { get; set; } = string.Empty;

        public long TotalFieldCount { get; set; }

        public long CorrectFieldCount { get; set; }

        public long ErrorFieldCount { get; set; }

        public long UnknownFieldCount { get; set; }
    }

    private sealed class TopFieldRaw
    {
        public Guid FieldId { get; set; }
        public string FieldName { get; set; } = string.Empty;

        public long SampleSize { get; set; }

        public long ErrorCount { get; set; }
    }

    private sealed class IndicatorRaw
    {
        public int Code { get; set; }

        public long Value { get; set; }
    }

    private sealed class PerfRaw
    {
        public Guid DimensionId { get; set; }
        public string Label { get; set; } = string.Empty;

        public long TotalNotes { get; set; }

        public long EligibleCount { get; set; }

        public long LateCount { get; set; }

        public long OnTimeCount { get; set; }

        public long SampleCount { get; set; }

        public long CorrectCount { get; set; }

        public long ErrorCount { get; set; }

        public long QualityEligibleCount { get; set; }
    }

    private sealed class TrendRaw
    {
        public string Label { get; set; } = string.Empty;

        public long CompanyResend { get; set; }

        public long DuplicateNumber { get; set; }
    }

    private sealed class CompletenessRaw
    {
        public long MissingWritingDate { get; set; }

        public long MissingFactory { get; set; }

        public long MissingProductionLine { get; set; }
    }

    private sealed class CompanyLookupRaw
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class FactoryLookupRaw
    {
        public Guid Id { get; set; }

        public Guid CompanyId { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class ProductionLineLookupRaw
    {
        public Guid Id { get; set; }

        public Guid FactoryId { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class NoteTypeLookupRaw
    {
        public Guid Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public string NameAr { get; set; } = string.Empty;
    }
}




