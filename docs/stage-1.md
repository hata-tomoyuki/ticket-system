# ステージ 1 — 静的 SSR でチケットを読む

チケットの一覧と詳細が見えるようになった。**まだ書き込みは一切できない**（追加・着手・解決はステージ 3）。
対話レンダリングも入れていないので、ブラウザ上で C# は動いていない。

## 足したファイル

```
src/HelpDesk.Core/
  TicketStatus.cs                       状態の enum
  Ticket.cs                             チケット本体と状態遷移のルール
  ITicketRepository.cs                  保管庫のインターフェース

src/HelpDesk.Web/
  Infrastructure/InMemoryTicketRepository.cs   メモリ上の実装＋初期データ
  Components/Shared/TicketStatusBadge.razor    状態バッジ
  Components/Pages/Tickets.razor               一覧 /tickets
  Components/Pages/TicketDetail.razor          詳細 /tickets/{Id}
  Program.cs                                   DI 登録を 1 行追加

tests/HelpDesk.Tests/
  TicketTests.cs                        ドメインのルールのテスト（12 件）
```

## 読む順番

**Core から読むこと。** 画面から読むと「どこに何があるか」を探す旅になる。

1. `TicketStatus.cs`
2. `Ticket.cs` ← このステージの中心
3. `ITicketRepository.cs`
4. `tests/HelpDesk.Tests/TicketTests.cs` ← `Ticket` の仕様書として読める
5. `Infrastructure/InMemoryTicketRepository.cs`
6. `Program.cs`（差分 1 行）
7. `Components/Pages/Tickets.razor`
8. `Components/Pages/TicketDetail.razor`
9. `Components/Shared/TicketStatusBadge.razor`

---

# Core 側

## `Ticket` が普通のクラスと違う 3 点

### ① セッターが `private`

```csharp
public TicketStatus Status { get; private set; }
```

外からは読めるが書けない。つまり **`ticket.Status = TicketStatus.Resolved;` はコンパイルエラー**になる。

状態を変える唯一の入口が `Start()` / `Resolve()` / `Reopen()` になるので、
「解決コメント無しでいきなり解決済みにする」といった抜け道が塞がる。

もし `public set` にしていたら、ルールを書いたメソッドがあっても、誰かが `.razor` の中で直接代入すれば終わりです。

### ② コンストラクタが `private` で、入口は `Create`

```csharp
private Ticket(int id, string title, ...) { ... }

public static Ticket Create(int id, string title, string description, string requesterName, DateTimeOffset createdAt)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(title);
    ArgumentNullException.ThrowIfNull(description);
    ArgumentException.ThrowIfNullOrWhiteSpace(requesterName);

    return new Ticket(id, title.Trim(), description.Trim(), requesterName.Trim(), createdAt);
}
```

得られるのは「**`Ticket` 型の変数が存在する ＝ 検証を通っている**」という保証。
件名が空の `Ticket` はこの世に存在できないので、使う側で毎回 `if (string.IsNullOrEmpty(ticket.Title))` を書かなくて済む。

`ArgumentException.ThrowIfNullOrWhiteSpace` は .NET 8 で入ったヘルパで、
`if (...) throw new ArgumentException(...)` を 1 行にしたもの。引数名も自動で入る。

### ③ 時刻を引数で受け取る

```csharp
public void Resolve(string comment, DateTimeOffset resolvedAt)
```

`DateTimeOffset.UtcNow` をこのメソッドの中で呼んでいない。**これは意図的**。

中で現在時刻を取ると、「解決日時が正しく入るか」をテストする手段が無くなる。
テストを実行した瞬間の時刻としか比較できず、結局こういう情けないテストになります。

```csharp
Assert.True(ticket.ResolvedAt > DateTimeOffset.UtcNow.AddSeconds(-5));  // 書きたくない
```

外から渡す形なら、テストは固定値を渡して等値比較できる。

```csharp
private static readonly DateTimeOffset ResolvedAt = new(2026, 9, 14, 17, 0, 0, TimeSpan.FromHours(9));
...
Assert.Equal(ResolvedAt, ticket.ResolvedAt);
```

「今何時か」はアプリの外の事情なので、ドメインの中には置かない。
ステージ 3 で画面から解決するときは、Web 側が `TimeProvider` を使って現在時刻を渡します。

### `DateTime` ではなく `DateTimeOffset`

`DateTime` は「2026/09/14 17:00」が**どこの 17 時なのか**を持っていません（`Kind` は付いていても保存・転送で落ちる）。
`DateTimeOffset` は `+09:00` を一緒に持つので、この曖昧さが無い。日時を保存するなら基本こちら。

---

## 状態遷移の書き方

3 つのメソッドが全部この形をしています。

```csharp
public void Start()
{
    EnsureStatusIs(TicketStatus.Open, "着手");   // ① 前提の確認

    Status = TicketStatus.InProgress;            // ② 状態の変更
}
```

**確認を全部済ませてから、状態を変える。** 順番が逆だと、途中で例外が出たときに
「半分だけ変わったチケット」が残ります。

`Resolve` はこれを意識して書いてあります。

```csharp
public void Resolve(string comment, DateTimeOffset resolvedAt)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(comment);   // ① 引数の確認
    EnsureStatusIs(TicketStatus.InProgress, "解決");      //   状態の確認

    Status = TicketStatus.Resolved;                       // ② ここから先は必ず最後まで通る
    ResolutionComment = comment.Trim();
    ResolvedAt = resolvedAt;
}
```

これを検査しているのが `解決に失敗してもチケットの状態は変わらない` のテストです。
コメントが空で例外が出たあと、`Status` が `InProgress` のままであることを確認しています。

---

## `ITicketRepository` — 矢印が逆を向いている場所

```csharp
// src/HelpDesk.Core/ITicketRepository.cs
public interface ITicketRepository
{
    Task<IReadOnlyList<Ticket>> ListAsync(TicketStatus? status = null, CancellationToken cancellationToken = default);
    Task<Ticket?> FindAsync(int id, CancellationToken cancellationToken = default);
}
```

インターフェースは Core、実装（`InMemoryTicketRepository`）は Web。
Core は「チケットを取り出せる何かが要る」とだけ言って、それがメモリなのか SQLite なのかは知らない。

### なぜ今から `async` なのか

メモリ上の `List` を引くだけなので、今は非同期である必要が全く無い。
実装も `Task.FromResult(...)` で包んでいるだけです。

それでも `async` にしてあるのは、**ステージ 2 で EF Core に差し替えるときにインターフェースを変えたくない**から。
同期で始めると、DB 化のときに `ITicketRepository` も `.razor` も全部書き換えることになります。

`Task` を返さない形から返す形への変更は、呼び出し側に波及します。逆は波及しません。

### `IReadOnlyList<Ticket>` を返す理由

`List<Ticket>` を返すと、受け取った画面側が `.Add()` できてしまう。
保管庫から借りてきたリストに画面が要素を足す、という意味の分からないコードが書けなくなります。

### `CancellationToken`

ブラウザを閉じられた・リロードされた、といったときに「もう要らない処理」を止めるための仕組み。
今の実装は受け取って無視していますが、ステージ 2 で EF Core に渡すと実際に効きます。

---

# Web 側

## `Program.cs` の差分は 1 行

```csharp
builder.Services.AddSingleton<ITicketRepository, InMemoryTicketRepository>();
```

「`ITicketRepository` を頼まれたら `InMemoryTicketRepository` を渡す」という対応表への登録。
**アプリ全体でここだけが実装クラスの名前を知っています。** 画面側はインターフェースしか見ていない。

ステージ 2 でやる差し替えは、この行の右辺を変えるだけです。

### `AddSingleton` にした理由と、その危うさ

DI の寿命は主に 3 つ。

| 登録方法 | インスタンスが作られる単位 |
|---|---|
| `AddSingleton` | アプリ起動から終了まで 1 個 |
| `AddScoped` | 1 リクエストにつき 1 個（※） |
| `AddTransient` | 要求されるたびに毎回新しく |

今回 Singleton にしたのは、**初期データを持つ `List` をアプリ全体で 1 つにしたい**から。
Scoped にするとリクエストのたびに初期データが作り直され、それはそれで今は動いてしまいますが、
ステージ 3 で「新しいチケットを追加」を作った瞬間、追加した内容が次のリクエストで消えます。

> ※ 表の `AddScoped` に付けた注は重要です。Blazor では「1 リクエスト」が素直な意味にならない場面があります。
> 静的 SSR の今は HTTP リクエスト単位ですが、ステージ 4 で対話モードにすると **ブラウザとの接続が切れるまで**が 1 スコープになります。
> ここはステージ 4 で実際に踏んで確かめます。

**今の実装の粗さ**（ステージ 2 で解消）: Singleton の `List<Ticket>` を複数リクエストから同時に触るのは、
読むだけの今は問題になりませんが、書き込みを足すと壊れます。ロックも `ConcurrentBag` も使っていません。

---

## 一覧ページ `/tickets`

### URL の `?status=...` を受け取る

```csharp
[SupplyParameterFromQuery(Name = "status")]
public string? StatusQuery { get; set; }
```

`/tickets?status=Open` の `Open` が `StatusQuery` に入ります。JavaScript も `HttpContext` も出てきません。

### なぜ `TicketStatus?` で直接受けないのか

```csharp
// こうは書かなかった
[SupplyParameterFromQuery] public TicketStatus? Status { get; set; }
```

理由は 2 つ。

**1. enum はクエリ文字列の自動変換に対応していない。**

これは実際に書き換えて確認しました。厄介なことに **ビルドは警告も無く通ります**。
壊れるのは実行時で、しかも `?status=` を付けていない `/tickets` まで 500 になります。

```
System.InvalidOperationException:
  Querystring values cannot be parsed as type 'System.Nullable`1[HelpDesk.Core.TicketStatus]'.
```

| URL | enum で直接受けた場合 |
|---|---|
| `/tickets` | 500 |
| `/tickets?status=Open` | 500 |
| `/tickets?status=xyz` | 500 |

**2. そもそも変換できない値が来るのが普通だから。** `?status=xyz` は誰でも打ち込めます。

文字列で受けて、自分で変換する。

```csharp
TicketStatus? status = Enum.TryParse<TicketStatus>(StatusQuery, ignoreCase: true, out var parsed)
    ? parsed
    : null;
```

変換に失敗したら `null`（＝全件）にしています。**例外にしていない**のがポイントで、
URL を手で書き換えただけでエラー画面になるのは親切ではない。

ここは「外から来た信用できない文字列を、ドメインの型に変換する境界」です。
この変換を Core の中でやらせない、というのがステージ 0 で話した分け方の実例になっています。

### `OnParametersSetAsync` を使っている理由（今は差が出ない）

```csharp
protected override async Task OnParametersSetAsync()
```

ライフサイクルメソッドの違いはこうです。

| メソッド | 呼ばれるタイミング |
|---|---|
| `OnInitializedAsync` | コンポーネントが作られたとき **1 回だけ** |
| `OnParametersSetAsync` | パラメータが設定されるたび（初回を含む） |

`?status=Open` → `?status=Resolved` と移動したとき、**同じコンポーネントが使い回される**なら
`OnInitializedAsync` は再実行されず、一覧が古いままになります。

ただし **正直に書くと、今の静的 SSR では `OnInitializedAsync` にしても同じように動きます**。
リクエストごとにコンポーネントが作り直されるためです。実際に書き換えて確認しました。

```
OnInitializedAsync 版:
  /tickets              -> 5 件
  /tickets?status=Open  -> 2 件
  /tickets?status=Resolved -> 1 件
```

差が出るのはステージ 4 で対話レンダリングを入れてからです。
今のうちに正しいほうを選んでおくと、ステージ 4 で「なぜか一覧が更新されない」に遭わずに済みます。
（遭ってみたい場合は、ステージ 4 のときに一度 `OnInitializedAsync` に戻してみてください）

### 一覧のリンクは普通の `<a>`

```razor
<a href="/tickets/@ticket.Id">@ticket.Title</a>
```

`@onclick` も `NavigationManager.NavigateTo` も使っていません。ただの HTML リンクです。
対話モードが無くてもページ遷移は成立する、という当たり前を確認しておいてください。

---

## 詳細ページ `/tickets/{Id:int}`

### ルート制約 `:int`

```razor
@page "/tickets/{Id:int}"
```

`{Id}` だけでなく `:int` を付けると、**数値でない URL はそもそもこのページに来ません**。

実測すると、`/tickets/abc` は 404 を返します。`:int` が無ければこのページに入ってしまい、
`int` への変換で例外になっていたところです。

### 存在しない ID のとき

```csharp
ticket = await Repository.FindAsync(Id);

if (ticket is null)
{
    Navigation.NotFound();
}
```

`NavigationManager.NotFound()` は .NET 10 で使えるメソッドで、
`Routes.razor` の `NotFoundPage` に指定されたページを描画しつつ、HTTP 404 を返します。

実測:

| URL | ステータス | 表示 |
|---|---|---|
| `/tickets/3` | 200 | Office のライセンス認証が切れた |
| `/tickets/999` | 404 | ページが見つかりません |
| `/tickets/abc` | 404 | ページが見つかりません |

「一覧に出ていない ID を URL に直接打たれた」ときに、200 で空のページを返さないのが大事です。
検索エンジンにもブラウザにも「無い」と伝わります。

### `is { } comment` という書き方

```razor
@if (ticket.ResolutionComment is { } comment)
{
    <p>@comment</p>
}
```

「null でなければ、その値を `comment` という名前で使う」。
`if (x is not null)` と書いたあとに `x!` や `x.Value` を書かずに済みます。

`ResolvedAt` は `DateTimeOffset?` なので、こちらも同じ形で `.Value` を書かずに取り出しています。

---

## `TicketStatusBadge` — 初めての自作コンポーネント

```razor
<span class="badge @CssClass">@Label</span>

@code {
    [Parameter]
    [EditorRequired]
    public TicketStatus Status { get; set; }
    ...
}
```

`[Parameter]` を付けたプロパティが、外から属性として渡せる値になります。

```razor
<TicketStatusBadge Status="ticket.Status" />
```

`[EditorRequired]` を付けておくと、渡し忘れたときにビルド警告が出ます（実測）。

```
warning RZ2012: Component 'TicketStatusBadge' expects a value for the parameter 'Status',
                but a value may not have been provided.
```

**警告であってエラーではない**ので、ビルドは通ってしまいます。気づける、というだけのもの。

### 「未対応」という日本語を Core に置かなかった判断

```csharp
private string Label => Status switch
{
    TicketStatus.Open => "未対応",
    ...
};
```

`TicketStatus.Open` を画面で何と呼ぶかは表示の都合です。

- 英語版を作るなら変わる
- 管理者向け画面では「Open (未対応)」と出したくなるかもしれない
- 帳票では別の言い方をするかもしれない

一方 `TicketStatus.Open` という**概念**自体は変わりません。変わるものを Web 側に、変わらないものを Core に置いています。

とはいえ、これは判断の分かれるところです。「未対応」が社内の正式な業務用語として定義されているなら、
Core に置く判断もあり得ます。ステージ 0 で話した「迷ったら Web に置く」に従っています。

### `_ =>` を書いてある理由

```csharp
_ => Status.ToString(),
```

3 つの値を網羅しているので不要に見えますが、C# の enum は `(TicketStatus)99` のようなキャストを許します。
網羅したつもりでも漏れるので、既定の枝を残しています。

---

## 今が「静的 SSR だけ」であることの確認

ブラウザの開発者ツールでネットワークタブを開き、「未対応」ボタンを押してみてください。
実際に観測した通信はこうでした。

```
GET /tickets                     200        ← 最初のページ表示
GET /lib/bootstrap/.../bootstrap.min.46ein0sx1k.css   200
GET /app.khy4lop6wu.css          200
GET /_framework/blazor.web.wyu7y4jcvb.js              200

（「未対応」をクリック）

GET /tickets?status=Open                   ← これだけ
```

読み取れることが 2 つあります。

**① WebSocket 接続が 1 本も無い。** `_blazor` へのネゴシエーションも起きていません。
`Program.cs` に `AddInteractiveServerComponents()` が無いので、回線が張られないためです。
ステージ 4 でここに WebSocket が現れます。

**② クリック後、CSS と JS を取り直していない。**
ふつうのリンククリックならページ全体が読み直され、CSS も JS も再取得されるはずです。
そうなっていないのは、`blazor.web.js` がリンクのクリックを横取りして、
HTML だけを `fetch` し、DOM の変わった部分だけを差し替えているから（**拡張ナビゲーション**）。

ここが紛らわしいところで、**拡張ナビゲーションは「対話機能」ではありません**。
C# はサーバーでしか動いておらず、返ってきているのは完成済みの HTML です。
ページ遷移が速く見えるだけで、`@onclick` は依然として使えません。

つまり今のアプリは、Blazor というより「Razor で HTML を組み立てて返すサーバー ＋ 遷移を滑らかにする JS」です。
それでも一覧・詳細・絞り込み・404 はここまで作れている、というのがこのステージの主題です。

---

## 手を動かす課題

### 課題 1 — ルールを迂回してみる

`TicketDetail.razor` の `@code` に次の行を足してビルドする。

```csharp
ticket!.Status = TicketStatus.Resolved;
```

どんなエラーが出るか。`private set` が何を守っているかを確認する。

### 課題 2 — ルート制約を外してみる

`TicketDetail.razor` の `@page "/tickets/{Id:int}"` から `:int` を消して、`/tickets/abc` を開く。

404 だったものが 500 に変わります。**見てほしいのは画面ではなくコンソールのログ**で、
どの層で何が起きたのかが書いてあります。

```
System.InvalidOperationException: Unable to set property 'Id' on object of type
  'HelpDesk.Web.Components.Pages.TicketDetail'. The error was:
  Unable to cast object of type 'System.String' to type 'System.Int32'.
```

ルート制約は「変換できない URL をここに到達させない」ための門番だった、ということが読み取れます。

### 課題 3 — 寿命を変えてみる

`Program.cs` の `AddSingleton` を `AddScoped` に変えて、一覧を何度か読み込む。

**今は見た目が変わりません。** なぜ変わらないのか、そしてステージ 3 で「チケットを追加」を作ったら
何が起きるようになるかを考えてみてください。

### 課題 4 — テストを 1 つ足す

`TicketTests.cs` に、次を確かめるテストを自分で書いてください（`Ticket` 側は変更不要）。

> 解決済みのチケットを再オープンしてから、もう一度着手できる

ヒント: `Reopen()` すると `Open` に戻るので、`Start()` が通るはずです。

---

## 今のステージで残っている粗さ

正直に列挙しておきます。すべて後のステージで扱います。

| 内容 | 対処 |
|---|---|
| `Id` を `Ticket.Create` の引数で渡している。採番の責任が曖昧 | ステージ 2（DB が採番する） |
| Singleton の `List` がスレッド安全でない | ステージ 2（DB に移す） |
| 再起動すると初期データに戻る | ステージ 2 |
| 依頼者が文字列。誰でも名乗れる | ステージ 5（ログインユーザー） |
| 一覧にページングが無い。5 件だから成立している | ステージ 8（QuickGrid） |
| `Description` の改行を `white-space: pre-wrap` で処理している | 当面このまま |

---

## 次のステージ

ステージ 2 で `InMemoryTicketRepository` を EF Core + SQLite の実装に差し替えます。

確認してほしいのは、**`Ticket.cs` と `.razor` を 1 行も変えずに DB 化が終わる**ことです。
変える必要があった場合、それは Core と Web の分け方がどこかで間違っていたということになります。
