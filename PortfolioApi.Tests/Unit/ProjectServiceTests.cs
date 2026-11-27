using PortfolioApi.Dtos;
using PortfolioApi.Models;
using PortfolioApi.Services;
using PortfolioApi.Tests.Support;

namespace PortfolioApi.Tests.Unit;

/// <summary>
/// ProjectService on its own, without HTTP: the logic behind the five project routes. Every test gets
/// a fresh database (xUnit makes a new instance of this class per test).
/// </summary>
public sealed class ProjectServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private static CreateProjectDto NewProject(List<string>? tags = null) =>
        new("Title", "Description", "Category", null, new DateOnly(2026, 11, 27), tags);

    [Fact]
    public async Task GetAllAsync_SeededProjects_InProgressFirstThenNewestFirst()
    {
        // Arrange
        var service = new ProjectService(_database.CreateContext());

        // Act
        var projects = await service.GetAllAsync();

        // Assert
        Assert.Equal([7, 5, 6, 4, 3, 2, 1], projects.Select(p => p.Id));
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        // Arrange
        var service = new ProjectService(_database.CreateContext());

        // Act
        var project = await service.GetByIdAsync(99999);

        // Assert
        Assert.Null(project);
    }

    [Fact]
    public async Task CreateAsync_ValidDto_StoresProjectWithTrimmedTags()
    {
        // Arrange
        var service = new ProjectService(_database.CreateContext());

        // Act
        var created = await service.CreateAsync(NewProject([" React ", "C#"]));

        // Assert
        var stored = await _database.CreateContext().Projects.FindAsync(created.Id);
        Assert.Equal(["React", "C#"], stored!.Tags);
    }

    [Fact]
    public async Task CreateAsync_WithoutTags_StoresAnEmptyTagList()
    {
        // Arrange: tags are optional.
        var service = new ProjectService(_database.CreateContext());

        // Act
        var created = await service.CreateAsync(NewProject(tags: null));

        // Assert
        Assert.Empty(created.Tags);
        Assert.Empty((await _database.CreateContext().Projects.FindAsync(created.Id))!.Tags);
    }

    [Fact]
    public async Task UpdateAsync_ExistingId_OverwritesAllFields()
    {
        // Arrange
        var service = new ProjectService(_database.CreateContext());
        var update = new UpdateProjectDto("New title", "New description", "New category",
            "https://github.com/example/new", new DateOnly(2025, 1, 1), ["New"], InProgress: true);

        // Act
        var updated = await service.UpdateAsync(1, update);

        // Assert
        Assert.NotNull(updated);
        var stored = await _database.CreateContext().Projects.FindAsync(1);
        Assert.Equal("New title", stored!.Title);
        Assert.Equal("https://github.com/example/new", stored.GitHubUrl);
        Assert.True(stored.InProgress);
        Assert.Equal(["New"], stored.Tags);
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_ReturnsNull()
    {
        // Arrange
        var service = new ProjectService(_database.CreateContext());
        var update = new UpdateProjectDto("x", "x", "x", null, new DateOnly(2026, 1, 1));

        // Act
        var updated = await service.UpdateAsync(99999, update);

        // Assert
        Assert.Null(updated);
    }

    [Fact]
    public async Task DeleteAsync_ExistingId_ReturnsTrueAndRemovesTheProject()
    {
        // Arrange
        var service = new ProjectService(_database.CreateContext());

        // Act
        var deleted = await service.DeleteAsync(1);

        // Assert
        Assert.True(deleted);
        Assert.Null(await _database.CreateContext().Projects.FindAsync(1));
    }

    [Fact]
    public async Task DeleteAsync_UnknownId_ReturnsFalse()
    {
        // Arrange
        var service = new ProjectService(_database.CreateContext());

        // Act
        var deleted = await service.DeleteAsync(99999);

        // Assert
        Assert.False(deleted);
    }
}
