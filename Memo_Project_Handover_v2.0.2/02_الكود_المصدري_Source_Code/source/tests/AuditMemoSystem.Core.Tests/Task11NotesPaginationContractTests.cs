using AuditMemoSystem.Core.Models;
using Xunit;

namespace AuditMemoSystem.Core.Tests;

public sealed class Task11NotesPaginationContractTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(99, 1)]
    [InlineData(100, 1)]
    [InlineData(101, 2)]
    [InlineData(1001, 11)]
    public void PagedResultCalculatesExpectedTotalPages(int totalCount, int expectedPages)
    {
        var result = new PagedResult<NoteDetailsModel>
        {
            TotalCount = totalCount,
            PageNumber = 1,
            PageSize = 100
        };

        Assert.Equal(expectedPages, result.TotalPages);
    }

    [Fact]
    public void EmptyNoteDateRangeMeansAllExistingNotes()
    {
        var filter = new NoteSearchFilterModel();

        Assert.Equal(DateOnly.MinValue, filter.From);
        Assert.Equal(DateOnly.MaxValue, filter.To);
    }

    [Fact]
    public void SearchNotesUsesServerSidePageAndStableOrderingWithoutListPayloads()
    {
        var service = File.ReadAllText(FindRepositoryFile("src/AuditMemoSystem.Data/Repositories/DataServices.cs"));

        Assert.Contains("NoteSearchFilterModel", service, StringComparison.Ordinal);
        Assert.Contains("PagedResult<NoteDetailsModel>", service, StringComparison.Ordinal);
        Assert.Contains("limit @page_size offset @offset", service, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("order by d.arrival_at desc, d.created_at desc, d.note_id desc", service, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("select count(*)", service, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("select d.image_path", service, StringComparison.OrdinalIgnoreCase);
        var searchStart = service.IndexOf("SearchNotesAsync", StringComparison.Ordinal);
        var searchEnd = service.IndexOf("GetNoteAsync", searchStart, StringComparison.Ordinal);
        var searchMethod = service.Substring(searchStart, searchEnd - searchStart);
        Assert.DoesNotContain("select nfv.", searchMethod, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"تعذر تحديد ملف الاختبار: {relativePath}");
    }
}
