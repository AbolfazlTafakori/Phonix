using Phonix.Api.Dtos;
using Xunit;

namespace Phonix.Api.Tests;

public class PagedResultTests
{
    [Fact]
    public void Returns_the_requested_page_slice()
    {
        var all = Enumerable.Range(1, 25).ToList();
        var page = PagedResult<int>.From(all, page: 2, pageSize: 10);

        Assert.Equal(10, page.Items.Count);
        Assert.Equal(11, page.Items[0]);
        Assert.Equal(25, page.Total);
        Assert.Equal(3, page.TotalPages);
        Assert.Equal(2, page.Page);
    }

    [Fact]
    public void Clamps_page_below_one_to_one()
    {
        var page = PagedResult<int>.From(Enumerable.Range(1, 5).ToList(), page: 0, pageSize: 10);
        Assert.Equal(1, page.Page);
        Assert.Equal(5, page.Items.Count);
    }

    [Fact]
    public void Caps_an_excessive_page_size()
    {
        var page = PagedResult<int>.From(Enumerable.Range(1, 500).ToList(), page: 1, pageSize: 9999);
        Assert.Equal(200, page.PageSize);
        Assert.Equal(200, page.Items.Count);
    }

    [Fact]
    public void Last_page_may_be_partial()
    {
        var page = PagedResult<int>.From(Enumerable.Range(1, 25).ToList(), page: 3, pageSize: 10);
        Assert.Equal(5, page.Items.Count);
        Assert.Equal(25, page.Items[^1]);
    }
}
