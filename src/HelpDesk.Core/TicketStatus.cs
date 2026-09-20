namespace HelpDesk.Core;

/// <summary>
/// チケットの状態。遷移のルールは <see cref="Ticket"/> のメソッドが持つ。
/// </summary>
public enum TicketStatus
{
    /// <summary>受け付けたが、まだ誰も着手していない。</summary>
    Open,

    /// <summary>担当者が対応中。</summary>
    InProgress,

    /// <summary>対応が終わり、解決コメントが付いている。</summary>
    Resolved,
}
