# Agent Rules

This file defines the common development process and documentation standards for the project.
When given an implementation task, follow these rules to prepare documentation before starting.

---

## Workflow

### Starting a new feature
1. Create `docs/project_overview.md`, `docs/glossary.md`, and `docs/tags.md` if they do not exist
2. Run `/add-feature <feature-area>` to set up the directory, templates, and index.md at once
3. Write content in order: overview.md → spec.md → design.md
4. After writing spec.md, evaluate whether subdivision is needed per the split rules
5. If subdivision is needed, run `/add-feature <sub-item-path>`
6. Write content in tasks.md and begin implementation

### During implementation
7. Record decisions, issues, and changes in dev-notes.md as they occur
8. If skills, permissions, or information are lacking, record the request in dev-notes.md

### After implementation
9. Update the status in tasks.md
10. If the implementation deviates from the design, update design.md and record the diff in dev-notes.md

### Committing
- Commit after each logical unit of work (e.g. per sub-item completed, per doc section written)
- Do not batch multiple unrelated changes into a single commit

---

## Split Rules

**Default is to split. Extract any independently implementable unit into a sub-item immediately.**

### Criteria for splitting

Split if any of the following apply:

- Has its own screen, view, or UI surface
- Has its own data definition or schema
- Can be implemented and tested without the other units existing
- A separate developer could work on it in parallel

### Exception — integration is only allowed when BOTH conditions are met

- Neither half can be verified in isolation after splitting
- AND the code and responsibility are extremely small (equivalent to 1 class / 1 file)

### Procedure

1. Evaluate splitting immediately after writing spec.md
2. When a split target is identified, run `/add-feature <sub-item-path>` without hesitation
3. Parent-level design.md / tasks.md should contain only links to sub-items and cross-cutting concerns
4. After splitting, keep the parent's overview.md and spec.md as-is (do not delete them)

---

## Bug Ticket Workflow

### When a bug is reported
1. Assign the next available ID by checking the highest existing number in `issues/tickets/`
2. Create `issues/tickets/BUG-{NNN}.md` using `issues/templates/BUG-template.md`
3. Add a row to `issues/index.md` with state `Open`
4. Investigate the cause, then fix the code
5. Update the ticket with the cause, fix details, and relevant commit hash; change state to `Fixed`
6. Commit the ticket file together with (or immediately after) the fix commit

### Rules
- All bug tickets go in `issues/tickets/` — never in feature-area `tasks.md` or `dev-notes.md`
- One ticket per distinct root cause; link related tickets if they share a cause
- Do not close a ticket as `Closed` without user confirmation that the fix was verified
- **Separate what was measured from what was inferred.** In every ticket, the symptom,
  the cause, and the impact must each be marked as one or the other. Writing an inference
  in the voice of a measurement destroys the ticket's value as a precedent
- **A step that verification could not complete is an open risk, not a closed question.**
  Record it in a ticket even when the cause is believed to be outside your code —
  the user cannot use the product if they cannot get past that step

---

## Verification Loop (when you cannot run the product yourself)

When the agent cannot execute the product (no target OS, no hardware, CI-only builds),
all behaviour is confirmed by a **separate verification session on real hardware**.

```
tmp/確認/          request documents written by the agent  (verification requests)
tmp/フィードバック/  reports written by the verification session
tmp/確認/old/, tmp/フィードバック/old/   archive after each round
```

### Each round
1. Push the change, wait for CI to go green
2. Run `/verify-request <title>` to generate the request document
   — it measures the artifacts itself so the numbers cannot be mis-transcribed
3. Fill in the items, naming the negative control for each
4. When feedback arrives: fix what is reported, update tickets and test-cases,
   archive both documents to `old/`, then start the next round

### Rules for request documents

These four rules each exist because breaking them cost a round or shipped a defect.

- **Re-derive every fact at the time of writing.** Sizes, hashes, paths, registry keys —
  take them from the artifact and from the product, never from memory or from an
  earlier document. A path or number written in a request document is itself a claim
- **When the expected value is `0`, supply the control alongside it.** Either a case where
  it becomes `1`, or evidence that the string being counted exists in the shipped binary.
  `0` otherwise means both "stable" and "never executed", and they are indistinguishable
- **Confirm the control exists before writing the item.** "The old build will fail this"
  is a prediction. Verify that the old build actually fails before asking anyone to
  compare against it; an item whose control does not exist cannot be judged
- **Mark every claim as 測った (measured) or 推測 (inferred).** The request template has a
  section for this. Inferences are allowed; inferences dressed as measurements are not

### Reading feedback
- **"Could not determine" is a result, not a gap.** It is often more informative than a
  pass — treat it as a lead, and never round it to "not a problem"
- When the verification session uses a stronger method than you asked for, **move that
  method into the test-case steps** so the next round does not regress to the weaker one
- A workaround that works does not identify a cause. Record both separately

---

## Regression Net for Ports and Rewrites

When replacing a UI framework, runtime, or any layer wholesale, build a coverage table
that maps **every item in the old `spec.md` feature lists** to 済 / 要 / 差
(done / to-verify / intentionally different).

- **Anchor the table to the spec's feature list, not to the old test cases.**
  A feature with no test case is invisible to a table built from test cases, and will be
  dropped silently. This has happened: an "asynchronous loading" spec item was lost in a
  port and went unnoticed until a user reported slowness weeks later
- Build the table **while the old implementation still runs**. Once it is removed, the
  comparison can never be taken again
- Staged verification only checks what each stage added, so it cannot catch what the port
  forgot. The table is the only thing that can

---

## Automated Tests

**Draw the line on day one.** Decide, before implementation starts, which code can be
tested without the target OS or hardware, and put those tests in CI from the first commit.

- Do not let "it is all UI / platform code" postpone the decision. Pure logic
  (formatting, argument parsing, serialization round-trips, path predicates, comparators,
  tree shapes) is usually a few thousand lines and is exactly where silent breakage lives
- Put the test step **before** the publish/package step so a failure stops the artifact
- Record the line in `docs/testing.md`: what is covered, what needs the target
  environment and therefore stays in the manual verification loop, and why
- `docs/**/test-cases.md` are **manual** procedures. They are not automated tests and must
  never be counted as such

---

## Guidelines

- Create documentation before implementation
- If requirements change during implementation, update the relevant documents immediately
- Write dev-notes.md as a record of decisions, not a work log
- **Write the invariant, not the history.** In code comments, prefer "what breaks if you
  change this" over "why this was done". History belongs in dev-notes.md and tickets;
  duplicating it in code lets the two drift apart
- **Apply a principle to every surface in the same session.** When a rule is established
  ("this call must not run on the UI thread", "this must be released in a finally"),
  grep for the other places it applies and fix them now. Writing the lesson down is not
  applying it — this has been broken in the very session the lesson was written
- Commit and push to GitHub at natural stopping points
- Do not load or reference any files under `_jp/` — that directory is for human reference only
