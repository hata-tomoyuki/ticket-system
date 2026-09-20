#!/usr/bin/env bash
#
# ステージごと・ステップごとの差分を表示する。
#
#   ./scripts/stage-diff.sh 2b           ステップ 2b の差分（コードのみ）
#   ./scripts/stage-diff.sh 2            ステージ 2 全体の差分
#   ./scripts/stage-diff.sh 2b --stat    ファイルごとの増減だけ
#   ./scripts/stage-diff.sh 2b --files   変更されたファイル名だけ
#   ./scripts/stage-diff.sh 2b --all     docs やスクリプトも含めた全差分
#
# 既定では EF が自動生成した Migrations/ を除外する（読む対象ではないため）。
#
set -euo pipefail

target="${1:-}"
mode="${2:---code}"

if [ -z "$target" ]; then
  echo "使い方: $0 <ステージ番号 または ステップ> [--code|--stat|--files|--all]" >&2
  echo >&2
  echo "利用できるタグ:" >&2
  git tag -l 'stage-*' | sort | sed 's/^/  /' >&2
  exit 1
fi

to="stage-${target}"

if ! git rev-parse -q --verify "refs/tags/$to" > /dev/null; then
  echo "タグ $to がありません。" >&2
  echo "利用できるタグ:" >&2
  git tag -l 'stage-*' | sort | sed 's/^/  /' >&2
  exit 1
fi

# 2b → 2a、2a → 1、3 → 2 のように 1 つ前を求める
if [[ "$target" =~ ^([0-9]+)([a-z])$ ]]; then
  num="${BASH_REMATCH[1]}"; letter="${BASH_REMATCH[2]}"
  if [ "$letter" = "a" ]; then
    from="stage-$((num - 1))"
  else
    prev=$(printf "\\$(printf '%03o' $(( $(printf '%d' "'$letter") - 1 )))")
    from="stage-${num}${prev}"
  fi
else
  from="stage-$((target - 1))"
fi

if ! git rev-parse -q --verify "refs/tags/$from" > /dev/null; then
  echo "$to の 1 つ前にあたる $from がありません。" >&2
  exit 1
fi

generated=':(exclude)src/HelpDesk.Infrastructure/Migrations'

case "$mode" in
  --stat)  git diff --stat "$from" "$to" -- src tests "$generated" ;;
  --files) git diff --name-status "$from" "$to" -- src tests "$generated" ;;
  --all)   git diff "$from" "$to" ;;
  --code)  git diff "$from" "$to" -- src tests "$generated" ;;
  *)       echo "不明なオプション: $mode" >&2; exit 1 ;;
esac
