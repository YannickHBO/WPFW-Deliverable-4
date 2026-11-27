using PortfolioApi.Dtos;
using PortfolioApi.Mappings;
using PortfolioApi.Models;

namespace PortfolioApi.Tests.Unit;

/// <summary>Converting blog posts to and from DTOs, in particular the commit fields.</summary>
public class BlogPostMappingsTests
{
    [Fact]
    public void ToDto_HandWrittenPost_HasNoCommitFields()
    {
        // Arrange
        var post = new BlogPost { Id = 1, Title = "Title", Content = "Content" };

        // Act
        var dto = post.ToDto();

        // Assert
        Assert.Null(dto.CommitSha);
        Assert.Null(dto.CommitUrl);
    }

    [Fact]
    public void ToDto_CommitPost_LinksToTheCommitOnGitHub()
    {
        // Arrange
        const string sha = "4c44f35aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var post = new BlogPost { Id = 1, Title = "Title", Content = "Content", CommitSha = sha };

        // Act
        var dto = post.ToDto();

        // Assert
        Assert.Equal(sha, dto.CommitSha);
        Assert.Equal($"https://github.com/YannickHBO/WPFW-Deliverable-4/commit/{sha}", dto.CommitUrl);
    }

    [Fact]
    public void ToDto_Images_AreInGalleryOrder()
    {
        // Arrange
        var post = new BlogPost
        {
            Images =
            [
                new BlogPostImage { Url = "/second.jpg", Alt = "2", SortOrder = 1 },
                new BlogPostImage { Url = "/first.jpg", Alt = "1", SortOrder = 0 }
            ]
        };

        // Act
        var dto = post.ToDto();

        // Assert
        Assert.Equal(["/first.jpg", "/second.jpg"], dto.Images.Select(i => i.Url));
    }

    [Fact]
    public void ApplyTo_CommitPost_KeepsTheCommitSha()
    {
        // Arrange
        var post = new BlogPost { Title = "Old", Content = "Old", CommitSha = "4c44f35aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" };
        var dto = new UpdateBlogPostDto("New", "New", new DateTime(2026, 11, 27));

        // Act
        dto.ApplyTo(post);

        // Assert
        Assert.Equal("New", post.Title);
        Assert.Equal("4c44f35aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", post.CommitSha);
    }
}
