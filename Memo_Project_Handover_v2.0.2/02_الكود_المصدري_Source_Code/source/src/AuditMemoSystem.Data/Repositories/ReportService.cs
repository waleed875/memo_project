using System;
using System.Data;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AuditMemoSystem.Core.Enums;
using AuditMemoSystem.Core.Models;
using AuditMemoSystem.Core.Services;
using AuditMemoSystem.Data.Connection;
using Npgsql;
using NpgsqlTypes;

namespace AuditMemoSystem.Data.Repositories;

public sealed partial class ReportService : RepositoryBase, IReportService
{
	private readonly ISystemModeService? _modeService;

	private static readonly JsonSerializerOptions DashboardJsonOptions = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = true
	};

	public ReportService(DbConnectionFactory factory, ISystemModeService? modeService = null)
		: base(factory)
	{
		_modeService = modeService;
	}

	public async Task<DetailedDashboardSnapshotModel> GetDashboardSnapshotAsync(ReportFilterModel filter, Guid requesterId, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentNullException.ThrowIfNull(filter, "filter");
		NormalizeFilterDates(filter);
		if (filter.From > filter.To)
		{
			throw new InvalidOperationException("تاريخ البداية يجب ألا يكون بعد تاريخ النهاية.");
		}
		if (!ReportAccessPolicy.CanAccessReport(await RepositoryBase.GetUserRoleAsync(base.Factory, requesterId, cancellationToken), "DashboardSummary"))
		{
			throw new UnauthorizedAccessException("ليس لديك صلاحية لعرض لوحة المعلومات.");
		}
		DetailedDashboardSnapshotModel result;
		await using (NpgsqlConnection connection = base.Factory.CreateConnection())
		{
			await connection.OpenAsync(cancellationToken);
			DetailedDashboardSnapshotModel detailedDashboardSnapshotModel;
			await using (NpgsqlCommand command = new NpgsqlCommand("select reporting.fn_dashboard_snapshot_v2(\r\n    @from, @to, @company_id, @factory_id, @production_line_id,\r\n    @note_type_id, @status, 'Asia/Damascus', @date_basis, @arrival_cutoff_date)::text", connection)
			{
				CommandTimeout = 30
			})
			{
				command.Parameters.Add("from", NpgsqlDbType.Date).Value = filter.From;
				command.Parameters.Add("to", NpgsqlDbType.Date).Value = filter.To;
				command.Parameters.Add("company_id", NpgsqlDbType.Uuid).Value = ((object)filter.CompanyId) ?? DBNull.Value;
				command.Parameters.Add("factory_id", NpgsqlDbType.Uuid).Value = ((object)filter.FactoryId) ?? DBNull.Value;
				command.Parameters.Add("production_line_id", NpgsqlDbType.Uuid).Value = ((object)filter.ProductionLineId) ?? DBNull.Value;
				command.Parameters.Add("note_type_id", NpgsqlDbType.Uuid).Value = ((object)filter.NoteTypeId) ?? DBNull.Value;
				command.Parameters.Add("status", NpgsqlDbType.Varchar).Value = ((object)filter.Status?.ToString()) ?? DBNull.Value;
				command.Parameters.AddWithValue("date_basis", filter.DateBasis == ReportDateBasis.ArrivalDate ? "ArrivalDate" : "WritingDate");
				command.Parameters.Add("arrival_cutoff_date", NpgsqlDbType.Date).Value = ((object?)filter.EntryCutoffDate) ?? DBNull.Value;
				string payload = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken));
				detailedDashboardSnapshotModel = JsonSerializer.Deserialize<DetailedDashboardSnapshotModel>(payload ?? "", DashboardJsonOptions) ?? throw new InvalidOperationException("تعذر تحميل بيانات لوحة المعلومات.");
			}
			await PopulateCompanyCoverageAsync(connection, filter, detailedDashboardSnapshotModel, cancellationToken);
            await PopulateFieldAccuracyAsync(connection, filter, detailedDashboardSnapshotModel.Summary, cancellationToken);
			result = detailedDashboardSnapshotModel;
		}
		return result;
	}


	    private static async Task PopulateCompanyCoverageAsync(NpgsqlConnection connection, ReportFilterModel filter, DetailedDashboardSnapshotModel snapshot, CancellationToken cancellationToken)
    {
        var periodExpression = filter.DateBasis == ReportDateBasis.WritingDate ? "n.writing_date" : "n.arrival_at::date";
        await using NpgsqlCommand command = new NpgsqlCommand($"""
select c.name, count(n.id)::int
from core.companies c
left join core.notes n
  on n.company_id = c.id
 and not n.is_deleted
 and {periodExpression} between @from and @to
 and (@arrival_cutoff_date is null or n.arrival_at::date <= @arrival_cutoff_date)
 and (@company_id is null or n.company_id = @company_id)
 and (@factory_id is null or n.factory_id = @factory_id)
 and (@production_line_id is null or n.production_line_id = @production_line_id)
 and (@note_type_id is null or n.note_type_id = @note_type_id)
 and (@status is null or n.status = @status)
where c.is_active and not c.is_deleted
group by c.id, c.name
order by c.name
""", connection)
        {
            CommandTimeout = 30
        };
        command.Parameters.Add("from", NpgsqlDbType.Date).Value = filter.From;
        command.Parameters.Add("to", NpgsqlDbType.Date).Value = filter.To;
        command.Parameters.Add("company_id", NpgsqlDbType.Uuid).Value = ((object?)filter.CompanyId) ?? DBNull.Value;
        command.Parameters.Add("factory_id", NpgsqlDbType.Uuid).Value = ((object?)filter.FactoryId) ?? DBNull.Value;
        command.Parameters.Add("production_line_id", NpgsqlDbType.Uuid).Value = ((object?)filter.ProductionLineId) ?? DBNull.Value;
        command.Parameters.Add("note_type_id", NpgsqlDbType.Uuid).Value = ((object?)filter.NoteTypeId) ?? DBNull.Value;
        command.Parameters.Add("status", NpgsqlDbType.Varchar).Value = ((object?)filter.Status?.ToString()) ?? DBNull.Value;
        command.Parameters.Add("arrival_cutoff_date", NpgsqlDbType.Date).Value = ((object?)filter.EntryCutoffDate) ?? DBNull.Value;

        var companies = new List<DashboardChartPointModel>();
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            companies.Add(new DashboardChartPointModel
            {
                Label = reader.GetString(0),
                Value = reader.GetInt32(1)
            });
        }

        snapshot.NotesByCompany = companies;
        snapshot.Summary.ActiveCompanies = companies.Count;
    }
	private static async Task PopulateFieldAccuracyAsync(NpgsqlConnection connection, ReportFilterModel filter, DetailedDashboardSummaryModel summary, CancellationToken cancellationToken)
	{
		var periodExpression = filter.DateBasis == ReportDateBasis.WritingDate ? "f.writing_date" : "f.arrival_at::date";
		await using NpgsqlCommand command = new NpgsqlCommand($"""
select
  count(*) filter (where f.is_applicable_reviewed)::int as total_field_count,
  count(*) filter (where f.is_correct)::int as correct_field_count,
  count(*) filter (where f.is_error)::int as error_field_count,
  count(*) filter (where f.is_unreviewed)::int as unknown_field_count
from reporting.vw_note_indicator_facts_v3 f
join core.notes n on n.id = f.note_id and n.is_deleted = false
where {periodExpression} between @from and @to
  and (@arrival_cutoff_date is null or f.arrival_at::date <= @arrival_cutoff_date)
  and (@company_id is null or f.company_id = @company_id)
  and (@factory_id is null or f.factory_id = @factory_id)
  and (@production_line_id is null or f.production_line_id = @production_line_id)
  and (@note_type_id is null or f.note_type_id = @note_type_id)
  and (@status is null or f.workflow_status = @status)
""", connection)
		{
			CommandTimeout = 30
		};
		command.Parameters.Add("from", NpgsqlDbType.Date).Value = filter.From;
		command.Parameters.Add("to", NpgsqlDbType.Date).Value = filter.To;
		command.Parameters.Add("company_id", NpgsqlDbType.Uuid).Value = ((object)filter.CompanyId) ?? DBNull.Value;
		command.Parameters.Add("factory_id", NpgsqlDbType.Uuid).Value = ((object)filter.FactoryId) ?? DBNull.Value;
		command.Parameters.Add("production_line_id", NpgsqlDbType.Uuid).Value = ((object)filter.ProductionLineId) ?? DBNull.Value;
		command.Parameters.Add("note_type_id", NpgsqlDbType.Uuid).Value = ((object)filter.NoteTypeId) ?? DBNull.Value;
		command.Parameters.Add("status", NpgsqlDbType.Varchar).Value = ((object)filter.Status?.ToString()) ?? DBNull.Value;
		command.Parameters.Add("arrival_cutoff_date", NpgsqlDbType.Date).Value = ((object?)filter.EntryCutoffDate) ?? DBNull.Value;
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		if (!await reader.ReadAsync(cancellationToken))
		{
			return;
		}
		var total = reader.GetInt32(0);
		var correct = reader.GetInt32(1);
		summary.TotalFieldCount = total;
		summary.CorrectFieldCount = correct;
		summary.ErrorFieldCount = reader.GetInt32(2);
		summary.UnknownFieldCount = reader.GetInt32(3);
		summary.FieldAccuracyPercentage = total > 0 ? decimal.Round(correct * 100m / total, 1) : 0m;
	}

    private static void NormalizeFilterDates(ReportFilterModel filter)
    {
        var normalized = ReportPeriodResolver.NormalizeFilter(filter.From, filter.To);
        filter.From = normalized.From ?? DateOnly.MinValue;
        filter.To = normalized.To;
    }

public async Task<ReportIdentitySettingsModel> GetReportIdentitySettingsAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		ReportIdentitySettingsModel result;
		await using (NpgsqlConnection connection = base.Factory.CreateConnection())
		{
			await connection.OpenAsync(cancellationToken);
			ReportIdentitySettingsModel reportIdentitySettingsModel2;
			await using (NpgsqlCommand command = new NpgsqlCommand("select republic_name_ar, republic_name_en,\r\n       ministry_name, ministry_name_en,\r\n       company_name, company_name_en,\r\n       administrative_level_one, administrative_level_one_en,\r\n       administrative_level_two, administrative_level_two_en,\r\n       administrative_level_three, administrative_level_three_en,\r\n       issuing_entity_name, default_city, updated_at, updated_by\r\nfrom system.report_identity_settings\r\nwhere singleton = true", connection))
			{
				ReportIdentitySettingsModel reportIdentitySettingsModel;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					reportIdentitySettingsModel = ((await reader.ReadAsync(cancellationToken)) ? new ReportIdentitySettingsModel
					{
						RepublicNameArabic = reader.GetString(0),
						RepublicNameEnglish = reader.GetString(1),
						MinistryName = reader.GetString(2),
						MinistryNameEnglish = reader.GetString(3),
						CompanyName = reader.GetString(4),
						CompanyNameEnglish = reader.GetString(5),
						AdministrativeLevelOne = reader.GetString(6),
						AdministrativeLevelOneEnglish = reader.GetString(7),
						AdministrativeLevelTwo = reader.GetString(8),
						AdministrativeLevelTwoEnglish = reader.GetString(9),
						AdministrativeLevelThree = reader.GetString(10),
						AdministrativeLevelThreeEnglish = reader.GetString(11),
						IssuingEntityName = reader.GetString(12),
						DefaultCity = reader.GetString(13),
						UpdatedAt = reader.GetDateTime(14),
						UpdatedBy = (reader.IsDBNull(15) ? ((Guid?)null) : new Guid?(reader.GetGuid(15)))
					} : new ReportIdentitySettingsModel());
				}
				reportIdentitySettingsModel2 = reportIdentitySettingsModel;
			}
			result = reportIdentitySettingsModel2;
		}
		return result;
	}

	public async Task SaveReportIdentitySettingsAsync(ReportIdentitySettingsModel settings, Guid requesterId, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentNullException.ThrowIfNull(settings, "settings");
		_modeService?.EnsureWritable();
		await RepositoryBase.EnsureRoleAsync(base.Factory, requesterId, new UserRole[1], cancellationToken);
		await using NpgsqlConnection connection = base.Factory.CreateConnection();
		await connection.OpenAsync(cancellationToken);
		await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
		await using NpgsqlCommand command = new NpgsqlCommand("insert into system.report_identity_settings (\r\n    singleton, republic_name_ar, republic_name_en,\r\n    ministry_name, ministry_name_en, company_name, company_name_en,\r\n    administrative_level_one, administrative_level_one_en,\r\n    administrative_level_two, administrative_level_two_en,\r\n    administrative_level_three, administrative_level_three_en,\r\n    issuing_entity_name, default_city, updated_at, updated_by)\r\nvalues (true, @republic_name_ar, @republic_name_en,\r\n        @ministry_name, @ministry_name_en, @company_name, @company_name_en,\r\n        @administrative_level_one, @administrative_level_one_en,\r\n        @administrative_level_two, @administrative_level_two_en,\r\n        @administrative_level_three, @administrative_level_three_en,\r\n        @issuing_entity_name, @default_city, now(), @updated_by)\r\non conflict (singleton) do update set\r\n    republic_name_ar = excluded.republic_name_ar,\r\n    republic_name_en = excluded.republic_name_en,\r\n    ministry_name = excluded.ministry_name,\r\n    ministry_name_en = excluded.ministry_name_en,\r\n    company_name = excluded.company_name,\r\n    company_name_en = excluded.company_name_en,\r\n    administrative_level_one = excluded.administrative_level_one,\r\n    administrative_level_one_en = excluded.administrative_level_one_en,\r\n    administrative_level_two = excluded.administrative_level_two,\r\n    administrative_level_two_en = excluded.administrative_level_two_en,\r\n    administrative_level_three = excluded.administrative_level_three,\r\n    administrative_level_three_en = excluded.administrative_level_three_en,\r\n    issuing_entity_name = excluded.issuing_entity_name,\r\n    default_city = excluded.default_city,\r\n    updated_at = excluded.updated_at,\r\n    updated_by = excluded.updated_by", connection, transaction);
		command.Parameters.AddWithValue("republic_name_ar", NormalizeSetting(settings.RepublicNameArabic));
		command.Parameters.AddWithValue("republic_name_en", NormalizeSetting(settings.RepublicNameEnglish));
		command.Parameters.AddWithValue("ministry_name", NormalizeSetting(settings.MinistryName));
		command.Parameters.AddWithValue("ministry_name_en", NormalizeSetting(settings.MinistryNameEnglish));
		command.Parameters.AddWithValue("company_name", NormalizeSetting(settings.CompanyName));
		command.Parameters.AddWithValue("company_name_en", NormalizeSetting(settings.CompanyNameEnglish));
		command.Parameters.AddWithValue("administrative_level_one", NormalizeSetting(settings.AdministrativeLevelOne));
		command.Parameters.AddWithValue("administrative_level_one_en", NormalizeSetting(settings.AdministrativeLevelOneEnglish));
		command.Parameters.AddWithValue("administrative_level_two", NormalizeSetting(settings.AdministrativeLevelTwo));
		command.Parameters.AddWithValue("administrative_level_two_en", NormalizeSetting(settings.AdministrativeLevelTwoEnglish));
		command.Parameters.AddWithValue("administrative_level_three", NormalizeSetting(settings.AdministrativeLevelThree));
		command.Parameters.AddWithValue("administrative_level_three_en", NormalizeSetting(settings.AdministrativeLevelThreeEnglish));
		command.Parameters.AddWithValue("issuing_entity_name", NormalizeSetting(settings.IssuingEntityName));
		command.Parameters.AddWithValue("default_city", NormalizeSetting(settings.DefaultCity));
		command.Parameters.AddWithValue("updated_by", requesterId);
		await command.ExecuteNonQueryAsync(cancellationToken);
		await RepositoryBase.WriteAuditRecordAsync(connection, transaction, requesterId, "UPDATE_REPORT_IDENTITY", "system", "report_identity_settings", null, "تم تحديث إعدادات الترويسة الموحدة للتقارير.", cancellationToken);
		await transaction.CommitAsync(cancellationToken);
	}

	private static string NormalizeSetting(string? value)
	{
		return (value ?? "").Trim();
	}

	public Task<DetailedDashboardSnapshotModel> GetDetailedDashboardSnapshotAsync(Guid requesterId, CancellationToken cancellationToken = default(CancellationToken))
	{
		return GetDashboardSnapshotAsync(new ReportFilterModel
		{
			From = DateOnly.MinValue,
			To = ReportPeriodResolver.OperationalToday()
		}, requesterId, cancellationToken);
	}

	private async Task<DetailedDashboardSnapshotModel> GetLegacyDetailedDashboardSnapshotAsync(Guid requesterId, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!ReportAccessPolicy.CanAccessReport(await RepositoryBase.GetUserRoleAsync(base.Factory, requesterId, cancellationToken), "DashboardSummary"))
		{
			throw new UnauthorizedAccessException("ليس لديك صلاحية لعرض لوحة المعلومات التفصيلية.");
		}
		DetailedDashboardSnapshotModel result;
		await using (NpgsqlConnection connection = base.Factory.CreateConnection())
		{
			await connection.OpenAsync(cancellationToken);
			DetailedDashboardSnapshotModel detailedDashboardSnapshotModel;
			await using (NpgsqlCommand command = new NpgsqlCommand("with scoped_notes as (\r\n    select n.id, n.note_number, n.company_id, n.note_type_id, n.status, n.writing_date, n.arrival_at\r\n    from core.notes n\r\n    where not n.is_deleted\r\n),\r\nsummary as (\r\n    select\r\n        count(*)::int as total_notes,\r\n        count(*) filter (where arrival_at::date = current_date)::int as today_notes,\r\n        count(*) filter (where arrival_at >= date_trunc('month', current_date))::int as current_month_notes,\r\n        count(*) filter (where status = 'APPROVED')::int as approved_notes,\r\n        count(*) filter (where status in ('COMPLETE', 'HAS_ERRORS', 'NEEDS_REVIEW', 'UNDER_REVIEW'))::int as pending_review_notes,\r\n        count(*) filter (where status = 'RETURNED_FOR_CORRECTION')::int as returned_notes,\r\n        count(*) filter (where writing_date is not null and arrival_at::date - writing_date > 5)::int as late_notes,\r\n        count(*) filter (where writing_date is not null and arrival_at::date - writing_date > 5 and date_trunc('month', arrival_at) = date_trunc('month', writing_date::timestamp))::int as late_same_month_notes,\r\n        count(*) filter (where writing_date is not null and arrival_at::date - writing_date > 5 and date_trunc('month', arrival_at) <> date_trunc('month', writing_date::timestamp))::int as late_cross_month_notes,\r\n        count(*) filter (where writing_date is not null and arrival_at::date >= writing_date and arrival_at::date - writing_date <= 5)::int as on_time_notes,\r\n        coalesce(round(avg(arrival_at::date - writing_date) filter (where writing_date is not null and arrival_at::date - writing_date > 5), 2), 0) as average_late_days,\r\n        coalesce(max(arrival_at::date - writing_date) filter (where writing_date is not null and arrival_at::date - writing_date > 5), 0)::int as maximum_late_days,\r\n        max(arrival_at) as latest_arrival_at\r\n    from scoped_notes\r\n)\r\nselect jsonb_build_object(\r\n    'summary', jsonb_build_object(\r\n        'totalNotes', summary.total_notes,\r\n        'todayNotes', summary.today_notes,\r\n        'currentMonthNotes', summary.current_month_notes,\r\n        'activeCompanies', (select count(*)::int from core.companies c where c.is_active and not c.is_deleted),\r\n        'approvedNotes', summary.approved_notes,\r\n        'pendingReviewNotes', summary.pending_review_notes,\r\n        'returnedNotes', summary.returned_notes,\r\n        'companyResendNotes', (select count(*)::int from core.duplicate_incidents where classification = 'COMPANY_RESEND'),\r\n        'duplicateNumberNotes', (select count(*)::int from core.duplicate_incidents where classification = 'COMPANY_DUPLICATE_NUMBER'),\r\n        'lateNotes', summary.late_notes,\r\n        'lateSameMonthNotes', summary.late_same_month_notes,\r\n        'lateCrossMonthNotes', summary.late_cross_month_notes,\r\n        'onTimeNotes', summary.on_time_notes,\r\n        'averageLateDays', summary.average_late_days,\r\n        'maximumLateDays', summary.maximum_late_days,\r\n        'latestArrivalAt', summary.latest_arrival_at\r\n    ),\r\n    'notesByCompany', coalesce((\r\n        select jsonb_agg(jsonb_build_object('label', item.label, 'value', item.value) order by item.value desc, item.label)\r\n        from (\r\n            select c.name as label, count(*)::int as value\r\n            from scoped_notes n\r\n            join core.companies c on c.id = n.company_id\r\n            group by c.name\r\n            order by count(*) desc, c.name\r\n            limit 8\r\n        ) item\r\n    ), '[]'::jsonb),\r\n    'monthlyNotes', coalesce((\r\n        select jsonb_agg(jsonb_build_object('label', item.label, 'value', item.value) order by item.month_start)\r\n        from (\r\n            select month_start, to_char(month_start, 'YYYY/MM') as label, count(n.id)::int as value\r\n            from generate_series(\r\n                date_trunc('month', current_date) - interval '5 months',\r\n                date_trunc('month', current_date),\r\n                interval '1 month') month_start\r\n            left join scoped_notes n\r\n                on n.arrival_at >= month_start\r\n               and n.arrival_at < month_start + interval '1 month'\r\n            group by month_start\r\n        ) item\r\n    ), '[]'::jsonb),\r\n    'notesByStatus', coalesce((\r\n        select jsonb_agg(jsonb_build_object('label', item.label, 'value', item.value) order by item.value desc, item.label)\r\n        from (\r\n            select status as label, count(*)::int as value\r\n            from scoped_notes\r\n            group by status\r\n        ) item\r\n    ), '[]'::jsonb),\r\n    'duplicatesByType', coalesce((\r\n        select jsonb_agg(jsonb_build_object('label', item.label, 'value', item.value) order by item.label)\r\n        from (\r\n            select classification as label, count(*)::int as value\r\n            from core.duplicate_incidents\r\n            group by classification\r\n        ) item\r\n    ), '[]'::jsonb),\r\n    'recentNotes', coalesce((\r\n        select jsonb_agg(jsonb_build_object(\r\n            'noteNumber', item.note_number,\r\n            'companyName', item.company_name,\r\n            'noteTypeName', item.note_type_name,\r\n            'status', item.status,\r\n            'arrivalAt', item.arrival_at) order by item.arrival_at desc)\r\n        from (\r\n            select n.note_number, c.name as company_name, nt.name_ar as note_type_name, n.status, n.arrival_at\r\n            from scoped_notes n\r\n            join core.companies c on c.id = n.company_id\r\n            join core.note_types nt on nt.id = n.note_type_id\r\n            order by n.arrival_at desc\r\n            limit 10\r\n        ) item\r\n    ), '[]'::jsonb)\r\n)::text\r\nfrom summary", connection)
			{
				CommandTimeout = 30
			})
			{
				string payload = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken));
				detailedDashboardSnapshotModel = JsonSerializer.Deserialize<DetailedDashboardSnapshotModel>(payload ?? "", DashboardJsonOptions) ?? throw new InvalidOperationException("تعذر تحميل بيانات لوحة المعلومات التفصيلية.");
			}
			result = detailedDashboardSnapshotModel;
		}
		return result;
	}

	public async Task<DataTable> GetReportTableAsync(string reportName, ReportFilterModel filter, Guid requesterId, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentNullException.ThrowIfNull(filter, "filter");
		NormalizeFilterDates(filter);
		if (filter.From > filter.To)
		{
			throw new InvalidOperationException("تاريخ البداية يجب ألا يكون بعد تاريخ النهاية.");
		}
		if (reportName == "SingleNote" && !filter.NoteId.HasValue)
		{
			throw new InvalidOperationException("تقرير المذكرة المفردة يتطلب اختيار مذكرة.");
		}
		if (reportName == "CompanyDetailed" && !filter.CompanyId.HasValue)
		{
			throw new InvalidOperationException("اختر شركة أولا\u064b لإصدار التقرير التفصيلي للشركة.");
		}
		UserRole role = await RepositoryBase.GetUserRoleAsync(base.Factory, requesterId, cancellationToken);
		if (ReportAccessPolicy.IsKnownReport(reportName) && !ReportAccessPolicy.CanAccessReport(role, reportName))
		{
			throw new UnauthorizedAccessException("ليس لديك صلاحية لعرض هذا التقرير.");
		}
		if (reportName == "Duplicates")
		{
			return await GetDuplicateIncidentTableAsync(filter, role, requesterId, cancellationToken);
		}
		if (reportName == "CompanyDetailed")
		{
			if (role == UserRole.DATA_ENTRY)
			{
				throw new UnauthorizedAccessException("التقرير التفصيلي للشركة يتطلب صلاحية عرض مؤشرات الشركة الكاملة.");
			}
			return await GetCompanyDetailedTableV3Async(filter, cancellationToken);
		}
		if (role == UserRole.DATA_ENTRY)
		{
			filter.CreatedBy = requesterId;
		}
		var reportDateExpression = filter.DateBasis == ReportDateBasis.WritingDate
			? "d.writing_date"
			: "d.arrival_at::date";
		string noteDateScope = $"((@note_id is not null and d.note_id = @note_id)\r\n         or (@note_id is null and {reportDateExpression} between @from and @to))\r\n        and (@arrival_cutoff_date is null or d.arrival_at::date <= @arrival_cutoff_date)";
		string noteScope = "with scoped_notes as (\r\n    select d.*, n.duplicate_reason, n.return_reason,\r\n           (\r\n               select string_agg(distinct di.classification, ', ' order by di.classification)\r\n               from core.duplicate_incidents di\r\n               where (di.created_note_id = d.note_id and di.classification = 'COMPANY_DUPLICATE_NUMBER')\r\n                  or (di.matched_note_id = d.note_id and di.classification = 'COMPANY_RESEND')\r\n           ) as duplicate_classification,\r\n           exists (\r\n               select 1\r\n               from core.duplicate_incidents di\r\n               where (di.created_note_id = d.note_id and di.classification = 'COMPANY_DUPLICATE_NUMBER')\r\n                  or (di.matched_note_id = d.note_id and di.classification = 'COMPANY_RESEND')\r\n           ) as has_duplicate_incident\r\n    from reporting.vw_note_details d\r\n    join core.notes n on n.id = d.note_id and n.is_deleted = false\r\n    where (\r\n        " + noteDateScope + "\r\n    )\r\n      and (@company_id is null or d.company_id = @company_id)\r\n      and (@factory_id is null or d.factory_id = @factory_id)\r\n      and (@production_line_id is null or d.production_line_id = @production_line_id)\r\n      and (@note_type_id is null or d.note_type_id = @note_type_id)\r\n      and (@created_by is null or d.created_by = @created_by)\r\n      and (@status is null or d.status = @status)\r\n      and (@note_id is not null or @has_writing_date is null\r\n           or (@has_writing_date = true and d.writing_date is not null)\r\n           or (@has_writing_date = false and d.writing_date is null))\r\n)";
		bool flag;
		switch (reportName)
		{
		case "SingleNote":
		case "GeneralNotes":
		case "DashboardSummary":
		case "DelaySummary":
		case "DelayDetails":
		case "ErrorsByCompany":
		case "ErrorsByField":
		case "CompanyPerformance":
		case "CompanyDetailed":
		case "FactoryPerformance":
		case "MonthlySummary":
		case "UnknownFields":
		case "Duplicates":
		case "PendingReview":
		case "ReturnedNotes":
		case "NoteLinks":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		bool usesNoteScope = flag;
		if (1 == 0)
		{
		}
		string text = reportName switch
		{
			"SingleNote" => "select f.*\r\nfrom reporting.vw_note_field_details f\r\njoin scoped_notes s on s.note_id = f.note_id\r\nwhere f.note_id = @note_id\r\norder by f.field_name", 
			"GeneralNotes" => "select note_number, company_name, note_type_name, writing_date, arrival_at,\r\n       case status\r\n           when 'DRAFT' then 'مسودة'\r\n           when 'COMPLETE' then 'مكتملة'\r\n           when 'HAS_ERRORS' then 'تحتوي أخطاء'\r\n           when 'NEEDS_REVIEW' then 'تحتاج مراجعة'\r\n           when 'UNDER_REVIEW' then 'قيد التدقيق'\r\n           when 'RETURNED_FOR_CORRECTION' then 'معادة للتصحيح'\r\n           when 'APPROVED' then 'معتمدة'\r\n           else status\r\n       end as status,\r\n       created_by_name,\r\n       case when writing_date is null then 'تاريخ المذكرة مفقود' else 'موجود' end as writing_date_status,\r\n       active_count, inactive_count, unknown_count, accuracy_percentage\r\nfrom scoped_notes\r\norder by arrival_at desc", 
			"DashboardSummary" => "select count(*)::int as total_notes,\r\n       (select count(*)::int from core.companies where is_active and not is_deleted) as active_companies,\r\n       count(*) filter (where status = 'APPROVED')::int as approved_notes,\r\n       count(*) filter (where status in ('COMPLETE', 'HAS_ERRORS', 'NEEDS_REVIEW', 'UNDER_REVIEW'))::int as pending_review_notes,\r\n       count(*) filter (where status = 'RETURNED_FOR_CORRECTION')::int as returned_for_correction,\r\n       count(*) filter (where inactive_count > 0)::int as notes_with_errors,\r\n       count(*) filter (where unknown_count > 0)::int as notes_needing_review,\r\n       count(*) filter (where writing_date is null)::int as missing_writing_date,\r\n       count(*) filter (where factory_id is null)::int as unassigned_factory_notes,\r\n       count(*) filter (where writing_date is not null and arrival_at::date - writing_date > 5)::int as late_notes,\r\n       count(*) filter (where writing_date is not null and arrival_at::date - writing_date > 5 and date_trunc('month', arrival_at) = date_trunc('month', writing_date::timestamp))::int as late_same_month_notes,\r\n       count(*) filter (where writing_date is not null and arrival_at::date - writing_date > 5 and date_trunc('month', arrival_at) <> date_trunc('month', writing_date::timestamp))::int as late_cross_month_notes,\r\n       count(*) filter (where writing_date is not null and arrival_at::date >= writing_date and arrival_at::date - writing_date <= 5)::int as on_time_notes,\r\n       coalesce(round(avg(arrival_at::date - writing_date) filter (where writing_date is not null and arrival_at::date - writing_date > 5), 2), 0) as average_late_days,\r\n       coalesce(max(arrival_at::date - writing_date) filter (where writing_date is not null and arrival_at::date - writing_date > 5), 0)::int as maximum_late_days,\r\n                                (\r\n             select count(*)::int\r\n             from core.duplicate_incidents di\r\n             join core.notes matched_note on matched_note.id = di.matched_note_id\r\n             where not matched_note.is_deleted\r\n               and di.classification = 'COMPANY_DUPLICATE_NUMBER'\r\n               and matched_note.arrival_at::date between @from and @to\r\n               and (@note_id is null or matched_note.id = @note_id)\r\n               and (@company_id is null or matched_note.company_id = @company_id)\r\n               and (@factory_id is null or matched_note.factory_id = @factory_id)\r\n               and (@production_line_id is null or matched_note.production_line_id = @production_line_id)\r\n               and (@note_type_id is null or matched_note.note_type_id = @note_type_id)\r\n         ) as duplicate_notes,\r\n       (\r\n           select count(*)::int\r\n           from core.duplicate_incidents di\r\n           join core.notes matched_note on matched_note.id = di.matched_note_id\r\n           where not matched_note.is_deleted\r\n             and di.classification = 'COMPANY_RESEND'\r\n             and matched_note.arrival_at::date between @from and @to\r\n             and (@note_id is null or matched_note.id = @note_id)\r\n             and (@company_id is null or matched_note.company_id = @company_id)\r\n             and (@factory_id is null or matched_note.factory_id = @factory_id)\r\n             and (@production_line_id is null or matched_note.production_line_id = @production_line_id)\r\n             and (@note_type_id is null or matched_note.note_type_id = @note_type_id)\r\n             and (@status is null or matched_note.status = @status)\r\n       ) as company_resend_notes,\r\n       coalesce(round(count(*) filter (where inactive_count = 0)::numeric * 100 / nullif(count(*), 0), 2), 0) as average_accuracy\r\nfrom scoped_notes", 
			"DelaySummary" => "select count(*)::int as total_notes,\r\n       count(*) filter (where writing_date is not null)::int as notes_with_writing_date,\r\n       count(*) filter (where writing_date is null)::int as missing_writing_date,\r\n       count(*) filter (where writing_date is not null and arrival_at::date >= writing_date and arrival_at::date - writing_date <= 5)::int as on_time_notes,\r\n       count(*) filter (where writing_date is not null and arrival_at::date - writing_date > 5)::int as late_notes,\r\n       count(*) filter (where writing_date is not null and arrival_at::date - writing_date > 5 and date_trunc('month', arrival_at) = date_trunc('month', writing_date::timestamp))::int as late_same_month_notes,\r\n       count(*) filter (where writing_date is not null and arrival_at::date - writing_date > 5 and date_trunc('month', arrival_at) <> date_trunc('month', writing_date::timestamp))::int as late_cross_month_notes,\r\n       coalesce(round(\r\n           count(*) filter (where writing_date is not null and arrival_at::date - writing_date > 5)::numeric * 100\r\n           / nullif(count(*), 0), 2), 0) as late_percentage,\r\n       coalesce(round(avg(arrival_at::date - writing_date) filter (where writing_date is not null and arrival_at::date - writing_date > 5), 2), 0) as average_late_days,\r\n       coalesce(max(arrival_at::date - writing_date) filter (where writing_date is not null and arrival_at::date - writing_date > 5), 0)::int as maximum_late_days\r\nfrom scoped_notes", 
			"DelayDetails" => "select note_number, company_name, note_type_name, writing_date, arrival_at,\r\n       case status\r\n           when 'DRAFT' then 'مسودة'\r\n           when 'COMPLETE' then 'مكتملة'\r\n           when 'HAS_ERRORS' then 'تحتوي أخطاء'\r\n           when 'NEEDS_REVIEW' then 'تحتاج مراجعة'\r\n           when 'UNDER_REVIEW' then 'قيد التدقيق'\r\n           when 'RETURNED_FOR_CORRECTION' then 'معادة للتصحيح'\r\n           when 'APPROVED' then 'معتمدة'\r\n           else status\r\n       end as status,\r\n       case\r\n           when writing_date is null then 'تاريخ كتابة المذكرة مفقود'\r\n           when arrival_at::date < writing_date then 'تاريخ الوصول قبل تاريخ الكتابة'\r\n           when arrival_at::date - writing_date <= 5 then 'غير متأخرة (ضمن المدة المسموحة)'\r\n           when date_trunc('month', arrival_at) = date_trunc('month', writing_date::timestamp) then 'متأخرة ضمن الشهر'\r\n           else 'متأخرة خارج الشهر'\r\n       end as delay_type,\r\n       case when writing_date is null then null else arrival_at::date - writing_date end as days_between,\r\n       case when writing_date is not null and arrival_at::date - writing_date > 5 then arrival_at::date - writing_date - 5 else 0 end as excess_delay_days,\r\n       case when writing_date is null then null else to_char(writing_date, 'YYYY/MM') end as writing_month,\r\n       to_char(arrival_at, 'YYYY/MM') as arrival_month\r\nfrom scoped_notes\r\norder by arrival_at desc, note_number", 
			"ErrorsByCompany" => "select s.company_name, count(f.field_id)::int as total_fields_checked,\r\n       count(f.field_id) filter (where f.field_value between 2 and 6)::int as total_errors,\r\n       count(f.field_id) filter (where f.field_value = 0)::int as total_unknown,\r\n       coalesce(round(count(f.field_id) filter (where f.field_value = 1)::numeric * 100 / nullif(count(f.field_id) filter (where f.field_value <> 0), 0), 2), 0) as average_accuracy\r\nfrom scoped_notes s\r\njoin reporting.vw_note_field_details f on f.note_id = s.note_id\r\nwhere f.field_value is not null\r\ngroup by s.company_name\r\norder by total_errors desc, s.company_name", 
			"ErrorsByField" => "select f.field_name, count(f.field_id)::int as total_checked,\r\n       count(f.field_id) filter (where f.field_value between 2 and 6)::int as total_errors,\r\n       count(f.field_id) filter (where f.field_value = 0)::int as total_unknown,\r\n       coalesce(round(count(f.field_id) filter (where f.field_value between 2 and 6)::numeric * 100 / nullif(count(f.field_id) filter (where f.field_value <> 0), 0), 2), 0) as error_rate\r\nfrom scoped_notes s\r\njoin reporting.vw_note_field_details f on f.note_id = s.note_id\r\nwhere f.field_value is not null\r\ngroup by f.field_name\r\norder by total_errors desc, f.field_name", 
			"CompanyDetailed" => ", trend_notes as (\n    select d.*\n    from reporting.vw_note_details d\n    join core.notes company_scope_note on company_scope_note.id = d.note_id and company_scope_note.is_deleted = false\n    where d.company_id = @company_id\n      and d.arrival_at::date between\n          (make_date(extract(year from cast(@from as date))::int,\n                  case when extract(month from cast(@from as date))::int <= 6 then 1 else 7 end,\n                  1))::date and @to\n      and (@factory_id is null or d.factory_id = @factory_id)\n      and (@production_line_id is null or d.production_line_id = @production_line_id)\n      and (@note_type_id is null or d.note_type_id = @note_type_id)\n      and (@status is null or d.status = @status)\n),\ntrend_months as (\n    select month_start::date as month_start\n    from generate_series(\n        (make_date(extract(year from cast(@from as date))::int,\n                  case when extract(month from cast(@from as date))::int <= 6 then 1 else 7 end,\n                  1))::date,\n        (make_date(extract(year from cast(@from as date))::int,\n                  case when extract(month from cast(@from as date))::int <= 6 then 1 else 7 end,\n                  1) + interval '5 months')::date,\n        interval '1 month'\n    ) month_start\n),\nfield_errors as (\n    select f.field_name,\n           count(*) filter (where f.field_value between 2 and 6)::int as field_error_count,\n           count(*) filter (where f.field_value <> 0)::int as field_checked,\n           coalesce(round(\n               count(*) filter (where f.field_value between 2 and 6)::numeric * 100\n               / nullif(count(*) filter (where f.field_value <> 0), 0), 2), 0) as field_error_rate\n    from scoped_notes s\n    join reporting.vw_note_field_details f on f.note_id = s.note_id\n    where f.field_value is not null\n    group by f.field_name\n    having count(*) filter (where f.field_value between 2 and 6) > 0\n    order by field_error_count desc, f.field_name\n    limit 8\n), summary as (\n    select\n        coalesce(max(s.company_name), (select c.name from core.companies c where c.id = @company_id)) as company_name,\n        count(*)::int as total_notes,\n        count(*) filter (where s.status = 'APPROVED')::int as approved_notes,\n        count(*) filter (where s.status in ('COMPLETE', 'HAS_ERRORS', 'NEEDS_REVIEW', 'UNDER_REVIEW'))::int as pending_review_notes,\n        count(*) filter (where s.status = 'RETURNED_FOR_CORRECTION')::int as returned_notes,\n        count(*) filter (where s.writing_date is not null and s.arrival_at::date >= s.writing_date and s.arrival_at::date - s.writing_date > 5)::int as late_notes,\n        count(*) filter (where s.writing_date is not null and s.arrival_at::date >= s.writing_date and s.arrival_at::date - s.writing_date between 0 and 5)::int as on_time_notes,\n        coalesce(round(count(*) filter (where s.writing_date is not null and s.arrival_at::date >= s.writing_date and s.arrival_at::date - s.writing_date > 5)::numeric * 100 / nullif(count(*) filter (where s.writing_date is not null), 0), 2), 0) as late_percentage,\n        coalesce(round(avg(s.arrival_at::date - s.writing_date) filter (where s.writing_date is not null and s.arrival_at::date >= s.writing_date and s.arrival_at::date - s.writing_date > 5), 2), 0) as average_late_days,\n        coalesce(max(s.arrival_at::date - s.writing_date) filter (where s.writing_date is not null and s.arrival_at::date >= s.writing_date and s.arrival_at::date - s.writing_date > 5), 0)::int as maximum_late_days,\n        coalesce(round(count(*) filter (where s.inactive_count = 0)::numeric * 100 / nullif(count(*), 0), 2), 0) as average_accuracy,\n        coalesce(round(count(*) filter (where s.inactive_count > 0)::numeric * 100 / nullif(count(*), 0), 2), 0) as error_rate,\n        coalesce(sum(s.unknown_count), 0)::int as unknown_indicators,\n        (\n            select count(*)::int\n            from core.duplicate_incidents di\n            join core.notes duplicate_note on duplicate_note.id = di.created_note_id\n            where di.classification = 'COMPANY_DUPLICATE_NUMBER'\n              and duplicate_note.arrival_at::date between @from and @to\n              and (@company_id is null or di.company_id = @company_id)\n              and (@factory_id is null or di.factory_id = @factory_id)\n              and (@production_line_id is null or di.production_line_id = @production_line_id)\n              and (@note_type_id is null or di.note_type_id = @note_type_id)\n        ) as duplicate_notes,\n        count(distinct s.factory_id)::int as factory_count,\n        max(s.arrival_at) as last_arrival_at,\n        count(*) filter (where s.inactive_count = 0)::int as clean_notes,\n        count(*) filter (where s.inactive_count > 0)::int as error_notes,\n        (\n            select count(*)::int\n            from core.duplicate_incidents di\n            join core.notes resend_note on resend_note.id = di.created_note_id\n            where di.classification = 'COMPANY_RESEND'\n              and resend_note.arrival_at::date between @from and @to\n              and (@company_id is null or di.company_id = @company_id)\n              and (@factory_id is null or di.factory_id = @factory_id)\n              and (@production_line_id is null or di.production_line_id = @production_line_id)\n              and (@note_type_id is null or di.note_type_id = @note_type_id)\n        ) as company_resend_notes,\n        count(*) filter (where s.writing_date is not null and s.arrival_at::date >= s.writing_date and s.arrival_at::date - s.writing_date > 5 and date_trunc('month', s.arrival_at) = date_trunc('month', s.writing_date::timestamp))::int as late_same_month_notes,\n        count(*) filter (where s.writing_date is not null and s.arrival_at::date >= s.writing_date and s.arrival_at::date - s.writing_date > 5 and date_trunc('month', s.arrival_at) <> date_trunc('month', s.writing_date::timestamp))::int as late_cross_month_notes\n    from scoped_notes s\n)\nselect 'summary' as row_kind, 1 as sort_order,\n       company_name, total_notes, approved_notes, pending_review_notes, returned_notes,\n       late_notes, on_time_notes, late_percentage, average_late_days, maximum_late_days,\n       average_accuracy, error_rate, unknown_indicators, duplicate_notes, factory_count, last_arrival_at,\n       null::text as period_label, null::int as period_total, null::int as period_late, null::numeric as period_accuracy,\n       null::text as status_label, null::int as status_count,\n       null::text as factory_label, null::int as factory_total, null::int as factory_late, null::numeric as factory_accuracy,\n       clean_notes, error_notes, company_resend_notes, late_same_month_notes, late_cross_month_notes,\n       null::int as period_clean_notes, null::int as period_error_notes,\n       null::text as field_label, null::int as field_error_count, null::int as field_checked, null::numeric as field_error_rate,\n       null::int as factory_clean_notes, null::int as factory_error_notes, null::int as factory_on_time, null::int as factory_max_late_days\nfrom summary\nunion all\nselect 'monthly', 2,\n       null::text, null::int, null::int, null::int, null::int, null::int, null::int, null::numeric, null::numeric, null::int,\n       null::numeric, null::numeric, null::int, null::int, null::int, null::timestamp,\n       to_char(tm.month_start, 'YYYY/MM'), count(t.note_id)::int,\n       count(t.note_id) filter (where t.writing_date is not null and t.arrival_at::date >= t.writing_date and t.arrival_at::date - t.writing_date > 5)::int,\n       coalesce(round(count(t.note_id) filter (where t.inactive_count = 0)::numeric * 100 / nullif(count(t.note_id), 0), 2), 0),\n       null::text, null::int, null::text, null::int, null::int, null::numeric,\n       null::int, null::int, null::int, null::int, null::int,\n       count(t.note_id) filter (where t.inactive_count = 0)::int,\n       count(t.note_id) filter (where t.inactive_count > 0)::int,\n       null::text, null::int, null::int, null::numeric,\n       null::int, null::int, null::int, null::int\nfrom trend_months tm\nleft join trend_notes t on t.arrival_at::date >= tm.month_start and t.arrival_at::date < (tm.month_start + interval '1 month')::date\ngroup by tm.month_start\nunion all\nselect 'status', 3,\n       null::text, null::int, null::int, null::int, null::int, null::int, null::int, null::numeric, null::numeric, null::int,\n       null::numeric, null::numeric, null::int, null::int, null::int, null::timestamp,\n       null::text, null::int, null::int, null::numeric,\n       case s.status\n           when 'DRAFT' then 'مسودة'\n           when 'COMPLETE' then 'مكتملة'\n           when 'HAS_ERRORS' then 'تحتوي أخطاء'\n           when 'NEEDS_REVIEW' then 'تحتاج مراجعة'\n           when 'UNDER_REVIEW' then 'قيد التدقيق'\n           when 'RETURNED_FOR_CORRECTION' then 'معادة للتصحيح'\n           when 'APPROVED' then 'معتمدة'\n           else s.status\n       end,\n       count(*)::int, null::text, null::int, null::int, null::numeric,\n       null::int, null::int, null::int, null::int, null::int, null::int, null::int,\n       null::text, null::int, null::int, null::numeric,\n       null::int, null::int, null::int, null::int\nfrom scoped_notes s\ngroup by s.status\nunion all\nselect 'factory', 4,\n       null::text, null::int, null::int, null::int, null::int, null::int, null::int, null::numeric, null::numeric, null::int,\n       null::numeric, null::numeric, null::int, null::int, null::int, null::timestamp,\n       null::text, null::int, null::int, null::numeric, null::text, null::int,\n       coalesce(s.factory_name, 'غير محدد'), count(*)::int,\n       count(*) filter (where s.writing_date is not null and s.arrival_at::date >= s.writing_date and s.arrival_at::date - s.writing_date > 5)::int,\n       coalesce(round(count(*) filter (where s.inactive_count = 0)::numeric * 100 / nullif(count(*), 0), 2), 0),\n       null::int, null::int, null::int, null::int, null::int, null::int, null::int,\n       null::text, null::int, null::int, null::numeric,\n       count(*) filter (where s.inactive_count = 0)::int,\n       count(*) filter (where s.inactive_count > 0)::int,\n       count(*) filter (where s.writing_date is not null and s.arrival_at::date >= s.writing_date and s.arrival_at::date - s.writing_date between 0 and 5)::int,\n       coalesce(max(s.arrival_at::date - s.writing_date) filter (where s.writing_date is not null and s.arrival_at::date >= s.writing_date and s.arrival_at::date - s.writing_date > 5), 0)::int\nfrom scoped_notes s\ngroup by s.factory_name\nunion all\nselect 'field_error', 5,\n       null::text, null::int, null::int, null::int, null::int, null::int, null::int, null::numeric, null::numeric, null::int,\n       null::numeric, null::numeric, null::int, null::int, null::int, null::timestamp,\n       null::text, null::int, null::int, null::numeric, null::text, null::int,\n       null::text, null::int, null::int, null::numeric,\n       null::int, null::int, null::int, null::int, null::int, null::int, null::int,\n       field_name, field_error_count, field_checked, field_error_rate,\n       null::int, null::int, null::int, null::int\nfrom field_errors\norder by sort_order, period_label nulls first, status_label nulls first, factory_label nulls first, field_error_count desc", 
			"CompanyPerformance" => "select s.company_name, count(*)::int as total_notes,\r\n       coalesce(round(count(*) filter (where s.inactive_count = 0)::numeric * 100 / nullif(count(*), 0), 2), 0) as average_accuracy,\r\n       coalesce(round(count(*) filter (where s.inactive_count > 0)::numeric * 100 / nullif(count(*), 0), 2), 0) as error_rate\r\nfrom scoped_notes s\r\ngroup by s.company_name\r\norder by average_accuracy desc, s.company_name", 
			"FactoryPerformance" => "select coalesce(f.name, 'غير محدد') as factory_name, count(*)::int as total_notes,\r\n       coalesce(round(count(*) filter (where s.inactive_count = 0)::numeric * 100 / nullif(count(*), 0), 2), 0) as average_accuracy,\r\n       coalesce(round(count(*) filter (where s.inactive_count > 0)::numeric * 100 / nullif(count(*), 0), 2), 0) as error_rate\r\nfrom scoped_notes s\r\njoin core.notes n on n.id = s.note_id\r\nleft join core.factories f on f.id = n.factory_id\r\ngroup by f.name\r\norder by average_accuracy desc, factory_name", 
			"MonthlySummary" => "select date_trunc('month', arrival_at)::date as month, count(*)::int as total_notes,\r\n       count(*) filter (where inactive_count > 0)::int as total_errors,\r\n       count(*) filter (where unknown_count > 0)::int as total_unknown,\r\n       coalesce(round(count(*) filter (where inactive_count = 0)::numeric * 100 / nullif(count(*), 0), 2), 0) as average_accuracy\r\nfrom scoped_notes\r\ngroup by 1\r\norder by 1 desc", 
			"UnknownFields" => "select f.note_number, f.company_name, f.note_type_name, f.field_name, f.field_value_label_ar,\r\n       f.writing_date, f.arrival_at,\r\n       case f.status\r\n           when 'DRAFT' then 'مسودة'\r\n           when 'COMPLETE' then 'مكتملة'\r\n           when 'HAS_ERRORS' then 'تحتوي أخطاء'\r\n           when 'NEEDS_REVIEW' then 'تحتاج مراجعة'\r\n           when 'UNDER_REVIEW' then 'قيد التدقيق'\r\n           when 'RETURNED_FOR_CORRECTION' then 'معادة للتصحيح'\r\n           when 'APPROVED' then 'معتمدة'\r\n           else f.status\r\n       end as status\r\nfrom scoped_notes s\r\njoin reporting.vw_note_field_details f on f.note_id = s.note_id\r\nwhere f.field_value = 0\r\norder by f.arrival_at desc, f.field_name", 
			"Duplicates" => "select note_number, company_name, note_type_name, writing_date, arrival_at,\r\n       case duplicate_classification\r\n           when 'COMPANY_RESEND' then coalesce(nullif(trim(duplicate_reason), ''), 'إعادة إرسال من الشركة')\r\n           when 'COMPANY_DUPLICATE_NUMBER' then coalesce(nullif(trim(duplicate_reason), ''), 'تكرار رقم صادر عن الشركة')\r\n           else coalesce(nullif(trim(duplicate_reason), ''), 'تكرار')\r\n       end as duplicate_reason,\r\n       created_by_name\r\nfrom scoped_notes\r\nwhere duplicate_classification is not null or (duplicate_reason is not null and length(trim(duplicate_reason)) > 0)\r\norder by arrival_at desc",
			"PendingReview" => "select note_number, company_name, note_type_name, writing_date, arrival_at, status, created_by_name\r\nfrom scoped_notes\r\nwhere status in ('COMPLETE', 'HAS_ERRORS', 'NEEDS_REVIEW', 'UNDER_REVIEW')\r\norder by arrival_at desc", 
			"ReturnedNotes" => "select note_number, company_name, note_type_name, writing_date, arrival_at, return_reason, created_by_name\r\nfrom scoped_notes\r\nwhere scoped_notes.status = 'RETURNED_FOR_CORRECTION'\r\norder by arrival_at desc", 
			"NoteLinks" => "select s.note_number as source_note_number, r.note_number as related_note_number,\r\n       s.company_name as source_company, r.company_name as related_company,\r\n       l.created_at\r\nfrom core.note_links l\r\njoin scoped_notes s on s.note_id = l.source_note_id\r\njoin scoped_notes r on r.note_id = l.related_note_id\r\norder by l.created_at desc", 
			"UserProductivity" => "select p.full_name as user_name, p.created_notes, p.updated_notes,\r\n       p.approved_notes, p.last_activity\r\nfrom reporting.fn_user_productivity(@from, @to) p\r\norder by p.full_name", 
			"AuditLogs" => "select l.created_at, coalesce(u.full_name, 'النظام') as user_name,\r\n       case l.action_type\r\n           when 'CREATE_NOTE' then 'إنشاء مذكرة'\r\n           when 'UPDATE_NOTE' then 'تعديل مذكرة'\r\n           when 'UPDATE_APPROVED_NOTE' then 'تعديل مذكرة معتمدة'\r\n           when 'APPROVE_NOTE' then 'اعتماد مذكرة'\r\n           when 'DELETE_NOTE' then 'حذف مذكرة'\r\n           when 'OVERRIDE_DUPLICATE' then 'تجاوز تنبيه التكرار'\r\n           when 'LINK_NOTES' then 'ربط مذكرات'\r\n           when 'UNLINK_NOTES' then 'إلغاء ربط مذكرات'\r\n           when 'LOGIN' then 'تسجيل دخول'\r\n           else l.action_type\r\n       end as action_name,\r\n       l.description\r\nfrom audit.logs l\r\nleft join auth.users u on u.id = l.user_id\r\nwhere l.created_at::date between @from and @to\r\norder by l.created_at desc", 
			"BackupHistory" => "select backup_type, source_profile_name, target_profile_name, file_path, status, started_at, finished_at, error_message\r\nfrom system.backup_history\r\nwhere started_at::date between @from and @to\r\norder by started_at desc", 
			_ => throw new InvalidOperationException("اسم التقرير غير معتمد."), 
		};
		text = CorrectDuplicateReferenceScope(reportName, text);
		if (1 == 0)
		{
		}
		string sql = text;
		if (reportName == "MonthlySummary" && filter.DateBasis == ReportDateBasis.WritingDate)
		{
			sql = sql.Replace("date_trunc('month', arrival_at)", "date_trunc('month', writing_date::timestamp)", StringComparison.Ordinal);
		}
		if (usesNoteScope)
		{
			sql = noteScope + sql;
		}
		DataTable result;
		await using (NpgsqlConnection connection = base.Factory.CreateConnection())
		{
			await connection.OpenAsync(cancellationToken);
			DataTable dataTable2;
			await using (NpgsqlCommand command = new NpgsqlCommand(sql, connection))
			{
				command.Parameters.AddWithValue("from", filter.From);
				command.Parameters.AddWithValue("to", filter.To);
				command.Parameters.Add("note_id", NpgsqlDbType.Uuid).Value = ((object)filter.NoteId) ?? DBNull.Value;
				command.Parameters.Add("has_writing_date", NpgsqlDbType.Boolean).Value = ((object)filter.HasWritingDate) ?? DBNull.Value;
				command.Parameters.Add("company_id", NpgsqlDbType.Uuid).Value = ((object)filter.CompanyId) ?? DBNull.Value;
				command.Parameters.Add("factory_id", NpgsqlDbType.Uuid).Value = ((object)filter.FactoryId) ?? DBNull.Value;
				command.Parameters.Add("production_line_id", NpgsqlDbType.Uuid).Value = ((object)filter.ProductionLineId) ?? DBNull.Value;
				command.Parameters.Add("note_type_id", NpgsqlDbType.Uuid).Value = ((object)filter.NoteTypeId) ?? DBNull.Value;
				command.Parameters.Add("created_by", NpgsqlDbType.Uuid).Value = ((object)filter.CreatedBy) ?? DBNull.Value;
				command.Parameters.Add("status", NpgsqlDbType.Varchar).Value = ((object)filter.Status?.ToString()) ?? ((object)DBNull.Value);
				command.Parameters.Add("arrival_cutoff_date", NpgsqlDbType.Date).Value = ((object)filter.EntryCutoffDate) ?? DBNull.Value;
				DataTable dataTable;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					DataTable table = new DataTable(reportName);
					table.Load(reader);
					dataTable = table;
				}
				dataTable2 = dataTable;
                if (reportName == "CompanyDetailed" && filter.CompanyId.HasValue)
                {
                    await PopulateCompanyAdvancedMetricsAsync(connection, dataTable2, filter, cancellationToken);
                }
			}
			result = dataTable2;
		}
		return result;
	}

	private async Task<DataTable> GetDuplicateIncidentTableAsync(
		ReportFilterModel filter,
		UserRole role,
		Guid requesterId,
		CancellationToken cancellationToken)
	{
		var periodExpression = filter.DateBasis == ReportDateBasis.WritingDate
			? "affected.writing_date"
			: "affected.arrival_at::date";
		var accessPredicate = role == UserRole.DATA_ENTRY
			? "and affected.created_by = @requester_id"
			: string.Empty;
		await using var connection = base.Factory.CreateConnection();
		await connection.OpenAsync(cancellationToken);
		var hasNumericContract = await HasNumericNoteNumberContractAsync(connection, cancellationToken);
		var numericNoteNumberExpression = hasNumericContract ? "affected.note_number_value" : "null::bigint";
		var sql = $"""
select
    di.id as incident_id,
    case di.classification
        when 'COMPANY_DUPLICATE_NUMBER' then 'تكرار رقم صادر عن الشركة'
        when 'COMPANY_RESEND' then 'إعادة إرسال من الشركة'
        else di.classification
    end as incident_type,
    di.classification as incident_classification,
    di.created_at as incident_created_at,
    affected.id as affected_note_id,
    affected.note_number as affected_note_number,
    {numericNoteNumberExpression} as affected_note_number_value,
    affected.company_id,
    company.name as company_name,
    nt.name_ar as note_type_name,
    factory.name as factory_name,
    production_line.name as production_line_name,
    affected.writing_date,
    affected.arrival_at,
    matched.id as matched_note_id,
    matched.note_number as matched_note_number,
    created.id as created_note_id,
    created.note_number as created_note_number,
    coalesce(decider.full_name, 'النظام') as incident_user,
    di.metadata::text as incident_metadata,
    '{(filter.DateBasis == ReportDateBasis.WritingDate ? "تاريخ الكتابة" : "تاريخ الوصول")}' as date_basis,
    @from as period_from,
    @to as period_to,
    @arrival_cutoff_date as arrival_cutoff_date
from core.duplicate_incidents di
join core.notes affected
  on affected.id = case
      when di.classification = 'COMPANY_DUPLICATE_NUMBER' then di.created_note_id
      when di.classification = 'COMPANY_RESEND' then di.matched_note_id
      else null
  end
left join core.notes matched on matched.id = di.matched_note_id
left join core.notes created on created.id = di.created_note_id
left join core.companies company on company.id = affected.company_id
left join core.note_types nt on nt.id = affected.note_type_id
left join core.factories factory on factory.id = affected.factory_id
left join core.production_lines production_line on production_line.id = affected.production_line_id
left join auth.users decider on decider.id = di.decided_by
where di.classification in ('COMPANY_DUPLICATE_NUMBER', 'COMPANY_RESEND')
  and not affected.is_deleted
  and {periodExpression} between @from and @to
  and (@arrival_cutoff_date is null or affected.arrival_at::date <= @arrival_cutoff_date)
  and (@company_id is null or affected.company_id = @company_id)
  and (@factory_id is null or affected.factory_id = @factory_id)
  and (@production_line_id is null or affected.production_line_id = @production_line_id)
  and (@note_type_id is null or affected.note_type_id = @note_type_id)
  and (@status is null or affected.status = @status)
  {accessPredicate}
order by company.name asc, affected.note_number_value asc nulls last, di.created_at asc, di.id asc
""";

		await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
		command.Parameters.Add("from", NpgsqlDbType.Date).Value = filter.From;
		command.Parameters.Add("to", NpgsqlDbType.Date).Value = filter.To;
		command.Parameters.Add("arrival_cutoff_date", NpgsqlDbType.Date).Value = (object?)filter.EntryCutoffDate ?? DBNull.Value;
		command.Parameters.Add("company_id", NpgsqlDbType.Uuid).Value = (object?)filter.CompanyId ?? DBNull.Value;
		command.Parameters.Add("factory_id", NpgsqlDbType.Uuid).Value = (object?)filter.FactoryId ?? DBNull.Value;
		command.Parameters.Add("production_line_id", NpgsqlDbType.Uuid).Value = (object?)filter.ProductionLineId ?? DBNull.Value;
		command.Parameters.Add("note_type_id", NpgsqlDbType.Uuid).Value = (object?)filter.NoteTypeId ?? DBNull.Value;
		command.Parameters.Add("status", NpgsqlDbType.Varchar).Value = (object?)filter.Status?.ToString() ?? DBNull.Value;
		command.Parameters.Add("requester_id", NpgsqlDbType.Uuid).Value = requesterId;
		await using var reader = await command.ExecuteReaderAsync(cancellationToken);
		var table = new DataTable("DuplicateIncidentReport");
		table.Load(reader);
		return table;
	}

	private static async Task<bool> HasNumericNoteNumberContractAsync(
		NpgsqlConnection connection,
		CancellationToken cancellationToken)
	{
		await using var command = new NpgsqlCommand("""
			select exists(
			    select 1
			    from information_schema.columns
			    where table_schema = 'core'
			      and table_name = 'notes'
			      and column_name = 'note_number_value')
			""", connection);
		return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
	}

	
	private static async Task PopulateCompanyAdvancedMetricsAsync(NpgsqlConnection connection, DataTable table, ReportFilterModel filter, CancellationToken cancellationToken)
	{
		EnsureColumn(table, "written_month_label", typeof(string));
		EnsureColumn(table, "written_count", typeof(int));
		EnsureColumn(table, "memo_items_efficiency_index", typeof(decimal));
		EnsureColumn(table, "entry_notes", typeof(int));
		EnsureColumn(table, "unlinked_entry_notes", typeof(int));
		EnsureColumn(table, "time_stability_index", typeof(decimal));
		EnsureColumn(table, "recurrence_rate_index", typeof(decimal));
EnsureColumn(table, "mechanism_efficiency_index", typeof(decimal));
		EnsureColumn(table, "overall_general_efficiency_index", typeof(decimal));

		// Npgsql loads computed/aggregate output columns (count(*), coalesce, to_char...) as
		// read-only, which makes Dat  aRow assignment below throw ReadOnlyException. Unfreeze
		// every column this method writes so the fn results can be merged into the summary row.
		foreach (string name in new[]
		{
			"row_kind", "period_label", "written_month_label", "written_count",
			"total_notes", "on_time_notes", "late_notes", "duplicate_notes",
			"entry_notes", "unlinked_entry_notes", "memo_items_efficiency_index",
			"time_stability_index", "recurrence_rate_index", "mechanism_efficiency_index",
			"overall_general_efficiency_index"
		})
		{
			if (table.Columns.Contains(name)) table.Columns[name]!.ReadOnly = false;
		}

		await using NpgsqlCommand command = new NpgsqlCommand("""
select row_kind, period_label, written_count, total_notes, on_time_notes, late_notes,
       duplicate_notes, entry_notes, unlinked_entry_notes, memo_items_efficiency_index,
       time_stability_index, recurrence_rate_index, mechanism_efficiency_index,
       overall_general_efficiency_index
from reporting.fn_company_advanced_metrics(@company_id, @from, @to)
order by case when row_kind = 'metrics' then 0 else 1 end, period_label
""", connection)
		{
			CommandTimeout = 30
		};
		command.Parameters.Add("company_id", NpgsqlDbType.Uuid).Value = filter.CompanyId!.Value;
		command.Parameters.Add("from", NpgsqlDbType.Date).Value = filter.From;
		command.Parameters.Add("to", NpgsqlDbType.Date).Value = filter.To;

		var summary = table.AsEnumerable().FirstOrDefault(row => FieldText(row, "row_kind") == "summary");
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		while (await reader.ReadAsync(cancellationToken))
		{
			var rowKind = reader.GetString(0);
			if (rowKind == "metrics")
			{
				if (summary is null) continue;
				summary["written_count"] = reader.GetInt32(2);
				summary["total_notes"] = reader.GetInt32(3);
				summary["on_time_notes"] = reader.GetInt32(4);
				summary["late_notes"] = reader.GetInt32(5);
				summary["duplicate_notes"] = reader.GetInt32(6);
				summary["entry_notes"] = reader.GetInt32(7);
				summary["unlinked_entry_notes"] = reader.GetInt32(8);
				summary["memo_items_efficiency_index"] = reader.GetDecimal(9);
				summary["time_stability_index"] = reader.GetDecimal(10);
				summary["recurrence_rate_index"] = reader.GetDecimal(11);
				summary["mechanism_efficiency_index"] = reader.GetDecimal(12);
				summary["overall_general_efficiency_index"] = reader.GetDecimal(13);
				continue;
			}

if (rowKind != "written_month" || reader.IsDBNull(1)) continue;
			var writtenRow = table.NewRow();
			writtenRow["row_kind"] = rowKind;
			writtenRow["period_label"] = reader.GetString(1);
			writtenRow["written_month_label"] = reader.GetString(1);
			writtenRow["written_count"] = reader.GetInt32(2);
			table.Rows.Add(writtenRow);
		}

		// Close the fn reader first: Npgsql allows only one active command per connection.
		await reader.DisposeAsync();

		await PopulateWrittenMonthHistoryAsync(connection, table, filter, cancellationToken);
	}

	// Feeds the monthly written-memos bar chart. The fn only returns the months inside
	// the filter range (a single month for the monthly report), which rendered as one
	// oversized block. This query adds the preceding five months so the chart shows
	// six distinct monthly columns, using writing_date as the time reference.
	private static async Task PopulateWrittenMonthHistoryAsync(NpgsqlConnection connection, DataTable table, ReportFilterModel filter, CancellationToken cancellationToken)
	{
		await using NpgsqlCommand command = new NpgsqlCommand("""
select to_char(month_start, 'YYYY/MM'),
       count(n.id)::int
from generate_series(
         (make_date(extract(year from cast(@from as date))::int,
                    case when extract(month from cast(@from as date))::int <= 6 then 1 else 7 end,
                    1))::date,
         (make_date(extract(year from cast(@from as date))::int,
                    case when extract(month from cast(@from as date))::int <= 6 then 1 else 7 end,
                    1) + interval '5 months')::date,
         interval '1 month'
     ) as months(month_start)
left join core.notes n
       on n.company_id = @company_id
      and n.is_deleted = false
      and n.writing_date >= months.month_start::date
      and n.writing_date < (months.month_start + interval '1 month')::date
group by months.month_start
order by months.month_start
""", connection)
		{
			CommandTimeout = 30
		};
		command.Parameters.Add("company_id", NpgsqlDbType.Uuid).Value = filter.CompanyId!.Value;
		command.Parameters.Add("from", NpgsqlDbType.Date).Value = filter.From;

		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		while (await reader.ReadAsync(cancellationToken))
		{
			var label = reader.GetString(0);
			var existing = table.AsEnumerable()
				.FirstOrDefault(row => FieldText(row, "row_kind") == "written_month" && FieldText(row, "written_month_label") == label);
			if (existing is not null)
			{
				existing["written_count"] = reader.GetInt32(1);
				continue;
			}
			var row = table.NewRow();
			row["row_kind"] = "written_month";
			row["period_label"] = label;
			row["written_month_label"] = label;
			row["written_count"] = reader.GetInt32(1);
			table.Rows.Add(row);
		}
	}

	public async Task<DataSet> GetMemoLogExportAsync(ReportFilterModel filter, Guid requesterId, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentNullException.ThrowIfNull(filter, "filter");
		NormalizeFilterDates(filter);
		if (filter.From > filter.To)
		{
			throw new InvalidOperationException("تاريخ البداية يجب ألا يكون بعد تاريخ النهاية.");
		}
		UserRole role = await RepositoryBase.GetUserRoleAsync(base.Factory, requesterId, cancellationToken);
		if (!ReportAccessPolicy.CanAccessReport(role, "MemoLogExport"))
		{
			throw new UnauthorizedAccessException("ليس لديك صلاحية لتصدير سجل المذكرات.");
		}
		if (role == UserRole.DATA_ENTRY)
		{
			filter.CreatedBy = requesterId;
		}

		DataSet result = new DataSet();
		Dictionary<string, DataTable> tables = new Dictionary<string, DataTable>
		{
			["ENTRY"] = result.Tables.Add("ENTRY"),
			["EXIT"] = result.Tables.Add("EXIT"),
			["TRANSFER"] = result.Tables.Add("TRANSFER"),
			["DELIVERY"] = result.Tables.Add("DELIVERY")
		};

		await using (NpgsqlConnection connection = base.Factory.CreateConnection())
		{
			await connection.OpenAsync(cancellationToken);

			// Follow-up columns = the type's active fields. Identity is the field id;
			// Caption is display-only because Arabic names are not required to be unique.
			await using (NpgsqlCommand headerCommand = new NpgsqlCommand("""
select nt.code, ntf.sort_order, ntf.field_id, nf.name_ar
from core.note_types nt
join core.note_type_fields ntf on ntf.note_type_id = nt.id and ntf.is_active and not ntf.is_deleted
join core.note_fields nf on nf.id = ntf.field_id and nf.is_active and not nf.is_deleted
order by nt.code, ntf.sort_order
""", connection)
			{
				CommandTimeout = 30
			})
			{
				Dictionary<string, List<(Guid FieldId, string Caption)>> columnsByType = new();
				await using (NpgsqlDataReader headerReader = await headerCommand.ExecuteReaderAsync(cancellationToken))
				{
					while (await headerReader.ReadAsync(cancellationToken))
					{
						string code = headerReader.GetString(0);
						if (!columnsByType.TryGetValue(code, out List<(Guid FieldId, string Caption)>? list))
						{
							list = new List<(Guid FieldId, string Caption)>();
							columnsByType[code] = list;
						}
						list.Add((headerReader.GetGuid(2), headerReader.GetString(3)));
					}
				}
				foreach (DataTable table in result.Tables)
				{
					AddMemoLogBasicColumns(table);
					if (columnsByType.TryGetValue(table.TableName, out List<(Guid FieldId, string Caption)>? columns))
					{
						foreach ((Guid fieldId, string caption) in columns)
						{
							string columnName = MemoLogFollowUpColumnName(fieldId);
							if (table.Columns.Contains(columnName)) continue;
							DataColumn column = table.Columns.Add(columnName, typeof(int));
							column.Caption = caption;
						}
					}
				}
			}

			await using (NpgsqlCommand dataCommand = new NpgsqlCommand("""
with scoped_notes as (
    select n.*
    from core.notes n
    where not n.is_deleted
      and (@company_id is null or n.company_id = @company_id)
      and (@from is null or n.arrival_at::date >= @from)
      and (@to is null or n.arrival_at::date <= @to)
      and (@created_by is null or n.created_by = @created_by)
      and (@arrival_cutoff_date is null or n.arrival_at::date <= @arrival_cutoff_date)
)
select
    sn.id as note_id,
    nt.code as note_type_code,
    sn.note_number as note_number,
    coalesce(c.name, '') as company_name,
    coalesce(f.name, '') as factory_name,
    coalesce(pl.name, '') as production_line_name,
    coalesce(linked.linked_note_number, '') as linked_note_number,
    sn.writing_date as writing_date,
    sn.arrival_at as arrival_at,
    case sn.status
        when 'DRAFT' then 'مسودة'
        when 'COMPLETE' then 'مكتملة'
        when 'HAS_ERRORS' then 'تحتوي أخطاء'
        when 'NEEDS_REVIEW' then 'تحتاج مراجعة'
        when 'UNDER_REVIEW' then 'قيد التدقيق'
        when 'RETURNED_FOR_CORRECTION' then 'معادة للتصحيح'
        when 'APPROVED' then 'معتمدة'
        else sn.status
    end as status,
    coalesce(u.full_name, '') as created_by_name,
    coalesce(sn.notes, '') as notes,
    ntf.field_id as field_id,
    nfv.field_value as field_value,
    nt.name_ar as note_type_name,
    sn.company_id as company_id
from scoped_notes sn
join core.note_types nt on nt.id = sn.note_type_id
left join core.companies c on c.id = sn.company_id
left join core.factories f on f.id = sn.factory_id
left join core.production_lines pl on pl.id = sn.production_line_id
left join auth.users u on u.id = sn.created_by
left join lateral (
    select case
        when coalesce(sn.is_loot, false) or exists (select 1 from core.note_sources s where s.note_id = sn.id and s.source_type = 'LOOT')
            then 'غنائم ومستوردات'
        else (
            select string_agg(o.note_number, ', ' order by l.created_at, o.note_number)
            from core.v_note_links_compat l
            join core.notes o on o.id in (l.source_note_id, l.related_note_id)
            where (l.source_note_id = sn.id or l.related_note_id = sn.id)
              and o.id <> sn.id
              and not o.is_deleted
        )
    end as linked_note_number
) linked on true
left join core.note_type_fields ntf on ntf.note_type_id = sn.note_type_id and ntf.is_active and not ntf.is_deleted
left join core.note_fields nf on nf.id = ntf.field_id and nf.is_active and not nf.is_deleted
left join core.note_field_values nfv on nfv.note_id = sn.id and nfv.field_id = ntf.field_id
order by sn.arrival_at, sn.note_number, sn.id, ntf.sort_order
""", connection)
			{
				CommandTimeout = 120
			})
			{
				dataCommand.Parameters.Add("company_id", NpgsqlDbType.Uuid).Value = filter.CompanyId.HasValue ? (object)filter.CompanyId.Value : DBNull.Value;
				dataCommand.Parameters.Add("from", NpgsqlDbType.Date).Value = filter.From == DateOnly.MinValue ? DBNull.Value : (object)filter.From;
				dataCommand.Parameters.Add("to", NpgsqlDbType.Date).Value = (object)filter.To;
				dataCommand.Parameters.Add("created_by", NpgsqlDbType.Uuid).Value = filter.CreatedBy.HasValue ? (object)filter.CreatedBy.Value : DBNull.Value;
				dataCommand.CommandText = dataCommand.CommandText
					.Replace("n.arrival_at::date >= @from", (filter.DateBasis == ReportDateBasis.WritingDate ? "n.writing_date" : "n.arrival_at::date") + " >= @from", StringComparison.Ordinal)
					.Replace("n.arrival_at::date <= @to", (filter.DateBasis == ReportDateBasis.WritingDate ? "n.writing_date" : "n.arrival_at::date") + " <= @to", StringComparison.Ordinal);
				dataCommand.Parameters.Add("arrival_cutoff_date", NpgsqlDbType.Date).Value = filter.EntryCutoffDate.HasValue ? (object)filter.EntryCutoffDate.Value : DBNull.Value;

				await using NpgsqlDataReader reader = await dataCommand.ExecuteReaderAsync(cancellationToken);
				DataTable? currentTable = null;
				DataRow? currentRow = null;
				Guid? currentNoteId = null;
				HashSet<string> setFields = new HashSet<string>();
				while (await reader.ReadAsync(cancellationToken))
				{
					Guid noteId = reader.GetGuid(0);
					if (noteId != currentNoteId)
					{
						FinalizeMemoLogRow(currentTable, currentRow, setFields);
						currentNoteId = noteId;
						currentTable = tables.TryGetValue(reader.GetString(1), out DataTable? table) ? table : null;
						currentRow = currentTable?.NewRow();
						if (currentRow is not null)
						{
                            currentRow["note_id"] = noteId;
                            currentRow["note_type_code"] = reader.GetString(1);
                            currentRow["note_type_name"] = reader.IsDBNull(14) ? reader.GetString(1) : reader.GetString(14);
                            currentRow["company_id"] = reader.IsDBNull(15) ? DBNull.Value : reader.GetGuid(15);
                            currentRow["company_name"] = reader.GetString(3);
							currentRow["factory_name"] = reader.GetString(4);
							currentRow["production_line_name"] = reader.GetString(5);
							currentRow["note_number"] = reader.GetString(2);
							currentRow["linked_note_number"] = reader.GetString(6);
							currentRow["writing_date"] = reader.IsDBNull(7) ? DBNull.Value : reader.GetDateTime(7);
							currentRow["arrival_at"] = reader.IsDBNull(8) ? DBNull.Value : reader.GetDateTime(8);
							currentRow["status"] = reader.GetString(9);
							currentRow["created_by_name"] = reader.GetString(10);
							currentRow["notes"] = reader.GetString(11);
							currentRow["date_basis"] = filter.DateBasis == ReportDateBasis.WritingDate ? "تاريخ الكتابة" : "تاريخ الوصول";
							currentRow["period_from"] = filter.From == DateOnly.MinValue ? DBNull.Value : filter.From.ToDateTime(TimeOnly.MinValue);
							currentRow["period_to"] = filter.To.ToDateTime(TimeOnly.MinValue);
							currentRow["arrival_cutoff_date"] = filter.EntryCutoffDate.HasValue ? (object)filter.EntryCutoffDate.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
						}
						setFields.Clear();
					}
					if (currentRow is not null && !reader.IsDBNull(12))
					{
						string columnName = MemoLogFollowUpColumnName(reader.GetGuid(12));
						if (currentTable!.Columns.Contains(columnName))
						{
							// Correct (value 1) is 0; every other value, including missing, requires follow-up (1).
							int followUpFlag = !reader.IsDBNull(13) && reader.GetInt16(13) == 1 ? 0 : 1;
							currentRow[columnName] = followUpFlag;
							setFields.Add(columnName);
						}
					}
				}
				FinalizeMemoLogRow(currentTable, currentRow, setFields);
			}
        DataTable companies = result.Tables.Add("COMPANIES");
        companies.Columns.Add("company_id", typeof(Guid));
        companies.Columns.Add("company_name", typeof(string));
        companies.Columns.Add("is_active", typeof(bool));
        companies.Columns.Add("note_count", typeof(int));
        await using (NpgsqlCommand companiesCommand = new NpgsqlCommand("""
select c.id, c.name, c.is_active, count(n.id)::int as note_count
from core.companies c
left join core.notes n
  on n.company_id = c.id
 and not n.is_deleted
 and (@from is null or n.arrival_at::date >= @from)
 and (@to is null or n.arrival_at::date <= @to)
  and (@created_by is null or n.created_by = @created_by)
  and (@arrival_cutoff_date is null or n.arrival_at::date <= @arrival_cutoff_date)
 where not c.is_deleted
group by c.id, c.name, c.is_active
order by c.name
""", connection)
        {
            CommandTimeout = 30
        })
        {
            companiesCommand.Parameters.Add("from", NpgsqlDbType.Date).Value = filter.From == DateOnly.MinValue ? DBNull.Value : (object)filter.From;
            companiesCommand.Parameters.Add("to", NpgsqlDbType.Date).Value = (object)filter.To;
            companiesCommand.Parameters.Add("created_by", NpgsqlDbType.Uuid).Value = filter.CreatedBy.HasValue ? (object)filter.CreatedBy.Value : DBNull.Value;
            companiesCommand.CommandText = companiesCommand.CommandText
                .Replace("n.arrival_at::date >= @from", (filter.DateBasis == ReportDateBasis.WritingDate ? "n.writing_date" : "n.arrival_at::date") + " >= @from", StringComparison.Ordinal)
                .Replace("n.arrival_at::date <= @to", (filter.DateBasis == ReportDateBasis.WritingDate ? "n.writing_date" : "n.arrival_at::date") + " <= @to", StringComparison.Ordinal);
            companiesCommand.Parameters.Add("arrival_cutoff_date", NpgsqlDbType.Date).Value = filter.EntryCutoffDate.HasValue ? (object)filter.EntryCutoffDate.Value : DBNull.Value;
            if (filter.CompanyId.HasValue)
            {
                companiesCommand.CommandText = companiesCommand.CommandText.Replace("where not c.is_deleted", "where not c.is_deleted and c.id = @company_id");
                companiesCommand.Parameters.Add("company_id", NpgsqlDbType.Uuid).Value = filter.CompanyId.Value;
            }
            await using NpgsqlDataReader companyReader = await companiesCommand.ExecuteReaderAsync(cancellationToken);
            while (await companyReader.ReadAsync(cancellationToken))
            {
                DataRow row = companies.NewRow();
                row["company_id"] = companyReader.GetGuid(0);
                row["company_name"] = companyReader.GetString(1);
                row["is_active"] = companyReader.GetBoolean(2);
                row["note_count"] = companyReader.GetInt32(3);
                companies.Rows.Add(row);
            }
        }
        }
    BuildBusinessExportTables(result);
    return result;
	}

	    private sealed class ExportAccumulator
    {
        public string Dimension { get; init; } = "";
        public string Label { get; init; } = "";
        public int TotalNotes { get; set; }
        public int CorrectItems { get; set; }
        public int FollowUpItems { get; set; }
    }

    private static void BuildBusinessExportTables(DataSet result)
    {
        DataTable companies = result.Tables["COMPANIES"] ?? throw new InvalidOperationException("جدول الشركات غير متاح.");
        DataTable summary = result.Tables.Add("SUMMARY");
        summary.Columns.Add("metric", typeof(string)).Caption = "المؤشر";
        summary.Columns.Add("value", typeof(decimal)).Caption = "القيمة";
        summary.Columns.Add("unit", typeof(string)).Caption = "الوحدة";
        summary.Columns.Add("state", typeof(string)).Caption = "حالة البيانات";
        summary.Columns.Add("definition", typeof(string)).Caption = "التعريف";

        DataTable notes = result.Tables.Add("NOTES");
        foreach ((string name, Type type, string caption) in new[]
        {
            ("note_id", typeof(Guid), "المعرف الثابت"),
            ("note_type_code", typeof(string), "رمز نوع المذكرة"),
            ("note_type", typeof(string), "نوع المذكرة"),
            ("company_id", typeof(Guid), "معرف الشركة"),
            ("company", typeof(string), "الشركة"),
            ("factory", typeof(string), "المصنع"),
            ("line", typeof(string), "خط الإنتاج"),
            ("note_number", typeof(string), "رقم المذكرة"),
            ("linked_note_number", typeof(string), "رقم المذكرة المرتبطة"),
            ("writing_date", typeof(DateTime), "تاريخ التحرير"),
            ("arrival_at", typeof(DateTime), "تاريخ الوصول"),
            ("status", typeof(string), "الحالة"),
            ("created_by", typeof(string), "المدخل"),
            ("notes", typeof(string), "ملاحظات")
        })
        {
            notes.Columns.Add(name, type).Caption = caption;
        }

        DataTable items = result.Tables.Add("ITEMS");
        foreach ((string name, Type type, string caption) in new[]
        {
            ("note_id", typeof(Guid), "المعرف الثابت"),
            ("note_type", typeof(string), "نوع المذكرة"),
            ("company", typeof(string), "الشركة"),
            ("note_number", typeof(string), "رقم المذكرة"),
            ("item_name", typeof(string), "البند"),
            ("follow_up", typeof(int), "علامة المتابعة"),
            ("result", typeof(string), "نتيجة البند")
        })
        {
            items.Columns.Add(name, type).Caption = caption;
        }

        DataTable audit = result.Tables.Add("AUDIT");
        foreach ((string name, Type type, string caption) in new[]
        {
            ("item_name", typeof(string), "البند"),
            ("total", typeof(int), "الإجمالي"),
            ("correct", typeof(int), "الصحيح"),
            ("follow_up", typeof(int), "يحتاج متابعة"),
            ("accuracy", typeof(decimal), "نسبة الصحة")
        })
        {
            audit.Columns.Add(name, type).Caption = caption;
        }

        DataTable statistics = result.Tables.Add("STATISTICS");
        foreach ((string name, Type type, string caption) in new[]
        {
            ("dimension", typeof(string), "نوع التجميع"),
            ("label", typeof(string), "الاسم"),
            ("total_notes", typeof(int), "إجمالي المذكرات"),
            ("correct_items", typeof(int), "البنود الصحيحة"),
            ("follow_up_items", typeof(int), "بنود تحتاج متابعة"),
            ("accuracy", typeof(decimal), "نسبة الصحة")
        })
        {
            statistics.Columns.Add(name, type).Caption = caption;
        }

        Dictionary<string, ExportAccumulator> stats = new(StringComparer.Ordinal);
        Dictionary<string, ExportAccumulator> audits = new(StringComparer.Ordinal);
        Dictionary<Guid, List<string>> statKeysByNote = new();

        void AddNoteStat(Guid? noteId, string dimension, string label)
        {
            string key = dimension + ":" + label;
            if (!stats.TryGetValue(key, out ExportAccumulator? accumulator))
            {
                accumulator = new ExportAccumulator { Dimension = dimension, Label = label };
                stats[key] = accumulator;
            }
            accumulator.TotalNotes++;
            if (noteId is Guid id)
            {
                if (!statKeysByNote.TryGetValue(id, out List<string>? keys))
                {
                    keys = new List<string>();
                    statKeysByNote[id] = keys;
                }
                if (!keys.Contains(key, StringComparer.Ordinal)) keys.Add(key);
            }
        }

        foreach (string typeCode in new[] { "ENTRY", "EXIT", "TRANSFER", "DELIVERY" })
        {
            DataTable? raw = result.Tables[typeCode];
            if (raw is null) continue;
            foreach (DataRow rawRow in raw.Rows)
            {
                DataRow note = notes.NewRow();
                CopyValue(rawRow, note, "note_id", "note_id");
                CopyValue(rawRow, note, "note_type_code", "note_type_code");
                CopyValue(rawRow, note, "note_type_name", "note_type");
                CopyValue(rawRow, note, "company_id", "company_id");
                CopyValue(rawRow, note, "company_name", "company");
                CopyValue(rawRow, note, "factory_name", "factory");
                CopyValue(rawRow, note, "production_line_name", "line");
                CopyValue(rawRow, note, "note_number", "note_number");
                CopyValue(rawRow, note, "linked_note_number", "linked_note_number");
                CopyValue(rawRow, note, "writing_date", "writing_date");
                CopyValue(rawRow, note, "arrival_at", "arrival_at");
                CopyValue(rawRow, note, "status", "status");
                CopyValue(rawRow, note, "created_by_name", "created_by");
                CopyValue(rawRow, note, "notes", "notes");
                notes.Rows.Add(note);

                Guid? noteId = note.IsNull("note_id") ? null : note.Field<Guid>("note_id");
                string company = CellText(note, "company");
                string status = CellText(note, "status");
                string month = note.IsNull("arrival_at") ? "غير محدد" : note.Field<DateTime>("arrival_at").ToString("yyyy/MM", System.Globalization.CultureInfo.InvariantCulture);
                AddNoteStat(noteId, "الشركة", string.IsNullOrWhiteSpace(company) ? "غير محددة" : company);
                AddNoteStat(noteId, "الحالة", string.IsNullOrWhiteSpace(status) ? "غير محددة" : status);
                AddNoteStat(noteId, "الشهر", month);

                foreach (DataColumn column in raw.Columns)
                {
                    if (!column.ColumnName.StartsWith("field_", StringComparison.OrdinalIgnoreCase)) continue;
                    int followUp = rawRow.IsNull(column) ? 1 : Convert.ToInt32(rawRow[column], System.Globalization.CultureInfo.InvariantCulture);
                    DataRow item = items.NewRow();
                    CopyValue(note, item, "note_id", "note_id");
                    CopyValue(note, item, "note_type", "note_type");
                    CopyValue(note, item, "company", "company");
                    CopyValue(note, item, "note_number", "note_number");
                    item["item_name"] = string.IsNullOrWhiteSpace(column.Caption) ? column.ColumnName : column.Caption;
                    item["follow_up"] = followUp == 0 ? 0 : 1;
                    item["result"] = followUp == 0 ? "صحيح" : "يحتاج متابعة";
                    items.Rows.Add(item);

                    string auditKey = typeCode + ":" + column.ColumnName;
                    if (!audits.TryGetValue(auditKey, out ExportAccumulator? auditAccumulator))
                    {
                        auditAccumulator = new ExportAccumulator { Dimension = typeCode, Label = item["item_name"].ToString() ?? column.ColumnName };
                        audits[auditKey] = auditAccumulator;
                    }
                    if (followUp == 0) auditAccumulator.CorrectItems++; else auditAccumulator.FollowUpItems++;
                    if (noteId is Guid id && statKeysByNote.TryGetValue(id, out List<string>? keys))
                    {
                        foreach (string key in keys)
                        {
                            if (followUp == 0) stats[key].CorrectItems++; else stats[key].FollowUpItems++;
                        }
                    }
                }
            }
        }

        foreach (DataRow company in companies.Rows)
        {
            DataRow row = summary.NewRow();
            row["metric"] = "مذكرات الشركة";
            row["value"] = company["note_count"];
            row["unit"] = "مذكرة";
            row["state"] = Convert.ToInt32(company["note_count"], System.Globalization.CultureInfo.InvariantCulture) == 0 ? "لا توجد بيانات" : "متاح";
            row["definition"] = company["company_name"];
            summary.Rows.Add(row);
        }

        AddSummary(summary, "إجمالي الشركات", companies.Rows.Count, "شركة", "جميع الشركات غير المحذوفة، بما فيها ذات الصفر.");
        AddSummary(summary, "إجمالي المذكرات", notes.Rows.Count, "مذكرة", "المذكرات الفعلية ضمن نطاق التصدير.");
        AddSummary(summary, "إجمالي البنود", items.Rows.Count, "بند", "كل بند نشط ظهر في المذكرات.");
        int correctItems = items.Rows.Cast<DataRow>().Count(row => Convert.ToInt32(row["follow_up"], System.Globalization.CultureInfo.InvariantCulture) == 0);
        AddSummary(summary, "البنود الصحيحة", correctItems, "بند", "قيمة الحقل الصحيحة.");
        AddSummary(summary, "بنود تحتاج متابعة", items.Rows.Count - correctItems, "بند", "كل قيمة غير صحيحة أو مفقودة.");
        AddSummary(summary, "نسبة الصحة", items.Rows.Count == 0 ? null : Math.Round(correctItems * 100m / items.Rows.Count, 2), "%", "الصحيح ÷ إجمالي البنود × 100.");

        foreach (ExportAccumulator accumulator in audits.Values.OrderBy(item => item.Dimension).ThenBy(item => item.Label, StringComparer.Ordinal))
        {
            DataRow row = audit.NewRow();
            row["item_name"] = accumulator.Dimension + " — " + accumulator.Label;
            row["total"] = accumulator.CorrectItems + accumulator.FollowUpItems;
            row["correct"] = accumulator.CorrectItems;
            row["follow_up"] = accumulator.FollowUpItems;
            int total = accumulator.CorrectItems + accumulator.FollowUpItems;
            row["accuracy"] = total == 0 ? DBNull.Value : Math.Round(accumulator.CorrectItems * 100m / total, 2);
            audit.Rows.Add(row);
        }

        foreach (ExportAccumulator accumulator in stats.Values.OrderBy(item => item.Dimension).ThenBy(item => item.Label, StringComparer.Ordinal))
        {
            DataRow row = statistics.NewRow();
            row["dimension"] = accumulator.Dimension;
            row["label"] = accumulator.Label;
            row["total_notes"] = accumulator.TotalNotes;
            row["correct_items"] = accumulator.CorrectItems;
            row["follow_up_items"] = accumulator.FollowUpItems;
            int total = accumulator.CorrectItems + accumulator.FollowUpItems;
            row["accuracy"] = total == 0 ? DBNull.Value : Math.Round(accumulator.CorrectItems * 100m / total, 2);
            statistics.Rows.Add(row);
        }
    }

    private static void AddSummary(DataTable table, string metric, decimal? value, string unit, string definition)
    {
        DataRow row = table.NewRow();
        row["metric"] = metric;
        row["value"] = value.HasValue ? value.Value : DBNull.Value;
        row["unit"] = unit;
        row["state"] = value.HasValue ? "متاح" : "لا توجد بيانات";
        row["definition"] = definition;
        table.Rows.Add(row);
    }

    private static void CopyValue(DataRow source, DataRow target, string sourceName, string targetName)
    {
        target[targetName] = !source.Table.Columns.Contains(sourceName) || source.IsNull(sourceName)
            ? DBNull.Value
            : source[sourceName];
    }

    private static string CellText(DataRow row, string columnName) =>
        !row.Table.Columns.Contains(columnName) || row.IsNull(columnName) ? "" : Convert.ToString(row[columnName]) ?? "";
private static void FinalizeMemoLogRow(DataTable? table, DataRow? row, HashSet<string> setFields)
	{
		if (table is null || row is null) return;
		foreach (DataColumn column in table.Columns)
		{
			if (column.DataType == typeof(int) && !setFields.Contains(column.ColumnName))
			{
				row[column] = 1;
			}
		}
		table.Rows.Add(row);
	}

	private static string MemoLogFollowUpColumnName(Guid fieldId) => $"field_{fieldId:N}";

	private static void AddMemoLogBasicColumns(DataTable table)
	{
		if (!table.Columns.Contains("note_id")) table.Columns.Add("note_id", typeof(Guid));
		if (!table.Columns.Contains("note_type_code")) table.Columns.Add("note_type_code", typeof(string));
		if (!table.Columns.Contains("note_type_name")) table.Columns.Add("note_type_name", typeof(string));
		if (!table.Columns.Contains("company_id")) table.Columns.Add("company_id", typeof(Guid));
		if (!table.Columns.Contains("company_name")) table.Columns.Add("company_name", typeof(string));
		if (!table.Columns.Contains("factory_name")) table.Columns.Add("factory_name", typeof(string));
		if (!table.Columns.Contains("production_line_name")) table.Columns.Add("production_line_name", typeof(string));
		if (!table.Columns.Contains("note_number")) table.Columns.Add("note_number", typeof(string));
		if (!table.Columns.Contains("linked_note_number")) table.Columns.Add("linked_note_number", typeof(string));
		if (!table.Columns.Contains("writing_date")) table.Columns.Add("writing_date", typeof(DateTime));
		if (!table.Columns.Contains("arrival_at")) table.Columns.Add("arrival_at", typeof(DateTime));
		if (!table.Columns.Contains("status")) table.Columns.Add("status", typeof(string));
		if (!table.Columns.Contains("created_by_name")) table.Columns.Add("created_by_name", typeof(string));
		if (!table.Columns.Contains("notes")) table.Columns.Add("notes", typeof(string));
		if (!table.Columns.Contains("date_basis")) table.Columns.Add("date_basis", typeof(string));
		if (!table.Columns.Contains("period_from")) table.Columns.Add("period_from", typeof(DateTime));
		if (!table.Columns.Contains("period_to")) table.Columns.Add("period_to", typeof(DateTime));
		if (!table.Columns.Contains("arrival_cutoff_date")) table.Columns.Add("arrival_cutoff_date", typeof(DateTime));
	}

	private static string FieldText(DataRow? row, string name)
{
    return row is null || !row.Table.Columns.Contains(name) || row.IsNull(name) ? "" : Convert.ToString(row[name]) ?? "";
}

private static void EnsureColumn(DataTable table, string name, Type type)
	{
		if (!table.Columns.Contains(name)) table.Columns.Add(name, type);
	}

private static string CorrectDuplicateReferenceScope(string reportName, string sql)
	{
		if (reportName == "DashboardSummary")
		{
			return sql
				.Replace(
					"join core.notes matched_note on matched_note.id = di.matched_note_id",
					"join scoped_notes matched_note on matched_note.note_id = coalesce(di.created_note_id, di.matched_note_id)",
					StringComparison.Ordinal)
				.Replace("where not matched_note.is_deleted", "where 1 = 1", StringComparison.Ordinal)
				.Replace("matched_note.id = @note_id", "matched_note.note_id = @note_id", StringComparison.Ordinal);
		}

		if (reportName == "CompanyDetailed")
		{
			return sql
				.Replace(
					"join core.notes duplicate_note on duplicate_note.id = di.created_note_id",
					"join scoped_notes duplicate_note on duplicate_note.note_id = coalesce(di.created_note_id, di.matched_note_id)",
					StringComparison.Ordinal)
				.Replace(
					"join core.notes resend_note on resend_note.id = di.created_note_id",
					"join scoped_notes resend_note on resend_note.note_id = coalesce(di.created_note_id, di.matched_note_id)",
					StringComparison.Ordinal);
		}

		return sql;
	}
}




