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

Report the path of the generated file and anything the script could not measure.
