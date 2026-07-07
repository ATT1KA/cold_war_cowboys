# Cold War Cowboys — Framework Assessment

*July 2026. A second full read of the codebase, this time with a different question: not "does the game work?" but "does this **work as a way of making games** — for CWC now, for other s&box games later, and for Dan + Claude as a standing methodology?" Follows the July wiring pass; all references are to current `main` (8e7962b).*

**The one-paragraph version:** As a CWC authoring environment this is one day of work away from genuinely good — but that day is mandatory, and half of it is fixing the most surprising defect in the repo: **the Content Authoring Manual documents a scene format that has never existed**. As a reusable s&box framework, this is not a framework — it's a well-seamed game, roughly 40% generic plumbing and 60% CWC-specific logic — but the *seams* (pure-C# core, provider abstraction, offline harnesses, JSON content boundary) are the real reusable asset and they're excellent. As a Claude-copilot methodology, the project's own history is the clearest lesson in the repo: every artifact that was tethered to executable truth (SmokeTest, PolicySim, the loud loader) paid off; every artifact that was written from conversation instead of code (the manual, the "single writer" comments, the mirrored balance script, Mason and Kira) turned out to be partly or wholly fiction. The methodology is worth investing in — with one rule added: **nothing is true until a harness executes it.**

---

# Part 1 — CWC as an authoring environment

## 1.1 The pipeline as it actually is

Dan writes JSON in `Data/Templates/`, and the pipeline that consumes it is, post-wiring-pass, in decent shape:

- **Loading is loud.** `TemplateLoader` (code/Generation/Templates/TemplateLoader.cs) collects every deserialization failure into `ContentWarnings` and logs them; `GameManager.NewGame()` surfaces them. The silent `catch { return null; }` era is over.
- **The parser is writer-friendly.** Case-insensitive property names, `// comments` allowed, trailing commas allowed, string enums (`"Priority": "Pressing"`, not `3`). These are the right defaults for a human hand-editing JSON.
- **The validator is real but partial.** `TemplateValidator` checks duplicate/empty IDs, trigger types against a whitelist, trigger stat names, cast resolvers, `scene:*`/`choice:*` flag cross-references (does anything ever *set* the flag you require?), skill names in mission weights, and sequence-node shape. That's a genuinely useful safety net.

But three gaps directly hit the authoring loop:

1. **No hot-reload.** Templates load once in `GameManager.NewGame()` and the loader is discarded. The loop is: edit JSON → restart the game → start a new game → contrive the trigger condition → see the scene. For prose iteration — the thing Dan will do a hundred times per scene — that loop is minutes long when it should be seconds. (The manual claims hot-reload exists. It doesn't. See §1.4.)
2. **No single-scene preview.** There is no way to say "show me `third_kill_intro` rendered with a sample cast." SmokeTest runs the full pipeline and checks *one* scene end-to-end; PolicySim ignores narrative entirely. To see a new scene, Dan must play until its trigger fires. For a scene gated on `any_op:defection_risk`, that could be twenty minutes of play per prose tweak.
3. **Dangling references fail silently.** The validator never cross-checks trait IDs in archetype pools (a typo'd trait is silently skipped by `OperativeGenerator`), never checks mission `TargetCandidates`/`ClientCandidates` against faction IDs, and a cast slot that can't resolve renders as the word **"someone"** in the scene text. Worse: unknown JSON properties are silently ignored, so a misspelled field name (`"FlagOnFire"` for `"FlagsOnFire"`) doesn't error — the scene just quietly loses its flag. For a solo author with no reviewer, typo-tolerance is the enemy: every one of these becomes a "why doesn't my scene work" debugging session with no error message anywhere.

**Verdict on friction:** the pipeline no longer *fights* Dan — the wiring pass fixed that — but it doesn't *serve* him yet either. It's built for correctness at load time, not for iteration speed at authoring time. The single highest-leverage improvement in this entire document is a `tools/ScenePreview` CLI (see §4.3), and because the core is pure .NET it's about half a day of work.

## 1.2 The JSON format: good bones, narrow throat

Read as a creative writer's medium, `scenes.json` is honestly pleasant. A scene is ~30 lines: id, title, setting, trigger flags, a cast list, prose lines with `{operative.name}` / `{gender:m|his|her}` tokens, choices with visible stat deltas. The example scenes read like the finished game. There's no ceremony, no nesting hell, no GUIDs. Dan can open the file, copy a scene, and start writing — the format does not fight creative impulses at the sentence level.

Where it constrains him is *shape*, and the constraint is severe:

- **Every scene is one text block and one row of choices.** No second beat, no reaction line keyed to the choice, no within-scene branching. `SceneChoice` carries five fixed deltas (Loyalty/Stress/Conscience/Heat/Morale) plus flags — that is the entire expressive budget of a choice. Dan cannot write "she pauses, then answers" without writing a whole second scene and a flag to chain them.
- **The consequence vocabulary is five dials and a string.** A scene choice cannot kill, bench, or transfer an operative, move a relationship, spawn a mission, or touch a faction. The design document already identifies this (§4, "scene choices need a bigger consequence vocabulary") — it's the right diagnosis. Until then, scenes can *comment* on the sandbox but only weakly *act* on it.
- **The trigger vocabulary is the format's hidden strength.** Fifteen trigger types (`any_op`, `flag_prefix`, `avg_stat_below`, `any_relationship_below`, `consecutive_successes`, `board_confidence_below`, `last_mission_catastrophe`, `cycle_reached`…) plus required/forbidden flags, priority, one-shots, and a 6-cycle repeat cooldown. This is genuinely rich — richer than the content currently exploits. Combined with the choice log and the automatic `seq:*` flags from mission sequences, the substrate for "the game remembers what you did" exists *now*.

**Can Dan write scenes that surprise the system's designer?** Structurally yes — that's the archetype/role system doing real work. Because scenes cast by role (`triggering_operative`, `role:anchor`, `highest_conscience`) rather than by name, a scene Dan writes about "the one who still feels it" will land on a differently-generated person every run, colliding with whatever traits, relationships, and history that operative accumulated. That's authentic emergence and it's the framework's best creative idea. The ceiling on emergence isn't the trigger system — it's that consequence vocabulary. Scenes can be *cast* surprisingly but can't yet *ripple* surprisingly.

## 1.3 Iteration speed: the honest number

Three loops exist today:

| Loop | Time | What it verifies |
|---|---|---|
| Edit JSON → `cd tests/SmokeTest && dotnet run` | ~10 seconds | Parses, validates, one scene fires end-to-end |
| Edit JSON → restart game in s&box → play to trigger | minutes to *tens* of minutes | The scene in context, with real UI |
| Edit JSON → PolicySim | ~30 seconds | Balance effects over 40 seeds × 20 cycles |

The 10-second loop is real and good — SmokeTest will immediately catch a malformed file, a bad enum, an unknown trigger type. But it answers "is this valid?", not "is this *good*?" The middle loop — the one where Dan reads his prose in the actual panel and feels the pacing — is the one that matters for a writer, and it's the slowest, and it currently has an unverified precondition: **the project has never been booted in the s&box editor.** The provider seam (`CwcFiles` + `SandboxFileProvider`) is the documented-correct API, but until the .sbproj opens and a scene fires on screen, the authoring environment's final stage exists on paper. This was the assessment's loudest point and it is still true.

## 1.4 The Content Authoring Manual: the wrong document, confidently

This is the most important finding of this pass. I extracted and read the full 23-page PDF. Its *craft* chapters are genuinely excellent — and its *technical* chapters describe a system that has never existed in this repo.

**What the manual teaches that is fiction:**

| Manual says | Reality |
|---|---|
| Scenes are multi-node dialogues: `"nodes": [...]` with `"next"` routing between them | A scene is one `TextLines` block + one `Choices` array (SceneData.cs). No nodes, no routing. |
| Choices carry generic `"effects": [{ "target", "stat", "delta" }]`, `"relationship"` effects, `"corporate_standing"` | Choices are five fixed named deltas + flags |
| Triggers use `"conditions": { "wet_work_count": { "gte": 3 } }` objects | Triggers are typed structs with a single `Key`/`Threshold` |
| `"priority": 80` (int 0–100), `"cooldown_cycles"` per scene | `Priority` is a four-value enum; cooldown is a global constant (6) |
| One JSON file per scene in `Data/Templates/corruption/`, `psychology/` etc., scanned recursively | One `scenes.json` array |
| "Hot-loadable: save the file … the scene plays immediately. No code changes, no recompilation" | Loads once at NewGame; no reload |
| "In the debug console, use the state override to set any operative's wet_work_count to 3" | No debug console, no state override exists |
| Corruption thresholds 20/40/60/80/95 | Code: 20/40/55/70/85 (CorruptionTracker.cs:39-43) |
| Voice guide keyed to archetypes "Street, CorpoClimber, Spook, Soldier, Fixer" | The 25 real archetypes have different names entirely |
| Title page: "built in Blazor WebAssembly" | It's s&box |

If Dan sits down tomorrow, follows the manual, and writes `corruption_fourth_wet_job.json` in a `corruption/` subdirectory with nodes and effects arrays — **nothing loads, and no error explains why**, because the loader never looks in subdirectories. The manual is not a flawed reference; it is a reference *to a different game*, written from the design conversation rather than from `SceneData.cs`, and never validated against the code. It's the single hardest authoring blocker in the repo — worse than the missing preview tool, because it doesn't slow Dan down, it *aims him wrong*.

**What the manual gets deeply right** — and must be kept in the rewrite: the writing-principle sections are the best design writing in the project. The mirror-role rules ("'Three names on your list now' is better than 'You've killed three people.' The first is a mirror; the second is a sermon"), the corruption-register table (the same insomnia line at corruption 20 vs 80), the corporate-euphemism glossary, the wet-work design rules ("The player should never feel that killing was inevitable. They should feel that it was the easiest option. That distinction is the entire game"), the don't-double-content gender guidance. These chapters *teach*. A creative writer could not learn the file format from this manual, but they could learn the voice — and the voice is the harder thing to teach.

**One more honest observation:** the manual's fictional schema is, in several ways, *better* than the real one — per-file scenes, generic effects, multi-node beats are all things the design document independently wants. Read charitably, the manual is an accidental v2 pipeline spec. The right move is still to rewrite it against the real schema *now* (authoring is the current phase; the format must be documented as it is), but steal its three best ideas into the schema deliberately rather than letting the document silently promise them.

---

# Part 2 — As a reusable s&box framework

## 2.1 The map: what's generic, what's CWC

Module by module, with porting effort for a hypothetical second game:

**Copy as-is (fully generic):**
- `EventBus` — typed pub/sub, instanced, zero coupling. Drop into any game.
- `Rng` — deterministic xoshiro256** with labeled fork streams and save/restore of stream state. This is a small library; it should literally become one.
- `CwcFiles`/`CwcLog` provider seams + `SandboxFileProvider`/`DiskFileProvider` — the engine-abstraction pattern, already proven by two harnesses.
- `Relationship` — generic directed edge with kind + score.
- `TemplateLoader` + `TemplateValidator` — the *pattern* (loud JSON loading, whitelist validation, warnings surfaced to the UI) is game-agnostic even where the whitelists aren't.
- `Dial.razor` — a genuinely generic widget, zero CWC references.
- SmokeTest/PolicySim — the harness *pattern* (compile the real game code into a .NET console app; check invariants; simulate policies over seeds) transfers wholesale.

**Rename and reuse (generic pattern, CWC vocabulary):**
- `WorldSetting`, `WorldState` (generic container, CWC metadata fields), `Faction` (rename kinds/agendas), `Trait` (rename axes), `Mission` (rename types, generalize `IsWetWork`), `SaveSystem` (hand-written DTO-per-entity pattern — ~15 lines of boilerplate per new system, deterministic, RNG-stream-aware; the pattern is good, the DTOs are CWC), `WorldGenerator`/`OperativeGenerator`/`NameGenerator` (orchestration pattern generic, variety rules and stat names CWC), the `IGameViewModel` seam and the UIRoot phase-routing pattern.

**CWC-specific (rewrite for a different game):**
- The **Psychology model**: five *named fields* (Loyalty/Stress/Morale/Conscience/Ambition + WetWorkCount), not a stat dictionary — and those names are baked into at least eight files: the corruption formula, decay logic, tripwire generation, assignment refusal, team-variety validation, save DTOs, trait modifiers, and the narrative director's `GetStat()` switch. Changing the character model is *the* structural cost of a second game.
- `PhaseManager` — a hardcoded six-phase enum + switch. Forty lines; copy and edit, don't generalize.
- The **entire Corporate layer** (FactionSystem, PoliticsSystem, ContractSystem, ReputationSystem, event generator) — this is CWC's game design in code form. The *pattern underneath it* — autonomous systems subscribing to the bus, a consequence queue drained at phase boundaries, directive/contract templates in JSON — is extractable as a pattern, not as code.
- `CorruptionTracker` — generic milestone-meter shape, CWC formula and names.
- ~35% of the UI: the phase panels are CWC pages through and through (corruption desaturation, wet-work badges, boardroom dials).

## 2.2 Is the NarrativeDirector a general-purpose scene engine?

About 45% yes. The plumbing — flag eligibility, priority/cooldown/one-shot selection, role-based cast resolution, token substitution, the choice log — is a real scene engine and would drive a detective noir tomorrow. But CWC leaks deep into the other 55%: the trigger evaluator's stat names, the fourteen hardcoded cast resolvers, the role-drift thresholds ("conscience > 65"), the corruption hooks that pattern-match choice flags containing `"cold"`/`"human"`/`"mercy"`, the tripwire flags (`third_kill:`, `hollowed_out:`) generated *by the engine* rather than by data, and `CheckCorporateTriggers()` which is pure CWC business logic living inside the "engine" file.

Could it drive a fantasy RPG? After a 3–4 week genericization (stat lookup by interface, milestones/roles/tripwires to config, generic effects list) — yes. **Should Dan do that genericization? No.** The honest arithmetic: the director is ~900 lines that Claude produced in roughly one sprint-night once the pattern existed. Re-deriving it for game #2 with different stats and roles costs less than maintaining a generic engine that serves neither game well. The reusable artifact is the *pattern* — flags + roles + tokens + priority queue + choice log — which is now documented in three places and in Claude's demonstrated ability to rebuild it.

## 2.3 What a second game actually inherits

If Dan started "Highway Kings" (or a heist sim, or a noir) tomorrow:

- **Direct code reuse:** the Core seams, Rng, EventBus, loader/validator pattern, save pattern, harness scaffolds, Dial, the viewmodel seam — call it 20–30% of the code, but disproportionately the code that took the most debugging to get right (determinism, serialization, the sandbox boundary).
- **Pattern reuse:** everything else — the phase loop shape, the consequence-queue discipline, the JSON content boundary, the archetype→character pipeline, role-cast scenes, the offline-harness methodology. This is where the real acceleration is, and it lives partly in the repo and partly in the documents (README architecture section, the design doc, this file).
- **A realistic estimate:** the infrastructure agent's figure of ~25–35 agent-days for a mid-scope second game of similar shape is about right — against the ~15 sprint-nights CWC took *plus* the two assessment/wiring passes its debts required. With the methodology fixes in Part 3, game #2 should cost *less* than CWC did and skip the debt.

**Recommended extraction (cheap, do it between projects, not now):** a `sandbox-game-core` starter repo containing EventBus, Rng, the file/log provider seams, TemplateLoader/Validator skeleton, SaveSystem skeleton, a PhaseManager template, SmokeTest scaffold, Dial.razor, and the IGameViewModel/UIRoot pattern — roughly 2,500 lines, all of it the already-generic code. That plus a CLAUDE.md describing the methodology *is* the framework. Do not attempt a generic NarrativeDirector or a generic psychology system; that's building an engine, which is a different (and worse) hobby than building games.

---

# Part 3 — As a Claude-copilot methodology

## 3.1 Is this codebase good for Claude to work in? Emphatically yes

This second full read confirms what the wiring pass suggested: the repo is close to optimal for AI-assisted development, mostly by accident of good instincts:

- **It fits in a head.** ~6.5k C# LOC + 2.4k Razor + 7k JSON. Claude can genuinely read *all of it* in one pass — which is exactly what made the fresh-eyes assessment possible, and what makes every future "re-read with lens X" cheap. Staying under ~10k core LOC is not a limitation; it's a superpower of this development model. Guard it.
- **Pure-C# core with the engine at arm's length.** Only three files touch `Sandbox.*`. Everything else compiles and runs under plain .NET 8 — which means Claude can *execute its own work* without the editor, and did: 84 smoke checks and a 40-seed policy simulator run against the real game code, not mirrors. This is the load-bearing architectural decision of the whole project.
- **Determinism everywhere.** Seeded, forkable, serializable RNG means every bug Claude finds is reproducible and every balance claim is testable. The 500-seed psychology simulation in the assessment was only possible because of this.
- **Data-driven content with a validator** means content errors are machine-checkable — the category of error Claude is best at eliminating.

## 3.2 Where the methodology failed, and why — the pattern behind the defects

Line up the project's failures and one pattern explains nearly all of them:

| Artifact | Tethered to execution? | Outcome |
|---|---|---|
| SmokeTest / PolicySim (compile real code) | Yes | Caught real bugs; still green; trustworthy |
| Loud loader + validator (post-fix) | Yes | Ended the silent-failure era |
| QA audit ("build NOT EXECUTED" self-report) | Partly | Honest about limits; missed data-flow bugs it couldn't run |
| `balance_test.py` (mirrored logic) | **No** | Validated a game that didn't exist; its PASS hid the death spiral |
| Authoring manual (schema from conversation) | **No** | Documents a fictional API; would misdirect all authoring |
| "Single-writer WorldState," "roles drift" doc comments | **No** | Marketing comments; both false |
| Mason, Kira, Ghost, Davi | **No** | Characters that never existed, discussed as if real |
| Nine sprint modules, each "complete" | **No end-to-end proof** | A third of the public surface dead; flagship system unplugged for two months |

The failure mode is specific to this way of working: **Claude produces plausible, internally-consistent artifacts at the same speed whether or not they correspond to the code.** A human team drifts too, but slower, and someone eventually tries to *use* the manual. In an autonomous-sprint model, nobody uses anything until much later — so unverified artifacts accumulate compound interest. The scene loader was broken for two months not because the bug was hard (one line) but because *every* layer that would have revealed it — the catch block, the smoke test of that era, the sprint report — was allowed to assert success without demonstrating it.

## 3.3 The corrected sprint contract

The 9-sprint model built real systems fast; keep it. Change the definition of done:

1. **Demo or it didn't happen.** A sprint ends with an executable proof — a new SmokeTest section that exercises the feature *through the game loop* (not the module in isolation) — or the sprint report says "built, unwired, unproven" in the first line. `MissionNarrativeRunner.Begin()` having zero call sites should have been impossible to miss; a rule of "the smoke test must show the new panel's data path firing" makes it impossible to hide.
2. **No mirrored logic, ever.** Any test/simulator must compile the real code. `balance_test.py` cost more than it ever paid.
3. **Silent catch blocks are defects.** Already fixed; keep it a rule. Every `catch` either logs to a surfaced channel or rethrows.
4. **Docs are code artifacts.** A reference document's examples must be *fixture files that a harness loads*. If the authoring manual's annotated example had been a file SmokeTest deserialized, the manual could never have drifted into fiction. This is a one-hour investment per document and it converts documentation from a liability (confident staleness) into a test suite.
5. **Engine-boot gates.** Offline development is not a flaw — it's the reason one person + Claude built this at all. But every milestone that touches an engine seam (file I/O, UI, save paths) ends with a manual boot of the .sbproj. The current project has *never* done this, which means one manual day of Dan's time is still the highest-risk item in the whole plan. Offline for logic; in-engine checkpoints for seams; never let the gap exceed one sprint.

## 3.4 Is the JSON boundary the right Claude/Dan boundary?

Mostly yes, with one refinement. "Claude builds systems, Dan authors JSON content" is the right *default* division — content is where Dan's taste is the product, and the format is now writer-friendly. But CWC's history shows the boundary is more productive as a gradient than a wall: the best content in the repo (the six mission sequences, several scenes) was Claude-drafted and would be Dan-curated; the best *system* ideas (role-casting, the corruption presentation stack) came from the design conversation, i.e., from Dan. The workable model: **Dan owns voice, stakes, and what gets kept; Claude owns schemas, wiring, validation, and volume drafting; everything crosses the boundary through files a harness can check.** The docs are where the boundary actually failed — because they belonged to neither side and were tethered to nothing.

---

# Part 4 — The honest verdict

## 4.1 Ready for content authoring?

**Not this morning; yes by the weekend.** The systems are (post-wiring-pass) ready, the format is ready, but three gates precede a content push, in order:

1. **Boot it in s&box** (Dan, ~1 day, already Phase 1 of the design doc roadmap). Non-negotiable and unchanged: until a scene fires on screen, the authoring target is theoretical.
2. **Rewrite the manual's technical chapters from `SceneData.cs`** (Claude, ~half a day) — keep every craft chapter nearly verbatim, replace the schema reference, worked example, checklist, file-naming and directory sections with the real format, and commit the worked example as a fixture SmokeTest loads. Until this happens the manual is a trap and should carry a warning banner in the repo.
3. **Build `tools/ScenePreview`** (Claude, ~half a day) — a third console harness alongside SmokeTest/PolicySim: `--validate` (parse + validate all templates, exit code for CI), `--scene <id>` (render a scene with a generated sample roster: resolved cast, tokens substituted, choices listed), `--list-eligible --state <flags>` (which scenes can fire given a state). This converts Dan's prose loop from minutes to seconds and is the single best creative-tool investment available.

After those: author, following the design doc's §4 priority table. The 40–60 scene target with callback chains is the right plan and the substrate now supports it.

## 4.2 Would this framework accelerate a second game?

**The code, modestly; the methodology, dramatically.** 20–30% direct code reuse — but it's the hardest-won code (determinism, save round-trips, the sandbox seam, loud loading). The real second-game asset is the proven shape: pure-.NET core + provider seams + JSON content + validator + offline harnesses + proof-gated sprints. With the §3.3 contract in place from day one, a second game of similar scope should reach content-ready in roughly half CWC's wall-clock, because CWC's actual cost was ~15 sprint-nights *plus two full assessment/repair passes* that the methodology fixes are designed to make unnecessary. Extract the ~2,500-line starter kit between projects; do not build a generic engine.

## 4.3 What I'd change to make it a better creative tool (priority order)

1. **Boot in-engine** — blocks everything (Dan, 1 day).
2. **Truthful manual** — blocks authoring (0.5 day, keep the craft chapters).
3. **ScenePreview CLI** — the iteration-speed multiplier (0.5 day).
4. **Strict-validate mode:** fail on unknown JSON properties (catches typo'd field names — currently silent), cross-check trait/faction/archetype ID references, warn on unresolvable cast slots instead of rendering "someone" (0.5 day).
5. **Per-file scenes in subdirectories** — `scenes.json` at 60 scenes will be 4,000+ lines and a merge hazard; the manual already (fictionally) promises this layout, and it's the better one (0.5 day, loader change + file split).
6. **Generic effects list on `SceneChoice`** (`target/stat/delta` array alongside the five deltas) — widens the consequence vocabulary the design doc already wants, and not coincidentally moves the real schema toward the manual's better ideas (1 day, touches SceneData/NarrativeDirector/ConsequenceProcessor).
7. **In-engine hot-reload** (rescan templates on a debug key) — worth doing only after boot verifies the file provider; then it's small and it upgrades the in-context prose loop from "restart" to "press F5" (0.5–1 day).
8. **A JSON Schema file** generated from the C# types, for editor autocomplete/validation while Dan types (0.5 day, pure convenience).

Items 2–5 together are about two Claude-days and they change authoring from "possible" to "pleasant." Items 6–8 can ride along with the content push.

## 4.4 The ideal Dan + Claude workflow, as revealed by this project's history

What CWC's two-month arc actually demonstrates: the sprint model *generates* fast, the harnesses *verify* cheaply, and the danger concentrates entirely in unverified prose artifacts and unwired seams. So:

- **Design in conversation, commit in JSON and C#, verify in harnesses.** Every sprint ends with a smoke check demonstrating the feature through the loop.
- **Dan's recurring roles:** creative director (voice, stakes, what ships), in-engine QA (the one thing Claude cannot do here — boot it, feel it, screenshot it), and content author inside the format.
- **Claude's recurring roles:** systems, wiring, validation tooling, volume content drafts for Dan to cut, and periodic *fresh-eyes assessment passes* — the July assessment found what the QA audit structurally couldn't, and "re-read everything with a hostile lens every N sprints" should be a standing scheduled task, not a one-off.
- **The one rule that would have prevented most of this repo's history:** no artifact — test, doc, comment, character, or claim — gets asserted unless something executable backs it. The harnesses exist; route everything through them.

That's a real methodology, it's already 80% built, and CWC is the right project to finish proving it on.

---

*Verification snapshot for this document: manual PDF text extracted and read in full (23 pp); corruption thresholds, cooldown constant, scene/mission/archetype JSON, README, ASSESSMENT.md, design doc read directly; four parallel deep-read passes over the pipeline, narrative engine, infrastructure, and UI/docs cross-checked against each other. Claims sourced from the pre-wiring-pass QA audit were discarded where the wiring pass superseded them.*
