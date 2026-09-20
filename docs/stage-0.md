# ステージ 0 — ソリューションの骨組み

画面の機能はまだ何も無い。作ったのは「これから機能を足していく入れ物」と、
**その入れ物が壊れていないことを自動で見張る仕組み**。

## 読む順番

1. `Directory.Build.props`
2. `HelpDesk.slnx`
3. 3 つの `.csproj`
4. `tests/HelpDesk.Tests/ArchitectureTests.cs`
5. `src/HelpDesk.Web/Program.cs`

---

## なぜ 3 プロジェクトに分けたか

1 つの Web プロジェクトに全部入れても動く。分けた理由は **依存の向きを一方通行に固定するため**。

```
HelpDesk.Web  ──┐
                ├──▶  HelpDesk.Core
HelpDesk.Tests ─┘
```

Core は誰にも依存しない。この向きが守られていると、

- ドメインのルールを、HTTP も DB も無い状態でテストできる
- 「画面の都合」や「DB の都合」がドメインに混ざり込めない。混ぜようとするとコンパイルが通らない

1 プロジェクトだと、この境界は「気をつける」でしか維持できない。

---

## `Directory.Build.props` — 設定を 1 箇所に集める

MSBuild は各 `.csproj` をビルドするとき、これを自動で読み込む。`import` を書く必要はない。

テンプレートが生成した直後、3 つの csproj に同じ 3 行が重複していた。

```xml
<TargetFramework>net10.0</TargetFramework>
<Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings>
```

これを移した結果、`HelpDesk.Core.csproj` は空になった。

効いているのは「重複が減った」ことより、**「全プロジェクトの設定が揃っている」が 1 ファイルで分かる**こと。
片方だけ `Nullable` が無効、という事故が起こらなくなる。

- `Nullable: enable` → `string` と `string?` が別の型になる。`ArchitectureTests.cs` で `name is not null &&` を書いているのはこのため（`AssemblyName.Name` は `string?`）
- `ImplicitUsings: enable` → `System`, `System.Linq` などの `using` が暗黙で入る。`System.Reflection` は対象外なので明示している

---

## `HelpDesk.slnx`

`dotnet new sln` が生成したのは、従来の `.sln` ではなく XML 形式の `.slnx` だった。

```xml
<Solution>
  <Folder Name="/src/">
    <Project Path="src/HelpDesk.Core/HelpDesk.Core.csproj" />
    ...
```

旧 `.sln` は GUID が並ぶ独自形式で、マージ衝突が起きると手に負えなかった。`.slnx` は読めるし直せる。

`<Folder>` は**表示上のグループ**で、ディスク上のフォルダとは別物。今回はたまたま一致させてある。

---

## `ArchitectureTests.cs` — 設計をテストで縛る

「Core は Web に依存しない」は、口約束にしておくと必ず破られる。テストなら `dotnet test` が落ちる。

```csharp
private static readonly Assembly CoreAssembly = typeof(CoreAssemblyMarker).Assembly;
```

`CoreAssemblyMarker` は中身が空のクラス。

```csharp
public sealed class CoreAssemblyMarker;
```

セミコロンで終わる書き方は C# 12 以降。空のクラスをアセンブリの目印にするのは実務でも使う手で、
`Assembly.Load("HelpDesk.Core")` のように文字列で書かずに済む（文字列はリネームで壊れても気づけない）。

> **このテストの限界**: `GetReferencedAssemblies()` が返すのは、コンパイル結果に実際に焼き込まれた参照。
> csproj に参照を足しただけで一度も使わなければ、ここには現れない。
> 「使ってしまったら落ちる」検査であって、「書いたら落ちる」検査ではない。

---

## `Program.cs` — 注目すべきは書かれていないこと

```csharp
builder.Services.AddRazorComponents();
...
app.MapRazorComponents<App>();
```

`AddInteractiveServerComponents()` が**無い**。
作成時に `--interactivity None` を指定したので、このアプリは静的 SSR だけで動いている
（詳しくは [なぜ今はボタンを押しても動かないのか](rendermodes.md)）。

これはステージ 4 のための仕込み。そこで対話機能を入れるとき、**この差分だけ**を見れば
「対話機能を有効にするとは具体的に何を足すことか」が分かる。
最初から `--interactivity Server` で作ると境目が見えない。

---

## テンプレートから消したもの／残したもの

**消した**: `Weather.razor`（サンプル）、`Class1.cs`、`UnitTest1.cs`、NavMenu の Weather リンク

**残した**: `Error.razor` / `NotFound.razor` とその設定（ステージ 8 で扱う）、
`BlazorDisableThrowNavigationException`（ステージ 3 でフォーム送信後のリダイレクトを書くときに効く）

---

## 手を動かす課題

読むだけより、壊して直すほうが残る。いずれも `git checkout .` で戻せる。

**課題 1 — 依存を逆向きにしてみる**

`HelpDesk.Core.csproj` に Web への参照を足してビルドする。

```xml
<ProjectReference Include="..\HelpDesk.Web\HelpDesk.Web.csproj" />
```

「気をつける」ではなくビルドシステムが止めてくれる、という感触を持っておく。

**課題 2 — アーキテクチャテストを落としてみる**

Core の csproj に `<FrameworkReference Include="Microsoft.AspNetCore.App" />` を足し、
**さらにその型を実際に使う**コードを書いて `dotnet test`。

そのうえで**そのコードだけ消して `FrameworkReference` は残す**と、テストは通ってしまう。
上の「このテストの限界」を実際に見ておく。

**課題 3 — `Nullable` を切ってみる**

`Directory.Build.props` の `enable` を `disable` に変え、`ArchitectureTests.cs` の
`name is not null &&` を消してビルド。`enable` に戻すと今度は何が起きるか。

---

## 次のステージ

ステージ 1 で `HelpDesk.Core` にチケットのドメインを置き、静的 SSR のまま一覧と詳細を作る。

---

<sub>この文書の事実主張は `./scripts/verify-claims.sh` で再検証できます。</sub>
