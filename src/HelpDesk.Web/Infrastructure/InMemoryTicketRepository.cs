using HelpDesk.Core;

namespace HelpDesk.Web.Infrastructure;

/// <summary>
/// メモリ上の <see cref="List{T}"/> にチケットを持つだけの実装。
/// アプリを再起動すると内容は初期状態に戻る。
/// ステージ 2 で EF Core + SQLite の実装に差し替える。
/// </summary>
public sealed class InMemoryTicketRepository : ITicketRepository
{
    private readonly List<Ticket> _tickets = CreateSeedData();

    public Task<IReadOnlyList<Ticket>> ListAsync(
        TicketStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Ticket> result = _tickets
            .Where(ticket => status is null || ticket.Status == status)
            .OrderByDescending(ticket => ticket.CreatedAt)
            .ToArray();

        return Task.FromResult(result);
    }

    public Task<Ticket?> FindAsync(int id, CancellationToken cancellationToken = default)
    {
        var ticket = _tickets.SingleOrDefault(ticket => ticket.Id == id);

        return Task.FromResult(ticket);
    }

    private static List<Ticket> CreateSeedData()
    {
        var jst = TimeSpan.FromHours(9);

        var printer = Ticket.Create(
            id: 1,
            title: "3階のプリンタから印刷できない",
            description: "ジョブを送っても「オフライン」と表示されます。電源は入っています。",
            requesterName: "営業部 山田",
            createdAt: new DateTimeOffset(2026, 9, 14, 9, 12, 0, jst));

        var vpn = Ticket.Create(
            id: 2,
            title: "VPN に接続できない",
            description: "在宅勤務中です。認証は通るのですが、社内システムに到達しません。",
            requesterName: "経理部 佐藤",
            createdAt: new DateTimeOffset(2026, 9, 15, 13, 40, 0, jst));
        vpn.Start();

        var license = Ticket.Create(
            id: 3,
            title: "Office のライセンス認証が切れた",
            description: "起動のたびにライセンスの警告が出ます。",
            requesterName: "総務部 鈴木",
            createdAt: new DateTimeOffset(2026, 9, 16, 10, 5, 0, jst));
        license.Start();
        license.Resolve("ライセンスを再割り当てし、再サインインで解消を確認しました。",
            new DateTimeOffset(2026, 9, 16, 15, 30, 0, jst));

        var monitor = Ticket.Create(
            id: 4,
            title: "モニタの追加を申請したい",
            description: "作業効率のため、デュアルモニタにしたいです。",
            requesterName: "開発部 田中",
            createdAt: new DateTimeOffset(2026, 9, 17, 16, 22, 0, jst));

        var mail = Ticket.Create(
            id: 5,
            title: "共有メールボックスが表示されない",
            description: "先週までは見えていた info@ の共有メールボックスが一覧から消えました。",
            requesterName: "営業部 高橋",
            createdAt: new DateTimeOffset(2026, 9, 18, 8, 55, 0, jst));
        mail.Start();

        return [printer, vpn, license, monitor, mail];
    }
}
