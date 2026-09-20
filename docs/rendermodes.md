# なぜ今はボタンを押しても動かないのか

## 一言で

ボタンを押したとき、**それを受け取って C# を動かす相手がサーバーにいるかどうか**。
今のアプリには、いない。

---

## 今のアプリにボタンを置くと

チュートリアルでおなじみのカウンターを置いてみます。

```razor
<button @onclick="Increment">+1</button>

@code {
    private int count = 0;
    private void Increment() => count++;
}
```

ビルドは**警告も出ずに**通り、画面にボタンも出ます。押しても何も起きません。エラーも出ません。

返ってきた HTML を見ると、`@onclick` が跡形もなく消えています。

```html
<button class="btn btn-secondary">+1</button>
```

---

## なぜか ― `count` を誰が覚えているか

静的 SSR がリクエストを処理する流れはこうです。

```
リクエスト到着
  → Home コンポーネントを作る（count = 0）
  → HTML を組み立てる
  → 返す
  → コンポーネントを捨てる        ← ここ
```

返し終わった時点で、`count` を持っていたオブジェクトはもう存在しません。
`Increment()` を呼びたくても、**呼ぶ相手がいない**。

だから HTML にイベントハンドラを書いても意味がなく、消えているわけです。

---

## 対話モードにすると何が変わるか

`Program.cs` に 2 行と、ページに 1 行足します。

```csharp
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();     // 追加

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();     // 追加
```

```razor
@rendermode InteractiveServer
```

流れがこう変わります。

```
リクエスト到着
  → コンポーネントを作る（count = 0）
  → HTML を返す
  → WebSocket が張られる
  → コンポーネントをサーバーのメモリに残したまま待つ   ← ここ
  → クリックが届く → Increment() が動く → count = 1
  → 変わった部分だけ送り返す
```

**コンポーネントがサーバーのメモリに生き続ける**ので、押した相手がいます。

実際に試すと動きました。ただし**最初の数クリックは無視されました**。
HTML が表示されてから WebSocket が張られるまでの間は、まだ誰もいないためです。
「画面は見えているのに押しても反応しない」時間が実在します（ステージ 4 で扱います）。

---

## 「ブラウザ上で C# が動く」のはどれか

Blazor のレンダーモードは 4 つあります。

| モード | C# が動く場所 | 押したら動くか |
|---|---|---|
| Static Server（**今のアプリ**） | サーバー | ✗ |
| Interactive Server | サーバー | ✔ |
| Interactive WebAssembly | **ブラウザ** | ✔ |
| Interactive Auto | 最初サーバー、以降ブラウザ | ✔ |

ステージ 4 で使うのは上から 2 番目で、**これもサーバーで動きます**。
C# が本当にブラウザで動くのは下 2 つだけで、.NET のランタイムごとブラウザにダウンロードされます。

---

## 静的 SSR でも操作はできる

**HTML フォームの POST** が使えます。

```razor
<EditForm Model="model" FormName="createTicket" OnValidSubmit="Submit">
```

出力されるのは素の `<form method="post">` です。
JavaScript を一切使わない `curl` の POST だけで C# の `Submit()` が動くことを確認しました。
ステージ 3 の起票画面はこれで作ります。

「ボタンを押して何か起こす」の大半はこれで足ります。
対話モードが要るのは、ページを送り直さずに一部だけ更新したいときです。

---

<sub>ここに書いた動作は `./scripts/verify-claims.sh` の [3] [4] [9] で再現できます。
モードの一覧は [公式ドキュメント](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/render-modes?view=aspnetcore-10.0) に準拠。
初版では「モードは 3 つ」と書いていましたが誤りで、Interactive Auto が抜けていました。</sub>
