using LocalAsrClient.App.ViewModels;
using LocalAsrClient.Core.Persistence;

namespace LocalAsrClient.App.Tests.ViewModels;

public sealed class HistoryViewModelTests
{
    [Fact]
    public void LoadGroupsEntriesByTodayYesterdayAndEarlier()
    {
        var now = new DateTimeOffset(2026, 7, 22, 15, 0, 0, TimeSpan.FromHours(8));
        var viewModel = new HistoryViewModel();

        viewModel.Load(
        [
            Entry(now.AddHours(-1), "今天较晚"),
            Entry(now.AddHours(-3), "今天较早"),
            Entry(now.AddDays(-1), "昨天"),
            Entry(now.AddDays(-4), "更早")
        ],
        now);

        Assert.Collection(
            viewModel.Groups,
            group =>
            {
                Assert.Equal("今天", group.Title);
                Assert.Equal(["今天较晚", "今天较早"], group.Items.Select(item => item.Text));
            },
            group =>
            {
                Assert.Equal("昨天", group.Title);
                Assert.Equal("昨天", Assert.Single(group.Items).Text);
            },
            group =>
            {
                Assert.Equal("更早", group.Title);
                Assert.Equal("更早", Assert.Single(group.Items).Text);
            });
    }

    [Fact]
    public void LoadOmitsEmptyGroups()
    {
        var now = new DateTimeOffset(2026, 7, 22, 15, 0, 0, TimeSpan.FromHours(8));
        var viewModel = new HistoryViewModel();

        viewModel.Load([Entry(now, "只有今天")], now);

        Assert.Equal("今天", Assert.Single(viewModel.Groups).Title);
    }

    private static TextHistoryEntry Entry(DateTimeOffset createdAt, string text)
    {
        return new TextHistoryEntry(
            Guid.NewGuid(),
            createdAt,
            text,
            text.Length,
            0,
            TimeSpan.Zero,
            TimeSpan.Zero,
            "whisper-server",
            "test-model");
    }
}
