using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HelpDesk.Infrastructure;

/// <summary>
/// DateTimeOffset を「UTC に揃えた固定長の文字列」として保存する。
///
/// SQLite は DateTimeOffset をそのまま並べ替えられない
/// （"SQLite does not support expressions of type 'DateTimeOffset' in ORDER BY clauses."）。
/// UTC に正規化した固定書式の文字列にすると、辞書順＝時刻順になるので
/// ORDER BY がデータベース側で成立する。
///
/// 数値（UtcTicks）でも同じことはできるが、文字列にしておくと
/// sqlite3 で開いたときに人間が読める。
/// </summary>
internal static class UtcTextConverter
{
    private const string Format = "yyyy-MM-ddTHH:mm:ss.fffffffZ";

    public static readonly ValueConverter<DateTimeOffset, string> Instance = new(
        value => value.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture),
        text => DateTimeOffset.ParseExact(
            text,
            Format,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal));
}
