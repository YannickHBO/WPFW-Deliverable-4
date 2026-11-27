using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PortfolioApi.Auth;
using PortfolioApi.Dtos;
using PortfolioApi.Models;
using PortfolioApi.Tests.Support;

namespace PortfolioApi.Tests.Integration;

/// <summary>
/// The five project routes via HTTP, with the real authorization: reading is public, writing needs
/// Editor or Admin (401 without sign-in, 403 as Gast). Writes check the effect in the database.
/// </summary>
public class ProjectsRoutesTests(PortfolioApiFactory factory) : IClassFixture<PortfolioApiFactory>
{
    private static object ValidBody(string title = "Test project") => new
    {
        title,
        description = "Made by a test.",
        category = "Testing",
        gitHubUrl = "https://github.com/example/test",
        date = "2026-11-27",
        tags = new[] { "xUnit", "C#" },
        inProgress = false
    };

    /// <summary>Puts a project straight into the test database, for tests that change or delete one.</summary>
    private Task<int> ArrangeProjectAsync() => factory.WithDbAsync(async db =>
    {
        var project = new Project
        {
            Title = "Existing project", Description = "Arranged by a test.", Category = "Testing",
            Date = new DateOnly(2020, 1, 1), Tags = ["Old"]
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return project.Id;
    });

    private Task<Project?> FindProjectAsync(int id) =>
        factory.WithDbAsync(db => db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id));

    private Task<int> CountProjectsAsync() => factory.WithDbAsync(db => db.Projects.CountAsync());

    // ---------- GET /api/projects ----------

    [Fact]
    public async Task GetAll_Anonymous_Returns200WithSeededProjectsInProgressFirstThenNewest()
    {
        // Arrange
        var client = factory.CreateBrowserClient();

        // Act
        var response = await client.GetAsync("/api/projects");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var projects = await response.ReadAsync<List<ProjectDto>>();
        var seededIds = projects.Where(p => p.Id <= 7).Select(p => p.Id).ToList();
        // In progress (7, 5, 6) from newest to oldest, then the finished ones from newest to oldest.
        Assert.Equal([7, 5, 6, 4, 3, 2, 1], seededIds);
    }

    // ---------- GET /api/projects/{id} ----------

    [Fact]
    public async Task GetById_ExistingId_Returns200WithThatProject()
    {
        // Arrange
        var client = factory.CreateBrowserClient();

        // Act
        var response = await client.GetAsync("/api/projects/1");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var project = await response.ReadAsync<ProjectDto>();
        Assert.Equal("Movie Database", project.Title);
        Assert.Equal(["JavaScript", "HTML & CSS", "SQL"], project.Tags);
    }

    [Fact]
    public async Task GetById_UnknownId_Returns404()
    {
        // Arrange
        var client = factory.CreateBrowserClient();

        // Act
        var response = await client.GetAsync("/api/projects/99999");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- POST /api/projects ----------

    [Fact]
    public async Task Create_Anonymous_Returns401AndStoresNothing()
    {
        // Arrange
        var client = factory.CreateBrowserClient();
        var countBefore = await CountProjectsAsync();

        // Act
        var response = await client.PostAsJsonAsync("/api/projects", ValidBody());

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(countBefore, await CountProjectsAsync());
    }

    [Fact]
    public async Task Create_AsGuest_Returns403AndStoresNothing()
    {
        // Arrange
        var client = await factory.CreateClientAsync(RoleNames.Guest);
        var countBefore = await CountProjectsAsync();

        // Act
        var response = await client.PostAsJsonAsync("/api/projects", ValidBody());

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(countBefore, await CountProjectsAsync());
    }

    [Theory]
    [InlineData(RoleNames.Editor)]
    [InlineData(RoleNames.Admin)]
    public async Task Create_AsEditorOrAdmin_Returns201AndStoresTheProject(string role)
    {
        // Arrange
        var client = await factory.CreateClientAsync(role);
        var title = $"Created by {role}";

        // Act
        var response = await client.PostAsJsonAsync("/api/projects", ValidBody(title));

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.ReadAsync<ProjectDto>();
        Assert.Equal(title, created.Title);
        Assert.Equal($"/api/projects/{created.Id}", response.Headers.Location?.AbsolutePath);

        var stored = await FindProjectAsync(created.Id);
        Assert.NotNull(stored);
        Assert.Equal(title, stored.Title);
        Assert.Equal(["xUnit", "C#"], stored.Tags);
    }

    [Fact]
    public async Task Create_AsEditorWithInvalidBody_Returns400AndStoresNothing()
    {
        // Arrange
        var client = await factory.CreateClientAsync(RoleNames.Editor);
        var countBefore = await CountProjectsAsync();
        var invalid = new { title = "", description = "x", category = "x", date = "2026-11-27" };

        // Act
        var response = await client.PostAsJsonAsync("/api/projects", invalid);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Title", await response.Content.ReadAsStringAsync());
        Assert.Equal(countBefore, await CountProjectsAsync());
    }

    [Fact]
    public async Task Create_AsEditorWithFourTags_Returns400()
    {
        // Arrange
        var client = await factory.CreateClientAsync(RoleNames.Editor);
        var body = new { title = "Too many tags", description = "x", category = "x", date = "2026-11-27",
            tags = new[] { "a", "b", "c", "d" } };

        // Act
        var response = await client.PostAsJsonAsync("/api/projects", body);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("at most 3 tags", await response.Content.ReadAsStringAsync());
    }

    // ---------- PUT /api/projects/{id} ----------

    [Fact]
    public async Task Update_Anonymous_Returns401AndChangesNothing()
    {
        // Arrange
        var id = await ArrangeProjectAsync();
        var client = factory.CreateBrowserClient();

        // Act
        var response = await client.PutAsJsonAsync($"/api/projects/{id}", ValidBody("Changed"));

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Existing project", (await FindProjectAsync(id))!.Title);
    }

    [Fact]
    public async Task Update_AsGuest_Returns403AndChangesNothing()
    {
        // Arrange
        var id = await ArrangeProjectAsync();
        var client = await factory.CreateClientAsync(RoleNames.Guest);

        // Act
        var response = await client.PutAsJsonAsync($"/api/projects/{id}", ValidBody("Changed"));

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Existing project", (await FindProjectAsync(id))!.Title);
    }

    [Theory]
    [InlineData(RoleNames.Editor)]
    [InlineData(RoleNames.Admin)]
    public async Task Update_AsEditorOrAdmin_Returns200AndStoresTheChanges(string role)
    {
        // Arrange
        var id = await ArrangeProjectAsync();
        var client = await factory.CreateClientAsync(role);

        // Act
        var response = await client.PutAsJsonAsync($"/api/projects/{id}", ValidBody($"Changed by {role}"));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal($"Changed by {role}", (await response.ReadAsync<ProjectDto>()).Title);

        var stored = await FindProjectAsync(id);
        Assert.Equal($"Changed by {role}", stored!.Title);
        Assert.Equal(["xUnit", "C#"], stored.Tags); // Tags are replaced, not added to.
    }

    [Fact]
    public async Task Update_AsEditorUnknownId_Returns404()
    {
        // Arrange
        var client = await factory.CreateClientAsync(RoleNames.Editor);

        // Act
        var response = await client.PutAsJsonAsync("/api/projects/99999", ValidBody());

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_AsEditorWithInvalidBody_Returns400AndChangesNothing()
    {
        // Arrange
        var id = await ArrangeProjectAsync();
        var client = await factory.CreateClientAsync(RoleNames.Editor);
        var invalid = new { title = "Duplicate tags", description = "x", category = "x", date = "2026-11-27",
            tags = new[] { "React", "react" } };

        // Act
        var response = await client.PutAsJsonAsync($"/api/projects/{id}", invalid);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Existing project", (await FindProjectAsync(id))!.Title);
    }

    // ---------- DELETE /api/projects/{id} ----------

    [Fact]
    public async Task Delete_Anonymous_Returns401AndKeepsTheProject()
    {
        // Arrange
        var id = await ArrangeProjectAsync();
        var client = factory.CreateBrowserClient();

        // Act
        var response = await client.DeleteAsync($"/api/projects/{id}");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotNull(await FindProjectAsync(id));
    }

    [Fact]
    public async Task Delete_AsGuest_Returns403AndKeepsTheProject()
    {
        // Arrange
        var id = await ArrangeProjectAsync();
        var client = await factory.CreateClientAsync(RoleNames.Guest);

        // Act
        var response = await client.DeleteAsync($"/api/projects/{id}");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotNull(await FindProjectAsync(id));
    }

    [Theory]
    [InlineData(RoleNames.Editor)]
    [InlineData(RoleNames.Admin)]
    public async Task Delete_AsEditorOrAdmin_Returns204AndRemovesTheProject(string role)
    {
        // Arrange
        var id = await ArrangeProjectAsync();
        var client = await factory.CreateClientAsync(role);

        // Act
        var response = await client.DeleteAsync($"/api/projects/{id}");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(await FindProjectAsync(id));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/projects/{id}")).StatusCode);
    }

    [Fact]
    public async Task Delete_AsEditorUnknownId_Returns404()
    {
        // Arrange
        var client = await factory.CreateClientAsync(RoleNames.Editor);

        // Act
        var response = await client.DeleteAsync("/api/projects/99999");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
