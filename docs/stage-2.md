# ステージ 2 — EF Core + SQLite に差し替える

> GitHub で見る: [stage-1...stage-2](https://github.com/hata-tomoyuki/ticket-system/compare/stage-1...stage-2)

5 つのステップに分けてある。**1 ステップずつ差分を見て、納得したら次へ進む。**

```bash
./scripts/stage-diff.sh 2a      # そのステップの差分（自動生成ファイルは除く）
```

| ステップ | 読むファイル | 内容 |
|---|---|---|
| [2a](#2a) | 2 | Ticket をテーブルに対応づける |
| [2b](#2b) | 3 | 保管庫を SQLite に差し替える ★壊れます |
| [2c](#2c) | 2 | 日時が並べ替えられない問題を直す ★ずれます |
| [2d](#2d) | 1 + 3行 | 表示のタイムゾーンを決める |
| [2e](#2e) | 1 | データベース経由のテスト |

---

## <a id="2a"></a>2a. Ticket をテーブルに対応づける

`HelpDeskDbContext.cs` と `TicketConfiguration.cs`。まだ何も動きは変わらない。

**`Ticket.cs` に `[Key]` も `[MaxLength]` も付けていない。** 対応づけは外から与える。
属性を付けると Core が EF Core を参照することになり、ステージ 0 で立てた壁が崩れる。

private コンストラクタと private セッターのままで EF は動く。カプセル化を緩める必要はない。

状態は数値ではなく文字列で保存している。数値だと、後から enum の途中に値を挿入したときに
既存データの意味がずれる。

ここで `HelpDesk.Infrastructure` を新設した（ステージ 0 で保留にした判断）。
決め手はテストで、Web に置くとテストプロジェクトが ASP.NET Core ごと引きずり込む。

---

## <a id="2b"></a>2b. 保管庫を SQLite に差し替える

**★ このコミットではアプリは動かない。** `/tickets` が 500 になる。意図的にここで区切っている。

`EfTicketRepository.cs`、`Program.cs`、`Ticket.cs` の差分 3 行。

いちばんの読みどころは `AddDbContext` **ではなく** `AddDbContextFactory` を使っていること。

```csharp
builder.Services.AddDbContextFactory<HelpDeskDbContext>(...);
```

`DbContext` はスレッド安全ではない。そして Blazor では 1 つのスコープが長く生きる
（対話モードでは HTTP リクエストではなく**ブラウザとの接続が切れるまで**）。
だから「`DbContext` そのもの」ではなく「作る工場」を注入し、使うたびに作って捨てる。

**この罠は静的 SSR の今は表面化しない。** ステージ 4 で対話モードにした瞬間に出る。

`Ticket.Create` から `id` を落とした。採番はデータベースの責任になった。
その結果 `InMemoryTicketRepository` は `Id` を設定する手段が無くなったので削除した。

`Migrations/` は EF の自動生成。読まなくていい。

---

## <a id="2c"></a>2c. 日時が並べ替えられない問題を直す

2b で 500 になっていた原因。

```
System.NotSupportedException: SQLite does not support expressions of
type 'DateTimeOffset' in ORDER BY clauses.
```

詳細ページ（`WHERE Id = 3`）は動くのに一覧（`ORDER BY CreatedAt`）だけ落ちていた。

`UtcTextConverter.cs` で、UTC に揃えた固定書式の文字列として保存する。

```
2026-09-14T00:12:00.0000000Z
```

UTC に正規化してあるので**辞書順がそのまま時刻順**になり、並べ替えが DB 側で成立する。

**★ 一覧は直るが、今度は画面の日時が 9 時間ずれる。** 次で直す。

---

## <a id="2d"></a>2d. 表示のタイムゾーンを決める

2c でずれた原因は、ステージ 1 のこの書き方。

```razor
@ticket.CreatedAt.ToString("yyyy/MM/dd HH:mm")
```

JST で表示されていたのは、メモリ上のオブジェクトが**たまたま `+09:00` を持っていたから**。
「どのタイムゾーンで見せるか」を誰も決めていなかった。DB を挟んだ瞬間に破綻した。

`JstDisplay.cs` に集約する。

```csharp
public static string ToJst(this DateTimeOffset value) =>
    value.ToOffset(TimeSpan.FromHours(9)).ToString("yyyy/MM/dd HH:mm");
```

**DB は UTC、画面は JST、境目は 1 ファイル。** 海外拠点に対応するならここだけ直せばいい。

---

## <a id="2e"></a>2e. データベース経由のテスト

本物の SQLite を相手にする。メモリ上のデータベースなので速く、後片付けも要らない。
**接続を開いたままにしないとデータベースごと消える**のが注意点。

固定したこと: Id の採番／private セッターの往復／`DateTimeOffset` が同じ瞬間として往復すること／
オフセットが違っても並び順が瞬間の順になること。

---

## 途中で踏んだ不具合（ステージ 2 とは無関係）

`<PageTitle>` を `@if` の中に置くと Razor のコード生成が壊れる。
**ステージ 1 から入っていた**が、差分ビルドが古い生成物を使い回していたため表面化していなかった。
`obj/` を消すと 22 個のコンパイルエラーになる。

`<PageTitle>` は `@if` の外に置くこと。

---

## 残っている粗さ

| 内容 | 対処 |
|---|---|
| 起動時にマイグレーションを自動適用 | 本番では別手順にすべき（このアプリでは直さない） |
| `ITicketRepository` に保存系のメソッドが無い | ステージ 3 |
| 一覧にページングが無い | ステージ 8 |
| 依頼者が文字列 | ステージ 5 |

---

## 次

ステージ 3 で起票フォームと、着手・解決の操作を作る。
**対話レンダリングは使わない。** 静的 SSR のまま `EditForm` の POST で書き込む。
