using AuditMemoSystem.Core.Models;
using AuditMemoSystem.Core.Services;
using MemoDashboard.Backend.Services;
using Microsoft.AspNetCore.Antiforgery;

namespace MemoDashboard.Backend.Endpoints;

public static class MasterDataEndpoints
{
    public static void MapMasterDataEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/master");

        // ---------------- Companies ----------------
        group.MapGet("/companies", async (
            ICompanyService service,
            IWebAdminAuthService auth,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (auth.GetCurrent(context) is null) return Results.Unauthorized();
            var companies = await service.GetCompaniesAsync(cancellationToken);
            return Results.Ok(companies);
        });

        group.MapGet("/companies/{id:guid}", async (
            Guid id,
            ICompanyService service,
            IWebAdminAuthService auth,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (auth.GetCurrent(context) is null) return Results.Unauthorized();
            var company = await service.GetCompanyAsync(id, cancellationToken);
            return company is null ? Results.NotFound() : Results.Ok(company);
        });

        group.MapPost("/companies", async (
            CompanyModel model,
            ICompanyService service,
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

            if (string.IsNullOrWhiteSpace(model.Name))
                return Results.BadRequest(new { message = "اسم الشركة مطلوب." });

            try
            {
                model.Id = Guid.Empty;
                await service.SaveCompanyAsync(model, user.Id, cancellationToken);
                return Results.Ok(model);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        group.MapPut("/companies/{id:guid}", async (
            Guid id,
            CompanyModel model,
            ICompanyService service,
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

            if (string.IsNullOrWhiteSpace(model.Name))
                return Results.BadRequest(new { message = "اسم الشركة مطلوب." });

            try
            {
                model.Id = id;
                await service.SaveCompanyAsync(model, user.Id, cancellationToken);
                return Results.Ok(model);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        group.MapDelete("/companies/{id:guid}", async (
            Guid id,
            ICompanyService service,
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

            try
            {
                await service.DeleteCompanyAsync(id, user.Id, cancellationToken);
                return Results.Ok(new { message = "تم حذف الشركة بنجاح." });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        // ---------------- Factories ----------------
        group.MapGet("/factories", async (
            Guid? companyId,
            IFactoryService service,
            IWebAdminAuthService auth,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (auth.GetCurrent(context) is null) return Results.Unauthorized();
            var factories = await service.GetFactoriesAsync(companyId, cancellationToken);
            return Results.Ok(factories);
        });

        group.MapPost("/factories", async (
            FactoryModel model,
            IFactoryService service,
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

            if (string.IsNullOrWhiteSpace(model.Name))
                return Results.BadRequest(new { message = "اسم المعمل مطلوب." });

            if (model.CompanyId is null || model.CompanyId == Guid.Empty)
                return Results.BadRequest(new { message = "الشركة مطلوبة للمعمل." });

            try
            {
                model.Id = Guid.Empty;
                await service.SaveFactoryAsync(model, user.Id, cancellationToken);
                return Results.Ok(model);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        group.MapPut("/factories/{id:guid}", async (
            Guid id,
            FactoryModel model,
            IFactoryService service,
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

            if (string.IsNullOrWhiteSpace(model.Name))
                return Results.BadRequest(new { message = "اسم المعمل مطلوب." });

            if (model.CompanyId is null || model.CompanyId == Guid.Empty)
                return Results.BadRequest(new { message = "الشركة مطلوبة للمعمل." });

            try
            {
                model.Id = id;
                await service.SaveFactoryAsync(model, user.Id, cancellationToken);
                return Results.Ok(model);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        group.MapDelete("/factories/{id:guid}", async (
            Guid id,
            IFactoryService service,
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

            try
            {
                await service.DeleteFactoryAsync(id, user.Id, cancellationToken);
                return Results.Ok(new { message = "تم حذف المعمل بنجاح." });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        // ---------------- Production Lines ----------------
        group.MapGet("/production-lines", async (
            Guid? factoryId,
            IProductionLineService service,
            IWebAdminAuthService auth,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (auth.GetCurrent(context) is null) return Results.Unauthorized();
            var lines = await service.GetProductionLinesAsync(factoryId, cancellationToken);
            return Results.Ok(lines);
        });

        group.MapPost("/production-lines", async (
            ProductionLineModel model,
            IProductionLineService service,
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

            if (string.IsNullOrWhiteSpace(model.Name))
                return Results.BadRequest(new { message = "اسم خط الإنتاج مطلوب." });

            if (model.FactoryId == Guid.Empty)
                return Results.BadRequest(new { message = "المعمل مطلوب لخط الإنتاج." });

            try
            {
                model.Id = Guid.Empty;
                await service.SaveProductionLineAsync(model, user.Id, cancellationToken);
                return Results.Ok(model);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        group.MapPut("/production-lines/{id:guid}", async (
            Guid id,
            ProductionLineModel model,
            IProductionLineService service,
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

            if (string.IsNullOrWhiteSpace(model.Name))
                return Results.BadRequest(new { message = "اسم خط الإنتاج مطلوب." });

            if (model.FactoryId == Guid.Empty)
                return Results.BadRequest(new { message = "المعمل مطلوب لخط الإنتاج." });

            try
            {
                model.Id = id;
                await service.SaveProductionLineAsync(model, user.Id, cancellationToken);
                return Results.Ok(model);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        group.MapDelete("/production-lines/{id:guid}", async (
            Guid id,
            IProductionLineService service,
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

            try
            {
                await service.DeleteProductionLineAsync(id, user.Id, cancellationToken);
                return Results.Ok(new { message = "تم حذف خط الإنتاج بنجاح." });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        group.MapPost("/production-lines/{id:guid}/toggle", async (
            Guid id,
            ToggleLineRequest request,
            IProductionLineService service,
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

            try
            {
                await service.ToggleProductionLineAsync(id, request.IsActive, user.Id, cancellationToken);
                return Results.Ok(new { message = "تم تحديث حالة خط الإنتاج." });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        // ---------------- Note Types & Dynamic Indicator Fields ----------------
        group.MapGet("/note-types", async (
            INoteTypeService service,
            IWebAdminAuthService auth,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (auth.GetCurrent(context) is null) return Results.Unauthorized();
            var types = await service.GetNoteTypesAsync(cancellationToken);
            return Results.Ok(types);
        });

        group.MapGet("/note-types/{id:guid}/fields", async (
            Guid id,
            INoteTypeService service,
            IWebAdminAuthService auth,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (auth.GetCurrent(context) is null) return Results.Unauthorized();
            var fields = await service.GetFieldsForTypeAsync(id, cancellationToken);
            return Results.Ok(fields);
        });

        group.MapPost("/note-types/fields/preview", async (
            NoteTypeFieldDefinitionModel definition,
            INoteTypeService service,
            IWebAdminAuthService auth,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var user = auth.GetCurrent(context);
            if (user is null) return Results.Unauthorized();
            if (!string.Equals(user.Role, "ADMIN", StringComparison.OrdinalIgnoreCase)) return Results.Forbid();

            try
            {
                var preview = await service.PreviewFieldDefinitionAsync(definition, user.Id, cancellationToken);
                return Results.Ok(preview);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        group.MapPost("/note-types/fields", async (
            NoteTypeFieldDefinitionModel definition,
            INoteTypeService service,
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

            try
            {
                var saved = await service.SaveFieldDefinitionAsync(definition, user.Id, cancellationToken);
                return Results.Ok(saved);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        group.MapDelete("/note-types/fields/{fieldId:guid}", async (
            Guid fieldId,
            INoteTypeService service,
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

            try
            {
                await service.DeactivateFieldDefinitionAsync(fieldId, user.Id, cancellationToken);
                return Results.Ok(new { message = "تم تعطيل حقل المؤشر بنجاح." });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });
    }

    public sealed record ToggleLineRequest(bool IsActive);
}