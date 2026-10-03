using App.Application.Common;

namespace App.UnitTests;

public class PagingTests
{
    [Fact]
    public void Defaults_to_page_one_of_twenty()
    {
        var (page, size, errors) = Paging.Parse(null, null);

        Assert.Equal((1, 20), (page, size));
        Assert.Null(errors);
    }

    [Theory]
    [InlineData(1, 101, "pageSize")]
    [InlineData(1, 0, "pageSize")]
    [InlineData(0, 10, "page")]
    public void Rejects_out_of_range_values(int page, int pageSize, string field) =>
        Assert.Contains(field, Paging.Parse(page, pageSize).Errors!.Keys);

    [Fact]
    public void Accepts_the_maximum_page_size() => Assert.Null(Paging.Parse(3, 100).Errors);
}
