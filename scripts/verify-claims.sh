#!/usr/bin/env bash
#
# docs/ に書いた「事実の主張」を、その場で再実行して検証する。
#
#   ./scripts/verify-claims.sh
#
# 各チェックは、必要なら作業ツリーを一時ディレクトリへ複製してから壊す。
# このリポジトリ自体は変更しない。
#
set -uo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORK="$(mktemp -d)"
PORT="${VERIFY_PORT:-5399}"
trap 'pkill -f "HelpDesk.Web" >/dev/null 2>&1; rm -rf "$WORK"' EXIT

pass=0; fail=0

check() { # 説明, 期待値, 実際の値
  if [ "$2" = "$3" ]; then
    printf '  \033[32mPASS\033[0m  %s\n' "$1"; pass=$((pass + 1))
  else
    printf '  \033[31mFAIL\033[0m  %s\n        期待: %s\n        実際: %s\n' "$1" "$2" "$3"; fail=$((fail + 1))
  fi
}

# 作業ツリーを複製する（bin / obj は除く）
clone() { # 複製先の名前
  local dir="$WORK/$1"
  mkdir -p "$dir"
  rsync -a --exclude bin --exclude obj --exclude .git "$REPO/" "$dir/"
  echo "$dir"
}

start_app() { # アプリのディレクトリ
  pkill -f "HelpDesk.Web" >/dev/null 2>&1
  sleep 1
  (cd "$1" && dotnet run --project src/HelpDesk.Web --urls "http://localhost:$PORT" > "$WORK/server.log" 2>&1 &)
  for _ in $(seq 1 40); do
    if curl -s -o /dev/null "http://localhost:$PORT/" 2>/dev/null; then return 0; fi
    sleep 1
  done
  return 1
}

code()  { curl -s -o /dev/null -w '%{http_code}' "http://localhost:$PORT$1"; }
count() { curl -s "http://localhost:$PORT$1" | grep -oE '[0-9]+ 件' | head -1; }

echo
echo "=============================================================="
echo " docs の主張を検証します（$(date '+%Y-%m-%d %H:%M')）"
echo "=============================================================="

# --------------------------------------------------------------------
echo
echo "[1] ビルドとテスト"
# --------------------------------------------------------------------
( cd "$REPO" && dotnet build HelpDesk.slnx > "$WORK/build.log" 2>&1 )
check "警告 0 でビルドが通る" "0" \
      "$(grep -oE '[0-9]+ 個の警告' "$WORK/build.log" | head -1 | grep -oE '[0-9]+')"

( cd "$REPO" && dotnet test HelpDesk.slnx > "$WORK/test.log" 2>&1 )
check "テストが 14 件すべて合格する" "失敗: 0、合格: 14" \
      "$(grep -oE '失敗:[[:space:]]*[0-9]+、合格:[[:space:]]*[0-9]+' "$WORK/test.log" | head -1 | tr -s ' ')"

# --------------------------------------------------------------------
echo
echo "[2] ルーティングと一覧の絞り込み  (stage-1.md)"
# --------------------------------------------------------------------
if start_app "$REPO"; then
  check "GET /tickets が 200"                    "200" "$(code /tickets)"
  check "GET /tickets/3 が 200"                  "200" "$(code /tickets/3)"
  check "存在しない ID は 404"                    "404" "$(code /tickets/999)"
  check "数値でない ID は 404（:int 制約）"        "404" "$(code /tickets/abc)"
  check "絞り込みなしは 5 件"                     "5 件" "$(count /tickets)"
  check "?status=Open は 2 件"                    "2 件" "$(count '/tickets?status=Open')"
  check "?status=Resolved は 1 件"                "1 件" "$(count '/tickets?status=Resolved')"
  check "不正な status は全件にフォールバック"      "5 件" "$(count '/tickets?status=xyz')"
else
  check "アプリが起動する" "起動" "起動しない"
fi
pkill -f "HelpDesk.Web" >/dev/null 2>&1

# --------------------------------------------------------------------
echo
echo "[3] 静的 SSR では @onclick が消える  (rendermodes.md)"
# --------------------------------------------------------------------
d="$(clone onclick)"
cat >> "$d/src/HelpDesk.Web/Components/Pages/Home.razor" <<'EOF'

<button class="btn btn-secondary" @onclick="Increment">+1</button>
<span id="count">@count</span>

@code {
    private int count = 0;
    private void Increment() => count++;
}
EOF
( cd "$d" && dotnet build src/HelpDesk.Web/HelpDesk.Web.csproj > "$WORK/onclick-build.log" 2>&1 )
check "@onclick を書いてもビルド警告は出ない" "0" \
      "$(grep -oE '[0-9]+ 個の警告' "$WORK/onclick-build.log" | head -1 | grep -oE '[0-9]+')"
if start_app "$d"; then
  # NavMenu.razor には素の JavaScript の onclick があるため、button 要素だけを見る
  check "+1 ボタンに onclick 属性が無い" "0" \
        "$(curl -s "http://localhost:$PORT/" | grep -oE '<button[^>]*>[^<]*</button>' | grep -c 'onclick' || true)"
  check "Blazor コンポーネントマーカーが 0 個" "0" \
        "$(curl -s "http://localhost:$PORT/" | grep -c 'Blazor:' || true)"
fi
pkill -f "HelpDesk.Web" >/dev/null 2>&1

# --------------------------------------------------------------------
echo
echo "[4] 対話モードにするとマーカーと SignalR が現れる  (rendermodes.md)"
# --------------------------------------------------------------------
d="$(clone interactive)"
cat >> "$d/src/HelpDesk.Web/Components/Pages/Home.razor" <<'EOF'

<button class="btn btn-secondary" @onclick="Increment">+1</button>
<span id="count">@count</span>

@code {
    private int count = 0;
    private void Increment() => count++;
}
EOF
python3 - "$d" <<'PY'
import sys, pathlib
d = pathlib.Path(sys.argv[1])
p = d / "src/HelpDesk.Web/Program.cs"; s = p.read_text()
s = s.replace("builder.Services.AddRazorComponents();",
              "builder.Services.AddRazorComponents()\n    .AddInteractiveServerComponents();")
s = s.replace("app.MapRazorComponents<App>();",
              "app.MapRazorComponents<App>()\n    .AddInteractiveServerRenderMode();")
p.write_text(s)
p = d / "src/HelpDesk.Web/Components/Pages/Home.razor"; s = p.read_text()
p.write_text(s.replace('@page "/"', '@page "/"\n@rendermode InteractiveServer'))
PY
if start_app "$d"; then
  check "Blazor コンポーネントマーカーが 2 個現れる" "2" \
        "$(curl -s "http://localhost:$PORT/" | grep -c 'Blazor:' || true)"
  check "マーカーの type が server" "server" \
        "$(curl -s "http://localhost:$PORT/" | grep -oE '"type":"[a-z]+"' | head -1 | grep -oE '[a-z]+"$' | tr -d '\"')"
  check "ボタンの HTML は静的 SSR と同じ（onclick 属性なし）" "0" \
        "$(curl -s "http://localhost:$PORT/" | grep -oE '<button[^>]*>[^<]*</button>' | grep -c 'onclick' || true)"
fi
pkill -f "HelpDesk.Web" >/dev/null 2>&1

# --------------------------------------------------------------------
echo
echo "[5] enum はクエリ文字列に束縛できない  (stage-1.md)"
# --------------------------------------------------------------------
d="$(clone enumquery)"
python3 - "$d" <<'PY'
import sys, pathlib
p = pathlib.Path(sys.argv[1]) / "src/HelpDesk.Web/Components/Pages/Tickets.razor"
s = p.read_text()
s = s.replace("public string? StatusQuery { get; set; }", "public TicketStatus? StatusQuery { get; set; }")
s = s.replace("""        TicketStatus? status = Enum.TryParse<TicketStatus>(StatusQuery, ignoreCase: true, out var parsed)
            ? parsed
            : null;

        tickets = await Repository.ListAsync(status);""",
              "        tickets = await Repository.ListAsync(StatusQuery);")
s = s.replace("value == StatusQuery", "value == StatusQuery?.ToString()")
p.write_text(s)
PY
( cd "$d" && dotnet build src/HelpDesk.Web/HelpDesk.Web.csproj > "$WORK/enum-build.log" 2>&1 )
check "enum で受けてもビルドは警告 0 で通る" "0" \
      "$(grep -oE '[0-9]+ 個の警告' "$WORK/enum-build.log" | head -1 | grep -oE '[0-9]+')"
if start_app "$d"; then
  check "実行時に /tickets が 500 になる" "500" "$(code /tickets)"
  check "例外は Querystring values cannot be parsed" "1" \
        "$(grep -c 'Querystring values cannot be parsed' "$WORK/server.log" || true)"
fi
pkill -f "HelpDesk.Web" >/dev/null 2>&1

# --------------------------------------------------------------------
echo
echo "[6] 依存の向きは仕組みで守られている  (stage-0.md)"
# --------------------------------------------------------------------
d="$(clone circular)"
cat > "$d/src/HelpDesk.Core/HelpDesk.Core.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\HelpDesk.Web\HelpDesk.Web.csproj" />
  </ItemGroup>
</Project>
EOF
( cd "$d" && dotnet build HelpDesk.slnx > "$WORK/circular.log" 2>&1 )
check "Core→Web の参照を足すとビルドが失敗する" "1" \
      "$(grep -c 'MSB4006' "$WORK/circular.log" > /dev/null && echo 1 || echo 0)"

d="$(clone archtest)"
cat > "$d/src/HelpDesk.Core/HelpDesk.Core.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
</Project>
EOF
cat > "$d/src/HelpDesk.Core/Oops.cs" <<'EOF'
using Microsoft.AspNetCore.Http;
namespace HelpDesk.Core;
public static class Oops
{
    public static string? PathOf(HttpContext context) => context.Request.Path.Value;
}
EOF
( cd "$d" && dotnet test HelpDesk.slnx > "$WORK/archtest.log" 2>&1 )
check "Core が AspNetCore の型を使うとテストが落ちる" "1" \
      "$(grep -c '失敗 HelpDesk.Tests.ArchitectureTests.Core_は_AspNetCore_に依存しない' "$WORK/archtest.log" || true)"

# --------------------------------------------------------------------
echo
echo "[7] EditorRequired の渡し忘れは警告になる  (stage-1.md)"
# --------------------------------------------------------------------
d="$(clone editorrequired)"
printf '\n<TicketStatusBadge />\n' >> "$d/src/HelpDesk.Web/Components/Pages/Home.razor"
( cd "$d" && dotnet build src/HelpDesk.Web/HelpDesk.Web.csproj > "$WORK/rz2012.log" 2>&1 )
check "Status を渡し忘れると RZ2012 が出る" "1" \
      "$(grep -c 'warning RZ2012' "$WORK/rz2012.log" > /dev/null && echo 1 || echo 0)"
check "RZ2012 はエラーではないのでビルドは成功する" "0" \
      "$(grep -oE '[0-9]+ エラー' "$WORK/rz2012.log" | head -1 | grep -oE '[0-9]+')"

# --------------------------------------------------------------------
echo
echo "[8] C# と .NET の言語仕様  (stage-0.md / stage-1.md)"
# --------------------------------------------------------------------
check "ImplicitUsings に System.Reflection は含まれない" "0" \
      "$(grep -c 'System.Reflection' "$REPO/src/HelpDesk.Core/obj/Debug/net10.0/HelpDesk.Core.GlobalUsings.g.cs" || true)"
check "ImplicitUsings に System.Linq は含まれる" "1" \
      "$(grep -c 'global using System.Linq;' "$REPO/src/HelpDesk.Core/obj/Debug/net10.0/HelpDesk.Core.GlobalUsings.g.cs" || true)"

p="$WORK/langprobe"; mkdir -p "$p"
cat > "$p/p.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>11</LangVersion>
  </PropertyGroup>
</Project>
EOF
echo 'public sealed class Marker;' > "$p/P.cs"
( cd "$p" && dotnet build > "$WORK/lang11.log" 2>&1 )
check "'class X;' は C# 11 では書けない" "1" \
      "$(grep -c 'CS9058' "$WORK/lang11.log" > /dev/null && echo 1 || echo 0)"

p="$WORK/apiprobe"; mkdir -p "$p"
cat > "$p/p.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
EOF
cat > "$p/P.cs" <<'EOF'
public enum Status { Open, InProgress, Resolved }
public static class P
{
    public static Status OutOfRange() => (Status)99;
    public static void Mutate(IReadOnlyList<string> list) => list.Add("x");
}
EOF
( cd "$p" && dotnet build > "$WORK/api.log" 2>&1 )
check "IReadOnlyList に Add は無い（CS1061）" "1" \
      "$(grep -c 'CS1061' "$WORK/api.log" > /dev/null && echo 1 || echo 0)"
check "enum は範囲外の値へキャストできる（エラーにならない）" "0" \
      "$(grep -c 'OutOfRange' "$WORK/api.log" || true)"

# --------------------------------------------------------------------
echo
echo "=============================================================="
printf ' 合格 %d / 失敗 %d\n' "$pass" "$fail"
echo "=============================================================="
echo
[ "$fail" -eq 0 ]
