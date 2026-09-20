namespace HelpDesk.Web;

/// <summary>
/// 表示用の日時整形。
///
/// データベースには UTC で保存している。「利用者にどのタイムゾーンで見せるか」は
/// 画面の都合なので、ドメインでもデータベースでもなくここに置く。
/// 海外拠点に対応するなら、この 1 ファイルを直せば済む。
/// </summary>
public static class JstDisplay
{
    private static readonly TimeSpan Jst = TimeSpan.FromHours(9);

    public static string ToJst(this DateTimeOffset value) =>
        value.ToOffset(Jst).ToString("yyyy/MM/dd HH:mm");
}
