namespace HelpDesk.Core;

/// <summary>
/// 問い合わせ 1 件。状態を変える手段はこのクラスのメソッドだけで、
/// 外から <see cref="Status"/> を直接書き換えることはできない。
/// </summary>
public sealed class Ticket
{
    private Ticket(int id, string title, string description, string requesterName, DateTimeOffset createdAt)
    {
        Id = id;
        Title = title;
        Description = description;
        RequesterName = requesterName;
        Status = TicketStatus.Open;
        CreatedAt = createdAt;
    }

    public int Id { get; private set; }

    public string Title { get; private set; }

    public string Description { get; private set; }

    /// <summary>問い合わせをした人。ステージ 5 でログインユーザーに置き換える。</summary>
    public string RequesterName { get; private set; }

    public TicketStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary><see cref="TicketStatus.Resolved"/> のときだけ値が入る。</summary>
    public string? ResolutionComment { get; private set; }

    /// <summary><see cref="TicketStatus.Resolved"/> のときだけ値が入る。</summary>
    public DateTimeOffset? ResolvedAt { get; private set; }

    /// <summary>
    /// 新しいチケットを受け付ける。作られた直後は必ず <see cref="TicketStatus.Open"/>。
    /// </summary>
    public static Ticket Create(int id, string title, string description, string requesterName, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(requesterName);

        return new Ticket(id, title.Trim(), description.Trim(), requesterName.Trim(), createdAt);
    }

    /// <summary>対応を開始する。未対応のチケットだけが着手できる。</summary>
    public void Start()
    {
        EnsureStatusIs(TicketStatus.Open, "着手");

        Status = TicketStatus.InProgress;
    }

    /// <summary>
    /// 対応を完了する。対応中のチケットだけが解決でき、解決コメントは必須。
    /// </summary>
    public void Resolve(string comment, DateTimeOffset resolvedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(comment);
        EnsureStatusIs(TicketStatus.InProgress, "解決");

        Status = TicketStatus.Resolved;
        ResolutionComment = comment.Trim();
        ResolvedAt = resolvedAt;
    }

    /// <summary>
    /// 解決済みのチケットを未対応に戻す。前回の解決内容は破棄する。
    /// </summary>
    public void Reopen()
    {
        EnsureStatusIs(TicketStatus.Resolved, "再オープン");

        Status = TicketStatus.Open;
        ResolutionComment = null;
        ResolvedAt = null;
    }

    private void EnsureStatusIs(TicketStatus expected, string operationName)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException(
                $"{operationName}できるのは {expected} のチケットだけです。（現在: {Status}）");
        }
    }
}
