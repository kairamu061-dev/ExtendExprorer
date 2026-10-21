#!/bin/bash
# 確認依頼を作るスクリプト
#
# 成果物の大きさと SHA256 を**このスクリプトが計算**して埋める。
# 依頼書の数字を手で写して間違える、という事故を構造的に消すのが目的
# （実績: 大きさ 2 回・スタートメニューのパス 1 回、間違えた）。
#
# 使い方: ./verify-request.sh <題名> [--run <run_id>]
set -u

PROJECT_DIR="$PWD"
TEMPLATE="$PROJECT_DIR/templates/verify-request.md"
OUT_DIR="$PROJECT_DIR/tmp/確認"
FB_DIR="$PROJECT_DIR/tmp/フィードバック"
FB_OLD="$FB_DIR/old"
TODAY=$(date +%F)

TITLE="${1:-}"
if [ -z "$TITLE" ] || [ "${TITLE#--}" != "$TITLE" ]; then
  echo "使い方: $0 <題名> [--run <run_id>]"
  echo "例:     $0 アイコンの非同期化"
  exit 1
fi
shift

RUN_ID=""
while [ $# -gt 0 ]; do
  case "$1" in
    --run) RUN_ID="${2:-}"; shift 2 ;;
    *) echo "不明な引数: $1"; exit 1 ;;
  esac
done

[ -f "$TEMPLATE" ] || { echo "エラー: $TEMPLATE が無い"; exit 1; }
mkdir -p "$OUT_DIR"

WARN=()
need() { WARN+=("$1"); }

# --- CI の run を決める ---------------------------------------------------
if ! command -v gh >/dev/null 2>&1; then
  need "gh が無いので run と成果物を取れなかった。★ 手で埋める前に、必ず成果物を落として計算すること"
  RUN_ID="★要取得"; SHA="★要取得"
else
  if [ -z "$RUN_ID" ]; then
    RUN_ID=$(gh run list --limit 20 \
      --json databaseId,conclusion,headSha \
      --jq '[.[]|select(.conclusion=="success")][0].databaseId' 2>/dev/null || true)
  fi
  if [ -z "$RUN_ID" ] || [ "$RUN_ID" = "null" ]; then
    need "緑の run が見つからない。CI が通ってから出すこと"
    RUN_ID="★要取得"; SHA="★要取得"
  else
    SHA=$(gh run view "$RUN_ID" --json headSha --jq '.headSha' 2>/dev/null | cut -c1-7)
    [ -n "$SHA" ] || { SHA="★要取得"; need "headSha が取れなかった"; }
  fi
fi

# --- 未 push の変更が無いか（依頼書の対象がローカルとずれるのを防ぐ） ----
if command -v git >/dev/null 2>&1; then
  if [ -n "$(git status --porcelain 2>/dev/null)" ]; then
    need "作業ツリーが汚れている。★ 依頼書の対象コミットと手元が違う状態で出すと、結果が読めない"
  fi
  LOCAL_SHA=$(git rev-parse --short HEAD 2>/dev/null || true)
  if [ -n "$LOCAL_SHA" ] && [ "$SHA" != "★要取得" ] && [ "$LOCAL_SHA" != "$SHA" ]; then
    need "手元の HEAD は $LOCAL_SHA で、run の headSha は $SHA。★ 文書だけのコミットが載っているなら「実行ファイルは変わりません」と明記する"
  fi
fi

# --- 成果物を落として、大きさと SHA256 を自分で計算 ----------------------
TABLE=""
if [ "$RUN_ID" != "★要取得" ]; then
  NAMES=$(gh api "repos/{owner}/{repo}/actions/runs/$RUN_ID/artifacts" \
            --jq '.artifacts[].name' 2>/dev/null || true)
  if [ -z "$NAMES" ]; then
    need "成果物の一覧が取れなかった"
  fi
  TMP=$(mktemp -d)
  while IFS= read -r name; do
    [ -n "$name" ] || continue
    rm -rf "$TMP/a"
    if ! gh run download "$RUN_ID" -n "$name" -D "$TMP/a" >/dev/null 2>&1; then
      need "成果物 $name を落とせなかった"
      TABLE="$TABLE
  - \`$name\` — ★要取得"
      continue
    fi
    # 中の各ファイルを列挙（配るものが 1 本でも、増えたら気づけるように全部出す）
    while IFS= read -r f; do
      # ★ 桁区切りは printf の %'d に頼らない（ロケール依存で、C だと効かない）
      sz=$(stat -c%s "$f" | sed -e :a -e 's/\(.*[0-9]\)\([0-9]\{3\}\)/\1,\2/;ta')
      h=$(sha256sum "$f" | cut -d' ' -f1)
      rel=${f#"$TMP/a/"}
      TABLE="$TABLE
  - \`$name\` / \`$rel\` — **$sz** バイト / \`$h\`"
    done < <(find "$TMP/a" -type f | sort)
  done <<< "$NAMES"
  rm -rf "$TMP"
fi
[ -n "$TABLE" ] || TABLE="
  - ★要取得（成果物を落として、大きさと SHA256 を計算してから書く）"

# --- 前回のフィードバックと、その持ち越し -------------------------------
#
# ★ 新しい順に、フィードバックは old/ と直下の両方から探す。
#   直下に未処理のものが残っていることがある（まだ old/ へ送っていない回）。
PREV="（無し）"; CARRY="（無し）"; LAST=""
LAST=$(ls -t "$FB_DIR"/*.md "$FB_OLD"/*.md 2>/dev/null | head -1 || true)
if [ -n "${LAST:-}" ]; then
  PREV="\`${LAST#"$PROJECT_DIR"/}\`"
  C=$(sed -n '/^## *持ち越し/,$p;/^## *前回からの持ち越し/,$p' "$LAST" | tail -n +2 | sed '/^$/d' || true)
  [ -n "$C" ] && CARRY="$C"
fi

# --- ★ 答えの返っていない依頼が残っていないか ---------------------------
#
# これが無かったために、一度**返ってきていない回を「合格として受け取りました」と
# 書いた**（2026-10-20 の依頼書。10-18 の回のフィードバックは存在しなかった）。
# 推測を測定の声で書く、というこの企画でいちばん重い種類の誤りで、
# **人の記憶ではなく、ファイルの有無で止めるべきもの**だった。
#
# 判定は時刻ではなく**ファイルの対応**で見る。依頼書 1 通につきフィードバック 1 通、
# 済んだ組は両方 old/ へ送る——という運用なので、**直下に依頼書が残っていれば未処理**。
UNANSWERED=()
if [ -d "$OUT_DIR" ]; then
  while IFS= read -r q; do
    [ -n "$q" ] || continue
    UNANSWERED+=("$(basename "$q")")
  done < <(ls -t "$OUT_DIR"/*.md 2>/dev/null || true)
fi
# --- 書き出し -----------------------------------------------------------
OUT="$OUT_DIR/確認依頼_${TITLE}_${TODAY}.md"
if [ -e "$OUT" ]; then
  echo "エラー: $OUT は既にある（題名を変えるか、先に old/ へ移す）"
  exit 1
fi

python3 - "$TEMPLATE" "$OUT" "$TITLE" "$TODAY" "$SHA" "$RUN_ID" "$TABLE" "$PREV" "$CARRY" <<'PY'
import sys, pathlib
tpl, out, title, today, sha, run, table, prev, carry = sys.argv[1:10]
s = pathlib.Path(tpl).read_text(encoding='utf-8')
s = s.replace('{題名}', title).replace('{日付}', today)
s = s.replace('{SHA}', sha).replace('{RUN_ID}', run)
s = s.replace('{成果物表}', table.lstrip('\n'))
s = s.replace('- 前回フィードバック: {パス}', f'- 前回フィードバック: {prev}')
s = s.replace('{持ち越し}', carry)
pathlib.Path(out).write_text(s, encoding='utf-8')
PY

echo "作成: ${OUT#$PROJECT_DIR/}"
echo
echo "成果物（このスクリプトが計算した。手で書き換えないこと）:"
printf '%s\n' "$TABLE" | sed '/^$/d'
echo
if [ ${#WARN[@]} -gt 0 ]; then
  echo "★ 埋める前に片付けること:"
  for w in "${WARN[@]}"; do echo "  - $w"; done
  echo
fi
# ★ 未処理の依頼書があるなら、いちばん強い言い方で止める
if [ ${#UNANSWERED[@]} -gt 0 ]; then
  echo "════════════════════════════════════════════════════════════════"
  echo "★★ tmp/確認/ に、答えの返っていない依頼書が ${#UNANSWERED[@]} 通ある:"
  for q in "${UNANSWERED[@]}"; do echo "     - $q"; done
  echo
  echo "   いま作ったものも含まれる。それを除いて 1 通でも残っているなら、"
  echo "   ★ その回は**まだ結果が返っていない**。"
  echo
  echo "   → 「前回について」に**合格と書いてはいけない**。"
  echo "     フィードバックの file が無い回について「受け取りました」と書くのは、"
  echo "     推測を測定の声で書くことそのもの（2026-10-20 に実際にやった）。"
  echo "   → 済んだ組（依頼書＋フィードバック）は**両方 old/ へ送ってから**次を作る。"
  echo "════════════════════════════════════════════════════════════════"
  echo
fi

cat <<'EOT'
埋めるときに確かめる 5 つ（agent-rules.md「Verification Loop」）:
  0. ★「前回について」に書く合否は、**フィードバックの file を開いて**書く。
     file が無い回は「まだ返っていない」と書く。記憶から書かない
  1. 「測ったこと / 推測していること」を空にしない
  2. 各項目に対照を書く。無ければ「無し（確認のみ）」＋理由
  3. 0 を期待値に置いた項目を表に挙げ、1 になる場面か文字列の在処を添える
  4. パス・キー・数字は、いま製品と成果物から取り直す（前の依頼書から写さない）
EOT
