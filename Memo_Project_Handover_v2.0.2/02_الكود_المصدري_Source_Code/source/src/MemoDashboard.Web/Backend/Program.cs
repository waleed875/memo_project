using AuditMemoSystem.Core.Models;
using AuditMemoSystem.Core.Services;
using AuditMemoSystem.Data.Connection;
using AuditMemoSystem.Data.Repositories;
using MemoDashboard.Backend.Endpoints;
using MemoDashboard.Backend.Services;
using MemoDashboard.Backend.Services.Updates;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using System.Threading.RateLimiting;

Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;

var builder = WebApplication.CreateBuilder(args);

var deploymentMode = builder.Configuration["DeploymentMode"]?.Trim();
var configuredUrls = builder.Configuration["ASPNETCORE_URLS"]
    ?? builder.Configuration["Urls"]
    ?? string.Empty;
var cookieSecurePolicy = string.Equals(deploymentMode, "InternalLanHttps", StringComparison.OrdinalIgnoreCase)
    || string.Equals(deploymentMode, "Https", StringComparison.OrdinalIgnoreCase)
    ? CookieSecurePolicy.Always
    : CookieSecurePolicy.SameAsRequest;

var dataPaths = DashboardDataDirectory.Initialize(builder.Environment, builder.Configuration);
builder.Services.AddSingleton(dataPaths);
builder.Services.AddSingleton(UpdateServiceOptions.Create(builder.Configuration, builder.Environment));
builder.Services.AddSingleton<UnifiedUpdateService>();
var dataProtection = builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataPaths.KeysDirectory))
    .SetApplicationName("MemoDashboard");
if (OperatingSystem.IsWindows()) dataProtection.ProtectKeysWithDpapi();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
});

builder.Services.AddSingleton<ConfigurationService>();
builder.Services.AddSingleton<DatabaseHealthCheck>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});
builder.Services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options =>
{
    options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});

// Register AppConfig and DbConnectionFactory tied dynamically to ConfigurationService
builder.Services.AddSingleton<AppConfig>(sp =>
{
    var configService = sp.GetRequiredService<ConfigurationService>();
    var appConfig = new AppConfig();
    var connStr = configService.BuildConnectionString();
    if (!string.IsNullOrWhiteSpace(connStr))
    {
        var csb = new Npgsql.NpgsqlConnectionStringBuilder(connStr);
        appConfig.Database.ActiveProfile = "Server";
        appConfig.Database.Profiles = new List<ConnectionProfileModel>
        {
            new ConnectionProfileModel
            {
                Name = "Server",
                Host = csb.Host ?? "localhost",
                Port = csb.Port,
                Database = csb.Database ?? "audit_memo",
                Username = csb.Username ?? "postgres",
                Password = csb.Password ?? "",
                SslMode = csb.SslMode.ToString(),
                IsActive = true
            }
        };
    }
    return appConfig;
});

builder.Services.AddSingleton<DbConnectionFactory>(sp =>
{
    var configService = sp.GetRequiredService<ConfigurationService>();
    return new DbConnectionFactory(() =>
    {
        var appConfig = new AppConfig();
        var connStr = configService.BuildConnectionString();
        if (!string.IsNullOrWhiteSpace(connStr))
        {
            var csb = new Npgsql.NpgsqlConnectionStringBuilder(connStr);
            appConfig.Database.ActiveProfile = "Server";
            appConfig.Database.Profiles = new List<ConnectionProfileModel>
            {
                new ConnectionProfileModel
                {
                    Name = "Server",
                    Host = csb.Host ?? "localhost",
                    Port = csb.Port,
                    Database = csb.Database ?? "audit_memo",
                    Username = csb.Username ?? "postgres",
                    Password = csb.Password ?? "",
                    SslMode = csb.SslMode.ToString(),
                    IsActive = true
                }
            };
        }
        return appConfig;
    });
});

// Domain repositories and services
builder.Services.AddSingleton<ISystemModeService, SystemModeService>();
builder.Services.AddSingleton<IAuditService, AuditService>();
builder.Services.AddSingleton<ISystemSettingsService, SystemSettingsService>();
builder.Services.AddSingleton<ICompanyService, CompanyService>();
builder.Services.AddSingleton<IFactoryService, FactoryService>();
builder.Services.AddSingleton<IProductionLineService, ProductionLineService>();
builder.Services.AddSingleton<INoteTypeService, NoteTypeService>();
builder.Services.AddSingleton<INoteService, NoteService>();
builder.Services.AddSingleton<IUserService, UserService>();
builder.Services.AddSingleton<IReportService, ReportService>();

builder.Services.AddSingleton<DashboardQueryService>();
builder.Services.AddSingleton<ICompanyMetricsService, CompanyMetricsService>();
builder.Services.AddSingleton<CompanyReportService>();
builder.Services.AddSingleton<IReportPdfRenderer, ChromiumPdfRenderer>();
builder.Services.AddSingleton<SetupAccessService>();
builder.Services.AddSingleton<DatabaseMigrationService>();
builder.Services.AddSingleton<IWebAdminAuthService, WebAdminAuthService>();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = ".MemoDashboard.Csrf";
    options.Cookie.HttpOnly = false;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = cookieSecurePolicy;
    options.Cookie.IsEssential = true;
    options.Cookie.Path = "/";
});
builder.Services.AddSession(options =>
{
    options.Cookie.Name = ".MemoDashboard.Admin";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = cookieSecurePolicy;
    options.Cookie.IsEssential = true;
    options.Cookie.Path = "/";
    options.IdleTimeout = TimeSpan.FromMinutes(30);
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("admin-login", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(5), QueueLimit = 0 }));
    options.AddPolicy("setup", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(5), QueueLimit = 0, AutoReplenishment = true }));
    options.AddPolicy("setup-read", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
    options.AddPolicy("dashboard", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
    options.AddPolicy("notes", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 300, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
    options.AddPolicy("reports", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
    options.AddPolicy("check-in", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("publishing", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));
    options.OnRejected = static async (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType = "application/problem+json";
        await context.HttpContext.Response.WriteAsJsonAsync(new { title = "تم تجاوز حد الطلبات.", status = 429 }, cancellationToken);
    };
});

AppDomain.CurrentDomain.UnhandledException += (sender, eventArgs) =>
{
    var ex = eventArgs.ExceptionObject as Exception;
    var msg = $"[{DateTime.UtcNow:O}] FATAL UNHANDLED EXCEPTION: {ex?.ToString() ?? eventArgs.ExceptionObject?.ToString()}";
    try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "crash.log"), msg + Environment.NewLine); } catch { }
    Console.Error.WriteLine(msg);
};

AppDomain.CurrentDomain.ProcessExit += (sender, eventArgs) =>
{
    var msg = $"[{DateTime.UtcNow:O}] ProcessExit triggered (ExitCode={Environment.ExitCode}). StackTrace:\n{Environment.StackTrace}";
    try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "backend_lifecycle.log"), msg + Environment.NewLine); } catch { }
    Console.WriteLine(msg);
};

try
{
    Console.CancelKeyPress += (sender, eventArgs) =>
    {
        var msg = $"[{DateTime.UtcNow:O}] Console.CancelKeyPress received (SpecialKey={eventArgs.SpecialKey}).";
        try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "backend_lifecycle.log"), msg + Environment.NewLine); } catch { }
        Console.WriteLine(msg);
    };
}
catch { }

TaskScheduler.UnobservedTaskException += (sender, eventArgs) =>
{
    var msg = $"[{DateTime.UtcNow:O}] UNOBSERVED TASK EXCEPTION: {eventArgs.Exception}";
    try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "crash.log"), msg + Environment.NewLine); } catch { }
    Console.Error.WriteLine(msg);
    eventArgs.SetObserved();
};

var app = builder.Build();

var appLifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
appLifetime.ApplicationStopping.Register(() =>
{
    var msg = $"[{DateTime.UtcNow:O}] IHostApplicationLifetime: ApplicationStopping triggered.\nStackTrace:\n{Environment.StackTrace}";
    try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "backend_lifecycle.log"), msg + Environment.NewLine); } catch { }
    Console.WriteLine(msg);
});
appLifetime.ApplicationStopped.Register(() =>
{
    var msg = $"[{DateTime.UtcNow:O}] IHostApplicationLifetime: ApplicationStopped triggered.";
    try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "backend_lifecycle.log"), msg + Environment.NewLine); } catch { }
    Console.WriteLine(msg);
});

app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        var exceptionHandlerPathFeature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature>();
        var ex = exceptionHandlerPathFeature?.Error;
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("GlobalExceptionHandler");
        logger.LogError(ex, "Unhandled exception on {Path}", context.Request.Path);

        try
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, "crash.log");
            File.AppendAllText(logPath, $"[{DateTime.UtcNow:O}] HTTP 500 on {context.Request.Path}: {ex}" + Environment.NewLine);
        }
        catch { }

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new
        {
            type = "https://tools.ietf.org/html/rfc7231#section-6.6.1",
            title = "حدث خطأ غير متوقع أثناء معالجة الطلب.",
            status = 500,
            detail = ex?.Message
        });
    });
});

app.UseForwardedHeaders();
if (cookieSecurePolicy == CookieSecurePolicy.Always) app.UseHsts();

app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "SAMEORIGIN";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers.Append("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
    await next();
});

app.UseRateLimiter();
app.UseSession();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        var auth = context.RequestServices.GetRequiredService<IWebAdminAuthService>();
        await auth.ValidateCurrentAsync(context, context.RequestAborted);
    }
    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles();

AuthEndpoints.MapAuthEndpoints(app);
MasterDataEndpoints.MapMasterDataEndpoints(app);
NotesEndpoints.MapNotesEndpoints(app);
ReportsEndpoints.MapReportsEndpoints(app);
DashboardEndpoints.MapDashboardEndpoints(app);
CompanyReportEndpoints.MapCompanyReportEndpoints(app);
HealthEndpoints.MapHealthEndpoints(app);
SetupEndpoints.MapSetupEndpoints(app);
AdminEndpoints.MapAdminEndpoints(app);
UpdateEndpoints.MapUpdateEndpoints(app);

app.MapFallbackToFile("index.html");

if (app.Services.GetRequiredService<ConfigurationService>().HasConfiguration)
{
    try
    {
        var migrationService = app.Services.GetRequiredService<DatabaseMigrationService>();
        var status = await migrationService.GetVersionStatusAsync();
        if (status.DatabaseIsNewer)
        {
            app.Logger.LogError("تنبيه حرج: إصدار قاعدة البيانات ({DatabaseVersion}) أحدث من إصدار البرنامج الحالي ({ApplicationVersion}). يرجى مراجعة دائرة نظم وقواعد البيانات للتأكد من التحديث.", status.DatabaseVersion, status.ApplicationVersion);
        }
        else if (status.RequiresUpgrade)
        {
            app.Logger.LogWarning("تنبيه: قاعدة البيانات الحالية بالإصدار ({DatabaseVersion}) وهي أقدم من إصدار النظام ({ApplicationVersion}). يلزم موافقة مدير النظام لتطبيق الترحيلات لتحديثها.", status.DatabaseVersion, status.ApplicationVersion);
        }
        else
        {
            app.Logger.LogInformation("قاعدة البيانات متوافقة ومحدثة للإصدار {DatabaseVersion}.", status.DatabaseVersion);
        }
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "تعذر التحقق من توافق إصدار قاعدة البيانات عند بدء التشغيل.");
    }
}

app.Run();

public partial class Program;