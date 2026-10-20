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

Before reporting done, check all four:

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

- **Match a prefix, not the whole string.** A full-string UTF-16 search returns `0` for
  strings that are present — Native AOT packs literals with no null terminator, so the
  last character runs into the next datum. Measured 2026-10-20:
  `ShellFileOperations.DeleteCore` full-match = 0 in a binary that contains it;
  prefix `ShellFileOp` finds it. **A `0` from a full-string search proves nothing.**
- **Bring a string you know is there.** Count one line from an earlier release in the
  *same* binary. If that comes back `1` and the new one `0`, the `0` is real; if both are
  `0`, the search is broken, not the build. Without it you cannot tell
  "not shipped" from "not found".

`gh run download` must be run **inside the repo** — elsewhere it fails with
`fatal: not a git repository` *after partially writing files*. Never pipe its output
through `tail`/`head`: a corrupt download once became the baseline in four documents.
Check the sizes against the ones the script printed.

Report the path of the generated file and anything the script could not measure.
