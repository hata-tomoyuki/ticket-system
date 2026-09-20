namespace HelpDesk.Core;

/// <summary>
/// チケットの保管庫。「保存して取り出せる何か」が要る、という要求だけを表し、
/// それがメモリなのか DB なのかは決めない。実装は HelpDesk.Web 側にある。
/// </summary>
public interface ITicketRepository
{
    /// <param name="status">null なら全件。指定すればその状態のものだけ。</param>
    Task<IReadOnlyList<Ticket>> ListAsync(TicketStatus? status = null, CancellationToken cancellationToken = default);

    /// <returns>見つからなければ null。</returns>
    Task<Ticket?> FindAsync(int id, CancellationToken cancellationToken = default);
}
