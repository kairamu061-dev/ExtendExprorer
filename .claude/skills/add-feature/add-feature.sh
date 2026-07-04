#!/bin/bash
# 機能エリアを追加するスクリプト
# 使い方: ./add-feature.sh <パス>
# 例:     ./add-feature.sh auth
#         ./add-feature.sh auth/login

set -e

PROJECT_DIR="$PWD"
DOCS_DIR="$PROJECT_DIR/docs"
TEMPLATES_DIR="$PROJECT_DIR/templates"
INDEX_FILE="$DOCS_DIR/index.md"

# 引数チェック
if [ -z "$1" ]; then
  echo "使い方: $0 <パス>"
  echo "例: $0 auth"
  echo "例: $0 auth/login"
  exit 1
fi

FEATURE_PATH="$1"
FEATURE_NAME=$(basename "$FEATURE_PATH")
TARGET_DIR="$DOCS_DIR/$FEATURE_PATH"

# 既存チェック
if [ -d "$TARGET_DIR" ]; then
  echo "エラー: '$TARGET_DIR' は既に存在します"
  exit 1
fi

# フォルダ作成
mkdir -p "$TARGET_DIR"
echo "作成: $TARGET_DIR"

# テンプレートファイルをコピー
for template in "$TEMPLATES_DIR"/*.md; do
  [ -f "$template" ] || continue
  filename=$(basename "$template")
  cp "$template" "$TARGET_DIR/$filename"
  echo "  コピー: $filename"
done

# インデントを計算（パスの深さに応じて2スペース）
DEPTH=$(echo "$FEATURE_PATH" | awk -F'/' '{print NF-1}')
INDENT=""
for i in $(seq 1 "$DEPTH"); do
  INDENT="  $INDENT"
done

# index.md に追記
RELATIVE_PATH="./$FEATURE_PATH/overview.md"
echo "${INDENT}- [$FEATURE_NAME]($RELATIVE_PATH)" >> "$INDEX_FILE"
echo "index.md に追記: ${INDENT}- [$FEATURE_NAME]"

echo ""
echo "完了: '$FEATURE_PATH' を追加しました"
