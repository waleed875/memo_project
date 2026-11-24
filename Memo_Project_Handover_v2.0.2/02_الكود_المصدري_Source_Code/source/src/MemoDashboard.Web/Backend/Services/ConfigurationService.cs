using System.Net;


using System.Text.Json;
using MemoDashboard.Backend.Models;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;

namespace MemoDashboard.Backend.Services;

public sealed class ConfigurationService
{
    private readonly IDataProtector _protector;

    private readonly ILogger<ConfigurationService> _logger;
    private readonly string _configFilePath;
    private AppSettings? _settings;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public string AppDataPath { get; }

    public bool HasConfiguration { get; private set; }

    public ConfigurationService(
        DashboardDataPaths paths,
        IDataProtectionProvider protectionProvider,
        ILogger<ConfigurationService> logger)
    {
        AppDataPath = paths.RootDirectory;
        _configFilePath = paths.ConfigurationFile;
        _protector = protectionProvider.CreateProtector("MemoDashboard.ConnectionString");

        _logger = logger;
        Load();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_configFilePath))
            {
                return;
            }

            var json = File.ReadAllText(_configFilePath);
            _settings = JsonSerializer.Deserialize<AppSettings>(json);
            HasConfiguration = _settings is not null
                && !string.IsNullOrWhiteSpace(_settings.Host)
                && !string.IsNullOrWhiteSpace(_settings.Database)
                && !string.IsNullOrWhiteSpace(_settings.Username)
                && _settings.Port is > 0 and <= 65535
                && !string.IsNullOrWhiteSpace(_settings.EncryptedPassword);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "تعذر قراءة ملف الإعدادات {Path}", _configFilePath);
            HasConfiguration = false;
        }
    }

    public async Task<TestConnectionResult> TestConnectionAsync(
        SetupRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var connectionString = BuildConnectionString(
                request.Host,
                request.Port,
                request.Database,
                request.Username,
                ResolvePassword(request.Password),
                NormalizeSslMode(request.SslMode));

            return await DatabaseMigrationService.InspectConnectionVersionAsync(connectionString, cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "تعذر اختبار اتصال قاعدة بيانات لوحة المتابعة");
            return new TestConnectionResult
            {
                Success = false,
                Message = GetConnectionErrorMessage(ex)
            };
        }
    }
    public void Save(SetupRequest request) => SaveAsync(request).GetAwaiter().GetResult();

    public async Task SaveAsync(SetupRequest request, CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken);
        var temporary = _configFilePath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            var current = _settings;
            var encryptedPassword = string.IsNullOrEmpty(request.Password) && current?.EncryptedPassword is not null
                ? current.EncryptedPassword
                : _protector.Protect(request.Password ?? string.Empty);

            var updated = new AppSettings
            {
                Host = request.Host?.Trim(),
                Port = request.Port,
                Database = request.Database?.Trim(),
                Username = request.Username?.Trim(),
                OrganizationName = request.OrganizationName?.Trim(),
                SubOrganizationName = request.SubOrganizationName?.Trim(),
                EncryptedPassword = encryptedPassword,
                SslMode = NormalizeSslMode(request.SslMode),
                DashboardVersion = ResolveVersion(request.DashboardVersion, current?.DashboardVersion)
            };

            Directory.CreateDirectory(Path.GetDirectoryName(_configFilePath)!);
            var json = JsonSerializer.Serialize(updated, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(temporary, json, cancellationToken);
            DashboardDataDirectory.HardenFile(temporary);
            File.Move(temporary, _configFilePath, overwrite: true);
            _settings = updated;
            HasConfiguration = true;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            _writeGate.Release();
        }
    }
    public string? BuildConnectionString()
    {
        if (!HasConfiguration || _settings is null)
        {
            return null;
        }

        var password = DecryptPassword();
        if (password is null)
        {
            return null;
        }

        return BuildConnectionString(
            _settings.Host,
            _settings.Port,
            _settings.Database,
            _settings.Username,
            password,
            _settings.SslMode);
    }
    public SetupConfig? GetConfigurationInfo()
    {
        if (_settings is null)
        {
            return null;
        }

        return new SetupConfig
        {
            Host = _settings.Host,
            Port = _settings.Port,
            Database = _settings.Database,
            Username = _settings.Username,
            OrganizationName = _settings.OrganizationName,
            SubOrganizationName = _settings.SubOrganizationName,
            SslMode = NormalizeSslMode(_settings.SslMode),
            DashboardVersion = _settings.DashboardVersion is 2 or 3 ? _settings.DashboardVersion : 1
        };
    }
    public static bool IsPrivateNetworkAddress(IPAddress? address)
    {
        if (address is null)
        {
            return false;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        var bytes = address.GetAddressBytes();
        if (bytes.Length == 4)
        {
            return bytes[0] == 10
                || bytes[0] == 192 && bytes[1] == 168
                || bytes[0] == 172 && bytes[1] is >= 16 and <= 31
                || bytes[0] == 169 && bytes[1] == 254;
        }

        return bytes.Length == 16
            && (address.IsIPv6LinkLocal || (bytes[0] & 0xFE) == 0xFC);
    }

    private string? DecryptPassword()
    {
        if (_settings?.EncryptedPassword is null)
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(_settings.EncryptedPassword);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "فشل فك تشفير كلمة مرور قاعدة البيانات");
            return null;
        }
    }

    private string? ResolvePassword(string? password)
    {
        if (!string.IsNullOrEmpty(password))
        {
            return password;
        }

        return DecryptPassword();
    }

    private static string GetConnectionErrorMessage(Exception exception)
    {
        if (exception is InvalidOperationException)
        {
            return exception.Message;
        }

        return "تعذر الاتصال بقاعدة البيانات باستخدام الإعدادات المقدمة.";
    }
    private static int ResolveVersion(int? requested, int? previous)
    {
        if (requested is 3)
        {
            return 3;
        }

        if (requested is 2)
        {
            return 2;
        }

        if (requested is null)
        {
            return previous is 3 ? 3 : previous is 2 ? 2 : 1;
        }

        return 1;
    }

    private static string BuildConnectionString(
        string? host,
        int port,
        string? database,
        string? username,
        string? password,
        string? sslMode = null)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = port,
            Database = database,
            Username = username,
            Password = password,
            Timeout = 10,
            CommandTimeout = 30,
            SslMode = NormalizeSslMode(sslMode) == "Require" ? SslMode.Require : SslMode.Disable
        };

        return builder.ConnectionString;
    }

    /// <summary>يسمح بـ Disable/Require فقط؛ أي قيمة أخرى تعود إلى Disable بأمان.</summary>
    public static string NormalizeSslMode(string? value) =>
        string.Equals(value?.Trim(), "Require", StringComparison.OrdinalIgnoreCase)
            ? "Require"
            : "Disable";
}