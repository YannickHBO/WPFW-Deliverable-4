using PortfolioApi.Dtos;
using PortfolioApi.GitHub;
using PortfolioApi.Models;

namespace PortfolioApi.Mappings;

/// <summary>
/// Converts between the BlogPost/BlogPostImage entities and their DTOs. CommitSha is never taken from
/// a create/update DTO: only the commit sync sets it.
/// </summary>
public static class BlogPostMappings
{
    public static BlogPostDto ToDto(this BlogPost post) =>
        new(post.Id, post.Title, post.Content, post.PublishedDate,
            post.Images
                .OrderBy(image => image.SortOrder)
                .Select(image => new BlogPostImageDto(image.Url, image.Alt))
                .ToList(),
            post.CommitSha,
            post.CommitSha is null ? null : GitHubRepository.CommitUrl(post.CommitSha));

    public static BlogPost ToEntity(this CreateBlogPostDto dto) => new()
    {
        Title = dto.Title,
        Content = dto.Content,
        PublishedDate = dto.PublishedDate,
        Images = ToImageEntities(dto.Images)
    };

    /// <remarks>The post's images must be loaded (Include) so the old ones can be replaced.</remarks>
    public static void ApplyTo(this UpdateBlogPostDto dto, BlogPost post)
    {
        post.Title = dto.Title;
        post.Content = dto.Content;
        post.PublishedDate = dto.PublishedDate;
        post.Images.Clear();
        post.Images.AddRange(ToImageEntities(dto.Images));
    }

    private static List<BlogPostImage> ToImageEntities(IEnumerable<BlogPostImageDto>? images) =>
        (images ?? [])
            .Select((image, index) => new BlogPostImage { Url = image.Url, Alt = image.Alt, SortOrder = index })
            .ToList();
}
