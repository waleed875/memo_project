using MemoDashboard.Backend.Services;
using MemoDashboard.Backend.Services.Updates;

namespace MemoDashboard.Backend.Endpoints;

public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", async (
            DatabaseHealthCheck healthCheck,
            UnifiedUpdateService updates,
            DatabaseMigrationService migrationService,
            CancellationToken cancellationToken) =>
        {
            var (databaseOk, databaseMessage) = await healthCheck.CheckAsync(cancellationToken);
            var update = updates.GetHealth();
            var status = databaseOk
                ? update.Configured ? "ok" : "degraded"
                : "error";

            var versionStatus = migrationService.LastKnownStatus;

            return Results.Json(
                new
                {
                    status,
                    database = databaseOk ? "ok" : "unreachable",
                    message = databaseOk ? "قاعدة البيانات متصلة" : databaseMessage ?? "تعذر الوصول إلى قاعدة البيانات",
                    databaseVersion = versionStatus?.DatabaseVersion,
                    applicationVersion = versionStatus?.ApplicationVersion,
                    requiresUpgrade = versionStatus?.RequiresUpgrade ?? false,
                    databaseIsNewer = versionStatus?.DatabaseIsNewer ?? false,
                    service = update.Service,
                    configured = update.Configured,
                    publishingConfigured = update.PublishingConfigured,
                    checkInConfigured = update.CheckInConfigured,
                    publicBaseUrlValid = update.PublicBaseUrlValid
                },
                statusCode: databaseOk ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
        });
    }
}