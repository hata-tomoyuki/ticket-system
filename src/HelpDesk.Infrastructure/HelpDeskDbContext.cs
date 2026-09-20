using HelpDesk.Core;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Infrastructure;

/// <summary>
/// データベースとの接続。テーブルの構成は <see cref="TicketConfiguration"/> が持つ。
/// </summary>
public sealed class HelpDeskDbContext(DbContextOptions<HelpDeskDbContext> options) : DbContext(options)
{
    public DbSet<Ticket> Tickets => Set<Ticket>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HelpDeskDbContext).Assembly);
    }
}
