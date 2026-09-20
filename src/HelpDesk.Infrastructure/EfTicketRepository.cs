using HelpDesk.Core;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Infrastructure;

/// <summary>
/// SQLite に保存する実装。
/// <see cref="DbContext"/> を直接注入せず <see cref="IDbContextFactory{TContext}"/> を使うのは、
/// DbContext がスレッド安全ではなく、Blazor では 1 つのインスタンスが長く生きるため。
/// </summary>
public sealed class EfTicketRepository(IDbContextFactory<HelpDeskDbContext> dbContextFactory) : ITicketRepository
{
    public async Task<IReadOnlyList<Ticket>> ListAsync(
        TicketStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // AsNoTracking: 読むだけなので、EF に変更追跡させない（速く、メモリも使わない）
        IQueryable<Ticket> query = db.Tickets.AsNoTracking();

        if (status is not null)
        {
            query = query.Where(ticket => ticket.Status == status);
        }

        return await query
            .OrderByDescending(ticket => ticket.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<Ticket?> FindAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Tickets
            .AsNoTracking()
            .SingleOrDefaultAsync(ticket => ticket.Id == id, cancellationToken);
    }
}
