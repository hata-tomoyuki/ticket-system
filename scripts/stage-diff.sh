#!/usr/bin/env bash
#
# ステージごとの差分を表示する。
#
#   ./scripts/stage-diff.sh 2          ステージ 2 のコード差分（src/ と tests/ のみ）
#   ./scripts/stage-diff.sh 2 --stat   ファイル一覧だけ
#   ./scripts/stage-diff.sh 2 --all    docs やスクリプトも含めた全差分
#   ./scripts/stage-diff.sh 2 --files  変更されたファイル名だけ
#
set -euo pipefail

stage="${1:-}"
mode="${2:---code}"

if [ -z "$stage" ]; then
  echo "使い方: $0 <ステージ番号> [--code|--stat|--all|--files]" >&2
  echo >&2
  echo "利用できるタグ:" >&2
  git tag -l 'stage-*' | sed 's/^/  /' >&2
  exit 1
fi

# 2a のようなサブステップ指定にも対応する
if [[ "$stage" =~ ^([0-9]+)([a-z])$ ]]; then
  to="stage-${stage}"
  prev="${BASH_REMATCH[2]}"
  if [ "$prev" = "a" ]; then
    from="stage-$(( ${BASH_REMATCH[1]} - 1 ))"
  else
    from="stage-${BASH_REMATCH[1]}$(printf "\\$(printf '%03o' $(( $(printf '%d' "'$prev") - 1 )))")"
  fi
  git diff "$from" "$to" -- src tests ':(exclude)src/HelpDesk.Infrastructure/Migrations'
  exit 0
fi

from="stage-$((stage - 1))"
to="stage-${stage}"

if ! git rev-parse -q --verify "refs/tags/$to" > /dev/null; then
  echo "タグ $to がありません。まだそのステージは作られていません。" >&2
  exit 1
fi

if [ "$stage" -eq 0 ]; then
  echo "ステージ 0 は最初のコミットなので、差分ではなく全体を見てください:" >&2
  echo "  git show stage-0" >&2
  exit 1
fi

case "$mode" in
  --stat)  git diff --stat "$from" "$to" ;;
  --files) git diff --name-status "$from" "$to" ;;
  --all)   git diff "$from" "$to" ;;
  --code)  git diff "$from" "$to" -- src tests ':(exclude)src/HelpDesk.Infrastructure/Migrations' ;;
  *)       echo "不明なオプション: $mode" >&2; exit 1 ;;
esac
