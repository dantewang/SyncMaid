# Guide: Composable filter expressions

**Status:** implementation guide — not yet implemented

## Current behavior (answering "AND or OR?")

**OR.** `Destination.Includes(relativePath)` returns true when the path matches **any**
rule in `Destination.Filters`; an empty list selects nothing, and the editor's "All files"
mode stores a single `AllFilesFilter`. There is no AND and no exclusion today.

So `[PathFilter("docs"), ExtensionFilter("jpg")]` means *"everything under docs/ **plus**
every jpg anywhere"* — not "jpgs under docs/".

## Goal

Let users express combinations like `(A or B) and C` and `A or (B and C)`, including
negation ("everything except…"), without making the simple cases harder.

## Model design (Core) — small and low-risk

Extend the existing closed, JSON-discriminated `FilterRule` hierarchy with composite
nodes. This is the same pattern already used everywhere (AOT-safe, no reflection):

```csharp
[JsonDerivedType(typeof(AllOfFilter), "allOf")]   // AND
[JsonDerivedType(typeof(AnyOfFilter), "anyOf")]   // OR
[JsonDerivedType(typeof(NotFilter),  "not")]      // exclusion

public sealed record AllOfFilter(IReadOnlyList<FilterRule> Rules) : FilterRule
{
    public override bool Matches(string p) => Rules.Count > 0 && Rules.All(r => r.Matches(p));
}
public sealed record AnyOfFilter(IReadOnlyList<FilterRule> Rules) : FilterRule
{
    public override bool Matches(string p) => Rules.Any(r => r.Matches(p));
}
public sealed record NotFilter(FilterRule Rule) : FilterRule
{
    public override bool Matches(string p) => !Rule.Matches(p);
}
```

- **Back-compat:** `Destination.Filters` stays `IReadOnlyList<FilterRule>` with the
  existing OR semantics — a persisted flat list keeps meaning "any of". Composites are
  just new rule kinds inside that list. Old `tasks.json` files load unchanged.
- **Serialization:** recursive polymorphism through the existing
  `TaskStoreJsonContext` — `FilterRule` is already registered and `IReadOnlyList<FilterRule>`
  is already reachable via `Destination.Filters`, so source-gen handles the recursion.
  Add a round-trip test with a nested expression to prove it (this is the one thing to
  verify early; if source-gen balks, register the composite types explicitly).
- Add a `Describe()`/description helper (or extend the existing filter-description logic
  in the UI layer) so badges and rows can render `docs/ and (jpg or png)`.

**Core difficulty: low.** Pure records + tests, ~half a day including edge cases
(empty `AllOf` matches nothing — decided above; empty `AnyOf` matches nothing; double
negation; deep nesting).

## UI design — where the real cost is

Three options considered:

| Option | Pros | Cons |
|---|---|---|
| **A. Two-level group editor (recommended)** | Covers both target examples directly; discoverable; matches existing visual language | Depth capped at 2 in UI (model supports more) |
| B. Free-text expression + parser | Ultimate power | Parser + error UX; hostile to casual users |
| C. Fully recursive tree editor | Unlimited nesting | Heavy UI; easy to make confusing |

**Option A:** the "Files to sync → Only matching" panel becomes:

1. A top-level connective segment: **"Match ANY group" / "Match ALL groups"**
   (reuse `RadioButton.segment`).
2. A list of **group cards** (reuse the `filterRow`/card styling). Each group card has:
   - its own small ANY/ALL segment (the inner connective — automatically the interesting
     one when it differs from the top level),
   - its rule rows (kind combo + pattern + remove, exactly today's row UI),
   - an "add rule" row, and a per-rule **"exclude" toggle** (renders the rule as
     `not jpg` — maps to `NotFilter`).
3. "Add group" button below.
4. A live **preview line** under the panel showing the expression in plain text, e.g.
   `docs/ and (jpg or png)` — this is the single best guard against user confusion, and
   doubles as the destination-row badge text.

Mapping: top-level ALL of groups, each group ANY of rules → `AllOf[AnyOf[...], ...]`
(covers `(A or B) and C`); top-level ANY, group ALL → `AnyOf[A, AllOf[B, C]]`
(covers `A or (B and C)`). A single loose rule is a one-rule group; the editor should
collapse the trivial shapes on save (one group → no wrapper; one rule in a group → the
bare rule) so simple configs persist as today.

- Keep the existing simple path untouched: "All files" mode and a flat list of rules with
  no groups behave exactly as before — groups are opt-in complexity.
- `DestinationEditorViewModel` grows a small tree of `FilterGroupViewModel` /
  `FilterRuleViewModel(+IsExcluded)`; `OK()` lowers the tree to the `FilterRule` AST and
  loading raises the AST back (unknown deeper nesting from hand-edited JSON: render
  read-only summary row rather than destroying it — do not silently rewrite what the
  editor can't represent).

**UI difficulty: medium.** Editor rework + tree↔AST mapping + preview text + tests
(view-model round-trips, headless editor test, planner behavior unchanged). Estimate for
Opus: the Core part is quick; the editor is 1–2 focused sessions.

## Test plan

- Core: truth-table tests for `AllOf`/`AnyOf`/`Not` incl. empty/nested; JSON round-trip
  of `AllOf[AnyOf[Path, Ext], Not[Ext]]`; back-compat load of a legacy flat list.
- Engine: filtered-set behavior for `(docs OR photos) AND jpg` against the in-memory fs.
- UI: editor builds the right AST for both canonical examples; collapse-on-save rules;
  exclude toggle; preview text; legacy destination loads into flat (group-less) editor.
