# 「ブラウザ上で C# が動く」とはどういうことか

ステージ 0・1 の文中で使った言い方が曖昧だったので、実際に動かして確かめた結果とあわせて整理します。

## 結論を先に

Blazor には C# の動き方が 3 通りあります。

| モード | C# が動く場所 | C# が動いている期間 | ボタンを押すと |
|---|---|---|---|
| **静的 SSR**（今のアプリ） | サーバー | HTML を組み立てる一瞬だけ | **何も起きない** |
| **InteractiveServer** | サーバー | ブラウザを閉じるまでずっと | WebSocket 越しにサーバーの C# が動く |
| **InteractiveWebAssembly** | **ブラウザの中** | ブラウザを閉じるまでずっと | ブラウザの中の C# が動く |

**「ブラウザ上で C# が動く」が文字通り当てはまるのは 3 番目だけ**です。
.NET のランタイムが WebAssembly にコンパイルされてブラウザにダウンロードされ、
ブラウザが C# のコードを直接実行します。比喩ではありません。

ステージ 1 の本文で「ブラウザ上で C# は動いていない」と書きましたが、
これだと「2 番目なら動く」と読めてしまいます。正確にはこうです。

> 今のアプリでは、**C# は HTML を組み立てる一瞬だけサーバーで動き、返し終わったら止まる。**

---

## 実験 1：今のアプリでボタンを置いてみる

ホーム画面に、チュートリアルでおなじみのカウンターを一時的に足しました。

```razor
<p>現在の値: <strong id="count">@count</strong></p>
<button class="btn btn-secondary" @onclick="Increment">+1</button>

@code {
    private int count = 0;

    private void Increment() => count++;
}
```

### ビルドは通る

```
build exit=0
    0 個の警告
    0 エラー
```

**警告すら出ません。** 「このボタンは動きませんよ」と誰も教えてくれない。

### 返ってきた HTML

```html
<button class="btn btn-secondary">&#x2B;1</button>
```

`@onclick="Increment"` が**跡形もなく消えています**。`onclick` 属性すらありません。

### ブラウザで 3 回クリックした結果

```
document.getElementById('count').textContent  →  "0"
```

コンソールにエラーも警告も出ません（`No console logs.`）。
**押しても静かに何も起きない**、が今のアプリの挙動です。

---

## 実験 2：対話レンダリングを一時的に有効にする

`Program.cs` に 2 箇所足し、ホーム画面にレンダーモードを指定しました。

```csharp
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();      // ← 追加

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();      // ← 追加
```

```razor
@page "/"
@rendermode InteractiveServer   @* ← 追加 *@
```

### 返ってきた HTML

```html
<button class="btn btn-secondary">&#x2B;1</button>
```

**ボタンの HTML は 1 文字も変わりません。** ここが引っかかりやすいところで、
Blazor は `onclick` 属性を使いません。JavaScript 側からイベントリスナーを付けます。

変わったのは、HTML の中にコメントが現れたことです。

```html
<!--Blazor:{"type":"server","prerenderId":"b312e436b47a4d4aaba9bf896f020d0e","key":{...}}-->
   ...（コンポーネントの中身）...
<!--Blazor:{"prerenderId":"b312e436b47a4d4aaba9bf896f020d0e"}-->
```

静的 SSR のときは、このコメントが **0 個**でした（`grep -c "Blazor:"` の結果が `0`）。
対話モードにすると **2 個**（開始と終了）現れます。

`blazor.web.js` はこのコメントを目印に「ここは対話コンポーネントだ」と見つけ、
サーバーと繋いでイベントリスナーを付けます。**この目印が無いページでは、何も起動しない。**

### 通信が増える

```
GET  /_blazor/initializers            200
POST /_blazor/negotiate?negotiateVersion=1   200
```

`negotiate` は SignalR の接続交渉です。静的 SSR のときには 1 本もありませんでした。
このあと WebSocket が張られ、ブラウザを閉じるまで繋ぎっぱなしになります。

### クリックすると動く

```
document.getElementById('count').textContent  →  "3"
```

---

## 実験 2 のおまけ：最初の 3 回は捨てられた

正直に書くと、**最初の 3 クリックは無視されました**。

```
（ページ表示 → すぐ 3 回クリック）
count → "0"

（少し待ってから 3 回クリック）
count → "3"
```

これが **プリレンダリング**です。

1. サーバーが先に HTML を組み立てて返す（この時点で画面は見えている。**が、まだ繋がっていない**）
2. `blazor.web.js` が読み込まれ、`negotiate` して WebSocket を張る
3. ここで初めてボタンが反応するようになる

**「表示されているのに押しても反応しない」時間が存在する**わけです。
ステージ 4 で実際に向き合う問題で、ステージ 0 の解説で「プリレンダリングで
`OnInitializedAsync` が 2 回走る問題」と書いたのはこれと同じ根っこです。

---

## なぜ静的 SSR ではカウンターが動かないのか

`count` という変数を**誰が覚えているか**を考えると分かります。

### 静的 SSR

```
リクエスト到着
  → Home コンポーネントのインスタンスを作る（count = 0）
  → HTML を組み立てる
  → HTML を返す
  → インスタンスを捨てる      ← ここ
```

返し終わった時点で、`count` を持っていたオブジェクトは消えています。
`Increment()` を呼びたくても、**呼ぶ相手がもういない**。

### InteractiveServer

```
リクエスト到着
  → Home のインスタンスを作る（count = 0）
  → HTML を組み立てて返す
  → WebSocket が張られる
  → インスタンスをサーバー側のメモリに残したまま待機   ← ここ
  → クリックが WebSocket で届く → Increment() が動く → count = 1
  → 変わった部分だけを WebSocket で送り返す
```

サーバーのメモリにコンポーネントが生き続けるので、`count` が保持されます。
これが「ステージ 0 で触れた、Blazor Server の `Scoped` は接続単位」の正体です。

---

## 拡張ナビゲーションと混同しないこと

ステージ 1 で観測したとおり、**今のアプリでもリンクのクリックは `blazor.web.js` が横取りしています**
（CSS と JS を取り直さず、HTML だけ `fetch` して DOM を差し替える）。

これは「対話機能」ではありません。JavaScript はページ遷移を滑らかにしているだけで、
サーバーから返ってくるのは完成済みの HTML です。C# はサーバーで一瞬動いて終わります。

| | 拡張ナビゲーション | 対話レンダリング |
|---|---|---|
| 今のアプリで有効か | **有効** | 無効 |
| 何をする JS か | リンクを横取りして HTML を fetch | イベントをサーバー／wasm に中継 |
| WebSocket | 使わない | 使う（Server の場合） |
| `@onclick` | 動かない | 動く |

---

## では静的 SSR で何もできないのか

できます。**HTML フォームの POST** です。

```razor
<EditForm Model="model" FormName="createTicket" OnValidSubmit="Submit">
    ...
</EditForm>
```

これは JavaScript も WebSocket も使わず、ブラウザが普通にフォームを送信し、
サーバーが受け取って C# のメソッドを呼びます。**ステージ 3 でこれを使って起票画面を作ります。**

「ボタンを押して何か起こす」の大部分は、実はこれで足ります。
対話レンダリングが本当に要るのは、ページ全体を送り直さずに一部だけ更新したい場合です。

---

## どれを選ぶべきか（ステージ 4 の予告）

このアプリでは、こうする予定です。

- **既定は静的 SSR のまま**。一覧も詳細も起票フォームも、これで足りる
- **一部のコンポーネントだけ** `@rendermode InteractiveServer` を付ける

全ページを対話モードにすると、ユーザー 1 人につきサーバーのメモリと WebSocket を 1 本占有します。
必要なところだけに付けるのが `--interactivity None` で始めた理由です。

なお **InteractiveWebAssembly**（ブラウザの中で C# が動くモード）はこのアプリでは使いません。
DB に直接触るアプリと相性が悪く、初回に数 MB のランタイムをダウンロードする必要があるためです。
「C# がブラウザで動く」を体験したい場合は、別途小さなプロジェクトを作るのが早いです。

---

## この文書を書くのに使った実験は全部戻してあります

`Program.cs` と `Home.razor` への変更は `git checkout` で復元済みで、
`git status` はクリーン、ビルドも警告 0 で通ることを確認しています。
リポジトリにカウンターは残っていません。
