using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MemoDashboard.Backend.Services;

public sealed class ChromiumNotAvailableException : InvalidOperationException
{
    public ChromiumNotAvailableException(string message)
        : base(message)
    {
    }
}

public interface IReportPdfRenderer
{
    Task<byte[]> RenderPdfAsync(string html, CancellationToken cancellationToken = default);
}

/// <summary>
/// Renders the self-contained report HTML to A4-landscape PDF via headless
/// Chromium (system package, no license cost, correct Arabic shaping).
/// Ubuntu: apt install chromium-browser (see doc/DEPLOYMENT.md).
/// Override discovery with MEMO_CHROMIUM_PATH.
/// </summary>
public sealed class ChromiumPdfRenderer : IReportPdfRenderer
{
    public const string ChromiumPathVariable = "MEMO_CHROMIUM_PATH";

    private readonly ILogger<ChromiumPdfRenderer> _logger;
    private readonly string? _overridePath;

    public ChromiumPdfRenderer(ILogger<ChromiumPdfRenderer> logger)
        : this(logger, Environment.GetEnvironmentVariable(ChromiumPathVariable))
    {
    }

    public ChromiumPdfRenderer(ILogger<ChromiumPdfRenderer> logger, string? overridePath)
    {
        _logger = logger;
        _overridePath = string.IsNullOrWhiteSpace(overridePath) ? null : overridePath.Trim().Trim('"');
    }

    public static bool TryFindExecutable(out string path)
    {
        var overridePath = Environment.GetEnvironmentVariable(ChromiumPathVariable);
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            var trimmed = overridePath.Trim().Trim('"');
            if (File.Exists(trimmed))
            {
                path = trimmed;
                return true;
            }
        }

        foreach (var candidate in CandidatePaths())
        {
            if (File.Exists(candidate))
            {
                path = candidate;
                return true;
            }
        }

        if (!OperatingSystem.IsWindows())
        {
            foreach (var name in new[] { "chromium", "chromium-browser", "google-chrome", "google-chrome-stable" })
            {
                var found = FindOnPath(name);
                if (found is not null)
                {
                    path = found;
                    return true;
                }
            }
        }

        path = string.Empty;
        return false;
    }

    private static IEnumerable<string> CandidatePaths()
    {
        if (OperatingSystem.IsWindows())
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            yield return Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe");
            yield return Path.Combine(programFilesX86, "Google", "Chrome", "Application", "chrome.exe");
            yield return Path.Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe");
            yield return Path.Combine(programFilesX86, "Microsoft", "Edge", "Application", "msedge.exe");
        }
        else
        {
            yield return "/usr/bin/chromium";
            yield return "/usr/bin/chromium-browser";
            yield return "/usr/bin/google-chrome";
            yield return "/usr/bin/google-chrome-stable";
            yield return "/snap/bin/chromium";
        }
    }

    private static string? FindOnPath(string name)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "/usr/bin/which",
                Arguments = name,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEnd().Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            process.WaitForExit(5000);
            return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output) && File.Exists(output) ? output : null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<byte[]> RenderPdfAsync(string html, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(html);

        string executable;
        if (_overridePath is not null)
        {
            if (!File.Exists(_overridePath))
            {
                throw new ChromiumNotAvailableException(
                    "تعذر العثور على متصفح Chromium في MEMO_CHROMIUM_PATH. راجع doc/DEPLOYMENT.md لتثبيت Chromium.");
            }
            executable = _overridePath;
        }
        else if (!TryFindExecutable(out executable))
        {
            throw new ChromiumNotAvailableException(
                "تصدير PDF غير متاح: لم يُعثر على متصفح Chromium/Chrome على الخادم. راجع doc/DEPLOYMENT.md.");
        }

        var workDir = Path.Combine(Path.GetTempPath(), "memo-report-pdf", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        try
        {
            var htmlPath = Path.Combine(workDir, "report.html");
            var pdfPath = Path.Combine(workDir, "report.pdf");
            await File.WriteAllTextAsync(htmlPath, html, cancellationToken);

            // file:// URL avoids any auth/session plumbing; the HTML is self-contained.
            var fileUrl = new Uri(htmlPath).AbsoluteUri;
            var arguments = "--headless=new --no-sandbox --disable-gpu --hide-scrollbars "
                + "--no-pdf-header-footer --print-to-pdf=\"" + pdfPath + "\" \"" + fileUrl + "\"";

            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = workDir
            };
            _logger.LogInformation("Rendering report PDF via headless Chromium.");
            if (!process.Start())
            {
                throw new ChromiumNotAvailableException("تعذر تشغيل متصفح Chromium لتوليد PDF.");
            }

            var exited = true;
            using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(60));
                try
                {
                    await process.WaitForExitAsync(timeoutCts.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    exited = false;
                }
            }
            if (!exited)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                throw new TimeoutException("انتهت مهلة توليد PDF (60 ثانية).");
            }

            if (process.ExitCode != 0 || !File.Exists(pdfPath))
            {
                string stderr;
                try { stderr = await process.StandardError.ReadToEndAsync(); }
                catch { stderr = string.Empty; }
                _logger.LogWarning("Chromium PDF render failed with exit code {ExitCode}: {Stderr}", process.ExitCode, stderr);
                throw new InvalidOperationException("فشل توليد PDF عبر Chromium (رمز الخروج " + process.ExitCode + ").");
            }

            return await File.ReadAllBytesAsync(pdfPath, cancellationToken);
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
