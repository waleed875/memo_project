using System.Data;
using AuditMemoSystem.Core.Enums;
using AuditMemoSystem.Core.Models;
using AuditMemoSystem.Core.Services;
using AuditMemoSystem.Core.Validation;
using AuditMemoSystem.Data.Connection;
using MemoDashboard.Backend.Services;
using Microsoft.AspNetCore.Antiforgery;
using Npgsql;
using NpgsqlTypes;

namespace MemoDashboard.Backend.Endpoints;

public static class NotesEndpoints
{
    public static void MapNotesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notes");

        // ---------------- Search Notes ----------------
        group.MapGet("", async (
            INoteService service,
            IWebAdminAuthService auth,
            HttpContext context,
            CancellationToken cancellationToken,
            string? noteNumber = null,
            Guid? companyId = null,
            Guid? factoryId = null,
            Guid? productionLineId = null,
            Guid? noteTypeId = null,
            Guid? createdBy = null,
            NoteStatus? status = null,
            string? from = null,
            string? to = null,
            string? dateBasis = null,
            bool? hasWritingDate = null,
            bool? unlinkedOnly = null,
            bool? isLoot = null,
            bool? isInvoice = null,
            string? entryCutoffDate = null,
            int pageNumber = 1,
            int pageSize = 25) =>
        {
            var user = auth.GetCurrent(context);
            if (user is null) return Results.Unauthorized();

            var filter = new NoteSearchFilterModel
            {
                NoteNumber = noteNumber,
                CompanyId = companyId,
                FactoryId = factoryId,
                ProductionLineId = productionLineId,
                NoteTypeId = noteTypeId,
                CreatedBy = createdBy,
                Status = status,
                HasWritingDate = hasWritingDate,
                UnlinkedOnly = unlinkedOnly,
                IsLoot = isLoot,
                IsInvoice = isInvoice,
                DateBasis = dateBasis,
                PageNumber = pageNumber,
                PageSize = pageSize
            };

            if (ParseDateFlexible(from, isEndOfPeriod: false) is { } fDate) filter.From = fDate;
            if (ParseDateFlexible(to, isEndOfPeriod: true) is { } tDate) filter.To = tDate;
            if (ParseDateFlexible(entryCutoffDate, isEndOfPeriod: true) is { } cDate) filter.EntryCutoffDate = cDate;

            var result = await service.SearchNotesAsync(filter, user.Id, cancellationToken);
            return Results.Ok(result);
        });

        // ---------------- Review Queue ----------------
        group.MapGet("/review-queue", async (
            INoteService service,
            IWebAdminAuthService auth,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var user = auth.GetCurrent(context);
            if (user is null) return Results.Unauthorized();
            var queue = await service.GetReviewQueueAsync(user.Id, cancellationToken);
            return Results.Ok(queue);
        });

        // ---------------- Incomplete Indicators ----------------
        group.MapGet("/incomplete-indicators", async (
            INoteService service,
            IWebAdminAuthService auth,
            HttpContext context,
            CancellationToken cancellationToken,
            Guid? companyId = null,
            Guid? noteTypeId = null,
            Guid? fieldId = null,
            Guid? createdBy = null,
            string? from = null,
            string? to = null,
            int pageNumber = 1,
            int pageSize = 50) =>
        {
            var user = auth.GetCurrent(context);
            if (user is null) return Results.Unauthorized();

            var filter = new IncompleteIndicatorFilterModel
            {
                CompanyId = companyId,
                NoteTypeId = noteTypeId,
                FieldId = fieldId,
                CreatedBy = createdBy,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
            if (DateOnly.TryParse(from, out var fDate)) filter.From = fDate;
            if (DateOnly.TryParse(to, out var tDate)) filter.To = tDate;

            var result = await service.SearchIncompleteIndicatorsAsync(filter, user.Id, cancellationToken);
            return Results.Ok(result);
        });

        group.MapPost("/incomplete-indicators/complete", async (
            CompleteIndicatorRequest request,
            INoteService service,
            IWebAdminAuthService auth,
            HttpContext context,
            IAntiforgery antiforgery,
            CancellationToken cancellationToken) =>
        {
            var user = auth.GetCurrent(context);
            if (user is null) return Results.Unauthorized();
            if (!await WebAdminSecurity.IsCsrfValidAsync(context, antiforgery))
                return Results.BadRequest(new { message = "رمز حماية الطلب غير صالح." });

            await service.CompleteUnreviewedFieldAsync(
                request.NoteId,
                request.FieldId,
                request.Value,
                request.ExpectedUpdatedAt,
                user.Id,
                request.Reason,
                cancellationToken);

            return Results.Ok(new { message = "تم تحديث المؤشر بنجاح." });
        });

        // ---------------- Duplicates Check ----------------
        group.MapGet("/duplicates/check", async (
            string noteNumber,
            Guid noteTypeId,
            Guid companyId,
            string? writingDate,
            Guid? currentNoteId,
            bool isInvoice,
            INoteService service,
            IWebAdminAuthService auth,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var user = auth.GetCurrent(context);
            if (user is null) return Results.Unauthorized();

            try
            {
                DateOnly? parsedWritingDate = DateOnly.TryParse(writingDate, out var wd) ? wd : null;
                var duplicates = await service.FindDuplicateNotesAsync(
                    currentNoteId,
                    noteNumber,
                    noteTypeId,
                    companyId,
                    parsedWritingDate,
                    user.Id,
                    cancellationToken,
                    isInvoice);

                return Results.Ok(duplicates);
            }
            catch (Exception ex)
            {
                return Results.Problem(statusCode: 500, title: "فشل فحص التكرار", detail: ex.Message);
            }
        });

        group.MapPost("/duplicates/resolve", async (
            ResolveDuplicateRequest request,
            INoteService service,
            IWebAdminAuthService auth,
            HttpContext context,
            IAntiforgery antiforgery,
            CancellationToken cancellationToken) =>
        {
            var user = auth.GetCurrent(context);
            if (user is null) return Results.Unauthorized();
            if (!await WebAdminSecurity.IsCsrfValidAsync(context, antiforgery))
                return Results.BadRequest(new { message = "رمز حماية الطلب غير صالح." });

            if (request?.Note is null || request.Resolution is null)
                return Results.BadRequest(new { message = "بيانات معالجة التكرار غير صالحة." });

            try
            {
                var result = await service.ResolveDuplicateAsync(
                    request.Note,
                    user.Id,
                    request.SaveAsDraft,
                    request.Resolution,
                    request.RelatedNoteId,
                    cancellationToken,
                    request.MarkAllIndicatorsCorrect);

                return Results.Ok(result);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem(statusCode: 500, title: "فشلت معالجة التكرار", detail: ex.Message);
            }
        });

        // ---------------- Note Details ----------------
        group.MapGet("/{id:guid}", async (
            Guid id,
            INoteService service,
            IWebAdminAuthService auth,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var user = auth.GetCurrent(context);
            if (user is null) return Results.Unauthorized();

            try
            {
                var note = await service.GetNoteAsync(id, user.Id, cancellationToken);
                return note is null ? Results.NotFound(new { message = "المذكرة غير موجودة." }) : Results.Ok(note);
            }
            catch (Exception ex)
            {
                return Results.Problem(statusCode: 500, title: "فشل جلب تفاصيل المذكرة", detail: ex.Message);
            }
        });

        // ---------------- Save / Update Note ----------------
        group.MapPost("", async (
            SaveNoteRequest request,
            INoteService service,
            IWebAdminAuthService auth,
            HttpContext context,
            IAntiforgery antiforgery,
            CancellationToken cancellationToken) =>
        {
            var user = auth.GetCurrent(context);
            if (user is null) return Results.Unauthorized();
            if (!await WebAdminSecurity.IsCsrfValidAsync(context, antiforgery))
                return Results.BadRequest(new { message = "رمز حماية الطلب غير صالح." });

            if (request?.Note is null)
                return Results.BadRequest(new { message = "بيانات المذكرة غير صالحة أو فارغة." });

            try
            {
                request.Note.Id = Guid.Empty;
                var savedId = await service.SaveNoteAsync(
                    request.Note,
                    user.Id,
                    request.SaveAsDraft,
                    request.RelatedNoteId,
                    request.DuplicateResolution,
                    cancellationToken,
                    request.MarkAllIndicatorsCorrect,
                    synchronizeRelatedNote: true,
                    operationId: null,
                    relatedNoteIds: request.RelatedNoteIds ?? (request.Note.RelatedNoteIds.Count > 0 ? request.Note.RelatedNoteIds : null));

                return Results.Ok(new { id = savedId, message = "تم حفظ المذكرة بنجاح." });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem(statusCode: 500, title: "فشل حفظ المذكرة", detail: ex.Message);
            }
        });

        group.MapPut("/{id:guid}", async (
            Guid id,
            SaveNoteRequest request,
            INoteService service,
            IWebAdminAuthService auth,
            HttpContext context,
            IAntiforgery antiforgery,
            CancellationToken cancellationToken) =>
        {
            var user = auth.GetCurrent(context);
            if (user is null) return Results.Unauthorized();
            if (!await WebAdminSecurity.IsCsrfValidAsync(context, antiforgery))
                return Results.BadRequest(new { message = "رمز حماية الطلب غير صالح." });

            if (request?.Note is null)
                return Results.BadRequest(new { message = "بيانات المذكرة غير صالحة أو فارغة." });

            try
            {
                request.Note.Id = id;
                var savedId = await service.SaveNoteAsync(
                    request.Note,
                    user.Id,
                    request.SaveAsDraft,
                    request.RelatedNoteId,
                    request.DuplicateResolution,
                    cancellationToken,
                    request.MarkAllIndicatorsCorrect,
                    synchronizeRelatedNote: true,
                    operationId: null,
                    relatedNoteIds: request.RelatedNoteIds ?? (request.Note.RelatedNoteIds.Count > 0 ? request.Note.RelatedNoteIds : null));

                return Results.Ok(new { id = savedId, message = "تم تحديث المذكرة بنجاح." });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem(statusCode: 500, title: "فشل تحديث المذكرة", detail: ex.Message);
            }
        });

        // ---------------- Delete Note ----------------
        group.MapDelete("/{id:guid}", async (
            Guid id,
            DateTime expectedUpdatedAt,
            INoteService service,
            IWebAdminAuthService auth,
            HttpContext context,
            IAntiforgery antiforgery,
            CancellationToken cancellationToken) =>
        {
            var user = auth.GetCurrent(context);
            if (user is null) return Results.Unauthorized();
            if (!await WebAdminSecurity.IsCsrfValidAsync(context, antiforgery))
                return Results.BadRequest(new { message = "رمز حماية الطلب غير صالح." });

            try
            {
                await service.DeleteNoteAsync(id, user.Id, expectedUpdatedAt, cancellationToken);
                return Results.Ok(new { message = "تم حذف المذكرة بنجاح." });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem(statusCode: 500, title: "فشل حذف المذكرة", detail: ex.Message);
            }
        });

        // ---------------- Review Actions ----------------
        group.MapPost("/{id:guid}/start-review", async (
            Guid id,
            ReviewActionRequest? request,
            INoteService service,
            IWebAdminAuthService auth,
            HttpContext context,
            IAntiforgery antiforgery,
            CancellationToken cancellationToken) =>
        {
            var user = auth.GetCurrent(context);
            if (user is null) return Results.Unauthorized();
            if (!await WebAdminSecurity.IsCsrfValidAsync(context, antiforgery))
                return Results.BadRequest(new { message = "رمز حماية الطلب غير صالح." });

            try
            {
                await service.StartReviewAsync(id, user.Id, request?.ExpectedUpdatedAt, cancellationToken);
                return Results.Ok(new { message = "بدأت عملية المراجعة." });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem(statusCode: 500, title: "فشل بدء المراجعة", detail: ex.Message);
            }
        });

        group.MapPost("/{id:guid}/approve", async (
            Guid id,
            ReviewActionRequest? request,
            INoteService service,
            IWebAdminAuthService auth,
            HttpContext context,
            IAntiforgery antiforgery,
            CancellationToken cancellationToken) =>
        {
            var user = auth.GetCurrent(context);
            if (user is null) return Results.Unauthorized();
            if (!await WebAdminSecurity.IsCsrfValidAsync(context, antiforgery))
                return Results.BadRequest(new { message = "رمز حماية الطلب غير صالح." });

            try
            {
                await service.ApproveNoteAsync(id, user.Id, request?.ExpectedUpdatedAt, cancellationToken);
                return Results.Ok(new { message = "تم اعتماد المذكرة بنجاح." });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem(statusCode: 500, title: "فشل اعتماد المذكرة", detail: ex.Message);
            }
        });

        group.MapPost("/{id:guid}/return", async (
            Guid id,
            ReturnNoteRequest request,
            INoteService service,
            IWebAdminAuthService auth,
            HttpContext context,
            IAntiforgery antiforgery,
            CancellationToken cancellationToken) =>
        {
            var user = auth.GetCurrent(context);
            if (user is null) return Results.Unauthorized();
            if (!await WebAdminSecurity.IsCsrfValidAsync(context, antiforgery))
                return Results.BadRequest(new { message = "رمز حماية الطلب غير صالح." });

            if (string.IsNullOrWhiteSpace(request?.Reason))
                return Results.BadRequest(new { message = "سبب الإعادة للتصحيح مطلوب." });

            try
            {
                await service.ReturnForCorrectionAsync(id, user.Id, request.Reason, request.ExpectedUpdatedAt, cancellationToken);
                return Results.Ok(new { message = "تمت إعادة المذكرة للتصحيح." });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem(statusCode: 500, title: "فشلت إعادة المذكرة", detail: ex.Message);
            }
        });

        group.MapPost("/{id:guid}/release-review", async (
            Guid id,
            ReleaseReviewRequest request,
            INoteService service,
            IWebAdminAuthService auth,
            HttpContext context,
            IAntiforgery antiforgery,
            CancellationToken cancellationToken) =>
        {
            var user = auth.GetCurrent(context);
            if (user is null) return Results.Unauthorized();
            if (!string.Equals(user.Role, "ADMIN", StringComparison.OrdinalIgnoreCase)) return Results.Forbid();
            if (!await WebAdminSecurity.IsCsrfValidAsync(context, antiforgery))
                return Results.BadRequest(new { message = "رمز حماية الطلب غير صالح." });

            if (string.IsNullOrWhiteSpace(request?.Reason))
                return Results.BadRequest(new { message = "سبب تحرير القفل مطلوب." });

            try
            {
                await service.ReleaseReviewAsync(id, user.Id, request.Reason, cancellationToken);
                return Results.Ok(new { message = "تم تحرير قفل المراجعة." });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem(statusCode: 500, title: "فشل تحرير قفل المراجعة", detail: ex.Message);
            }
        });

        // ---------------- Note Links ----------------
        group.MapGet("/link-candidates", async (
            Guid companyId,
            Guid noteTypeId,
            Guid? currentNoteId,
            DbConnectionFactory factory,
            IWebAdminAuthService auth,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var user = auth.GetCurrent(context);
            if (user is null) return Results.Unauthorized();

            await using var connection = factory.CreateConnection();
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand("""
                select n.id, n.note_number, company.name, candidate_type.name_ar,
                       factory.name, production_line.name, n.writing_date, n.arrival_at, n.status,
                       exists(
                           select 1 from core.v_note_links_compat current_link
                           where @current_note_id is not null
                             and ((current_link.source_note_id = @current_note_id and current_link.related_note_id = n.id)
                               or (current_link.related_note_id = @current_note_id and current_link.source_note_id = n.id))
                       ) as is_linked,
                       coalesce(n.is_invoice, false) as is_invoice
                from core.notes n
                join core.companies company on company.id = n.company_id
                join core.note_types candidate_type on candidate_type.id = n.note_type_id
                join core.note_types source_type on source_type.id = @note_type_id
                left join core.factories factory on factory.id = n.factory_id
                left join core.production_lines production_line on production_line.id = n.production_line_id
                where not n.is_deleted
                  and not coalesce(n.is_loot, false)
                  and not exists (select 1 from core.note_sources s where s.note_id = n.id and s.source_type = 'LOOT')
                  and n.company_id = @company_id
                  and (@current_note_id is null or n.id <> @current_note_id)
                  and ((source_type.code = 'ENTRY' and candidate_type.code = 'EXIT')
                    or (source_type.code = 'EXIT' and candidate_type.code = 'ENTRY'))
                  and (
                      exists(
                          select 1 from core.v_note_links_compat exact_link
                          where @current_note_id is not null
                            and ((exact_link.source_note_id = @current_note_id and exact_link.related_note_id = n.id)
                              or (exact_link.related_note_id = @current_note_id and exact_link.source_note_id = n.id))
                      )
                      or (
                          candidate_type.code = 'ENTRY'
                          or not exists(
                              select 1 from core.v_note_links_compat candidate_link
                              where candidate_link.source_note_id = n.id or candidate_link.related_note_id = n.id
                          )
                      )
                  )
                order by is_linked desc, n.arrival_at desc, n.note_number
                limit 100
                """, connection);

            command.Parameters.Add("current_note_id", NpgsqlTypes.NpgsqlDbType.Uuid).Value = (object?)currentNoteId ?? DBNull.Value;
            command.Parameters.AddWithValue("company_id", companyId);
            command.Parameters.AddWithValue("note_type_id", noteTypeId);

            var list = new List<object>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                list.Add(new
                {
                    noteId = reader.GetGuid(0),
                    noteNumber = reader.GetString(1),
                    companyName = reader.GetString(2),
                    noteTypeName = reader.GetString(3),
                    factoryName = reader.IsDBNull(4) ? null : reader.GetString(4),
                    productionLineName = reader.IsDBNull(5) ? null : reader.GetString(5),
                    writingDate = reader.IsDBNull(6) ? null : reader.GetFieldValue<DateOnly>(6).ToString("yyyy-MM-dd"),
                    arrivalAt = reader.GetFieldValue<DateOnly>(7).ToString("yyyy-MM-dd"),
                    status = reader.GetString(8),
                    isLinked = reader.GetBoolean(9),
                    isInvoice = reader.GetBoolean(10)
                });
            }
            return Results.Ok(list);
        });

        group.MapGet("/{id:guid}/link-candidates", async (
            Guid id,
            bool includeUnlinked,
            INoteService service,
            IWebAdminAuthService auth,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var user = auth.GetCurrent(context);
            if (user is null) return Results.Unauthorized();

            var candidates = await service.GetLinkCandidatesAsync(id, user.Id, includeUnlinked, cancellationToken);
            return Results.Ok(candidates);
        });

        group.MapPost("/{id:guid}/link", async (
            Guid id,
            LinkNoteRequest request,
            INoteService service,
            IWebAdminAuthService auth,
            HttpContext context,
            IAntiforgery antiforgery,
            CancellationToken cancellationToken) =>
        {
            var user = auth.GetCurrent(context);
            if (user is null) return Results.Unauthorized();
            if (!await WebAdminSecurity.IsCsrfValidAsync(context, antiforgery))
                return Results.BadRequest(new { message = "رمز حماية الطلب غير صالح." });

            var targets = new List<Guid>();
            if (request.RelatedNoteIds != null && request.RelatedNoteIds.Count > 0)
            {
                targets.AddRange(request.RelatedNoteIds.Where(tid => tid != Guid.Empty));
            }
            else if (request.RelatedNoteId.HasValue && request.RelatedNoteId.Value != Guid.Empty)
            {
                targets.Add(request.RelatedNoteId.Value);
            }

            if (targets.Count == 0)
            {
                return Results.BadRequest(new { message = "يرجى تحديد مذكرة واحدة على الأقل للربط." });
            }

            await service.LinkNotesAsync(id, targets, user.Id, cancellationToken);
            return Results.Ok(new { message = targets.Count > 1 ? "تم ربط المذكرات بنجاح." : "تم ربط المذكرتين بنجاح." });
        });

        group.MapDelete("/{id:guid}/unlink/{relatedNoteId:guid}", async (
            Guid id,
            Guid relatedNoteId,
            INoteService service,
            IWebAdminAuthService auth,
            HttpContext context,
            IAntiforgery antiforgery,
            CancellationToken cancellationToken) =>
        {
            var user = auth.GetCurrent(context);
            if (user is null) return Results.Unauthorized();
            await service.UnlinkNotesAsync(id, relatedNoteId, user.Id, cancellationToken);
            return Results.Ok(new { message = "تم فك ربط المذكرتين بنجاح." });
        });

        // ---------------- Note Source (Origin for Entry Notes) ----------------
        group.MapGet("/{id:guid}/source", async (
            Guid id,
            INoteService service,
            IWebAdminAuthService auth,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var user = auth.GetCurrent(context);
            if (user is null) return Results.Unauthorized();

            var source = await service.GetNoteSourceAsync(id, user.Id, cancellationToken);
            if (source is null) return Results.Ok((object?)null);

            var dto = new
            {
                id = source.Id,
                noteId = source.NoteId,
                sourceType = source.SourceType.ToString(),
                otherDescription = source.OtherDescription,
                lootDescription = source.LootDescription,
                updatedAt = source.UpdatedAt,
                exitReferences = source.ExitReferences.Select(r => new
                {
                    exitNoteId = r.ExitNoteId,
                    referenceOrder = r.ReferenceOrder,
                    noteNumber = r.NoteNumber
                }),
                invoices = source.Invoices.Select(inv => new
                {
                    id = inv.Id,
                    invoiceNumber = inv.InvoiceNumber,
                    invoiceDate = inv.InvoiceDate?.ToString("yyyy-MM-dd"),
                    destination = inv.Destination,
                    attachmentFileName = inv.AttachmentFileName,
                    hasAttachment = !string.IsNullOrEmpty(inv.AttachmentFileName)
                })
            };

            return Results.Ok(dto);
        });

        group.MapPost("/{id:guid}/source", async (
            Guid id,
            SaveNoteSourceApiRequest request,
            INoteService service,
            IWebAdminAuthService auth,
            HttpContext context,
            IAntiforgery antiforgery,
            CancellationToken cancellationToken) =>
        {
            var user = auth.GetCurrent(context);
            if (user is null) return Results.Unauthorized();
            if (!await WebAdminSecurity.IsCsrfValidAsync(context, antiforgery))
                return Results.BadRequest(new { message = "رمز حماية الطلب غير صالح." });

            if (!Enum.TryParse<NoteSourceType>(request.SourceType, true, out var parsedType))
                return Results.BadRequest(new { message = "نوع المصدر المحدد غير صالح." });

            var model = new NoteSourceModel
            {
                NoteId = id,
                SourceType = parsedType,
                OtherDescription = request.OtherDescription,
                LootDescription = request.LootDescription,
                ExitReferences = (request.ExitReferences ?? []).Select(r => new NoteSourceExitReferenceModel
                {
                    ExitNoteId = r.ExitNoteId,
                    ReferenceOrder = r.ReferenceOrder,
                    NoteNumber = r.NoteNumber
                }).ToList(),
                Invoices = (request.Invoices ?? []).Select(inv => new NoteSourceInvoiceModel
                {
                    Id = inv.Id ?? Guid.NewGuid(),
                    InvoiceNumber = inv.InvoiceNumber ?? "",
                    InvoiceDate = DateOnly.TryParse(inv.InvoiceDate, out var idate) ? idate : null,
                    Destination = inv.Destination,
                    AttachmentFileName = inv.AttachmentFileName,
                    AttachmentContentType = inv.AttachmentContentType,
                    AttachmentData = !string.IsNullOrEmpty(inv.AttachmentBase64) ? Convert.FromBase64String(inv.AttachmentBase64) : null
                }).ToList()
            };

            await service.SaveNoteSourceAsync(model, user.Id, cancellationToken);
            return Results.Ok(new { message = "تم حفظ مصدر مذكرة الإدخال بنجاح." });
        });

        group.MapDelete("/{id:guid}/source", async (
            Guid id,
            INoteService service,
            IWebAdminAuthService auth,
            HttpContext context,
            IAntiforgery antiforgery,
            CancellationToken cancellationToken) =>
        {
            var user = auth.GetCurrent(context);
            if (user is null) return Results.Unauthorized();
            if (!await WebAdminSecurity.IsCsrfValidAsync(context, antiforgery))
                return Results.BadRequest(new { message = "رمز حماية الطلب غير صالح." });

            await service.RemoveNoteSourceAsync(id, user.Id, cancellationToken);
            return Results.Ok(new { message = "تمت إزالة مصدر المذكرة بنجاح." });
        });

        group.MapGet("/{id:guid}/source/invoices/{invoiceId:guid}/attachment", async (
            Guid id,
            Guid invoiceId,
            DbConnectionFactory factory,
            IWebAdminAuthService auth,
            HttpContext context,
            CancellationToken cancellationToken,
            bool download = false) =>
        {
            if (auth.GetCurrent(context) is null) return Results.Unauthorized();

            await using var connection = factory.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("""
                select i.attachment_file_name, i.attachment_content_type, i.attachment_data
                from core.note_source_invoices i
                join core.note_sources s on s.id = i.source_id
                where s.note_id = @note_id and i.id = @invoice_id
                """, connection);
            command.Parameters.AddWithValue("note_id", id);
            command.Parameters.AddWithValue("invoice_id", invoiceId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull(2))
                return Results.NotFound(new { message = "مرفق الفاتورة غير موجود." });

            var fileName = reader.GetString(0);
            var contentType = reader.GetString(1);
            var data = (byte[])reader[2];

            if (download)
            {
                return Results.File(data, contentType, fileName, enableRangeProcessing: true);
            }
            return Results.File(data, contentType, enableRangeProcessing: true);
        });

        // ---------------- Attachments (Stored in PostgreSQL bytea) ----------------
        group.MapGet("/{id:guid}/attachment", async (
            Guid id,
            DbConnectionFactory factory,
            IWebAdminAuthService auth,
            HttpContext context,
            CancellationToken cancellationToken,
            bool download = false) =>
        {
            if (auth.GetCurrent(context) is null) return Results.Unauthorized();

            await using var connection = factory.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("""
                select coalesce(na.file_name, n.attachment_file_name, 'attachment'),
                       coalesce(na.content_type, n.attachment_content_type, 'application/octet-stream'),
                       coalesce(na.file_data, n.attachment_data)
                from core.notes n
                left join lateral (
                    select file_name, content_type, file_data
                    from core.note_attachments
                    where note_id = n.id
                    order by created_at asc
                    limit 1
                ) na on true
                where n.id = @id and n.is_deleted = false
                """, connection);
            command.Parameters.AddWithValue("id", id);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return Results.NotFound(new { message = "المذكرة غير موجودة." });

            if (reader.IsDBNull(2))
                return Results.NotFound(new { message = "لا يوجد مرفق لهذه المذكرة." });

            var fileName = reader.GetString(0);
            var contentType = reader.GetString(1);
            var data = (byte[])reader[2];

            if (download)
            {
                return Results.File(data, contentType, fileName, enableRangeProcessing: true);
            }
            return Results.File(data, contentType, enableRangeProcessing: true);
        });

        // Get list of attachments for a note
        group.MapGet("/{id:guid}/attachments", async (
            Guid id,
            DbConnectionFactory factory,
            IWebAdminAuthService auth,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (auth.GetCurrent(context) is null) return Results.Unauthorized();

            await using var connection = factory.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("""
                select id, file_name, content_type, file_size, created_at
                from core.note_attachments
                where note_id = @id
                order by created_at asc
                """, connection);
            command.Parameters.AddWithValue("id", id);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var list = new List<object>();
            while (await reader.ReadAsync(cancellationToken))
            {
                list.Add(new
                {
                    id = reader.GetGuid(0),
                    fileName = reader.GetString(1),
                    contentType = reader.GetString(2),
                    fileSize = reader.GetInt64(3),
                    createdAt = reader.GetDateTime(4)
                });
            }
            return Results.Ok(list);
        });

        // Get specific attachment file data inline or download
        group.MapGet("/{id:guid}/attachments/{attachmentId:guid}", async (
            Guid id,
            Guid attachmentId,
            DbConnectionFactory factory,
            IWebAdminAuthService auth,
            HttpContext context,
            CancellationToken cancellationToken,
            bool download = false) =>
        {
            if (auth.GetCurrent(context) is null) return Results.Unauthorized();

            await using var connection = factory.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("""
                select file_name, content_type, file_data
                from core.note_attachments
                where id = @attachment_id and note_id = @id
                """, connection);
            command.Parameters.AddWithValue("attachment_id", attachmentId);
            command.Parameters.AddWithValue("id", id);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return Results.NotFound(new { message = "المرفق غير موجود." });

            var fileName = reader.GetString(0);
            var contentType = reader.GetString(1);
            var data = (byte[])reader[2];

            if (download)
            {
                return Results.File(data, contentType, fileName, enableRangeProcessing: true);
            }
            return Results.File(data, contentType, enableRangeProcessing: true);
        });

        // Upload one or multiple attachments to a note
        group.MapPost("/{id:guid}/attachments", async (
            Guid id,
            HttpRequest httpRequest,
            DbConnectionFactory factory,
            IAuditService auditService,
            IWebAdminAuthService auth,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var user = auth.GetCurrent(context);
            if (user is null) return Results.Unauthorized();

            if (!httpRequest.HasFormContentType)
                return Results.BadRequest(new { message = "نوع الطلب يجب أن يكون multipart/form-data." });

            var form = await httpRequest.ReadFormAsync(cancellationToken);
            var files = form.Files;
            if (files.Count == 0)
                return Results.BadRequest(new { message = "لم يتم تحديد أي ملفات للرفع." });

            var results = new List<object>();

            await using var connection = factory.CreateConnection();
            await connection.OpenAsync(cancellationToken);

            foreach (var file in files)
            {
                if (file.Length == 0) continue;
                if (file.Length > 20 * 1024 * 1024)
                    return Results.BadRequest(new { message = $"حجم الملف {file.FileName} يتجاوز الحد المسموح (20 ميغابايت)." });

                using var memoryStream = new MemoryStream();
                await file.CopyToAsync(memoryStream, cancellationToken);
                var bytes = memoryStream.ToArray();

                try
                {
                    AttachmentFilePolicy.Validate(file.FileName, bytes);
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { message = $"{file.FileName}: {ex.Message}" });
                }

                var contentType = AttachmentFilePolicy.GetContentType(file.FileName);
                var attachmentId = Guid.NewGuid();

                await using var insertCmd = new NpgsqlCommand("""
                    insert into core.note_attachments (id, note_id, file_name, content_type, file_data, file_size, created_at, created_by)
                    values (@id, @note_id, @file_name, @content_type, @data, @file_size, clock_timestamp(), @user_id)
                    """, connection);
                insertCmd.Parameters.AddWithValue("id", attachmentId);
                insertCmd.Parameters.AddWithValue("note_id", id);
                insertCmd.Parameters.AddWithValue("file_name", file.FileName);
                insertCmd.Parameters.AddWithValue("content_type", contentType);
                insertCmd.Parameters.Add("data", NpgsqlDbType.Bytea).Value = bytes;
                insertCmd.Parameters.AddWithValue("file_size", (long)bytes.Length);
                insertCmd.Parameters.Add("user_id", NpgsqlDbType.Uuid).Value = user.Id;
                await insertCmd.ExecuteNonQueryAsync(cancellationToken);

                // Also update core.notes primary columns if null
                await using var updatePrimaryCmd = new NpgsqlCommand("""
                    update core.notes
                    set attachment_file_name = coalesce(attachment_file_name, @file_name),
                        attachment_content_type = coalesce(attachment_content_type, @content_type),
                        attachment_data = coalesce(attachment_data, @data),
                        updated_at = now(),
                        updated_by = @user_id
                    where id = @id and is_deleted = false
                    """, connection);
                updatePrimaryCmd.Parameters.AddWithValue("id", id);
                updatePrimaryCmd.Parameters.AddWithValue("file_name", file.FileName);
                updatePrimaryCmd.Parameters.AddWithValue("content_type", contentType);
                updatePrimaryCmd.Parameters.Add("data", NpgsqlDbType.Bytea).Value = bytes;
                updatePrimaryCmd.Parameters.Add("user_id", NpgsqlDbType.Uuid).Value = user.Id;
                await updatePrimaryCmd.ExecuteNonQueryAsync(cancellationToken);

                await auditService.WriteAsync(
                    user.Id,
                    "NOTE_ATTACHMENT_UPLOADED",
                    "core",
                    "note_attachments",
                    attachmentId,
                    $"تم رفع مرفق للمذكرة: {file.FileName} ({bytes.Length} بايت)",
                    cancellationToken);

                results.Add(new
                {
                    id = attachmentId,
                    fileName = file.FileName,
                    contentType,
                    fileSize = bytes.Length
                });
            }

            return Results.Ok(new
            {
                uploaded = results,
                message = $"تم رفع {results.Count} مرفق بنجاح."
            });
        }).DisableAntiforgery();

        // Delete an attachment
        group.MapDelete("/{id:guid}/attachments/{attachmentId:guid}", async (
            Guid id,
            Guid attachmentId,
            DbConnectionFactory factory,
            IAuditService auditService,
            IWebAdminAuthService auth,
            HttpContext context,
            IAntiforgery antiforgery,
            CancellationToken cancellationToken) =>
        {
            var user = auth.GetCurrent(context);
            if (user is null) return Results.Unauthorized();
            if (!await WebAdminSecurity.IsCsrfValidAsync(context, antiforgery))
                return Results.BadRequest(new { message = "رمز حماية الطلب غير صالح." });

            await using var connection = factory.CreateConnection();
            await connection.OpenAsync(cancellationToken);

            await using var deleteCmd = new NpgsqlCommand("""
                delete from core.note_attachments
                where id = @attachment_id and note_id = @id
                returning file_name
                """, connection);
            deleteCmd.Parameters.AddWithValue("attachment_id", attachmentId);
            deleteCmd.Parameters.AddWithValue("id", id);
            var deletedFileName = await deleteCmd.ExecuteScalarAsync(cancellationToken) as string;

            if (deletedFileName is null)
                return Results.NotFound(new { message = "المرفق غير موجود أو تم حذفه مسبقاً." });

            // Synchronize core.notes primary columns
            await using var syncNotesCmd = new NpgsqlCommand("""
                with next_att as (
                    select file_name, content_type, file_data
                    from core.note_attachments
                    where note_id = @id
                    order by created_at asc
                    limit 1
                )
                update core.notes n
                set attachment_file_name = na.file_name,
                    attachment_content_type = na.content_type,
                    attachment_data = na.file_data,
                    updated_at = now(),
                    updated_by = @user_id
                from (select * from next_att) na
                where n.id = @id;

                update core.notes
                set attachment_file_name = null,
                    attachment_content_type = null,
                    attachment_data = null,
                    updated_at = now(),
                    updated_by = @user_id
                where id = @id and not exists (select 1 from core.note_attachments where note_id = @id);
                """, connection);
            syncNotesCmd.Parameters.AddWithValue("id", id);
            syncNotesCmd.Parameters.Add("user_id", NpgsqlDbType.Uuid).Value = user.Id;
            await syncNotesCmd.ExecuteNonQueryAsync(cancellationToken);

            await auditService.WriteAsync(
                user.Id,
                "NOTE_ATTACHMENT_DELETED",
                "core",
                "note_attachments",
                attachmentId,
                $"تم حذف مرفق المذكرة: {deletedFileName}",
                cancellationToken);

            return Results.Ok(new { message = "تم حذف المرفق بنجاح." });
        });

        // Single file upload endpoint maintained for backwards compatibility
        group.MapPost("/{id:guid}/attachment", async (
            Guid id,
            IFormFile file,
            DbConnectionFactory factory,
            IAuditService auditService,
            IWebAdminAuthService auth,
            HttpContext context,
            IAntiforgery antiforgery,
            CancellationToken cancellationToken) =>
        {
            var user = auth.GetCurrent(context);
            if (user is null) return Results.Unauthorized();
            if (!await WebAdminSecurity.IsCsrfValidAsync(context, antiforgery))
                return Results.BadRequest(new { message = "رمز حماية الطلب غير صالح." });

            if (file is null || file.Length == 0)
                return Results.BadRequest(new { message = "الملف غير صالح أو فارغ." });

            if (file.Length > 20 * 1024 * 1024)
                return Results.BadRequest(new { message = "حجم الملف يتجاوز الحد المسموح (20 ميغابايت)." });

            using var memoryStream = new MemoryStream();
            await file.CopyToAsync(memoryStream, cancellationToken);
            var bytes = memoryStream.ToArray();

            try
            {
                AttachmentFilePolicy.Validate(file.FileName, bytes);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }

            var contentType = AttachmentFilePolicy.GetContentType(file.FileName);
            var attachmentId = Guid.NewGuid();

            await using var connection = factory.CreateConnection();
            await connection.OpenAsync(cancellationToken);

            await using var insertCmd = new NpgsqlCommand("""
                insert into core.note_attachments (id, note_id, file_name, content_type, file_data, file_size, created_at, created_by)
                values (@id, @note_id, @file_name, @content_type, @data, @file_size, clock_timestamp(), @user_id)
                """, connection);
            insertCmd.Parameters.AddWithValue("id", attachmentId);
            insertCmd.Parameters.AddWithValue("note_id", id);
            insertCmd.Parameters.AddWithValue("file_name", file.FileName);
            insertCmd.Parameters.AddWithValue("content_type", contentType);
            insertCmd.Parameters.Add("data", NpgsqlDbType.Bytea).Value = bytes;
            insertCmd.Parameters.AddWithValue("file_size", (long)bytes.Length);
            insertCmd.Parameters.Add("user_id", NpgsqlDbType.Uuid).Value = user.Id;
            await insertCmd.ExecuteNonQueryAsync(cancellationToken);

            await using var command = new NpgsqlCommand("""
                update core.notes
                set attachment_file_name = @file_name,
                    attachment_content_type = @content_type,
                    attachment_data = @data,
                    updated_at = now(),
                    updated_by = @user_id
                where id = @id and is_deleted = false
                """, connection);
            command.Parameters.AddWithValue("id", id);
            command.Parameters.AddWithValue("file_name", file.FileName);
            command.Parameters.AddWithValue("content_type", contentType);
            command.Parameters.Add("data", NpgsqlDbType.Bytea).Value = bytes;
            command.Parameters.Add("user_id", NpgsqlDbType.Uuid).Value = user.Id;

            var rows = await command.ExecuteNonQueryAsync(cancellationToken);
            if (rows == 0)
                return Results.NotFound(new { message = "المذكرة غير موجودة." });

            await auditService.WriteAsync(
                user.Id,
                "NOTE_ATTACHMENT_UPLOADED",
                "core",
                "notes",
                id,
                $"تم رفع مرفق المذكرة: {file.FileName} ({bytes.Length} بايت)",
                cancellationToken);

            return Results.Ok(new
            {
                fileName = file.FileName,
                contentType = contentType,
                size = bytes.Length,
                message = "تم حفظ المرفق في قاعدة البيانات بنجاح."
            });
        }).DisableAntiforgery();
    }

    public sealed record SaveNoteRequest(
        NoteModel Note,
        bool SaveAsDraft = false,
        Guid? RelatedNoteId = null,
        DuplicateResolutionRequest? DuplicateResolution = null,
        bool MarkAllIndicatorsCorrect = false,
        IReadOnlyList<Guid>? RelatedNoteIds = null);

    public sealed record ResolveDuplicateRequest(
        NoteModel Note,
        DuplicateResolutionRequest Resolution,
        bool SaveAsDraft = false,
        Guid? RelatedNoteId = null,
        bool MarkAllIndicatorsCorrect = false);

    public sealed record ReviewActionRequest(DateTime? ExpectedUpdatedAt);
    public sealed record ReturnNoteRequest(string Reason, DateTime? ExpectedUpdatedAt);
    public sealed record ReleaseReviewRequest(string Reason);
    public sealed record LinkNoteRequest(Guid? RelatedNoteId, List<Guid>? RelatedNoteIds = null);
    public sealed record CompleteIndicatorRequest(
        Guid NoteId,
        Guid FieldId,
        IndicatorValue Value,
        DateTime ExpectedUpdatedAt,
        string? Reason);

    public sealed record SaveNoteSourceApiRequest(
        string SourceType,
        string? OtherDescription,
        string? LootDescription,
        List<NoteSourceExitReferenceApiDto>? ExitReferences,
        List<NoteSourceInvoiceApiDto>? Invoices);

    public sealed record NoteSourceExitReferenceApiDto(Guid ExitNoteId, int ReferenceOrder, string? NoteNumber);

    public sealed record NoteSourceInvoiceApiDto(
        Guid? Id,
        string? InvoiceNumber,
        string? InvoiceDate,
        string? Destination,
        string? AttachmentFileName,
        string? AttachmentContentType,
        string? AttachmentBase64);

    private static DateOnly? ParseDateFlexible(string? input, bool isEndOfPeriod = false)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var trimmed = input.Trim();

        if (DateOnly.TryParse(trimmed, out var date))
            return date;

        // Handle YYYY-MM
        if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^\d{4}[-/]\d{1,2}$"))
        {
            var parts = trimmed.Split(new[] { '-', '/' });
            if (int.TryParse(parts[0], out var year) && int.TryParse(parts[1], out var month) && month >= 1 && month <= 12)
            {
                return isEndOfPeriod
                    ? new DateOnly(year, month, DateTime.DaysInMonth(year, month))
                    : new DateOnly(year, month, 1);
            }
        }

        // Handle MM/YYYY
        if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^\d{1,2}[-/]\d{4}$"))
        {
            var parts = trimmed.Split(new[] { '-', '/' });
            if (int.TryParse(parts[1], out var year) && int.TryParse(parts[0], out var month) && month >= 1 && month <= 12)
            {
                return isEndOfPeriod
                    ? new DateOnly(year, month, DateTime.DaysInMonth(year, month))
                    : new DateOnly(year, month, 1);
            }
        }

        if (DateTime.TryParse(trimmed, out var dt))
            return DateOnly.FromDateTime(dt);

        return null;
    }
}