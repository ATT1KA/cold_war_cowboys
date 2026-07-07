# Framework Map — what's reusable, what's CWC

*July 2026. The extraction map for the ~2,500 lines of generic infrastructure
this project produced, marked in-source with `[FRAMEWORK]` /
`[FRAMEWORK-PATTERN]` / `[CWC-SPECIFIC]` banner comments. This document is the
boundary's single source of truth; the banners are its in-context reminders.*

**Rule of thumb from the framework assessment:** extract the starter kit
*between* projects, not now, and never attempt a generic NarrativeDirector or
a generic psychology system — that's building an engine, which is a different
(and worse) hobby than building games.

## Tier 1 — `[FRAMEWORK]`: copy as-is into a new s&box project

| File | What it is |
|---|---|
| `code/Core/EventBus.cs` | Typed pub/sub, instanced, zero coupling. |
| `code/Core/Rng.cs` | Deterministic xoshiro256** with labeled fork streams and save/restore of stream state. Should eventually become a library. |
| `code/Core/CwcFiles.cs` | File + log provider seams. The engine-abstraction pattern that makes offline harnesses possible: sandbox FS in-engine, System.IO in tests, loud when unwired. |
| `code/SandboxFileProvider.cs` | The s&box side of the seam (Mounted reads, Data writes, FindFiles). |
| `tests/SmokeTest/DiskFileProvider.cs` | The System.IO side (repo-root walking, shared by all three harnesses). |
| `code/Domain/Relationship.cs` | Generic directed edge with kind + score. |
| `code/UI/Dial.razor` | Generic gauge widget, zero CWC references. |

## Tier 2 — `[FRAMEWORK-PATTERN]`: reuse the shape, rename the vocabulary

| File | The reusable pattern | The CWC part |
|---|---|---|
| `code/Generation/Templates/TemplateLoader.cs` | Loud two-pass JSON loading: lenient parse + strict unknown-field re-parse, errors surfaced to the UI; per-file content directories. | Which files it loads. |
| `code/Generation/Templates/TemplateValidator.cs` | Whitelist validation + cross-reference checking of all content at boot. | Every whitelist (trigger types, resolvers, roles, stats). |
| `code/Save/SaveSystem.cs`, `SaveData.cs` | Hand-written DTO-per-entity save pattern, deterministic, RNG-stream-aware. ~15 lines of boilerplate per new system. | Every DTO. |
| `code/Core/PhaseManager.cs` | Enum + switch turn loop. Forty lines; copy and edit. | The six CWC phases. |
| `code/Core/WorldState.cs` | The single shared state container + flag set. | Its fields. |
| `code/Generation/WorldGenerator.cs`, `OperativeGenerator.cs`, `NameGenerator.cs` | Template → rolled-entity pipeline with variety enforcement and per-entity RNG forks. | Stat names, variety rules, archetypes. |
| `code/UI/IGameViewModel.cs` | The UI/logic seam that keeps game code engine-independent. | Its members. |
| `tests/SmokeTest/Program.cs` | The harness pattern: compile the REAL game code into a console app; check invariants end-to-end **through the game loop**; every validator trap proven armed. | Every check. |
| `tools/PolicySim/Program.cs` | Scripted-policy balance simulation over N seeds, refusing to run against unclean content. | The two policies and the design targets. |
| `tools/ScenePreview/Program.cs` | Content-preview CLI: render any template against generated state, surface all warnings. | The scene rendering specifics. |

## Mixed — flagged in-source

`code/Narrative/NarrativeDirector.cs` is ~45% scene-engine plumbing (flag
eligibility, priority/cooldown/one-shot queue, role-based casting, token
substitution, choice log — would drive a detective noir tomorrow) and ~55% CWC
(stat lookups, the fourteen resolvers, role-drift thresholds, corruption
keyword hooks, corporate triggers). **Do not genericize it.** Re-deriving it
for game #2 costs less than maintaining an engine that serves neither game
well. The pattern — flags + roles + tokens + priority queue + choice log — is
the reusable artifact, and it's documented in the authoring manual.

## Tier 3 — `[CWC-SPECIFIC]`: rewrite for a different game

- **The psychology model** (`code/Domain/Stats.cs`, `Operative.cs`): five
  *named* fields + WetWorkCount, baked into the corruption formula, decay,
  tripwires, refusal, variety validation, save DTOs, trait modifiers, and the
  director's stat switch. Changing the character model is *the* structural
  cost of a second game.
- **The corporate layer** (`code/Corporate/*`): CWC's game design in code
  form. The pattern underneath — autonomous systems subscribing to the bus, a
  consequence queue drained at phase boundaries, directive/contract templates
  in JSON — is extractable as a pattern, not as code.
- **`code/Core/CorruptionTracker.cs`**: generic milestone-meter shape, CWC
  formula, names, and presentation stack.
- **`code/Game/GameManager.cs`**: the orchestration is the game.
- **`code/Missions/*`**: resolver math, consequence processing, narrative
  sequences — CWC mechanics on a reusable "pure resolver + single consequence
  writer" shape.
- ~35% of `code/UI/`: the phase panels are CWC pages through and through.

## The extraction, when it happens

A `sandbox-game-core` starter repo containing Tier 1 verbatim, Tier 2 as
skeletons with `TODO(game)` markers where the vocabulary goes, the three
harness scaffolds, and a CLAUDE.md describing the methodology (pure-.NET core,
provider seams, JSON content boundary, loud loading, proof-gated sprints).
That plus this map *is* the framework.
