# 認証 設計

## 技術選定

| 技術 | 用途 | 選定理由 |
|------|------|----------|
| Supabase Auth | 認証基盤 | プロジェクト全体で Supabase を採用済み。追加ライブラリ不要 |
| Next.js Middleware | ルートガード | サーバサイドでセッション検証してリダイレクトできる |
| `@supabase/ssr` | SSR 対応クライアント | App Router でクッキーベースのセッションを扱うための公式パッケージ |

## アーキテクチャ

```
/login, /register（公開ルート）
  └── LoginForm / RegisterForm（Client Component）
        └── supabase.auth.signInWithPassword / signUp

middleware.ts
  └── リクエストごとにセッション検証
        ├── 未認証 → /login にリダイレクト
        └── 認証済み → そのまま通過

/tasks 以下（保護ルート）
  └── Server Component でセッション取得
        └── ユーザ情報を元に RLS でデータ絞り込み
```

## データ構造

Supabase Auth が管理する `auth.users` テーブルを使用。アプリ側でユーザテーブルは持たない。

```
auth.users（Supabase 管理）
  - id: uuid
  - email: string
  - created_at: timestamp
```

## インターフェース

```typescript
// ログイン
const { data, error } = await supabase.auth.signInWithPassword({
  email: string,
  password: string,
})

// 登録
const { data, error } = await supabase.auth.signUp({
  email: string,
  password: string,
})

// ログアウト
const { error } = await supabase.auth.signOut()

// セッション取得（Server Component）
const { data: { user } } = await supabase.auth.getUser()
```

## 依存関係

| ライブラリ / サービス | 用途 |
|-----------------------|------|
| `@supabase/supabase-js` | Supabase クライアント |
| `@supabase/ssr` | App Router でのクッキーベースセッション管理 |
| Supabase Auth | 認証サービス本体 |
