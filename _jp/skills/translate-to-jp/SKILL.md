---
name: translate-to-jp
description: 英語のエージェントファイルを日本語に翻訳して _jp/ フォルダに保存する。
disable-model-invocation: true
allowed-tools: Read, Write, Glob
---

英語のエージェントファイルを日本語に翻訳して `_jp/` フォルダに保存する。

## 対象パターン

以下のパターンに一致するファイルをすべて翻訳する：

| パターン | 例 |
|---------|---------|
| プロジェクトルートの `*.md`（`CLAUDE.md` を除く） | `agent-rules.md` → `_jp/agent-rules.md` |
| `.claude/skills/*/SKILL.md` | `.claude/skills/add-feature/SKILL.md` → `_jp/skills/add-feature/SKILL.md` |

今後新しいエージェントファイルが追加された場合も、これらのパターンに一致する限り自動的に対象となる。

## 翻訳ルール

- 本文・説明はすべて自然な日本語に翻訳する
- 以下は翻訳せずそのまま残す：
  - コードブロックおよびインラインコード
  - ファイルパス・ディレクトリ名
  - コマンド名・スキル名
  - YAMLフロントマターのキー（例: `name:`、`description:`、`allowed-tools:`）
  - `name` と `allowed-tools` のフロントマター値
- `description` と `argument-hint` のフロントマター値は翻訳する

## 手順

1. Glob で対象パターンに一致するファイルをすべて検索する
2. 各ファイルの内容を読み込む
3. 上記ルールに従って日本語に翻訳する
4. `_jp/` 以下の対応するパスに書き込む
5. 翻訳したファイルを報告する
