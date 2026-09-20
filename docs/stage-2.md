# ステージ 2 — EF Core + SQLite に差し替える

保管庫をメモリから SQLite に移した。アプリを再起動してもデータが残る。

## まず、ステージ 1 の予告の答え合わせ

> 見どころは、**`Ticket.cs` と `.razor` を 1 行も変えずに DB 化が終わる**かどうか。
> 変える必要があったら、Core と Web の分け方がどこかで間違っていたということ。

**半分外れた。** 実際に変えた箇所はこうなった。

| ファイル | 変えたか | 内容 |
|---|---|---|
| `Ticket.cs` の状態遷移（`Start` / `Resolve` / `Reopen`） | **変えていない** | — |
| `Ticket.Create` | 変えた | 引数から `id` を落とした |
| `ITicketRepository` | **変えていない** | — |
| `Tickets.razor` / `TicketDetail.razor` | 変えた | 日時の表示を 3 箇所 |
| `Program.cs` | 変えた | DI 登録 |

原因は「分け方が間違っていた」ではなく、**ステージ 1 の積み残し 2 件**だった。

1. **`Id` の採番**。ステージ 1 の「残っている粗さ」に書いていたとおり、DB が採番する形に直した。予定どおり。
2. **表示のタイムゾーン**。これは**書いていなかった見落とし**。詳しくは下の「踏んだ問題」に書く。

分け方そのものは効いた。`Ticket.cs` に EF の属性は 1 つも付いていないし、
Core は EF Core を参照していない（`ArchitectureTests` が検査している）。

---

## ステージ 0 で保留にした判断を決めた

ステージ 0 でこう書いて先送りしていた。

> ステージ 2 で EF Core を入れるとき、`HelpDesk.Infrastructure` を切り出すか Web に置くかを改めて考える。

**切り出した。** 決め手はテストで、`EfTicketRepository` をテストしたいが、
Web に置くとテストプロジェクトが ASP.NET Core ごと引きずり込むことになる。

```
HelpDesk.Web  ──┐
                ├──▶  HelpDesk.Infrastructure  ──▶  HelpDesk.Core
HelpDesk.Tests ─┘
```

Core は相変わらず誰にも依存しない。

---

## `IDbContextFactory` — Blazor で一番刺さる罠

```csharp
builder.Services.AddDbContextFactory<HelpDeskDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("HelpDesk")));
```

普通の ASP.NET Core なら `AddDbContext` で `DbContext` を直接注入する。Blazor ではこれが壊れる。

理由は 2 つ重なっている。

1. **`DbContext` はスレッド安全ではない。** 同じインスタンスを同時に 2 箇所から使うと例外になる
2. **Blazor では 1 つのスコープが長く生きる。** ステージ 1 で触れたとおり、対話モードのスコープは
   HTTP リクエストではなく**ブラウザとの接続（circuit）**。画面を開いている間ずっと同じ `DbContext` を使い回すことになる

そこで「`DbContext` そのもの」ではなく「`DbContext` を作る工場」を注入し、使うたびに作って捨てる。

```csharp
await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
```

この罠は**静的 SSR の今は表面化しない**。ステージ 4 で対話モードにした瞬間に出る。
先に正しい形にしてある。

---

## `Ticket.cs` に EF の痕跡が 1 つも無い

マッピングは `TicketConfiguration.cs` が外から与えている。

```csharp
builder.Property(ticket => ticket.Title).HasMaxLength(200).IsRequired();
```

`Ticket` 側には `[Key]` も `[MaxLength]` も付いていない。属性を付けると Core が EF Core を参照することになり、
ステージ 0 で立てた壁が崩れる。

**private なコンストラクタと private なセッターのままで EF は動く。** カプセル化を緩める必要はなかった。

### 状態は数値ではなく文字列で保存する

```csharp
builder.Property(ticket => ticket.Status).HasConversion<string>();
```

既定では enum は数値で保存される（`Open` = 0、`InProgress` = 1 …）。
後から enum の**途中**に値を挿入すると、既存データの意味が丸ごとずれる。

実際の中身はこうなっている。

```
$ sqlite3 helpdesk.db "SELECT Id, Status FROM Tickets;"
1|Open
2|InProgress
3|Resolved
```

---

## 踏んだ問題 — SQLite は `DateTimeOffset` を並べ替えられない

一覧を開いたら 500 になった。

```
System.NotSupportedException: SQLite does not support expressions of type
'DateTimeOffset' in ORDER BY clauses.
```

詳細ページ（`WHERE Id = 3`）は動くのに、一覧（`ORDER BY CreatedAt`）だけ落ちる。

### 直し方

**UTC に揃えた固定書式の文字列**として保存することにした（`UtcTextConverter.cs`）。

```
2026-09-14T00:12:00.0000000Z
```

UTC に正規化してあるので、**辞書順がそのまま時刻順**になる。並べ替えは DB 側で成立する。
数値（`UtcTicks`）でも同じことはできるが、文字列なら `sqlite3` で開いたときに人間が読める。

### ここで露呈したステージ 1 の見落とし

保存形式を UTC に変えたら、**画面の表示が 9 時間ずれた**（09:12 → 00:12）。

ステージ 1 の `.razor` はこう書いていた。

```razor
@ticket.CreatedAt.ToString("yyyy/MM/dd HH:mm")
```

これが JST で表示されていたのは、**メモリ上のオブジェクトがたまたま `+09:00` を持っていたから**。
「どのタイムゾーンで見せるか」を誰も決めていなかった。DB を挟んだ瞬間に破綻した。

表示の責任を Web 側の 1 箇所に置いて直した。

```csharp
// src/HelpDesk.Web/JstDisplay.cs
public static string ToJst(this DateTimeOffset value) =>
    value.ToOffset(TimeSpan.FromHours(9)).ToString("yyyy/MM/dd HH:mm");
```

```razor
@ticket.CreatedAt.ToJst()
```

**DB は UTC、画面は JST、境目は 1 ファイル。** 海外拠点に対応するならここだけ直せばいい。

この往復は `EfTicketRepositoryTests` の
`DateTimeOffset_は同じ瞬間として往復する` と
`並べ替えはオフセットが違っても瞬間の順になる` で固定してある。

---

## DI の寿命が Singleton から Scoped に変わった

```csharp
// ステージ 1
builder.Services.AddSingleton<ITicketRepository, InMemoryTicketRepository>();

// ステージ 2
builder.Services.AddScoped<ITicketRepository, EfTicketRepository>();
```

ステージ 1 では **Singleton でなければならなかった**。
状態（`List<Ticket>`）をリポジトリ自身が抱えていたので、作り直されると消えてしまうから。

いまリポジトリは工場を 1 つ持っているだけで、状態はすべて DB にある。
**だからどちらでも動く。** 迷わなくていい状態になったのが、DB に移した効果のひとつ。

Scoped を選んだのは、後で「このリクエスト中はキャッシュしたい」といった要求が来たときに素直だから。

### `InMemoryTicketRepository` は消した

`Id` の採番を DB に移した結果、メモリ実装には **`Id` を設定する手段が無くなった**
（`Id` のセッターは `private`）。無理に残すとドメインに穴を開けることになる。

テスト用の代替は SQLite のメモリモードで足りる。

```csharp
connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();   // 閉じるとデータベースごと消えるので開いたままにする
```

本物の SQLite を相手にするので、「テストは通るが本番で落ちる」が起きにくい。

---

## マイグレーション

ツールはリポジトリ内にローカルインストールしてある（`.config/dotnet-tools.json`）。

```bash
dotnet tool restore
```

```bash
dotnet ef migrations add <名前> --project src/HelpDesk.Infrastructure --startup-project src/HelpDesk.Web
```

適用は起動時に自動で走る。

```csharp
await db.Database.MigrateAsync();
await DatabaseSeeder.SeedAsync(db);
```

**これは学習用の割り切り**で、本番では普通やらない（デプロイ手順として別に流す）。
複数のインスタンスが同時に起動すると競合するため。

初期データを `HasData` ではなく通常の保存で入れているのは、`Ticket.Create` を通して
ドメインのルールを迂回しないため。

---

## 手を動かす課題

**課題 1 — 日時の変換を外してみる**

`TicketConfiguration.cs` の `CreatedAt` から `.HasConversion(UtcTextConverter.Instance)` を消して一覧を開く。
上に貼った `NotSupportedException` が再現する。詳細ページは動くのに一覧だけ落ちる理由を考える。

**課題 2 — `IDbContextFactory` をやめてみる**

`AddDbContextFactory` を `AddDbContext` に変え、`EfTicketRepository` が `HelpDeskDbContext` を
直接受け取るように書き換える。**今は動いてしまう。** なぜ今は平気で、ステージ 4 で何が起きるか。

**課題 3 — 状態の保存形式を戻してみる**

`.HasConversion<string>()` を消してマイグレーションを作り直し、`sqlite3` で中身を見る。
`Status` が何になるか。そのうえで `TicketStatus` の `Open` と `InProgress` の間に
新しい値を足したら、既存データはどう解釈されるか。

**課題 4 — データを消してみる**

`src/HelpDesk.Web/helpdesk.db` を消して起動し直す。何が起きるか、`DatabaseSeeder` のどの行が効いているか。

---

## 今のステージで残っている粗さ

| 内容 | 対処 |
|---|---|
| 起動時にマイグレーションを自動適用している | 本番では別手順にすべき（このアプリでは直さない） |
| `ITicketRepository` に保存系のメソッドが無い | ステージ 3（起票・更新を足す） |
| 一覧にページングが無い | ステージ 8（QuickGrid） |
| 依頼者が文字列 | ステージ 5（ログインユーザー） |
| タイムゾーンが JST 固定 | 当面このまま。直すなら `JstDisplay.cs` 1 ファイル |

---

## 次のステージ

ステージ 3 で起票フォームと、着手・解決の操作を作る。
**対話レンダリングは使わない。** 静的 SSR のまま `EditForm` の POST で書き込む。

見どころは、`@onclick` が使えない状態でどこまで実用的な画面が作れるか。

---

<sub>この文書の事実主張は `./scripts/verify-claims.sh` で再検証できます（[10] [11]）。</sub>
