using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PortfolioApi.Auth;
using PortfolioApi.Dtos;
using PortfolioApi.Models;
using PortfolioApi.Tests.Support;

namespace PortfolioApi.Tests.Integration;

/// <summary>
/// The five blog post routes via HTTP, with the real authorization: reading is public, writing needs
/// Editor or Admin (401 without sign-in, 403 as Gast). Writes check the effect in the database,
/// including the images (replaced on PUT, removed with the post on DELETE).
/// </summary>
public class BlogPostsRoutesTests(PortfolioApiFactory factory) : IClassFixture<PortfolioApiFactory>
{
    private const string SeededCommitSha = "f7353fb131697e83aec75eb39167cd3490fb436d"; // Blog Post 13

    private static object ValidBody(string title = "Test post", int imageCount = 1) => new
    {
        title,
        content = "Written by a test.",
        publishedDate = "2026-11-27T12:00:00",
        images = Enumerable.Range(1, imageCount)
            .Select(i => new { url = $"/images/blog/test-{i}.jpg", alt = $"Test image {i}" })
            .ToArray()
    };

    /// <summary>Puts a post with two images straight into the test database.</summary>
    private Task<int> ArrangePostAsync(string? commitSha = null) => factory.WithDbAsync(async db =>
    {
        var post = new BlogPost
        {
            Title = "Existing post", Content = "Arranged by a test.", PublishedDate = new DateTime(2020, 1, 1),
            CommitSha = commitSha,
            Images =
            [
                new BlogPostImage { Url = "/images/blog/old-1.jpg", Alt = "Old 1", SortOrder = 0 },
                new BlogPostImage { Url = "/images/blog/old-2.jpg", Alt = "Old 2", SortOrder = 1 }
            ]
        };
        db.BlogPosts.Add(post);
        await db.SaveChangesAsync();
        return post.Id;
    });

    private Task<BlogPost?> FindPostAsync(int id) => factory.WithDbAsync(db =>
        db.BlogPosts.AsNoTracking().Include(b => b.Images).FirstOrDefaultAsync(b => b.Id == id));

    private Task<int> CountPostsAsync() => factory.WithDbAsync(db => db.BlogPosts.CountAsync());

    // ---------- GET /api/blogposts ----------

    [Fact]
    public async Task GetAll_Anonymous_Returns200WithSeededPostsNewestFirst()
    {
        // Arrange
        var client = factory.CreateBrowserClient();

        // Act
        var response = await client.GetAsync("/api/blogposts");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var posts = await response.ReadAsync<List<BlogPostDto>>();
        var seededIds = posts.Where(p => p.Id <= 20).Select(p => p.Id).ToList();
        // The seed posts are numbered in chronological order, so newest first = highest number first.
        Assert.Equal(Enumerable.Range(1, 20).Reverse(), seededIds);
    }

    // ---------- GET /api/blogposts/{id} ----------

    [Fact]
    public async Task GetById_HandWrittenPostWithGallery_Returns200WithImagesInOrderAndNoCommit()
    {
        // Arrange
        var client = factory.CreateBrowserClient();

        // Act
        var response = await client.GetAsync("/api/blogposts/1");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var post = await response.ReadAsync<BlogPostDto>();
        Assert.Equal(9, post.Images.Count);
        Assert.Equal("/images/blog/fuji-2026-01-sengen-torii.jpg", post.Images[0].Url);
        Assert.Null(post.CommitSha);
        Assert.Null(post.CommitUrl);
    }

    [Fact]
    public async Task GetById_CommitPost_Returns200WithCommitShaAndGitHubLink()
    {
        // Arrange
        var client = factory.CreateBrowserClient();

        // Act
        var response = await client.GetAsync("/api/blogposts/13");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var post = await response.ReadAsync<BlogPostDto>();
        Assert.Equal(SeededCommitSha, post.CommitSha);
        Assert.Equal($"https://github.com/YannickHBO/WPFW-Deliverable-4/commit/{SeededCommitSha}", post.CommitUrl);
    }

    [Fact]
    public async Task GetById_UnknownId_Returns404()
    {
        // Arrange
        var client = factory.CreateBrowserClient();

        // Act
        var response = await client.GetAsync("/api/blogposts/99999");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- POST /api/blogposts ----------

    [Fact]
    public async Task Create_Anonymous_Returns401AndStoresNothing()
    {
        // Arrange
        var client = factory.CreateBrowserClient();
        var countBefore = await CountPostsAsync();

        // Act
        var response = await client.PostAsJsonAsync("/api/blogposts", ValidBody());

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(countBefore, await CountPostsAsync());
    }

    [Fact]
    public async Task Create_AsGuest_Returns403AndStoresNothing()
    {
        // Arrange
        var client = await factory.CreateClientAsync(RoleNames.Guest);
        var countBefore = await CountPostsAsync();

        // Act
        var response = await client.PostAsJsonAsync("/api/blogposts", ValidBody());

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(countBefore, await CountPostsAsync());
    }

    [Theory]
    [InlineData(RoleNames.Editor)]
    [InlineData(RoleNames.Admin)]
    public async Task Create_AsEditorOrAdmin_Returns201AndStoresThePostWithImages(string role)
    {
        // Arrange
        var client = await factory.CreateClientAsync(role);
        var title = $"Written by {role}";

        // Act
        var response = await client.PostAsJsonAsync("/api/blogposts", ValidBody(title, imageCount: 2));

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.ReadAsync<BlogPostDto>();
        Assert.Equal($"/api/blogposts/{created.Id}", response.Headers.Location?.AbsolutePath);
        Assert.Null(created.CommitSha); // A post written via the API is never a commit post.

        var stored = await FindPostAsync(created.Id);
        Assert.Equal(title, stored!.Title);
        Assert.Equal(["/images/blog/test-1.jpg", "/images/blog/test-2.jpg"],
            stored.Images.OrderBy(i => i.SortOrder).Select(i => i.Url));
    }

    [Fact]
    public async Task Create_AsEditorWithCommitShaInBody_IgnoresIt()
    {
        // Arrange: someone tries to make a hand-written post look like a commit (overposting).
        var client = await factory.CreateClientAsync(RoleNames.Editor);
        var body = new { title = "Fake commit", content = "x", publishedDate = "2026-11-27T12:00:00",
            commitSha = "0000000000000000000000000000000000000000" };

        // Act
        var response = await client.PostAsJsonAsync("/api/blogposts", body);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.ReadAsync<BlogPostDto>();
        Assert.Null((await FindPostAsync(created.Id))!.CommitSha);
    }

    [Fact]
    public async Task Create_AsEditorWithInvalidBody_Returns400AndStoresNothing()
    {
        // Arrange
        var client = await factory.CreateClientAsync(RoleNames.Editor);
        var countBefore = await CountPostsAsync();
        var invalid = new { title = "No content", content = "", publishedDate = "2026-11-27T12:00:00" };

        // Act
        var response = await client.PostAsJsonAsync("/api/blogposts", invalid);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Content", await response.Content.ReadAsStringAsync());
        Assert.Equal(countBefore, await CountPostsAsync());
    }

    [Fact]
    public async Task Create_AsEditorWithElevenImages_Returns400()
    {
        // Arrange
        var client = await factory.CreateClientAsync(RoleNames.Editor);

        // Act
        var response = await client.PostAsJsonAsync("/api/blogposts", ValidBody(imageCount: 11));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Images", await response.Content.ReadAsStringAsync());
    }

    // ---------- PUT /api/blogposts/{id} ----------

    [Fact]
    public async Task Update_Anonymous_Returns401AndChangesNothing()
    {
        // Arrange
        var id = await ArrangePostAsync();
        var client = factory.CreateBrowserClient();

        // Act
        var response = await client.PutAsJsonAsync($"/api/blogposts/{id}", ValidBody("Changed"));

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Existing post", (await FindPostAsync(id))!.Title);
    }

    [Fact]
    public async Task Update_AsGuest_Returns403AndChangesNothing()
    {
        // Arrange
        var id = await ArrangePostAsync();
        var client = await factory.CreateClientAsync(RoleNames.Guest);

        // Act
        var response = await client.PutAsJsonAsync($"/api/blogposts/{id}", ValidBody("Changed"));

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Existing post", (await FindPostAsync(id))!.Title);
    }

    [Theory]
    [InlineData(RoleNames.Editor)]
    [InlineData(RoleNames.Admin)]
    public async Task Update_AsEditorOrAdmin_Returns200AndReplacesTextAndImages(string role)
    {
        // Arrange
        var id = await ArrangePostAsync();
        var client = await factory.CreateClientAsync(role);

        // Act
        var response = await client.PutAsJsonAsync($"/api/blogposts/{id}", ValidBody($"Changed by {role}", imageCount: 1));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = await FindPostAsync(id);
        Assert.Equal($"Changed by {role}", stored!.Title);
        var image = Assert.Single(stored.Images); // The two old images are gone, the new one is there.
        Assert.Equal("/images/blog/test-1.jpg", image.Url);
    }

    [Fact]
    public async Task Update_AsEditorOnCommitPost_KeepsTheCommitSha()
    {
        // Arrange
        var sha = Guid.NewGuid().ToString("N") + "00000000";
        var id = await ArrangePostAsync(commitSha: sha);
        var client = await factory.CreateClientAsync(RoleNames.Editor);

        // Act
        var response = await client.PutAsJsonAsync($"/api/blogposts/{id}", ValidBody("Better commit text"));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = await FindPostAsync(id);
        Assert.Equal("Better commit text", stored!.Title);
        Assert.Equal(sha, stored.CommitSha);
    }

    [Fact]
    public async Task Update_AsEditorUnknownId_Returns404()
    {
        // Arrange
        var client = await factory.CreateClientAsync(RoleNames.Editor);

        // Act
        var response = await client.PutAsJsonAsync("/api/blogposts/99999", ValidBody());

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_AsEditorWithInvalidBody_Returns400AndChangesNothing()
    {
        // Arrange
        var id = await ArrangePostAsync();
        var client = await factory.CreateClientAsync(RoleNames.Editor);
        var invalid = new { title = new string('x', 151), content = "x", publishedDate = "2026-11-27T12:00:00" };

        // Act
        var response = await client.PutAsJsonAsync($"/api/blogposts/{id}", invalid);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Existing post", (await FindPostAsync(id))!.Title);
    }

    // ---------- DELETE /api/blogposts/{id} ----------

    [Fact]
    public async Task Delete_Anonymous_Returns401AndKeepsThePost()
    {
        // Arrange
        var id = await ArrangePostAsync();
        var client = factory.CreateBrowserClient();

        // Act
        var response = await client.DeleteAsync($"/api/blogposts/{id}");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotNull(await FindPostAsync(id));
    }

    [Fact]
    public async Task Delete_AsGuest_Returns403AndKeepsThePost()
    {
        // Arrange
        var id = await ArrangePostAsync();
        var client = await factory.CreateClientAsync(RoleNames.Guest);

        // Act
        var response = await client.DeleteAsync($"/api/blogposts/{id}");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotNull(await FindPostAsync(id));
    }

    [Theory]
    [InlineData(RoleNames.Editor)]
    [InlineData(RoleNames.Admin)]
    public async Task Delete_AsEditorOrAdmin_Returns204AndRemovesThePostAndItsImages(string role)
    {
        // Arrange
        var id = await ArrangePostAsync();
        var client = await factory.CreateClientAsync(role);

        // Act
        var response = await client.DeleteAsync($"/api/blogposts/{id}");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(await FindPostAsync(id));
        Assert.Equal(0, await factory.WithDbAsync(db => db.BlogPostImages.CountAsync(i => i.BlogPostId == id)));
    }

    [Fact]
    public async Task Delete_AsEditorUnknownId_Returns404()
    {
        // Arrange
        var client = await factory.CreateClientAsync(RoleNames.Editor);

        // Act
        var response = await client.DeleteAsync("/api/blogposts/99999");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
