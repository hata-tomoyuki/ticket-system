# ステージ 0 — ソリューションの骨組み

画面の機能はまだ何も無い。作ったのは「これから機能を足していく入れ物」と、
**その入れ物が壊れていないことを自動で見張る仕組み**。

## 読む順番

上から順に読むと、外側から内側へ降りていける。

1. `global.json`
2. `Directory.Build.props`
3. `HelpDesk.slnx`
4. `src/HelpDesk.Core/HelpDesk.Core.csproj` → `src/HelpDesk.Web/HelpDesk.Web.csproj` → `tests/HelpDesk.Tests/HelpDesk.Tests.csproj`
5. `tests/HelpDesk.Tests/ArchitectureTests.cs`
6. `src/HelpDesk.Web/Program.cs`
7. `src/HelpDesk.Web/Components/App.razor` → `Routes.razor` → `Layout/MainLayout.razor` → `Pages/Home.razor`

---

## なぜ 3 プロジェクトに分けたか

1 つの Web プロジェクトに全部入れても動く。分けた理由は **依存の向きを一方通行に固定するため**。

```
HelpDesk.Web  ──┐
                ├──▶  HelpDesk.Core
HelpDesk.Tests ─┘
```

Core は誰にも依存しない。この向きが守られていると、

- ドメインのルール（「解決済みのチケットは再オープンできる」等）を、HTTP も DB も無い状態でテストできる
- 「画面の都合」や「DB の都合」がドメインのコードに混ざり込めない。混ぜようとするとコンパイルが通らないので、気づける

逆に 1 プロジェクトだと、この境界は「気をつける」でしか維持できない。

### 他の選択肢

- **1 プロジェクト**: 小さいうちは速い。ステージ 5（認証）あたりで Web の型がドメインに染み出し始める。
- **4 層以上（Application / Infrastructure を分ける）**: 実務では見るが、今の規模だとファイルを探す手間が増えるだけ。ステージ 2 で EF Core を入れるとき、`HelpDesk.Infrastructure` を切り出すか Web に置くかを改めて考える。

---

## `Directory.Build.props`

MSBuild は各 `.csproj` をビルドするとき、**親ディレクトリを遡って最初に見つけた `Directory.Build.props` を自動で読み込む**。
`import` を書く必要はない。

テンプレートが生成した直後、3 つの csproj にはこれが重複して書かれていた。

```xml
<TargetFramework>net10.0</TargetFramework>
<Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings>
```

これを `Directory.Build.props` に移した結果、`HelpDesk.Core.csproj` は空になった。

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <!-- 設定は Directory.Build.props に集約。ここには何も書かない。 -->
</Project>
```

効いているのは「重複が減った」ことより、**「全プロジェクトの設定が揃っている」が 1 ファイルを見るだけで分かる**こと。
片方だけ `Nullable` が無効になっている、という事故が起こらなくなる。

### `Nullable` と `ImplicitUsings` が何をしているか

- `Nullable: enable` → 参照型を既定で「null を入れられない」として扱う。`string` と `string?` が別物になる。`ArchitectureTests.cs` で `name is not null &&` を書いているのはこのため（`AssemblyName.Name` は `string?`）。
- `ImplicitUsings: enable` → `System`, `System.Linq`, `System.Collections.Generic` などの `using` が暗黙で入る。`ArchitectureTests.cs` に `using System.Linq;` が無いのに `Select`/`Where` が使えるのはこれ。`using System.Reflection;` は対象外なので明示している。

---

## `HelpDesk.slnx`

`dotnet new sln` が生成したのは、従来の `.sln` ではなく XML 形式の `.slnx` だった。

```xml
<Solution>
  <Folder Name="/src/">
    <Project Path="src/HelpDesk.Core/HelpDesk.Core.csproj" />
    <Project Path="src/HelpDesk.Web/HelpDesk.Web.csproj" />
  </Folder>
  <Folder Name="/tests/">
    <Project Path="tests/HelpDesk.Tests/HelpDesk.Tests.csproj" />
  </Folder>
</Solution>
```

旧 `.sln` は独自形式で GUID が並び、マージ衝突が起きると人間の手には負えなかった。`.slnx` は素直な XML なので読めるし直せる。

なお `<Folder>` は**表示上のグループ**であって、ディスク上のフォルダとは別物。たまたま今回は一致させてある。

---

## `ArchitectureTests.cs` — 設計をテストで縛る

「Core は Web に依存しない」は、口約束にしておくと必ず破られる。テストにしておけば `dotnet test` が落ちる。

```csharp
private static readonly Assembly CoreAssembly = typeof(CoreAssemblyMarker).Assembly;
```

`CoreAssemblyMarker` は中身が空のクラス。

```csharp
public sealed class CoreAssemblyMarker;
```

セミコロンで終わっているのは C# 12 以降の書き方で、`{ }` を省略できる。
この「空のクラスを 1 個置いてアセンブリの目印にする」のは実務でもよく使う手で、
`Assembly.Load("HelpDesk.Core")` のように**文字列で書かずに済む**のが利点。文字列だとリネームで壊れても気づけない。

検査そのものは `GetReferencedAssemblies()` で参照一覧を取り、名前で絞っているだけ。

> **注意**: `GetReferencedAssemblies()` が返すのは、**コンパイル結果に実際に焼き込まれた**参照。
> csproj に参照を足しただけでコードから 1 度も使わなければ、ここには現れない。
> つまりこのテストは「使ってしまったら落ちる」検査であって、「書いたら落ちる」検査ではない。
> 実際にその挙動を確認する手順は、下の課題 2 にある。

---

## `Program.cs` — 今は 3 行しかない

```csharp
builder.Services.AddRazorComponents();
...
app.MapStaticAssets();
app.MapRazorComponents<App>();
```

注目すべきは**書かれていないこと**。

`AddInteractiveServerComponents()` も `AddInteractiveServerRenderMode()` も無い。
プロジェクト作成時に `--interactivity None` を指定したため、このアプリは今 **静的サーバーレンダリング（静的 SSR）だけ** で動いている。

つまり現状は、PHP や Rails と同じように「リクエストが来たらサーバーで HTML を組み立てて返しておしまい」。
ブラウザ上で C# は動かないし、WebSocket 接続も張られない。
`blazor.web.js` は読み込まれるが、対話モードのコンポーネントが 1 つも無いので何も起動しない。

これはステージ 4 のための仕込み。そこで対話機能を入れるとき、**この差分だけ**を見れば「対話機能を有効にするとは具体的に何を足すことか」が分かる。
最初から `--interactivity Server` で作ると、全部が最初から入っているので境目が見えない。

### ついでに確認できること

`dotnet run` して表示されたページの HTML ソースを見ると、CSS のファイル名がこうなっている。

```html
<link rel="stylesheet" href="app.khy4lop6wu.css" />
```

`App.razor` では `@Assets["app.css"]` としか書いていない。
`MapStaticAssets()` がビルド時に中身のハッシュを計算してファイル名に埋め込み、`@Assets[...]` がその対応表を引いている。
内容が変われば名前も変わるので、ブラウザキャッシュが原因の「直したのに反映されない」が起きない。

---

## テンプレートから消したもの

- `Components/Pages/Weather.razor` — テンプレートのサンプル。読むノイズになるので削除
- `src/HelpDesk.Core/Class1.cs`、`tests/HelpDesk.Tests/UnitTest1.cs` — 中身の無いひな形
- `NavMenu.razor` の Weather へのリンク

残したもの：

- `Pages/Error.razor` / `Pages/NotFound.razor` と、`Program.cs` の `UseExceptionHandler` / `UseStatusCodePagesWithReExecute` — ステージ 8 で手を入れる
- `HelpDesk.Web.csproj` の `BlazorDisableThrowNavigationException` — 静的 SSR 中の画面遷移の挙動に関わる設定。ステージ 3 でフォーム送信後のリダイレクトを書くときに実際に効いてくるので、そこで扱う

---

## 手を動かす課題

読むだけより、壊して直すほうが残る。いずれも `git checkout .` で戻せる。

### 課題 1 — 依存を逆向きにしてみる

`src/HelpDesk.Core/HelpDesk.Core.csproj` に Web への参照を足す。

```xml
<ItemGroup>
  <ProjectReference Include="..\HelpDesk.Web\HelpDesk.Web.csproj" />
</ItemGroup>
```

```bash
dotnet build HelpDesk.slnx
```

何というエラーが出るか。「気をつける」ではなくビルドシステムが止めてくれる、という感触を持っておく。

### 課題 2 — アーキテクチャテストを落としてみる

Core の csproj に ASP.NET Core への参照を足し、**さらにその型を実際に使う**コードを書く。

```xml
<ItemGroup>
  <FrameworkReference Include="Microsoft.AspNetCore.App" />
</ItemGroup>
```

```csharp
// src/HelpDesk.Core/Oops.cs
using Microsoft.AspNetCore.Http;

namespace HelpDesk.Core;

public static class Oops
{
    public static string? PathOf(HttpContext context) => context.Request.Path.Value;
}
```

`dotnet test` で `Core_は_AspNetCore_に依存しない` が落ちる（確認済み）。

そのうえで **`Oops.cs` だけ消して csproj の `FrameworkReference` は残す**と、テストは通ってしまう。
上の「注意」に書いた、このテストの限界をここで実際に見ておく。

### 課題 3 — `Nullable` を切ってみる

`Directory.Build.props` の `<Nullable>enable</Nullable>` を `disable` に変え、
`ArchitectureTests.cs` の `name is not null &&` を消してビルドする。

`enable` に戻すと今度は何が起きるか。`string` と `string?` が別の型として扱われる、という意味を体感する。

---

## 次のステージ

ステージ 1 では `HelpDesk.Core` にチケットのドメイン（`Ticket`、`TicketStatus`、ステータス遷移のルール）を置き、
静的 SSR のまま一覧ページと詳細ページを作る。
インメモリのリポジトリを DI 経由で注入する形にして、ステージ 2 で EF Core に差し替えられるようにしておく。
