# ステージ 1 — 静的 SSR でチケットを読む

> **このステージの差分**
> ```bash
> ./scripts/stage-diff.sh 1          # コードだけ
> ./scripts/stage-diff.sh 1 --files  # 変更ファイル一覧
> ```
> GitHub: [stage-0...stage-1](https://github.com/hata-tomoyuki/ticket-system/compare/stage-0...stage-1)

チケットの一覧と詳細が見えるようになった。**書き込みはまだ一切できない**（起票・着手・解決はステージ 3）。
ボタンを押しても C# は呼ばれない（理由は [なぜ今はボタンを押しても動かないのか](rendermodes.md)）。

## 読む順番

**Core から読むこと。** 画面から読むと「どこに何があるか」を探す旅になる。

1. `Core/TicketStatus.cs`
2. `Core/Ticket.cs` ← **このステージの中心**
3. `Core/ITicketRepository.cs`
4. `Tests/TicketTests.cs` ← `Ticket` の仕様書として読める
5. `Web/Infrastructure/InMemoryTicketRepository.cs`
6. `Web/Program.cs`（差分 1 行）
7. `Web/Components/Pages/Tickets.razor` → `TicketDetail.razor` → `Shared/TicketStatusBadge.razor`

---

# Core 側

## `Ticket` が普通のクラスと違う 3 点

### ① セッターが `private`

```csharp
public TicketStatus Status { get; private set; }
```

外からは読めるが書けない。**`ticket.Status = TicketStatus.Resolved;` はコンパイルエラー**になる。

状態を変える入口が `Start()` / `Resolve()` / `Reopen()` だけになるので、
「解決コメント無しでいきなり解決済みにする」という抜け道が塞がる。

`public set` だったら、ルールを書いたメソッドがあっても、誰かが `.razor` で直接代入すれば終わり。

### ② コンストラクタが `private` で、入口は `Create`

```csharp
public static Ticket Create(int id, string title, ...)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(title);
    ...
    return new Ticket(id, title.Trim(), ...);
}
```

得られるのは「**`Ticket` 型の変数が存在する ＝ 検証を通っている**」という保証。
件名が空の `Ticket` は存在できないので、使う側で毎回 `if (string.IsNullOrEmpty(...))` を書かなくて済む。

### ③ 時刻を引数で受け取る

```csharp
public void Resolve(string comment, DateTimeOffset resolvedAt)
```

`DateTimeOffset.UtcNow` をメソッドの中で呼んでいない。**これは意図的**。

中で現在時刻を取ると、テストがこうなってしまう。

```csharp
Assert.True(ticket.ResolvedAt > DateTimeOffset.UtcNow.AddSeconds(-5));  // 書きたくない
```

外から渡せば、テストは固定値で等値比較できる。
「今何時か」はアプリの外の事情なので、ドメインの中には置かない。

なお `DateTime` ではなく `DateTimeOffset` を使っているのは、
`DateTime` だと「17:00」が**どこの 17 時か**を型として持てないため。

---

## 状態遷移は「確認してから変える」

3 つのメソッドが全部この形になっている。

```csharp
public void Resolve(string comment, DateTimeOffset resolvedAt)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(comment);   // ① 確認
    EnsureStatusIs(TicketStatus.InProgress, "解決");      //

    Status = TicketStatus.Resolved;                       // ② ここから先は必ず最後まで通る
    ResolutionComment = comment.Trim();
    ResolvedAt = resolvedAt;
}
```

順番が逆だと、途中で例外が出たときに「半分だけ変わったチケット」が残る。
`解決に失敗してもチケットの状態は変わらない` のテストがこれを検査している。

---

## `ITicketRepository` — 矢印が逆を向いている場所

インターフェースは **Core**、実装（`InMemoryTicketRepository`）は **Web**。
Core は「チケットを取り出せる何かが要る」とだけ言い、それがメモリか SQLite かは知らない。

```csharp
Task<IReadOnlyList<Ticket>> ListAsync(TicketStatus? status = null, CancellationToken ct = default);
Task<Ticket?> FindAsync(int id, CancellationToken ct = default);
```

**なぜ今から `async` なのか**: メモリを引くだけなので今は不要（実装は `Task.FromResult` で包んでいるだけ）。
それでも `async` なのは、ステージ 2 で EF Core に差し替えるときにインターフェースを変えたくないから。
同期→非同期の変更は呼び出し側すべてに波及するが、逆は波及しない。

**なぜ `IReadOnlyList` か**: `List` を返すと画面側が `.Add()` できてしまう
（`IReadOnlyList` には `Add` が無く `CS1061` になる）。

**`CancellationToken`**: 今は受け取って無視しているが、ステージ 2 で EF Core に渡すと実際に効く。

---

# Web 側

## `Program.cs` の差分は 1 行

```csharp
builder.Services.AddSingleton<ITicketRepository, InMemoryTicketRepository>();
```

**アプリ全体でここだけが実装クラスの名前を知っている。** 画面側はインターフェースしか見ていない。
ステージ 2 の差し替えは、この行の右辺を変えるだけ。

### なぜ `AddSingleton` か

| 登録方法 | インスタンスが作られる単位 |
|---|---|
| `AddSingleton` | アプリ起動から終了まで 1 個 |
| `AddScoped` | 1 リクエストにつき 1 個（※） |
| `AddTransient` | 要求されるたびに毎回新しく |

初期データを持つ `List` をアプリ全体で 1 つにしたいため。
Scoped にすると今は動いてしまうが、ステージ 3 で「チケットを追加」を作った瞬間、
追加した内容が次のリクエストで消える。

> ※ Blazor では「1 リクエスト」が素直な意味にならない。
> 対話モードでは**ブラウザとの接続（circuit）が切れるまで**が 1 スコープになる。
> （[公式ドキュメント](https://learn.microsoft.com/en-us/aspnet/core/blazor/fundamentals/dependency-injection?view=aspnetcore-10.0) の Service lifetime）
> ステージ 4 で実際に踏んで確かめる。

**今の粗さ**: Singleton の `List` は複数リクエストから同時に触っても、読むだけの今は問題にならないが、
書き込みを足すと壊れる。ロックも `ConcurrentBag` も使っていない（ステージ 2 で DB に移して解消）。

---

## 一覧ページ `/tickets`

### クエリ文字列は「外から来る文字列」の境界

```csharp
[SupplyParameterFromQuery(Name = "status")]
public string? StatusQuery { get; set; }
```

`TicketStatus?` で直接受けたくなるが、**enum はクエリ文字列の自動変換に対応していない**。
しかも厄介なことに、ビルドは警告も無く通り、実行時に 500 になる
（`?status=` を付けていない `/tickets` まで落ちる）。

文字列で受けて、自分で変換する。

```csharp
TicketStatus? status = Enum.TryParse<TicketStatus>(StatusQuery, ignoreCase: true, out var parsed)
    ? parsed
    : null;
```

変換に失敗したら `null`（＝全件）。**例外にしていない**のがポイントで、
URL を手で書き換えただけでエラー画面になるのは親切ではない。

ここが「外から来た信用できない文字列を、ドメインの型に変換する境界」。
この変換を Core にやらせないのが、ステージ 0 で話した分け方の実例。

### `OnParametersSetAsync` を使っている理由（今は差が出ない）

| メソッド | 呼ばれるタイミング |
|---|---|
| `OnInitializedAsync` | コンポーネントが作られたとき **1 回だけ** |
| `OnParametersSetAsync` | パラメータが設定されるたび（初回を含む） |

`?status=Open` → `?status=Resolved` と移動したとき、同じコンポーネントが使い回されるなら
`OnInitializedAsync` は再実行されず、一覧が古いままになる。

ただし **今の静的 SSR では `OnInitializedAsync` にしても同じように動く**（書き換えて確認した）。
リクエストごとにコンポーネントが作り直されるため。差が出るのはステージ 4 から。

### リンクは普通の `<a>`

```razor
<a href="/tickets/@ticket.Id">@ticket.Title</a>
```

`@onclick` も `NavigateTo` も使っていない。対話モードが無くてもページ遷移は成立する。

---

## 詳細ページ `/tickets/{Id:int}`

**`:int` はただの飾りではない。** これがあると数値でない URL はこのページに来ない。
外すと `/tickets/abc` が 404 から 500 に変わる（課題 2 で確かめる）。

存在しない ID のときは 404 を返す。

```csharp
ticket = await Repository.FindAsync(Id);

if (ticket is null)
{
    Navigation.NotFound();   // NotFound ページを描画しつつ HTTP 404
}
```

一覧に出ていない ID を直接打たれたときに、200 で空のページを返さないのが大事。

### `is { } comment` という書き方

```razor
@if (ticket.ResolutionComment is { } comment)
```

「null でなければ、その値を `comment` として使う」。`x!` や `x.Value` を書かずに済む。

---

## `TicketStatusBadge` — 初めての自作コンポーネント

`[Parameter]` を付けたプロパティが、外から属性として渡せる値になる。

```razor
<TicketStatusBadge Status="ticket.Status" />
```

`[EditorRequired]` を付けると、渡し忘れたときに `warning RZ2012` が出る。
**警告でありエラーではない**のでビルドは通る。気づける、というだけ。

### 「未対応」という日本語を Core に置かなかった判断

`TicketStatus.Open` を画面で何と呼ぶかは表示の都合（英語版を作れば変わる）。
一方 `TicketStatus.Open` という**概念**は変わらない。変わるものを Web に、変わらないものを Core に。

判断の分かれるところではある。「未対応」が社内の正式な業務用語なら Core に置く判断もあり得る。
ステージ 0 の「迷ったら Web に置く」に従っている。

### `_ =>` を書いてある理由

3 つの値を網羅しているので不要に見えるが、C# の enum は `(TicketStatus)99` のようなキャストを
エラーにしない。網羅したつもりでも漏れるので、既定の枝を残している。

---

## 手を動かす課題

**課題 1 — ルールを迂回してみる**

`TicketDetail.razor` の `@code` に `ticket!.Status = TicketStatus.Resolved;` を足してビルド。
`private set` が何を守っているかを確認する。

**課題 2 — ルート制約を外してみる**

`@page "/tickets/{Id:int}"` から `:int` を消して `/tickets/abc` を開く。
404 が 500 に変わる。**見てほしいのは画面ではなくコンソールのログ**で、どの層で何が起きたかが書いてある。

**課題 3 — 寿命を変えてみる**

`AddSingleton` を `AddScoped` に変えて一覧を何度か読み込む。
**今は見た目が変わらない。** なぜ変わらないのか、ステージ 3 で「チケットを追加」を作ったら何が起きるか。

**課題 4 — テストを 1 つ足す**

「解決済みのチケットを再オープンしてから、もう一度着手できる」を確かめるテストを書く
（`Ticket` 側は変更不要）。

---

## 今のステージで残っている粗さ

| 内容 | 対処 |
|---|---|
| `Id` を `Create` の引数で渡している。採番の責任が曖昧 | ステージ 2（DB が採番） |
| Singleton の `List` がスレッド安全でない | ステージ 2 |
| 再起動すると初期データに戻る | ステージ 2 |
| 依頼者が文字列。誰でも名乗れる | ステージ 5 |
| 一覧にページングが無い。5 件だから成立している | ステージ 8 |

---

## 次のステージ

ステージ 2 で `InMemoryTicketRepository` を EF Core + SQLite に差し替える。

見どころは、**`Ticket.cs` と `.razor` を 1 行も変えずに DB 化が終わる**かどうか。
変える必要があったら、Core と Web の分け方がどこかで間違っていたということ。

> この予想は**半分外れた**。何がどう外れたかは [ステージ 2](stage-2.md) の冒頭に書いてある。

---

<sub>この文書の事実主張は `./scripts/verify-claims.sh` で再検証できます。</sub>
