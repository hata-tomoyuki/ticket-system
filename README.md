# HelpDesk

社内からの問い合わせをチケットとして受け付け、担当者が対応するアプリ。
C# / Blazor の学習用に、1 ステージずつ機能を足しながら育てていく。

## 動かす

```bash
dotnet run --project src/HelpDesk.Web
```

```bash
dotnet test HelpDesk.slnx
```

## 構成

```
HelpDesk.slnx              ソリューション
Directory.Build.props      全プロジェクト共通のビルド設定
global.json                使う .NET SDK の固定
src/HelpDesk.Core/         ドメイン。他のどのプロジェクトにも依存しない
src/HelpDesk.Web/          Blazor Web App。Core に依存する
tests/HelpDesk.Tests/      テスト。Core に依存する
docs/                      各ステージの解説
scripts/verify-claims.sh   docs の主張を再実行して検証する
```

## 文書の主張を検証する

docs/ に書いた事実の主張は、すべて実行して再検証できます。

```bash
./scripts/verify-claims.sh
```

一時ディレクトリへ作業ツリーを複製してから壊すので、リポジトリ自体は変更されません。
30 件のチェックが走ります。

## 補足

- [なぜ今はボタンを押しても動かないのか](docs/rendermodes.md) — 静的 SSR と対話レンダリングの違い

## ステージ

| # | 内容 | 状態 |
|---|---|---|
| 0 | ソリューション構成 | 完了 → [docs/stage-0.md](docs/stage-0.md) |
| 1 | 静的 SSR でチケット一覧・詳細 | 完了 → [docs/stage-1.md](docs/stage-1.md) |
| 2 | EF Core + SQLite | これから |
| 3 | 起票・編集フォームと検証 | |
| 4 | InteractiveServer の部分導入 | |
| 5 | ログインとロール | |
| 6 | 更新のリアルタイム反映 | |
| 7 | テスト（xUnit / bUnit） | |
| 8 | 仕上げ（QuickGrid・エラー処理・ログ） | |
