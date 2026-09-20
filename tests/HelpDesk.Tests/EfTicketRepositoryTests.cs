using HelpDesk.Core;
using HelpDesk.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Tests;

/// <summary>
/// 本物の SQLite を相手にしたテスト。
/// ファイルではなくメモリ上のデータベースを使うので、後片付けが要らず速い。
/// 接続を開いたままにしておかないとデータベースごと消えるのが注意点。
/// </summary>
public sealed class EfTicketRepositoryTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Jst9am = new(2026, 9, 14, 9, 12, 0, TimeSpan.FromHours(9));

    private SqliteConnection connection = null!;
    private TestDbContextFactory factory = null!;

    public async Task InitializeAsync()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<HelpDeskDbContext>()
            .UseSqlite(connection)
            .Options;

        factory = new TestDbContextFactory(options);

        await using var db = factory.CreateDbContext();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await connection.DisposeAsync();

    private EfTicketRepository CreateRepository() => new(factory);

    private async Task<Ticket> SaveAsync(Ticket ticket)
    {
        await using var db = factory.CreateDbContext();
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();
        return ticket;
    }

    [Fact]
    public async Task Id_はデータベースが採番する()
    {
        var ticket = Ticket.Create("プリンタが動かない", "本文", "営業部 山田", Jst9am);
        Assert.Equal(0, ticket.Id);

        await SaveAsync(ticket);

        Assert.True(ticket.Id > 0);
    }

    [Fact]
    public async Task 非公開のセッターを通した状態も保存され復元される()
    {
        var ticket = Ticket.Create("VPN に接続できない", "本文", "経理部 佐藤", Jst9am);
        ticket.Start();
        ticket.Resolve("再接続で解消しました。", Jst9am.AddHours(3));
        await SaveAsync(ticket);

        var loaded = await CreateRepository().FindAsync(ticket.Id);

        Assert.NotNull(loaded);
        Assert.Equal(TicketStatus.Resolved, loaded.Status);
        Assert.Equal("再接続で解消しました。", loaded.ResolutionComment);
    }

    [Fact]
    public async Task DateTimeOffset_は同じ瞬間として往復する()
    {
        var ticket = await SaveAsync(Ticket.Create("往復の確認", "本文", "開発部 田中", Jst9am));

        var loaded = await CreateRepository().FindAsync(ticket.Id);

        Assert.NotNull(loaded);

        // 保存時は +09:00、読み直すと UTC。指している瞬間は同じ。
        Assert.Equal(Jst9am, loaded.CreatedAt);
        Assert.Equal(TimeSpan.Zero, loaded.CreatedAt.Offset);
        Assert.Equal(0, loaded.CreatedAt.Hour);   // 09:12 JST = 00:12 UTC
        Assert.Equal(12, loaded.CreatedAt.Minute);
    }

    [Fact]
    public async Task 一覧は受付日時の降順で返る()
    {
        await SaveAsync(Ticket.Create("古い", "本文", "山田", Jst9am));
        await SaveAsync(Ticket.Create("新しい", "本文", "山田", Jst9am.AddDays(2)));
        await SaveAsync(Ticket.Create("中間", "本文", "山田", Jst9am.AddDays(1)));

        var tickets = await CreateRepository().ListAsync();

        Assert.Equal(["新しい", "中間", "古い"], tickets.Select(ticket => ticket.Title));
    }

    [Fact]
    public async Task 並べ替えはオフセットが違っても瞬間の順になる()
    {
        // 同じ「2026-09-14 00:12 UTC」を、違うオフセットの表記で作る
        var jst = new DateTimeOffset(2026, 9, 14, 9, 12, 0, TimeSpan.FromHours(9));
        var utc = new DateTimeOffset(2026, 9, 14, 0, 13, 0, TimeSpan.Zero);   // 1 分だけ後

        await SaveAsync(Ticket.Create("JST 表記のほうが先", "本文", "山田", jst));
        await SaveAsync(Ticket.Create("UTC 表記のほうが後", "本文", "山田", utc));

        var tickets = await CreateRepository().ListAsync();

        Assert.Equal(["UTC 表記のほうが後", "JST 表記のほうが先"], tickets.Select(ticket => ticket.Title));
    }

    [Fact]
    public async Task 状態で絞り込める()
    {
        await SaveAsync(Ticket.Create("未対応のまま", "本文", "山田", Jst9am));

        var inProgress = Ticket.Create("対応中にする", "本文", "山田", Jst9am);
        inProgress.Start();
        await SaveAsync(inProgress);

        var tickets = await CreateRepository().ListAsync(TicketStatus.InProgress);

        Assert.Equal("対応中にする", Assert.Single(tickets).Title);
    }

    [Fact]
    public async Task 存在しない_Id_は_null()
    {
        Assert.Null(await CreateRepository().FindAsync(999));
    }

    private sealed class TestDbContextFactory(DbContextOptions<HelpDeskDbContext> options)
        : IDbContextFactory<HelpDeskDbContext>
    {
        public HelpDeskDbContext CreateDbContext() => new(options);
    }
}
