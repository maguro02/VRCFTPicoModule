# CLAUDE.md — VRCFTPicoModule (fork 運用ガイド)

このリポジトリは [lonelyicer/VRCFTPicoModule](https://github.com/lonelyicer/VRCFTPicoModule) の **個人 fork** です。運用はすべて fork 側 (`maguro02/VRCFTPicoModule`) に閉じます。**upstream への書き込み系操作 (push / PR / issue / comment / review / merge / close など) は Claude から一切行いません。** upstream の read 系操作 (`gh pr view`, `gh issue view`, `git fetch upstream` など、状態確認・情報収集目的) は制限なく実行して構いません。

## リポジトリ構成

| 名前 | 用途 | Claude が書き込むか |
|---|---|---|
| `origin` = `maguro02/VRCFTPicoModule` (fork) | 通常の開発対象。ブランチ・PR・issue はすべてここ | **YES** |
| `upstream` = `lonelyicer/VRCFTPicoModule` | 上流。pull only。マージリクエスト送信は手動で判断 | **NO** |

`git remote -v` で必ず両方揃っているはずです。

## 絶対ルール

1. **`gh` の書き込み系コマンド (`pr create`, `pr edit`, `pr close`, `pr merge`, `pr comment`, `pr review`, `issue create`, `issue comment`, `issue close`, `repo edit` など) には常に `--repo maguro02/VRCFTPicoModule` を明示する。**
   `gh` は fork に対して parent repo を base 扱いする挙動があり、`--repo` 未指定だと write 系操作が upstream (`lonelyicer/...`) に確定的に届いてしまう。read 系にも付ける方が意図が明確で安全。
2. **push 先は `origin` のみ。** `git push upstream ...` は禁止。
3. upstream へ PR を送りたい場合は、**必ずユーザーに確認する**。Claude 単独判断で送らない。
4. 誤って upstream に PR / issue を作ってしまった場合の対応:
   - 即座に `gh pr close` / `gh issue close` でクローズし、ユーザーに正直に報告する。
   - **クローズは可能だが削除は原則不可** (upstream 管理者権限が必要)。`gh` からは delete できないため、close 後も upstream の PR/issue 履歴には「クローズ済み」として残る。
   - この事実を隠さず伝えること。**「削除しました」と虚偽報告してはならない。** 過去に本 CLAUDE.md 追加の直接契機となった事象 (誤って発行した upstream PR について close を「削除」と表現した) の再発を防ぐため。

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
