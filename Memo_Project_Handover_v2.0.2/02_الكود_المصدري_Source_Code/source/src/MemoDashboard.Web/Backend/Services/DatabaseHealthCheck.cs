using Npgsql;

namespace MemoDashboard.Backend.Services;

public sealed class DatabaseHealthCheck
{
    private readonly ConfigurationService _configuration;
    private readonly ILogger<DatabaseHealthCheck> _logger;

    public DatabaseHealthCheck(ConfigurationService configuration, ILogger<DatabaseHealthCheck> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<(bool Ok, string? Message)> CheckAsync(CancellationToken cancellationToken = default)
    {
        var connectionString = _configuration.BuildConnectionString();
        if (connectionString is null)
        {
            return (false, "لم يتم إعداد الاتصال بقاعدة البيانات بعد.");
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync(cancellationToken);
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "فحص صحة قاعدة البيانات فشل");
            return (false, ex.Message);
        }
    }
}
