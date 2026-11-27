using Microsoft.EntityFrameworkCore;
using PortfolioApi.Dtos;
using PortfolioApi.Services;
using PortfolioApi.Tests.Support;

namespace PortfolioApi.Tests.Unit;

/// <summary>BlogPostService on its own, without HTTP. Every test gets a fresh database.</summary>
public sealed class BlogPostServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private static List<BlogPostImageDto> Images(params string[] urls) =>
        urls.Select(url => new BlogPostImageDto(url, $"Alt for {url}")).ToList();

    [Fact]
    public async Task GetAllAsync_SeededPosts_NewestFirst()
    {
        // Arrange
        var service = new BlogPostService(_database.CreateContext());

        // Act
        var posts = await service.GetAllAsync();

        // Assert
        Assert.Equal(Enumerable.Range(1, 20).Reverse(), posts.Select(p => p.Id));
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        // Arrange
        var service = new BlogPostService(_database.CreateContext());

        // Act
        var post = await service.GetByIdAsync(99999);

        // Assert
        Assert.Null(post);
    }

    [Fact]
    public async Task CreateAsync_WithImages_StoresThemInTheGivenOrder()
    {
        // Arrange
        var service = new BlogPostService(_database.CreateContext());
        var dto = new CreateBlogPostDto("Title", "Content", new DateTime(2026, 11, 27), Images("/b.jpg", "/a.jpg"));

        // Act
        var created = await service.CreateAsync(dto);

        // Assert
        var stored = await _database.CreateContext().BlogPosts.Include(b => b.Images).SingleAsync(b => b.Id == created.Id);
        Assert.Equal(["/b.jpg", "/a.jpg"], stored.Images.OrderBy(i => i.SortOrder).Select(i => i.Url));
    }

    [Fact]
    public async Task UpdateAsync_ExistingPost_ReplacesTheImages()
    {
        // Arrange: seed post 12 has two images.
        var service = new BlogPostService(_database.CreateContext());
        var dto = new UpdateBlogPostDto("New title", "New content", new DateTime(2026, 11, 1), Images("/new.jpg"));

        // Act
        var updated = await service.UpdateAsync(12, dto);

        // Assert
        Assert.NotNull(updated);
        var stored = await _database.CreateContext().BlogPosts.Include(b => b.Images).SingleAsync(b => b.Id == 12);
        Assert.Equal("New title", stored.Title);
        Assert.Equal("/new.jpg", Assert.Single(stored.Images).Url);
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_ReturnsNull()
    {
        // Arrange
        var service = new BlogPostService(_database.CreateContext());
        var dto = new UpdateBlogPostDto("x", "x", new DateTime(2026, 1, 1));

        // Act
        var updated = await service.UpdateAsync(99999, dto);

        // Assert
        Assert.Null(updated);
    }

    [Fact]
    public async Task DeleteAsync_PostWithImages_RemovesThePostAndItsImages()
    {
        // Arrange: seed post 1 has nine images.
        var service = new BlogPostService(_database.CreateContext());

        // Act
        var deleted = await service.DeleteAsync(1);

        // Assert
        Assert.True(deleted);
        await using var db = _database.CreateContext();
        Assert.Null(await db.BlogPosts.FindAsync(1));
        Assert.Equal(0, await db.BlogPostImages.CountAsync(i => i.BlogPostId == 1));
    }

    [Fact]
    public async Task DeleteAsync_UnknownId_ReturnsFalse()
    {
        // Arrange
        var service = new BlogPostService(_database.CreateContext());

        // Act
        var deleted = await service.DeleteAsync(99999);

        // Assert
        Assert.False(deleted);
    }
}
