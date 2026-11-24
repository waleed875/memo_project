using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using MemoDashboard.Backend.Services.Updates;

namespace MemoDashboard.Backend.Endpoints;

public static class UpdateEndpoints
{
    public static void MapUpdateEndpoints(this WebApplication app)
    {
        app.MapGet("/api/updates/health", (UnifiedUpdateService updates) => Results.Ok(updates.GetHealth()));

        app.MapGet("/api/updates/public-key", (UnifiedUpdateService updates) =>
        {
            var result = updates.GetPublicKey();
            return result.StatusCode == StatusCodes.Status200OK
                ? Results.Ok(new { status = result.Status, publicKeyPem = result.PublicKeyPem })
                : Results.Json(new { status = result.Status, message = result.Error }, statusCode: result.StatusCode);
        });

        app.MapGet("/api/updates/manifest/{os}/{architecture}", async (
            string os,
            string architecture,
            UnifiedUpdateService updates,
            CancellationToken cancellationToken) =>
        {
            var result = await updates.GetManifestAsync(os, architecture, cancellationToken);
            return result.Manifest is not null
                ? Results.Ok(result.Manifest)
                : Error(result.StatusCode, result.Error);
        });

        app.MapPost("/api/updates/releases", async (
            HttpRequest request,
            UnifiedUpdateService updates,
            CancellationToken cancellationToken) =>
        {
            if (!updates.IsPublisherAllowed(request)) return Results.Unauthorized();
            var parsed = await ReadPublishRequestAsync(request, updates.MaxUploadRequestBytes, cancellationToken);
            if (parsed.Error is not null) return Results.Json(new { message = parsed.Error }, statusCode: parsed.StatusCode);
            var result = await updates.PublishAsync(parsed.Request!, cancellationToken);
            return ToResult(result);
        }).RequireRateLimiting("publishing");

        app.MapPost("/api/updates/releases/{version}/publish", async (
            string version,
            HttpRequest request,
            UnifiedUpdateService updates,
            CancellationToken cancellationToken) =>
        {
            if (!updates.IsPublisherAllowed(request)) return Results.Unauthorized();
            return ToResult(await updates.PromoteAsync(version, cancellationToken));
        }).RequireRateLimiting("publishing");

        app.MapPost("/api/updates/installations/check-in", async (
            UpdateCheckInRequest checkIn,
            HttpRequest request,
            UnifiedUpdateService updates,
            CancellationToken cancellationToken) =>
        {
            if (!updates.IsCheckInAllowed(request)) return Results.Unauthorized();
            return ToResult(await updates.CheckInAsync(checkIn, cancellationToken));
        })
        .RequireRateLimiting("check-in")
        .WithMetadata(new RequestSizeLimitAttribute(16 * 1024));

        app.MapGet("/packages/{file}", (string file, UnifiedUpdateService updates) =>
        {
            var path = updates.ResolvePackagePath(file);
            return path is null
                ? Results.NotFound()
                : Results.File(path, "application/zip", enableRangeProcessing: true);
        });
    }

    public static async Task<PublishRequestParseResult> ReadPublishRequestAsync(
        HttpRequest request,
        long maxUploadRequestBytes,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength is long length && length > maxUploadRequestBytes)
            return new(413, "حجم طلب الرفع يتجاوز الحد المسموح.", null);

        var sizeFeature = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is not null && !sizeFeature.IsReadOnly)
            sizeFeature.MaxRequestBodySize = maxUploadRequestBytes;

        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(cancellationToken);
        }
        catch (InvalidDataException)
        {
            return new(413, "حجم طلب الرفع يتجاوز الحد المسموح.", null);
        }

        var package = form.Files.GetFile("package");
        if (package is null || package.Length <= 0 || package.Length > maxUploadRequestBytes)
            return new(400, "يلزم رفع ملف ZIP صالح.", null);

        var parsed = new UpdatePublishRequest(
            package,
            form["version"].ToString(),
            form["operatingSystem"].ToString(),
            form["architecture"].ToString(),
            form["minimumVersion"].ToString(),
            form["minimumDatabaseVersion"].ToString(),
            form["targetDatabaseVersion"].ToString(),
            form["notes"].ToString());
        return new(200, null, parsed);
    }

    public static IResult ToResult(UpdateOperationResult result)
    {
        if (result.StatusCode == StatusCodes.Status204NoContent) return Results.NoContent();
        if (result.StatusCode == StatusCodes.Status404NotFound) return Results.NotFound(result.Body);
        if (result.StatusCode == StatusCodes.Status401Unauthorized) return Results.Unauthorized();
        if (result.StatusCode == StatusCodes.Status400BadRequest) return Results.BadRequest(result.Body);
        if (result.StatusCode == StatusCodes.Status409Conflict) return Results.Conflict(result.Body);
        if (result.StatusCode >= 500) return Results.Problem(Convert.ToString(result.Body), statusCode: result.StatusCode);
        if (result.StatusCode == StatusCodes.Status201Created)
            return Results.Created(result.Location ?? string.Empty, result.Body);
        return Results.Json(result.Body, statusCode: result.StatusCode);
    }

    private static IResult Error(int statusCode, string? message) => statusCode switch
    {
        StatusCodes.Status400BadRequest => Results.BadRequest(message),
        StatusCodes.Status404NotFound => Results.NotFound(),
        _ when statusCode >= 500 => Results.Problem(message, statusCode: statusCode),
        _ => Results.StatusCode(statusCode)
    };
}

public sealed record PublishRequestParseResult(
    int StatusCode,
    string? Error,
    UpdatePublishRequest? Request);