---
name: verify-request
description: Generate a verification request document for the real-hardware verification session. Use when a change is pushed, CI is green, and behaviour needs confirming on the target OS. Measures the CI artifacts itself so sizes and hashes cannot be mis-transcribed.
argument-hint: <title> (e.g. アイコンの非同期化) [--run <id>]
allowed-tools: Bash
---

Run the following command:

```bash
bash "${CLAUDE_SKILL_DIR}/verify-request.sh" $ARGUMENTS
```

Then open the generated file and fill it in. **Do not edit the 成果物 table** — it was
measured from the artifacts. Every other section is yours to write.

Before reporting done, check all five:

0. **The 前回について section is written from the feedback file, opened and read.**
   If no feedback file exists for the previous round, write 「まだ返っていない」 — never
   a pass. The script lists unanswered requests still sitting in `tmp/確認/`; if any
   remains besides the one just created, that round has not reported. This happened:
   on 2026-10-20 the request claimed 「Y 系・Z 系とも合格として受け取りました」 and named
   a specific item (Y-02) as passed, for a round whose feedback was never written.
   **A fabricated pass is the one error this whole loop exists to prevent** — and it is
   invisible to the reader, because it reads exactly like a real one.
1. **測ったこと / 推測していること** is filled in. An unmarked claim is not allowed.
2. Every item has a **対照** (control). Where none exists, it says 「無し（確認のみ）」
   and the 対照が無い項目 section explains why and what can still be concluded.
3. Every `0` expectation appears in the **`0` を期待値に置いた項目** table with either a
   case that makes it `1`, or evidence the counted string exists in the shipped binary.
4. Facts were **re-derived now**, not copied from a previous document — paths and registry
   keys read from the product, numbers from the artifacts.

## Searching the shipped binary for a string

This is the technique that turns a `0` expectation into a real control. Two rules, both
learned by getting them wrong:

- **Match a prefix, and stop at least one character short of what you care about** —
  the rule is about the **end** of the needle, never the start.
  A full-string UTF-16 search returns `0` for strings that are present — Native AOT packs
  literals with no null terminator, so the **last character runs into the next datum**.
  Measured 2026-10-20: `ShellFileOperations.DeleteCore` full-match = 0 in a binary that
  contains it; prefix `ShellFileOp` finds it. Confirmed again 2026-10-21 by the
  verification session: **`同時=` → 0/0, `同時` → 2/1.** Drop trailing punctuation
  (`=`, `件`) before counting. **A `0` from a full-string search proves nothing.**
- **Interpolated strings are not one literal.** `$"[fileop] 開始 {what} 同時={count}"`
  ships as separate pieces split at each hole, so a search spanning a hole can never
  match. Pick a needle that lies strictly inside one piece.
- **The needle itself is a claim — dump the bytes before concluding anything.**
  A `0` means "these exact bytes are absent", which is also what a mistyped needle gives.
  Worked example: the verification session reported `（ごみ箱へ` as `0/0` and inferred that
  fragments after a hole start one character early. Re-measured 2026-10-23 on the same
  artifact: **`（ごみ箱へ` with a full-width paren is 1; `(ごみ箱へ` with a half-width paren
  is 0.** The inference was a mistyped bracket, and the two are indistinguishable at a
  glance in most fonts. Only the trailing-character rule above is real.
  **When two sessions' counts disagree, print the surrounding bytes decoded as UTF-16 and
  compare the literal — do not reason about the format from the counts.**
- **Bring a string you know is there.** Count one line from an earlier release in the
  *same* binary. If that comes back `1` and the new one `0`, the `0` is real; if both are
  `0`, the search is broken, not the build. Without it you cannot tell
  "not shipped" from "not found".

`gh run download` must be run **inside the repo** — elsewhere it fails with
`fatal: not a git repository` *after partially writing files*. Never pipe its output
through `tail`/`head`: a corrupt download once became the baseline in four documents.
Check the sizes against the ones the script printed.

Report the path of the generated file and anything the script could not measure.
