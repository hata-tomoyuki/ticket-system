using HelpDesk.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HelpDesk.Infrastructure;

/// <summary>
/// Ticket をどうテーブルへ対応づけるか。
///
/// 対応づけを「外から」与えているのがポイント。
/// Ticket.cs には [Key] も [MaxLength] も付けていない。
/// 属性を付けると Core が EF Core を参照することになり、
/// ステージ 0 で立てた「Core は何にも依存しない」という壁が崩れる。
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
        builder.Property(ticket => ticket.CreatedAt).IsRequired();
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
