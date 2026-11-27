namespace PortfolioApi.Models;

/// <summary>EF Core entity. Never exposed directly by the API — see Dtos/BlogPostDtos.cs.</summary>
public class BlogPost
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime PublishedDate { get; set; }

    /// <summary>
    /// The full git commit SHA (40 characters) when this post is a commit from the repository;
    /// null for a post written by hand. Set by the commit sync (and the seed data), never by the API's
    /// create/update routes.
    /// </summary>
    public string? CommitSha { get; set; }

    /// <summary>Optional photos shown with the post (one-to-many).</summary>
    public List<BlogPostImage> Images { get; set; } = [];
}
