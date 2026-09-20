using HelpDesk.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HelpDesk.Infrastructure;

/// <summary>
/// Ticket をどうテーブルへ対応づけるか。
/// この設定は Core からは見えない。Ticket.cs には属性が 1 つも付いていない。
/// </summary>
internal sealed class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("Tickets");

        builder.HasKey(ticket => ticket.Id);
        builder.Property(ticket => ticket.Id).ValueGeneratedOnAdd();

        builder.Property(ticket => ticket.Title).HasMaxLength(200).IsRequired();
        builder.Property(ticket => ticket.Description).HasMaxLength(4000).IsRequired();
        builder.Property(ticket => ticket.RequesterName).HasMaxLength(100).IsRequired();
        // DateTimeOffset をそのまま保存すると SQLite で並べ替えできない。UtcTextConverter を参照。
        builder.Property(ticket => ticket.CreatedAt)
            .HasConversion(UtcTextConverter.Instance)
            .HasMaxLength(28)
            .IsRequired();

        builder.Property(ticket => ticket.ResolvedAt)
            .HasConversion(UtcTextConverter.Instance!)
            .HasMaxLength(28);
        builder.Property(ticket => ticket.ResolutionComment).HasMaxLength(4000);

        // enum を数値ではなく文字列で保存する。
        // 数値だと、後から enum の途中に値を挿入したときに既存データの意味が変わる。
        builder.Property(ticket => ticket.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.HasIndex(ticket => ticket.Status);
        builder.HasIndex(ticket => ticket.CreatedAt);
    }
}
