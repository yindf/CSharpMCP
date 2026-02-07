using Xunit;
using FluentAssertions;

namespace CSharpMcp.Tests;

[Trait("Category", "Unit")]
public class PlaceholderTest
{
    [Fact]
    public void Placeholder_Passes()
    {
        // 此测试仅为验证测试框架正常工作
        true.Should().BeTrue();
    }
}
