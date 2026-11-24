using AuditMemoSystem.Core.Models;
using Microsoft.Extensions.Configuration;

namespace AuditMemoSystem.Data.Connection;

public static class ConfigurationLoader
{
    public static AppConfig Load(string baseDirectory)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(baseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();

        return config.Get<AppConfig>() ?? new AppConfig();
    }
}
