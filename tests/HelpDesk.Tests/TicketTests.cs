using HelpDesk.Core;

namespace HelpDesk.Tests;

/// <summary>
/// ドメインのルールのテスト。DB も HTTP も出てこないので、準備は 1 行で済む。
/// </summary>
public class TicketTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 14, 9, 0, 0, TimeSpan.FromHours(9));
    private static readonly DateTimeOffset ResolvedAt = new(2026, 9, 14, 17, 0, 0, TimeSpan.FromHours(9));

    private static Ticket CreateTicket() =>
        Ticket.Create(1, "プリンタが動かない", "オフラインと表示されます。", "営業部 山田", CreatedAt);

    [Fact]
    public void 受け付けた直後は未対応()
    {
        var ticket = CreateTicket();

        Assert.Equal(TicketStatus.Open, ticket.Status);
        Assert.Null(ticket.ResolutionComment);
        Assert.Null(ticket.ResolvedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void 件名が空のチケットは作れない(string title)
    {
        Assert.Throws<ArgumentException>(
            () => Ticket.Create(1, title, "本文", "営業部 山田", CreatedAt));
    }

    [Fact]
    public void 件名と依頼者名は前後の空白が取り除かれる()
    {
        var ticket = Ticket.Create(1, "  プリンタが動かない  ", "本文", "  営業部 山田  ", CreatedAt);

        Assert.Equal("プリンタが動かない", ticket.Title);
        Assert.Equal("営業部 山田", ticket.RequesterName);
    }

    [Fact]
    public void 未対応のチケットは着手できる()
    {
        var ticket = CreateTicket();

        ticket.Start();

        Assert.Equal(TicketStatus.InProgress, ticket.Status);
    }

    [Fact]
    public void 対応中のチケットは再度着手できない()
    {
        var ticket = CreateTicket();
        ticket.Start();

        Assert.Throws<InvalidOperationException>(ticket.Start);
    }

    [Fact]
    public void 未対応のチケットはいきなり解決できない()
    {
        var ticket = CreateTicket();

        Assert.Throws<InvalidOperationException>(
            () => ticket.Resolve("直りました", ResolvedAt));
    }

    [Fact]
    public void 解決するとコメントと日時が記録される()
    {
        var ticket = CreateTicket();
        ticket.Start();

        ticket.Resolve("ドライバを再インストールして復旧しました。", ResolvedAt);

        Assert.Equal(TicketStatus.Resolved, ticket.Status);
        Assert.Equal("ドライバを再インストールして復旧しました。", ticket.ResolutionComment);
        Assert.Equal(ResolvedAt, ticket.ResolvedAt);
    }

    [Fact]
    public void 解決コメントが空なら解決できない()
    {
        var ticket = CreateTicket();
        ticket.Start();

        Assert.Throws<ArgumentException>(() => ticket.Resolve("   ", ResolvedAt));
    }

    [Fact]
    public void 解決に失敗してもチケットの状態は変わらない()
    {
        var ticket = CreateTicket();
        ticket.Start();

        Assert.Throws<ArgumentException>(() => ticket.Resolve("   ", ResolvedAt));

        Assert.Equal(TicketStatus.InProgress, ticket.Status);
        Assert.Null(ticket.ResolvedAt);
    }

    [Fact]
    public void 再オープンすると未対応に戻り解決内容は消える()
    {
        var ticket = CreateTicket();
        ticket.Start();
        ticket.Resolve("復旧しました。", ResolvedAt);

        ticket.Reopen();

        Assert.Equal(TicketStatus.Open, ticket.Status);
        Assert.Null(ticket.ResolutionComment);
        Assert.Null(ticket.ResolvedAt);
    }

    [Fact]
    public void 解決済みでないチケットは再オープンできない()
    {
        var ticket = CreateTicket();

        Assert.Throws<InvalidOperationException>(ticket.Reopen);
    }
}
