# CLAUDE.md — VRCFTPicoModule (fork 運用ガイド)

このリポジトリは [lonelyicer/VRCFTPicoModule](https://github.com/lonelyicer/VRCFTPicoModule) の **個人 fork** です。運用はすべて fork 側 (`maguro02/VRCFTPicoModule`) に閉じます。**upstream には Claude から一切書き込みません。**

## リポジトリ構成

| 名前 | 用途 | Claude が書き込むか |
|---|---|---|
| `origin` = `maguro02/VRCFTPicoModule` (fork) | 通常の開発対象。ブランチ・PR・issue はすべてここ | **YES** |
| `upstream` = `lonelyicer/VRCFTPicoModule` | 上流。pull only。マージリクエスト送信は手動で判断 | **NO** |

`git remote -v` で必ず両方揃っているはずです。

## 絶対ルール

1. **`gh` コマンドには常に `--repo maguro02/VRCFTPicoModule` を明示する。**
   `gh pr create` / `gh issue create` などは、明示しないと `upstream` を対象にすることがあります (fork のデフォルト挙動)。
2. **push 先は `origin` のみ。** `git push upstream ...` は禁止。
3. upstream へ PR を送りたい場合は、**必ずユーザーに確認する**。Claude 単独判断で送らない。
4. 誤って upstream に PR / issue を作ってしまった場合は、即座にクローズしてユーザーに報告する。

## よく使うコマンドの雛形

```bash
# PR 作成 (必ず --repo を付ける)
gh pr create --repo maguro02/VRCFTPicoModule --base master --head <branch> \
  --draft --title "..." --body "..."

# PR 一覧
gh pr list --repo maguro02/VRCFTPicoModule

# issue 作成
gh issue create --repo maguro02/VRCFTPicoModule --title "..." --body "..."

# ブランチ push (origin 固定)
git push -u origin <branch>

# upstream から最新を取り込む (fetch のみ、merge はユーザー判断)
git fetch upstream
```

## upstream からの取り込み

upstream の変更を取り込みたい時は `git fetch upstream` までを Claude が行い、`git merge upstream/master` や rebase はユーザーの明示指示があってから実行します。

## Base branch

`master`。ブランチ命名は `<type>/<summary>` 形式 (例: `docs/fork-workflow-claude-md`, `feat/foo`, `fix/bar`)。
