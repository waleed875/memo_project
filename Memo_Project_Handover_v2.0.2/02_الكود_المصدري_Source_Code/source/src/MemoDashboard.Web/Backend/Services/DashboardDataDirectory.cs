using System.Security.Cryptography;

namespace MemoDashboard.Backend.Services;

public sealed record DashboardDataPaths(
    string RootDirectory,
    string KeysDirectory,
    string ConfigurationFile);

public static class DashboardDataDirectory
{
    public const string EnvironmentVariable = "MEMO_DASHBOARD_DATA_DIR";

    public static DashboardDataPaths Initialize(
        IWebHostEnvironment environment,
        IConfiguration configuration)
    {
        var configuredRoot = configuration[EnvironmentVariable]?.Trim();
        var root = string.IsNullOrWhiteSpace(configuredRoot)
            ? DefaultRoot()
            : configuredRoot;

        if (!Path.IsPathRooted(root))
        {
            throw new InvalidOperationException($"{EnvironmentVariable} must be an absolute path.");
        }

        root = Path.GetFullPath(root);
        var contentRoot = Path.GetFullPath(environment.ContentRootPath);
        if (IsWithin(contentRoot, root))
        {
            throw new InvalidOperationException(
                $"{EnvironmentVariable} must point outside the application content root.");
        }

        var keys = Path.Combine(root, "keys");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(keys);
        HardenDirectory(root);
        HardenDirectory(keys);

        var legacyRoot = Path.Combine(contentRoot, "App_Data");
        if (Directory.Exists(legacyRoot))
        {
            MigrateLegacyFile(
                Path.Combine(legacyRoot, "app_settings.json"),
                Path.Combine(root, "app_settings.json"));

            var legacyKeys = Path.Combine(legacyRoot, "keys");
            if (Directory.Exists(legacyKeys))
            {
                foreach (var source in Directory.EnumerateFiles(legacyKeys, "key-*.xml", SearchOption.TopDirectoryOnly))
                {
                    MigrateLegacyFile(source, Path.Combine(keys, Path.GetFileName(source)));
                }

                DeleteIfEmpty(legacyKeys);
            }

            DeleteIfEmpty(legacyRoot);
        }

        return new DashboardDataPaths(
            root,
            keys,
            Path.Combine(root, "app_settings.json"));
    }

    public static void HardenFile(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static string DefaultRoot()
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData))
        {
            throw new InvalidOperationException(
                $"{EnvironmentVariable} is required because no local application-data directory is available.");
        }

        return Path.Combine(localData, "AuditMemoSystem", "MemoDashboard");
    }

    private static bool IsWithin(string parent, string candidate)
    {
        var relative = Path.GetRelativePath(parent, candidate);
        return relative == "."
            || (!Path.IsPathRooted(relative)
                && relative != ".."
                && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal));
    }

    private static void MigrateLegacyFile(string source, string destination)
    {
        if (!File.Exists(source)) return;

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(destination))
        {
            if (!FilesMatch(source, destination))
            {
                throw new InvalidOperationException(
                    "Legacy MemoDashboard runtime data conflicts with the configured data directory. Resolve it before startup.");
            }

            File.Delete(source);
            return;
        }

        var temporary = destination + ".migration-" + Guid.NewGuid().ToString("N");
        try
        {
            File.Copy(source, temporary, overwrite: false);
            HardenFile(temporary);
            File.Move(temporary, destination, overwrite: false);
            File.Delete(source);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static bool FilesMatch(string left, string right)
    {
        using var leftStream = File.OpenRead(left);
        using var rightStream = File.OpenRead(right);
        var leftHash = SHA256.HashData(leftStream);
        var rightHash = SHA256.HashData(rightStream);
        return CryptographicOperations.FixedTimeEquals(leftHash, rightHash);
    }

    private static void HardenDirectory(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static void DeleteIfEmpty(string path)
    {
        if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
        {
            Directory.Delete(path);
        }
    }
}

