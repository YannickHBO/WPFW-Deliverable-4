using PortfolioApi.Dtos;

namespace PortfolioApi.Tests.Unit;

/// <summary>The business rules for project tags: at most 3, each 1–30 characters, no duplicates.</summary>
public class ProjectTagRulesTests
{
    [Theory]
    [InlineData("")]
    [InlineData("React")]
    [InlineData("React,C#,SQL")]
    public void Validate_ValidTags_GivesNoErrors(string commaSeparatedTags)
    {
        // Arrange
        var list = commaSeparatedTags.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

        // Act
        var errors = ProjectTagRules.Validate(list).ToList();

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_NoTags_GivesNoErrors()
    {
        // Arrange
        List<string>? tags = null;

        // Act
        var errors = ProjectTagRules.Validate(tags).ToList();

        // Assert
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(new[] { "a", "b", "c", "d" }, "at most 3 tags")]
    [InlineData(new[] { "" }, "1 to 30 characters")]
    [InlineData(new[] { "   " }, "1 to 30 characters")]
    [InlineData(new[] { "1234567890123456789012345678901" }, "1 to 30 characters")]
    [InlineData(new[] { "React", " react " }, "unique")]
    public void Validate_InvalidTags_GivesTheMatchingError(string[] tags, string expectedMessage)
    {
        // Arrange
        var list = tags.ToList();

        // Act
        var error = Assert.Single(ProjectTagRules.Validate(list));

        // Assert
        Assert.Contains(expectedMessage, error.ErrorMessage);
        Assert.Equal(["Tags"], error.MemberNames);
    }
}
