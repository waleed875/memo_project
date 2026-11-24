using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using MemoUpdateContracts;
using Npgsql;
using Microsoft.AspNetCore.Http.Features;

namespace MemoDashboard.Backend.Services.Updates;

/// <summary>
/// Update publishing/catalogue service used by the unified Backend process.
/// MemoUpdateService keeps the same behaviour during the compatibility window,
/// but the Dashboard host no longer calls it over HTTP.
/// </summary>
public sealed class UnifiedUpdateService
{
    private static readonly Regex DatabaseVersionPattern = new("^[0-9]{1,6}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly UpdateServiceOptions _options;
    private readonly ILogger<UnifiedUpdateService> _logger;

    public UnifiedUpdateService(UpdateServiceOptions options, ILogger<UnifiedUpdateService> logger)
    {
        _options = options;
        _logger = logger;
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    public long MaxUploadRequestBytes => _options.MaxUploadRequestBytes;

    public UnifiedUpdateHealth GetHealth() => new(
        "MemoUpdateService",
        _options.IsConfigured,
        _options.IsConfigured && !string.IsNullOrWhiteSpace(_options.AdminKey),
        _options.IsConfigured && !string.IsNullOrWhiteSpace(_options.CheckInKey),
        _options.PublicBaseUri is not null,
        _options.IsConfigured ? "ready" : "degraded");

    public UpdatePublicKeyResult GetPublicKey()
    {
        if (!_options.SigningPrivateKeyReady)
            return new(503, null, "missing", "مفتاح التوقيع الخاص غير مهيأ أو غير صالح.");
        if (!_options.PublicKeyMatchesPrivate)
            return new(503, null, "mismatch", "المفتاح العام لا يطابق مفتاح التوقيع الخاص الحالي.");

        var publicKey = _options.ExportCurrentPublicKeyPem();
        return string.IsNullOrWhiteSpace(publicKey)
            ? new(503, null, "missing", "تعذر استخراج المفتاح العام الآمن.")
            : new(200, publicKey, "ready");
    }

    public bool IsPublisherAllowed(HttpRequest request) =>
        IsSharedSecretAllowed(request, _options.AdminKey, "X-Update-Admin-Key");

    public bool IsCheckInAllowed(HttpRequest request) =>
        IsSharedSecretAllowed(request, _options.CheckInKey, "X-Update-Device-Key");

    public string? ResolvePackagePath(string file)
    {
        var safeName = Path.GetFileName(file);
        if (string.IsNullOrWhiteSpace(safeName) || !string.Equals(safeName, file, StringComparison.Ordinal)) return null;
        var root = Path.GetFullPath(_options.PackageRoot);
        var path = Path.GetFullPath(Path.Combine(root, safeName));
        return path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && File.Exists(path)
            ? path
            : null;
    }

    public async Task<UpdateManifestResult> GetManifestAsync(string operatingSystem, string architecture, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
            return new(503, null, "خدمة التحديثات غير مهيأة.");

        var os = operatingSystem.Trim().ToLowerInvariant();
        var arch = architecture.Trim().ToLowerInvariant();
        if (os is not ("windows" or "linux") || arch is not ("x64" or "arm64"))
            return new(400, null, "نظام التشغيل والمعمارية غير صالحين.");

        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        var rows = await connection.QueryAsync<ArtifactRow>(new CommandDefinition("""
            select r.version, coalesce(r.minimum_version, '') as minimum_version,
                   a.operating_system, a.architecture, a.package_url, a.package_size, a.sha256,
                   coalesce(r.minimum_database_version, '') as required_database_version,
                   coalesce(r.target_database_version, '') as target_database_version, r.published_at
            from updates.releases r join updates.artifacts a on a.release_id = r.id
            where r.status = 'PUBLISHED' and a.operating_system = @os and a.architecture = @architecture
            """, new { os, architecture = arch }, cancellationToken: cancellationToken));

        ArtifactRow? selected = null;
        ReleaseVersion? newest = null;
        foreach (var candidate in rows)
        {
            if (!ReleaseVersion.TryParse(candidate.Version, out var version)) continue;
            if (newest is null || version.CompareTo(newest.Value) > 0
                || version.CompareTo(newest.Value) == 0 && candidate.PublishedAt > selected!.PublishedAt)
            {
                selected = candidate;
                newest = version;
            }
        }

        if (selected is null) return new(404, null);
        if (_options.PublicBaseUri is null)
            return new(503, null, "يلزم ضبط PublicBaseUrl ثابت عبر HTTPS لخدمة التحديثات.");
        if (!_options.SigningKeyReady)
            return new(503, null, "مفتاح توقيع خدمة التحديثات غير صالح أو غير مهيأ.");

        var packageUri = Uri.TryCreate(selected.PackageUrl, UriKind.Absolute, out var absolutePackageUri)
            ? absolutePackageUri
            : new Uri(_options.PublicBaseUri, selected.PackageUrl.TrimStart('/'));
        if (packageUri.Scheme != Uri.UriSchemeHttps)
            return new(503, null, "رابط الحزمة يجب أن يستخدم HTTPS.");

        using var rsa = RSA.Create();
        rsa.ImportFromPem(_options.SigningPrivateKeyPem);
        var publishedAt = new DateTimeOffset(selected.PublishedAt, TimeSpan.Zero);
        var payload = new SignedManifestPayload(
            selected.Version,
            selected.MinimumVersion,
            selected.OperatingSystem,
            selected.Architecture,
            packageUri.ToString(),
            selected.PackageSize,
            selected.Sha256,
            selected.RequiredDatabaseVersion,
            selected.TargetDatabaseVersion,
            "AuditMemoSystem.App.exe",
            publishedAt);
        var manifest = new UpdateManifest(
            payload.Version,
            payload.MinimumVersion,
            payload.OperatingSystem,
            payload.Architecture,
            packageUri,
            payload.PackageSize,
            payload.Sha256,
            payload.RequiredDatabaseVersion,
            payload.TargetDatabaseVersion,
            payload.ExecutableRelativePath,
            payload.PublishedAt,
            ManifestSecurity.Sign(payload, rsa));
        return new(200, manifest);
    }

    public async Task<UpdateOperationResult> PublishAsync(UpdatePublishRequest request, CancellationToken cancellationToken)
    {
        var versionText = request.Version.Trim();
        var os = string.IsNullOrWhiteSpace(request.OperatingSystem) ? "windows" : request.OperatingSystem.Trim().ToLowerInvariant();
        var architecture = string.IsNullOrWhiteSpace(request.Architecture) ? "x64" : request.Architecture.Trim().ToLowerInvariant();
        var minimumVersion = request.MinimumVersion.Trim();
        var minimumDatabaseVersion = request.MinimumDatabaseVersion.Trim();
        var targetDatabaseVersion = request.TargetDatabaseVersion.Trim();
        var notes = request.Notes.Trim();

        if (request.Package is null || request.Package.Length == 0 || request.Package.Length > _options.MaxPackageBytes
            || !request.Package.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            || !ReleaseVersion.TryParse(versionText, out var releaseVersion)
            || os is not ("windows" or "linux")
            || architecture is not ("x64" or "arm64"))
            return new(400, "يلزم ملف ZIP ورقم إصدار بصيغة صحيحة.");

        if (!string.IsNullOrEmpty(minimumVersion)
            && (!ReleaseVersion.TryParse(minimumVersion, out var parsedMinimumVersion)
                || parsedMinimumVersion.CompareTo(releaseVersion) > 0))
            return new(400, "الحد الأدنى للإصدار يجب أن يكون SemVer صحيحاً ولا يتجاوز الإصدار المنشور.");

        if ((!string.IsNullOrEmpty(minimumDatabaseVersion) && !DatabaseVersionPattern.IsMatch(minimumDatabaseVersion))
            || (!string.IsNullOrEmpty(targetDatabaseVersion) && !DatabaseVersionPattern.IsMatch(targetDatabaseVersion))
            || (!string.IsNullOrEmpty(minimumDatabaseVersion) && !string.IsNullOrEmpty(targetDatabaseVersion)
                && int.Parse(targetDatabaseVersion) < int.Parse(minimumDatabaseVersion)))
            return new(400, "إصدارات قاعدة البيانات يجب أن تكون أرقام ترحيل صحيحة، والهدف لا يقل عن الحد الأدنى.");

        if (string.IsNullOrWhiteSpace(_options.ConnectionString) || _options.PublicBaseUri is null || !_options.SigningKeyReady)
            return new(503, "خدمة التحديثات أو مفتاح التوقيع غير مهيأ.");

        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "select pg_advisory_xact_lock(hashtext(@channel))",
            new { channel = $"updates:release:{versionText}" }, transaction, cancellationToken: cancellationToken));

        var artifactExists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
            select exists(
                select 1 from updates.releases r
                join updates.artifacts a on a.release_id = r.id
                where r.version = @version and a.operating_system = @os and a.architecture = @architecture)
            """, new { version = versionText, os, architecture }, transaction, cancellationToken: cancellationToken));
        if (artifactExists) return new(409, $"يوجد artifact للإصدار {versionText} لهذه المنصة والمعمارية مسبقاً.");

        var existingRelease = await connection.QuerySingleOrDefaultAsync<ReleaseMetadataRow>(new CommandDefinition("""
            select id, status,
                   coalesce(release_notes, '') as "Notes",
                   coalesce(minimum_version, '') as "MinimumVersion",
                   coalesce(minimum_database_version, '') as "MinimumDatabaseVersion",
                   coalesce(target_database_version, '') as "TargetDatabaseVersion"
            from updates.releases
            where version = @version
            for update
            """, new { version = versionText }, transaction, cancellationToken: cancellationToken));
        if (existingRelease is not null)
        {
            var differences = GetMetadataDifferences(
                existingRelease.Notes,
                existingRelease.MinimumVersion,
                existingRelease.MinimumDatabaseVersion,
                existingRelease.TargetDatabaseVersion,
                notes,
                minimumVersion,
                minimumDatabaseVersion,
                targetDatabaseVersion);
            if (differences.Count > 0)
            {
                return new(
                    409,
                    new
                    {
                        message = "metadata الإصدار موجودة مسبقاً ولا تطابق artifact الجديد.",
                        fields = differences,
                        status = existingRelease.Status
                    });
            }
        }

        var publishedVersions = await connection.QueryAsync<string>(new CommandDefinition("""
            select distinct r.version from updates.releases r
            join updates.artifacts a on a.release_id = r.id
            where r.status = 'PUBLISHED' and a.operating_system = @os and a.architecture = @architecture
            """, new { os, architecture }, transaction, cancellationToken: cancellationToken));
        if (publishedVersions.Any(value => ReleaseVersion.TryParse(value, out var published)
            && published.CompareTo(releaseVersion) > 0))
            return new(409, "لا يمكن نشر إصدار أدنى من إصدار منشور مسبقاً لهذه المنصة والمعمارية.");

        var safeName = $"{versionText}-{os}-{architecture}.zip";
        var destination = Path.Combine(_options.PackageRoot, safeName);
        var temporary = destination + ".upload-" + Guid.NewGuid().ToString("N");
        var destinationOwnedByRequest = false;
        var committed = false;
        try
        {
            await using var input = request.Package.OpenReadStream();
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await CopyWithLimitAsync(input, output, _options.MaxPackageBytes, cancellationToken);
            }

            ValidatePackage(temporary, os, _options.MaxZipEntries, _options.MaxUncompressedPackageBytes, _options.MaxCompressionRatio);
            var packageInfo = new FileInfo(temporary);
            string sha;
            await using (var hashInput = packageInfo.OpenRead())
            {
                sha = await ManifestSecurity.Sha256Async(hashInput, cancellationToken);
            }
            var packageUrl = new Uri(_options.PublicBaseUri, $"packages/{Uri.EscapeDataString(safeName)}").ToString();
            File.Move(temporary, destination);
            destinationOwnedByRequest = true;

            var releaseId = existingRelease?.Id;
            if (releaseId is null)
            {
                releaseId = await connection.QuerySingleAsync<Guid>(new CommandDefinition("""
                    insert into updates.releases(version, status, release_notes, minimum_version, minimum_database_version, target_database_version, published_at)
                    values (@version, 'PUBLISHED', @notes, nullif(@minimumVersion, ''), nullif(@minimumDatabaseVersion, ''), nullif(@targetDatabaseVersion, ''), now())
                    returning id
                    """, new
                {
                    version = versionText,
                    notes,
                    minimumVersion,
                    minimumDatabaseVersion,
                    targetDatabaseVersion
                }, transaction, cancellationToken: cancellationToken));
            }

            await connection.ExecuteAsync(new CommandDefinition("""
                insert into updates.artifacts(release_id, operating_system, architecture, package_url, package_size, sha256, manifest_signature)
                values (@releaseId, @os, @architecture, @packageUrl, @size, @sha, '')
                """, new { releaseId, os, architecture, packageUrl, size = packageInfo.Length, sha }, transaction, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition(
                "insert into updates.audit_log(action, release_id, description) values ('PUBLISH_RELEASE', @releaseId, @description)",
                new { releaseId, description = $"Published {safeName} through unified Backend" }, transaction, cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
            committed = true;
            return new(201, new { version = versionText, sha256 = sha, status = "PUBLISHED" }, $"/api/updates/releases/{Uri.EscapeDataString(versionText)}");
        }
        catch (IOException)
        {
            return new(409, $"ملف الإصدار {versionText} موجود مسبقاً. استخدم رقم إصدار جديد.");
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return new(409, $"الإصدار {versionText} موجود مسبقاً. استخدم رقم إصدار جديد.");
        }
        finally
        {
            TryDelete(temporary);
            if (!committed && destinationOwnedByRequest) TryDelete(destination);
        }
    }

    public async Task<UpdateOperationResult> PromoteAsync(string version, CancellationToken cancellationToken)
    {
        if (!ReleaseVersion.TryParse(version, out var candidate)) return new(400, "رقم الإصدار غير صالح.");
        if (string.IsNullOrWhiteSpace(_options.ConnectionString)) return new(503, "خدمة التحديثات غير مهيأة.");
        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var latestPublished = await connection.QueryFirstOrDefaultAsync<string>(new CommandDefinition(
            "select version from updates.releases where status = 'PUBLISHED' order by published_at desc nulls last limit 1",
            cancellationToken: cancellationToken));
        if (latestPublished is not null
            && ReleaseVersion.TryParse(latestPublished, out var published)
            && published.CompareTo(candidate) > 0)
            return new(409, "لا يمكن ترقية إصدار أدنى من إصدار منشور مسبقاً.");
        var updated = await connection.ExecuteAsync(new CommandDefinition(
            "update updates.releases set status = 'PUBLISHED', published_at = now() where version = @version and status = 'DRAFT'",
            new { version }, cancellationToken: cancellationToken));
        return updated == 1 ? new(200, new { version, status = "PUBLISHED" }) : new(404, null);
    }

    public async Task<UpdateOperationResult> CheckInAsync(UpdateCheckInRequest checkIn, CancellationToken cancellationToken)
    {
        if (checkIn.DeviceId == Guid.Empty
            || string.IsNullOrWhiteSpace(checkIn.DeviceName) || checkIn.DeviceName.Length > 128
            || checkIn.OperatingSystem is not ("windows" or "linux")
            || checkIn.Architecture is not ("x64" or "arm64")
            || !ReleaseVersion.TryParse(checkIn.InstalledVersion, out _)
            || checkIn.LastUpdateStatus?.Length > 64
            || checkIn.LastUpdateMessage?.Length > 2_000)
            return new(400, "بيانات check-in غير صالحة.");
        if (string.IsNullOrWhiteSpace(_options.ConnectionString)) return new(503, "خدمة التحديثات غير مهيأة.");

        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.ExecuteAsync(new CommandDefinition("""
            insert into updates.installations(device_id, device_name, operating_system, architecture, installed_version, last_checked_at, last_update_status, last_update_message)
            values (@DeviceId, @DeviceName, @OperatingSystem, @Architecture, @InstalledVersion, now(), @LastUpdateStatus, @LastUpdateMessage)
            on conflict (device_id) do update set device_name = excluded.device_name,
              installed_version = excluded.installed_version, last_checked_at = excluded.last_checked_at,
              last_update_status = excluded.last_update_status, last_update_message = excluded.last_update_message, updated_at = now()
            """, checkIn, cancellationToken: cancellationToken));
        return new(204, null);
    }

    private static bool IsSharedSecretAllowed(HttpRequest request, string? expectedSecret, string headerName)
    {
        if (string.IsNullOrWhiteSpace(expectedSecret) || !request.Headers.TryGetValue(headerName, out var received)) return false;
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expectedSecret));
        var receivedHash = SHA256.HashData(Encoding.UTF8.GetBytes(received.ToString()));
        return CryptographicOperations.FixedTimeEquals(expectedHash, receivedHash);
    }

    private static async Task CopyWithLimitAsync(Stream input, Stream output, long limit, CancellationToken cancellationToken)
    {
        var buffer = new byte[80 * 1024];
        long copied = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            copied = checked(copied + read);
            if (copied > limit) throw new InvalidDataException("حجم الحزمة يتجاوز الحد المسموح.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static void ValidatePackage(string packagePath, string operatingSystem, int maxEntries, long maxUncompressedBytes, int maxCompressionRatio)
    {
        using var input = File.OpenRead(packagePath);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        if (archive.Entries.Count == 0 || archive.Entries.Count > maxEntries) throw new InvalidDataException("عدد ملفات ZIP غير مسموح.");

        var executable = operatingSystem == "windows" ? "AuditMemoSystem.App.exe" : "AuditMemoSystem.App";
        long totalUncompressed = 0;
        long totalCompressed = 0;
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var executableFound = false;
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName;
            if (string.IsNullOrWhiteSpace(name) || name.IndexOf('\0') >= 0 || Path.IsPathRooted(name)
                || name.StartsWith('/') || name.StartsWith('\\') || name.Contains(':')
                || name.Split(new[] { '/', '\\' }, StringSplitOptions.None).Any(part => part is "." or ".."))
                throw new InvalidDataException("مسار غير آمن داخل ZIP.");
            var normalized = name.Replace('\\', '/').TrimEnd('/');
            if (normalized.Length == 0 || normalized.Length > 240 || !paths.Add(normalized))
                throw new InvalidDataException("مسار ZIP فارغ أو طويل أو مكرر.");
            var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixType == 0xA000) throw new InvalidDataException("الروابط الرمزية غير مسموحة داخل حزمة التحديث.");
            if (name.EndsWith('/') || name.EndsWith('\\')) continue;

            if (entry.Length > maxUncompressedBytes || entry.CompressedLength < 0
                || entry.CompressedLength == 0 && entry.Length > 0
                || entry.CompressedLength > 0 && entry.Length / Math.Max(1D, entry.CompressedLength) > maxCompressionRatio)
                throw new InvalidDataException("نسبة ضغط ZIP أو حجم ملفه غير مسموح.");
            totalUncompressed = checked(totalUncompressed + entry.Length);
            totalCompressed = checked(totalCompressed + entry.CompressedLength);
            if (totalUncompressed > maxUncompressedBytes
                || totalUncompressed > 0 && (totalCompressed == 0 || totalUncompressed / Math.Max(1D, totalCompressed) > maxCompressionRatio))
                throw new InvalidDataException("حجم ZIP بعد الفك أو نسبة ضغطه غير مسموح.");
            executableFound |= normalized.Equals(executable, StringComparison.OrdinalIgnoreCase) && entry.Length > 0;
        }
        if (!executableFound) throw new InvalidDataException($"حزمة التحديث لا تحتوي {executable} صالحاً في جذر ZIP.");
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    public static IReadOnlyList<string> GetMetadataDifferences(
        string storedNotes,
        string storedMinimumVersion,
        string storedMinimumDatabaseVersion,
        string storedTargetDatabaseVersion,
        string requestedNotes,
        string requestedMinimumVersion,
        string requestedMinimumDatabaseVersion,
        string requestedTargetDatabaseVersion)
    {
        var differences = new List<string>();
        if (!string.Equals(storedNotes, requestedNotes, StringComparison.Ordinal))
            differences.Add("notes");
        if (!string.Equals(storedMinimumVersion, requestedMinimumVersion, StringComparison.Ordinal))
            differences.Add("minimumVersion");
        if (!string.Equals(storedMinimumDatabaseVersion, requestedMinimumDatabaseVersion, StringComparison.Ordinal))
            differences.Add("minimumDatabaseVersion");
        if (!string.Equals(storedTargetDatabaseVersion, requestedTargetDatabaseVersion, StringComparison.Ordinal))
            differences.Add("targetDatabaseVersion");
        return differences;
    }

    private sealed record ReleaseMetadataRow(
        Guid Id,
        string Status,
        string Notes,
        string MinimumVersion,
        string MinimumDatabaseVersion,
        string TargetDatabaseVersion);

    private sealed record ArtifactRow(
        string Version,
        string MinimumVersion,
        string OperatingSystem,
        string Architecture,
        string PackageUrl,
        long PackageSize,
        string Sha256,
        string RequiredDatabaseVersion,
        string TargetDatabaseVersion,
        DateTime PublishedAt);
}