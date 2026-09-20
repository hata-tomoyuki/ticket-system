using HelpDesk.Core;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Infrastructure;

/// <summary>
/// 空のデータベースに初期データを入れる。既に何か入っていれば何もしない。
/// EF の HasData ではなく通常の保存で入れているのは、
/// Ticket.Create を通すことでドメインのルールを迂回しないため。
/// </summary>
public static class DatabaseSeeder
{
    public static async Task SeedAsync(HelpDeskDbContext db, CancellationToken cancellationToken = default)
    {
        if (await db.Tickets.AnyAsync(cancellationToken))
        {
            return;
        }

        var jst = TimeSpan.FromHours(9);

        var printer = Ticket.Create(
            "3階のプリンタから印刷できない",
            "ジョブを送っても「オフライン」と表示されます。電源は入っています。",
            "営業部 山田",
            new DateTimeOffset(2026, 9, 14, 9, 12, 0, jst));

        var vpn = Ticket.Create(
            "VPN に接続できない",
            "在宅勤務中です。認証は通るのですが、社内システムに到達しません。",
            "経理部 佐藤",
            new DateTimeOffset(2026, 9, 15, 13, 40, 0, jst));
        vpn.Start();

        var license = Ticket.Create(
            "Office のライセンス認証が切れた",
            "起動のたびにライセンスの警告が出ます。",
            "総務部 鈴木",
            new DateTimeOffset(2026, 9, 16, 10, 5, 0, jst));
        license.Start();
        license.Resolve(
            "ライセンスを再割り当てし、再サインインで解消を確認しました。",
            new DateTimeOffset(2026, 9, 16, 15, 30, 0, jst));

        var monitor = Ticket.Create(
            "モニタの追加を申請したい",
            "作業効率のため、デュアルモニタにしたいです。",
            "開発部 田中",
            new DateTimeOffset(2026, 9, 17, 16, 22, 0, jst));

        var mail = Ticket.Create(
            "共有メールボックスが表示されない",
            "先週までは見えていた info@ の共有メールボックスが一覧から消えました。",
            "営業部 高橋",
            new DateTimeOffset(2026, 9, 18, 8, 55, 0, jst));
        mail.Start();

        db.Tickets.AddRange(printer, vpn, license, monitor, mail);

        await db.SaveChangesAsync(cancellationToken);
    }
}
