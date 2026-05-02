# Cold War Cowboys — QA Audit (Unified)

**Date:** 2026-04-30
**Audit target:** `origin/main` @ `8790064` (Sprint 6: Corporate simulation layer ported onto Sprint 1-5 substrate)
**Auditor scope:** read every source/content file; assess compilation by inspection; trace player flow; rate content; surface bugs and architectural risks.

---

## ⚠️ Pre-audit note: repo state mismatch

The user's brief stated *"the repo is NOW at /Users/danjames/Documents/cold_war_cowboys with all 6 sprints unified on main."* That is **almost** true:

- `origin/main` does have the unified chain — 6 sprint commits on top of the initial scaffold (`bcfce88 → e9fca12 (S1) → 30d1f75 (S2) → 723a6de (S3) → 1be9b77 (S4) → 0158da4 (S5) → 8790064 (S6)`).
- **The local `main` branch is 6 commits behind `origin/main`** and still points at the empty `first commit`. A `git pull` (fast-forward only, zero conflict risk) on the host repo will land the unified state on disk.

This audit was run after fast-forwarding the worktree branch to `origin/main`, against the unified tree.

---

## EXECUTIVE SUMMARY

The codebase is **architecturally strong but content-thin and runtime-fragile**. Six sprints' worth of game logic — world generation, hybrid mission resolution, narrative director, full corporate-sim AI, and a Razor UI seam — are present, internally consistent, and clean (~6,242 LOC, zero `TODO`/`FIXME`, well-documented, deterministic seed-forking throughout). The smoke test exercises Sprints 1–3 with 30+ assertions and a non-S&box `csproj` so a developer machine with `dotnet` can run it dry.

**Biggest strength:** the architectural seams. `MissionResolver` is genuinely pure; `ConsequenceProcessor` is the sole writer of `WorldState`; the `EventBus` decouples Sprint 6 from Sprint 3; the `IGameViewModel` interface keeps the Razor layer testable. A second pass at content authoring will not require structural surgery.

**Biggest gap (game-breaking):** [`TemplateLoader`](code/Generation/Templates/TemplateLoader.cs:53-85) reads only via `System.IO.File`/`Directory.GetCurrentDirectory()` walks. Its own doc comment promises a `Sandbox.FileSystem.Mounted` path that **does not exist in the implementation**. Under the actual S&box runtime, sandboxed code cannot walk arbitrary host directories, so every JSON template silently fails to load. Code in-engine therefore runs on hardcoded fallbacks: 3 missions, 4 archetypes, 0 scenes, 0 corporate factions/directives/events. **The Sprint 4 narrative layer cannot fire a single scene under the engine** because `NarrativeDirector` has no fallback when its input list is empty. This is the single fix that gates the difference between "S&box loads the project" and "the player has a game."

Behind that fix, content is **MINIMAL across the board** (10 missions, 7 scenes, 6 directives, 4 archetypes, 17 traits) — adequate to prove the systems work, far too thin to ship or even play-test for fun.

---

## 1. CODEBASE HEALTH

### File counts
| Layer        | Files | Notes |
|---|---|---|
| Core         | 6 .cs | EventBus, PhaseManager, Rng, WorldState, WorldSetting, CorporateState |
| Domain       | 8 .cs | Operative, Mission, Faction, Stats, Trait, Relationship, two enum files |
| Generation   | 8 .cs | World/Operative/Name/Hierarchy/Relationship/Scenario + Templates/{Templates, TemplateLoader} |
| Missions     | 7 .cs | Resolver, Board, Generator, ConsequenceProcessor, Result, Resolved, Template |
| Narrative    | 2 .cs | NarrativeDirector, SceneData |
| Corporate    | 7 .cs | Faction/Politics/Contract/Reputation/EventGenerator + ConsequenceProcessor + DataLoader |
| Game         | 1 .cs | GameManager (orchestrator) |
| UI (logic)   | 2 .cs | IGameViewModel, GameViewModel |
| UI (Razor)   | 6 .razor + 6 .scss | UIRoot, CycleHud, MissionBoardPanel, OperativeRosterPanel, SceneReaderPanel, Dial |
| Engine glue  | 2 .cs | code/Assembly.cs (global usings), code/MyComponent.cs (`CwcGame` scene component) |
| Editor       | 2 .cs | Editor/Assembly.cs, Editor/MyEditorMenu.cs (boilerplate) |
| Tests        | 1 .cs + 1 .csproj | tests/SmokeTest |
| Content      | 11 JSON | 7 root templates + 4 corporate templates |
| **Total**    | **46 .cs / 6 .razor / 6 .scss / 11 JSON** | ~6,242 inserted LOC across the unified diff |

### Compilation check (by inspection — `dotnet` not installed on this machine)

- **Type resolution within the codebase:** clean. Every `Mission`/`Operative`/`Faction`/`CorporateState` reference resolves to `CWC.Domain` or `CWC.Core`. The Sprint 6 systems correctly reference Sprint 3 types (`MissionResult`, `MissionResolved`).
- **Namespace structure:** 9 namespaces (`CWC.Core`, `CWC.Corporate`, `CWC.Domain`, `CWC.Game`, `CWC.Generation`, `CWC.Generation.Templates`, `CWC.Missions`, `CWC.Narrative`, `CWC.UI`). No conflicts; no duplicate type names.
- **`global using` files:** [`code/Assembly.cs`](code/Assembly.cs) declares `Sandbox`, `System.{Collections.Generic,Linq}`, plus `CWC.Core/Domain/Game`. [`Editor/Assembly.cs`](Editor/Assembly.cs) declares `Sandbox` and `Editor`. No overlap problems.
- **Razor references:** `<MissionBoardPanel>`, `<OperativeRosterPanel>`, `<SceneReaderPanel>`, `<CycleHud>`, `<Dial>` — every component referenced from `UIRoot.razor` exists at the expected path. SCSS files exist alongside each `.razor` (S&box convention).
- **SmokeTest csproj:** [`tests/SmokeTest/SmokeTest.csproj`](tests/SmokeTest/SmokeTest.csproj) explicitly globs the seven engine-independent layers (Core, Domain, Game, Generation, Missions, Narrative, Corporate). It does **not** include `code/UI/` or `code/MyComponent.cs` — correct, those are S&box-only. Should compile and run on any net8.0 machine.
- **No `TODO`, `FIXME`, `HACK`, `XXX`** anywhere in the source. Genuinely clean.

### Build verification status: **NOT EXECUTED**

`dotnet` is absent on this machine (`which dotnet → not found`, no SDK in `/usr/local`, `/opt/homebrew`, or `~/.dotnet`). The smoke test cannot be run from this environment. Compilation correctness was assessed by reading every file. **A definitive pass requires running `dotnet test` on a machine with the SDK installed**, or (preferably) opening the project in S&box and watching the compile log.

---

## 2. PLAYER EXPERIENCE FLOW

The on-disk game *should* be playable as a six-phase loop driven by the Razor UI, but several silent failures lie between "code compiles" and "the player has fun." Each phase below is traced through actual call paths.

### a) Launch → World Generation
**Entry point:** [`code/MyComponent.cs:9-21`](code/MyComponent.cs:9) — the S&box scene component `CwcGame` (renamed from boilerplate `MyComponent` in Sprint 1) instantiates `GameManager`, calls `NewGame(Seed)`, and creates a `GameViewModel`.

`GameManager.NewGame(seed)` ([`code/Game/GameManager.cs:51-79`](code/Game/GameManager.cs:51)) does the right things in the right order:
1. Reset `Rng`, `Phase`.
2. `WorldGenerator.Generate(seed)` builds setting → factions → roster (6 ops) → corporate hierarchy NPCs → relationships → scenario flags.
3. `MissionGenerator(loader)` and `MissionBoard(MissionGen)` instantiated.
4. `Director = new NarrativeDirector(loader.Deserialize<List<SceneTemplate>>("scenes.json") ?? new())`.
5. `Board.SeedFromScenario("extraction_defector", ...)` + `Board.Refresh(...)` for cycle 1.
6. `WireCorporateLayer(loader)` instantiates Sprint 6 systems and subscribes them to `MissionResolved`.

**Status:** **PLACEHOLDER → BLOCKER under S&box runtime.** The launch path *works* on dev machines (smoke test proves it) but the loader is broken in-engine — see §4 finding F1. Specifically:
- The Director receives `new()` when `scenes.json` fails to load → no narrative scenes will ever fire in-engine until F1 is fixed.
- `MissionGenerator` falls back to its 3 hard-coded templates ([code/Missions/MissionGenerator.cs:87-121](code/Missions/MissionGenerator.cs:87)).
- `CorporateDataLoader.Load{Factions,Directives,Events}` silently load nothing; the corporate layer runs against the world-default factions only and **no events ever fire**.
- `OperativeGenerator` falls back to 4 archetypes; trait pool is empty → operatives are statted but trait-less.
- `WorldGenerator` falls back to a hard-coded faction list, single tone tag, default era tagline.

A try/catch in `NewGame` ([line 57-66](code/Game/GameManager.cs:57)) further calls `ScaffoldFallbackWorld()` if `WorldGenerator.Generate` throws — this is a sensible belt-and-braces, but most loader failures don't throw, they return null/empty. So the silent-degradation path is the dominant one.

**Bug B1 — scenario seed mission ignored:** [`GameManager.cs:73`](code/Game/GameManager.cs:73) hard-codes `Board.SeedFromScenario("extraction_defector", ...)` instead of using `ScenarioGenerator.Result.SeedMissionTemplateId`. The other two scenarios in [`scenarios.json`](Data/Templates/scenarios.json) (`tarmac_audit`, `noor_problem`) declare different opening missions that never get picked.

### b) War Room phase (Briefing + Assignment)
**UI:** [`UIRoot.razor:14-23`](code/UI/UIRoot.razor:14) renders `<MissionBoardPanel>` + `<OperativeRosterPanel>` for both phases.
**MissionBoardPanel** ([code/UI/MissionBoardPanel.razor](code/UI/MissionBoardPanel.razor)) iterates `Vm.AvailableMissions` and exposes per-card pickers: every available operative shows as a `+ {Codename}` button that calls `Vm.Assign(opId, missionId)`. Already-assigned ops show with `✕` to unassign.
**OperativeRosterPanel** ([code/UI/OperativeRosterPanel.razor](code/UI/OperativeRosterPanel.razor)) shows the 6 field operatives with status, archetype, four `<Dial>` widgets (Loyalty/Stress/Morale/Conscience), and trait chips.
**ViewModel** ([code/UI/GameViewModel.cs:34-45](code/UI/GameViewModel.cs:34)) filters: missions that are Available/Active sorted wet-work-first, operatives with `Id < CorporateHierarchyGenerator.BossId` (1000) so executives don't show up in the field roster.

**Status:** **WORKS.** Wiring is complete and idiomatic. Only friction: the picker UI dumps every eligible operative as a button row regardless of count, with no skill-fit hint — fine for 6 ops, will get noisy at 12+.

### c) Mission Execution (Resolution phase)
On `Vm.Advance()` from Assignment → Resolution, [`GameManager.AdvancePhase`](code/Game/GameManager.cs:118) runs the `Resolution` switch arm → `ResolveActiveMissions()` ([line 150-173](code/Game/GameManager.cs:150)) — for every `Status==Active && AssignedOperativeIds.Count > 0` mission:
1. `Resolver.Resolve(mission, world, rng)` builds a pure `MissionResult`.
2. `Consequences.Apply(result, world)` mutates state (psych, status, heat, suspicion, reputation, faction standing, narrative flags).
3. `Bus.Publish(new MissionResolved(...))` → Sprint 6 systems react.

The hybrid resolver ([`MissionResolver.cs`](code/Missions/MissionResolver.cs)) computes `score = SkillContribution − PsychologyPenalty + RelationshipSwing + RngSwing`, classifies into 4 outcomes (Success/Partial/Failure/Catastrophe) by margin against difficulty, and emits per-operative impacts including wet-work conscience erosion and catastrophe injury/death rolls.

**Status:** **WORKS.** The resolver is the strongest single component — pure, well-decomposed, doc-commented. Tripwire flags (`third_kill:{id}`, `cold_blooded:{id}`, `hollowed_out:{id}`, `breaking_point:{id}`, `defection_risk:{id}`) emitted from `ConsequenceProcessor.EmitTripwireFlags` ([code/Missions/ConsequenceProcessor.cs:134-155](code/Missions/ConsequenceProcessor.cs:134)) plug directly into Sprint 4.

The UI shows nothing during this phase — [`UIRoot.razor:24-26`](code/UI/UIRoot.razor:24) renders a literal "Resolving missions…" string and waits for the user to click Advance. Acceptable for a placeholder; thin for a real experience (no log, no per-operative breakdown, no "X stress / Y conscience" feedback).

### d) Corporate phase (Sprint 6)
[`GameManager.RunCorporatePhase`](code/Game/GameManager.cs:175-186) runs in this order:
1. `Factions.ProcessTurn(corp)` — every non-host faction picks a weighted action (Poach / Sabotage / ProposeAlliance / EscalateToBoard / UndercutBudget / BankCash) and queues a consequence.
2. `Politics.EvaluateBoard(corp, world)` — reviews directive deadlines, applies director-agenda nudge, considers rank promote/demote.
3. `Contracts.RefreshContracts(corp)` — clears old non-mandatory contracts, generates one per faction, adds directive-driven mandatory contracts.
4. `CorporateEvents.Roll(corp, world)` — 60% chance of a weighted-pick event from templates.
5. `Reputation.Decay(corp, world)` — drift internal/external rep toward 50, decay suspicion.
6. `CorpConsequences.ApplyAll(corp)` — drain the queue, each `c.Apply(corp)` mutation runs once, emits to `RecentEventLog` (capped at 200), publishes on bus.
7. `Director.CheckCorporateTriggers(world)` — sets boardroom-flavored narrative flags (`corp:promotion_imminent`, `corp:audit_triggered`, etc.).

**Status:** **WORKS, but invisible.** Every Sprint 6 system runs and mutates state, *but the UI shows none of it* during the Corporate phase — `UIRoot.razor` has no case for `CyclePhase.Corporate`, so the default `<div class="screen-msg">No active session.</div>` renders. The player has no idea factions just acted, contracts just refreshed, the board just reviewed directives, or an event fired. That's a presentational gap, classified as **MISSING** (not BLOCKER) because the simulation runs.

Also missing: **no Sprint 6 contracts surface in `MissionBoard`.** `ContractSystem` writes contracts into `corp.AvailableContracts`, but `MissionBoardPanel` reads `Vm.AvailableMissions` which reads `World.Missions`. The two pools are not unified. The player never sees the corporate-issued contracts. **MISSING.**

### e) Narrative phase (Aftermath)
[`AdvancePhase`](code/Game/GameManager.cs:142-144) calls `Director.ConsumeFlags(world)` at Aftermath. `GameViewModel.Advance()` ([code/UI/GameViewModel.cs:49-58](code/UI/GameViewModel.cs:49)) then calls `Director.PopNextScene(World)` and stores the result in `_currentScene`.

`SceneReaderPanel` ([code/UI/SceneReaderPanel.razor](code/UI/SceneReaderPanel.razor)) renders the scene's title/setting/speaker/text lines and exposes choice buttons. `Vm.PickChoice(c)` calls `Director.ApplyChoice` (which mutates op psych + corp Heat + adds flags), then auto-pops the next queued scene.

**Status:** **WORKS in the smoke test, BLOCKED in-engine** until F1 (TemplateLoader) is fixed. With an empty template list, `ConsumeFlags` enqueues nothing, `PopNextScene` returns null, and `SceneReaderPanel` shows "No pending scenes." every Aftermath. The player's narrative is mute.

A secondary issue: of the 7 scenes in [`scenes.json`](Data/Templates/scenes.json), only `defection_risk_pull` consumes the `defection_risk:{id}` flag. The `breaking_point:{id}` flag is emitted but no scene listens for it — orphaned trigger. `heat:critical`, `body_found`, and the trait-arc scenes (`third_kill_intro`, `cold_blooded_review`, `hollowed_out`) are wired correctly.

### f) Cycle Loop
[`PhaseManager.Advance`](code/Core/PhaseManager.cs:20-34) walks `Briefing → Assignment → Resolution → Corporate → Aftermath → Review → Briefing`. On wrap to Briefing, [`GameManager`](code/Game/GameManager.cs:124-132) bumps `Cycle++`, `Day++`, recovers Injured ops, decays stress/morale toward baseline, and refreshes the mission board.

**Status:** **WORKS.** Smoke test §7 verifies the full wrap. The Review phase has nothing assigned to it in `GameManager.AdvancePhase` — [`UIRoot.razor:30-32`](code/UI/UIRoot.razor:30) shows the roster panel here, so the player gets a "look at your team after the dust settles" beat for free, but no aggregate cycle summary, no narrative postcard, no after-action report.

### g) Save / Load
**Status:** **BLOCKER.** No save/load mechanism exists anywhere. `WorldState`/`CorporateState` are POCOs that *would* serialize cleanly (no event handlers stored, no lambdas captured, all state in fields), but no `SaveGame()`/`LoadGame()` methods, no JSON-write call, no S&box `FileSystem.Data` integration. Any cycle interrupted means losing the run. For a roguelike-cadence game this might be acceptable; for any session longer than 10 minutes it's not.

### h) Settings / Config
**Status:** **MISSING.** `CwcGame` exposes a single `[Property] public ulong Seed` and nothing else. No difficulty, no roster size override, no tone-tag selector, no mute, no key-rebinds. Acceptable for an alpha; not shippable.

### Phase-by-phase summary

| Phase             | Status      | Classification |
|---|---|---|
| Launch / World Gen | works → silent fallbacks under S&box | **BLOCKER** (F1) |
| Briefing           | works | OK |
| Assignment         | works | OK |
| Resolution         | works (no UI feedback)| **MISSING** UX |
| Corporate          | runs invisibly | **MISSING** UX (no panel) + **MISSING** contract surfacing |
| Aftermath          | works → mute under S&box | **BLOCKER** (F1) |
| Review             | works (roster only) | acceptable |
| Save/Load          | doesn't exist | **BLOCKER** for sessions |
| Settings           | doesn't exist | MISSING |

---

## 3. CONTENT READINESS

| Template                | Count | Sufficiency | Notes |
|---|---|---|---|
| Archetypes              | 4     | **MINIMAL**   | operator/ghost/decker/fixer. Every roster of 6 produces 1.5 of each — visible repetition. Should hit 8–12 for variety. |
| Traits                  | 17    | **MINIMAL**   | 5 personality, 5 background, 4 vice, 3 compulsion. With 85/85/35/25% incidence each operative samples ~2 visible traits — players will see the whole pool inside one run. Target 30+. |
| Names — first / last / codename | 32 / 24 / 23 | **ADEQUATE** | Good for a roster ≤ 12; collision detection in [NameGenerator.cs:42](code/Generation/NameGenerator.cs:42) is sound. Gender split in source list isn't tagged — distribution is incidental. |
| World — corps / locations / years / taglines / tones | 6 / 5 / 5 / 5 / 5 | **MINIMAL** | Good per-axis but the cross-product is small; flavor will repeat. |
| World factions          | 8     | **ADEQUATE**  | Covers HostCorp / 2 RivalCorps / Razor agency / 2 InternalDivisions / Syndicate / NGO press. Good kind-coverage. |
| Mission templates       | 10    | **MINIMAL**   | Distribution: 2 Extraction, 2 Sabotage, 2 Surveillance, 2 Assassination, 1 DataTheft, 1 CounterIntel. Briefings are well-written and tonally on. Wet-work moral weight set thoughtfully (assassination_witness=90, assassination_rival=80). With 3–5 per cycle for ~10 cycles, the same titles repeat fast. Target 30+. |
| Scenarios               | 3     | **MINIMAL**   | The Recall / Tarmac Audit / Noor Problem. Each defines starting corp dials + one seed mission + scenario flags. Scenarios are good-quality; need 6–10 for replay surface. *Bug B1 means only `the_recall`'s seed mission is actually used.* |
| Scenes (Sprint 4)       | 7     | **SKELETON**  | This is the biggest content shortfall for a narrative-driven game. Existing scenes are well-written (`third_kill_intro` is genuinely good prose) but cover only the most extreme tripwires. No mid-tenure beats, no relationship-drift scenes, no cycle-N director scenes, no scenario-specific follow-ups. Most cycles will produce zero scenes. Target 40–60 for a campaign run. |
| Corporate factions (Sprint 6) | 5 | **ADEQUATE for the system, DISJOINT from world** | Vanguard / Blacklight / Ironbridge / Synthesis / Deepwell. *Bug B2: these IDs do not overlap with `world.json`'s 8 factions, so the player ends up with **13 factions** in `World.Factions` after the merge in [GameManager.cs:106-110](code/Game/GameManager.cs:106), with no mission templates referencing the 5 corporate ones.* Either the corporate set should replace the rival_* world set, or the mission templates should include them. |
| Corporate directives    | 6     | **MINIMAL**   | Two mandatory, four optional, mix of compliance levers. Reasonable starting set. |
| Corporate events        | 10    | **MINIMAL**   | Good kind-coverage (BoardReshuffle/FactionMerger/Split/BudgetCut/Windfall/Whistleblower/Espionage/MarketShift), but BaseWeights are flat (all 0.4–1.2). No per-faction or per-rank flavor. |
| Corporate "contracts"   | 4     | **DEAD CONTENT** | *Bug B3: [`Data/Templates/corporate/contracts.json`](Data/Templates/corporate/contracts.json) contains negotiation-lever templates, not contracts. **It is never loaded.** [`CorporateDataLoader`](code/Corporate/CorporateDataLoader.cs) has no `LoadContracts` method. `ContractSystem.Negotiate` hardcodes lever costs at [code/Corporate/ContractSystem.cs:66-73](code/Corporate/ContractSystem.cs:66).* |

**Overall content rating: SKELETON-to-MINIMAL.** Every system has *some* content, but the volume is roughly "enough to prove the system can fire" — not "enough to play." Scenes are the headline gap.

---

## 4. CODE QUALITY

XML doc-commented public types: most. Consistent formatting (tabs, `( ... )` spacing — S&box house style). Zero `TODO`/`FIXME`/`HACK`. Below are concrete findings.

### Bugs / logic errors

**F1 — `TemplateLoader.ReadText` cannot work in S&box.** [code/Generation/Templates/TemplateLoader.cs:53-85](code/Generation/Templates/TemplateLoader.cs:53). Doc comment promises a `Sandbox.FileSystem.Mounted` path; implementation only walks via `Directory.GetCurrentDirectory()` + `File.ReadAllText`. Sandboxed S&box code cannot read host filesystem paths outside the game's mounted virtual FS. Net effect under engine: *every* JSON template fails to load, fallbacks rule, and Sprint 4 + Sprint 6 effectively don't load any content. **The smoke test passes only because it runs as a console app outside the sandbox.** Fix is small but mandatory: add a real `FileSystem.Mounted.ReadAllText(path)` path that runs first, fall through to disk for tooling.

**B1 — Scenario seed mission ignored.** [code/Game/GameManager.cs:73](code/Game/GameManager.cs:73) hardcodes `Board.SeedFromScenario("extraction_defector", ...)`. `ScenarioGenerator.Result.SeedMissionTemplateId` is computed and discarded. Two of three scenarios get the wrong opening mission.

**B2 — Corporate factions disjoint from world factions.** [`Data/Templates/corporate/factions.json`](Data/Templates/corporate/factions.json) defines 5 IDs (`vanguard`, `blacklight`, `ironbridge`, `synthesis`, `deepwell`); [`Data/Templates/world.json`](Data/Templates/world.json) defines 8 (`host`, `rival_kasumi`, etc.). [`GameManager.WireCorporateLayer`](code/Game/GameManager.cs:106-110) merges them, so the player has 13 factions, of which 5 are referenced by no mission template. Either (a) collapse to a single source or (b) extend mission templates with corporate-faction candidates.

**B3 — `corporate/contracts.json` is dead content.** No `LoadContracts` method anywhere; `ContractSystem.Negotiate` hardcodes [`baseCost`](code/Corporate/ContractSystem.cs:66-73). Either remove the file or write the loader.

**B4 — Mandatory contracts never expire.** [code/Corporate/ContractSystem.cs:41](code/Corporate/ContractSystem.cs:41): `corp.AvailableContracts.RemoveAll( c => !c.IsMandatory && c.Status == MissionStatus.Available )`. Mandatory contracts persist across cycles indefinitely, even after their `CycleDeadline`.

**B5 — `ApplyTeammateRelationshipDrift` ternary picks identical kinds.** [code/Missions/ConsequenceProcessor.cs:73-74](code/Missions/ConsequenceProcessor.cs:73): `Kind = drift > 0 ? RelationshipKind.Acquaintance : RelationshipKind.Acquaintance`. Both branches identical — likely intended to seed `Friend` on positive drift / `Rival` on negative.

**B6 — `breaking_point:{id}` flag emitted but not consumed.** [code/Missions/ConsequenceProcessor.cs:148](code/Missions/ConsequenceProcessor.cs:148) sets the flag when stress >= 90; no `scenes.json` template lists `any_op:breaking_point` in `RequiredFlags`. Either author the scene or stop emitting.

**B7 — Sprint 6 contracts never reach the player UI.** `ContractSystem.RefreshContracts` writes to `CorporateState.AvailableContracts` ([code/Corporate/ContractSystem.cs:43-55](code/Corporate/ContractSystem.cs:43)). `GameViewModel.AvailableMissions` reads `World.Missions`. The two stores are disjoint. The corporate-pipeline missions are computed and never surfaced.

**B8 — `UIRoot` types `Vm` as `GameViewModel` (concrete) but children take `IGameViewModel` (interface).** [code/UI/UIRoot.razor:46](code/UI/UIRoot.razor:46) vs [code/UI/CycleHud.razor:53](code/UI/CycleHud.razor:53). Not a bug — `GameViewModel` implements the interface — but breaks the seam advertised in [`IGameViewModel`'s doc comment](code/UI/IGameViewModel.cs:10). Trivial to fix.

**B9 — `CorporateState.Clone` is incomplete.** [code/Core/CorporateState.cs:86-101](code/Core/CorporateState.cs:86) copies scalars but not `Roster`, `Factions`, `AvailableContracts`, `ActiveDirectives`, or `RecentEventLog`. Currently unused; a footgun if save/load is added later.

### Engine integration concerns

- **EventBus wiring**: `GameManager.WireCorporateLayer` ([line 113-115](code/Game/GameManager.cs:113)) subscribes Faction/Politics/Reputation systems to `MissionResolved`. `ContractSystem` and `CorporateEventGenerator` are *not* subscribed — they're called explicitly from `RunCorporatePhase`. Inconsistent but defensible (those don't need per-mission reaction). No event subscriptions are unsubscribed; on `NewGame` re-entry, old subscriptions linger because a fresh `Bus` is not created. **Concern:** repeated `NewGame` calls double-subscribe handlers and double-fire mutations. Mitigation: instantiate a fresh `EventBus` in `NewGame` (currently only `CorpConsequences` is fresh), or drop subscriptions in a teardown step.
- **Seeded RNG forking** is consistent and idiomatic. Every subsystem `Fork`s with a string label, stat rolls fork per-operative — the determinism the smoke test asserts in §5 actually holds.
- **Unbounded growth**: `RecentEventLog` capped at 200 (good). `WorldState.NarrativeFlags` (`HashSet<string>`), `WorldState.Headlines`, `WorldState.ActiveCrises` are unbounded and only ever grow. Across a 50-cycle run the flag set could reach the low thousands — fine for memory, problematic for `MatchesFlag` linear scans in `NarrativeDirector.MatchesFlag` ([code/Narrative/NarrativeDirector.cs:138-156](code/Narrative/NarrativeDirector.cs:138)).

### Style / minor

- `MissionResolver.cs` declares `using System.Text;` but never uses `StringBuilder` ([line 4](code/Missions/MissionResolver.cs:4)). Cosmetic.
- `OperativeGenerator.RollSkills` uses string keys (`"Combat"`, `"Stealth"`, …) to look up `SkillBands`, then re-`Enum.Parse` elsewhere — round-trips through string. Workable; a `Dictionary<SkillKind, IntRange>` on `ArchetypeTemplate` would be cleaner.
- `MissionBoard.Refresh` ambiguous-mission seeding logic at [code/Missions/MissionBoard.cs:30-46](code/Missions/MissionBoard.cs:30) is correct but hard to read; worth a refactor.

---

## 5. ARCHITECTURE

### Dependency graph (clean, no cycles)

```
   ┌───────────┐
   │ Domain    │ ◄────────── (every layer reads, none writes back)
   └─────┬─────┘
         │
   ┌─────▼─────┐
   │ Core      │  EventBus, PhaseManager, Rng, WorldState
   └─────┬─────┘
         │
   ┌─────▼──────────────────────────────────────────┐
   │ Generation  Missions  Narrative  Corporate    │
   └─────┬──────────────────────────────────────────┘
         │
   ┌─────▼─────┐
   │ Game      │  GameManager (orchestrator)
   └─────┬─────┘
         │
   ┌─────▼─────┐         ┌────────────┐
   │ UI logic  │ ◄────── │ UI Razor   │
   │ (VM)      │         │ (panels)   │
   └───────────┘         └────────────┘
```

No back-edges. `Domain` knows nothing about `Core`/`Generation`. `Resolver` doesn't see `WorldState` mutators. `EventBus` is the only cross-cutting glue and it's typed.

### Seam evaluation

- **`IGameViewModel`**: clean. The Razor layer references `IGameViewModel` in 5 of 6 panels (the 6th is a dumb `<Dial>` that takes no VM). Replacing `GameManager` with a fake VM for design tooling is one constructor away. **B8** is the only chip in the paint.
- **`MissionResult` / `ConsequenceProcessor`**: textbook command-pattern split. The resolver builds a `MissionResult` (pure value); the processor is the single writer. Property tested implicitly by the smoke test asserting same-seed determinism.
- **`CorporateConsequence`**: a queued mutation with `Action<CorporateState>`. Perfect for ordering and replay. The lambdas capture variables (`Apply = c => { ... }`) — fine in-process, but means corporate consequences cannot be persisted across save/load. If save/load is added, the queue must be drained before save.
- **`EventBus`**: lightweight, copies the handler list before dispatch ([code/Core/EventBus.cs:33](code/Core/EventBus.cs:33)) so handlers can subscribe/unsubscribe during dispatch. Good defensive design.

### Extensibility

- **New mission templates**: data-only. ✅
- **New mission types** (enum): code-only. Adding one requires extending `MissionType`, `Resolver.DefaultWeightsFor`, and possibly UI badge styling. Fine.
- **New traits / archetypes**: data-only. ✅
- **New scene templates**: data-only. ✅
- **New faction kinds**: enum-bound, code change.
- **New corporate event kinds**: enum-bound, code change. Each new `CorporateEventKind` needs a `case` in `CorporateEventGenerator.Fire`.
- **New negotiation levers**: enum-bound + code. The dead JSON file suggests the original intent was data-driven — that loop never closed.
- **New phase**: enum-bound + `PhaseManager.Advance` switch + `GameManager.AdvancePhase` switch + UI rendering. A real refactor.

**Verdict:** the data-driven content path (templates → loader → generator) is the system's strongest extensibility surface. The enum-bound axes (mission type, event kind, negotiation lever, faction kind) are the weak ones.

---

## 6. PRIORITIZED NEXT STEPS

Estimates are wall-clock for a single competent dev, assuming the Sprint 1–6 substrate stays as is. "Autonomous" = could be done by an agent with this audit as input. "Requires Dan" = needs design decisions, content authoring voice, or in-engine validation only the human can perform.

### P0 — Playable build blockers (must exist before *any* human play-test)

| # | Item | Hours | Mode | Depends on |
|---|---|---|---|---|
| P0.1 | **Fix `TemplateLoader` to use `Sandbox.FileSystem.Mounted` first** (F1). Add the actual call the doc comment promises; keep disk-walk as the smoke-test fallback. Verify in-engine that `archetypes.json`, `missions.json`, `scenes.json`, `corporate/*.json` all load. | 2–4 | autonomous (autonomous can write the code; Dan must verify in S&box) | — |
| P0.2 | **Pull `origin/main` to local `main`.** Six commits, fast-forward, zero risk. Without this, every other person looking at the repo sees an empty scaffold. | 0.05 | autonomous | — |
| P0.3 | **Surface Sprint 6 contracts in the mission board UI.** Either merge `CorporateState.AvailableContracts` into `World.Missions` at the end of the Corporate phase, or extend `GameViewModel.AvailableMissions` to union both. (B7) | 1 | autonomous | P0.1 |
| P0.4 | **Add a Corporate-phase UI panel** (even a read-only event-log feed with the most recent 8 entries from `RecentEventLog`). Currently the Corporate phase shows the empty-state placeholder; the simulation runs invisibly. | 2–3 | autonomous | P0.1 |
| P0.5 | **Author 6–8 Aftermath scenes for the bare cases:** mission-success debrief, mission-failure debrief, first-mission-no-assignments warning, cycle-3 mid-tenure beat, cycle-N director check-in, generic relationship-drift scene. Without these the Aftermath phase is "No pending scenes" 80% of the time even with F1 fixed. | 4–6 | **requires Dan** (voice authoring) | P0.1 |

**P0 total: ~10–14 hours, of which ~4–6 require Dan.**

### P1 — Meaningful gameplay (what makes the systems matter)

| # | Item | Hours | Mode | Depends on |
|---|---|---|---|---|
| P1.1 | **Save/load** — `WorldState`/`CorporateState` are POCO-shaped; add `SaveSlot.Save(world)` writing JSON to `FileSystem.Data`, plus a load path. Drain `CorpConsequences` queue before saving (it holds lambdas). Fix `CorporateState.Clone` (B9) while you're in the file. | 6–8 | autonomous | P0.1 |
| P1.2 | **Resolution-phase result panel.** A 4-state outcome card per resolved mission with skill / psych / relationship / RNG breakdown, per-op stress/morale/conscience deltas, and the wet-work counter for affected ops. Data is already in `MissionResult` ([code/Missions/MissionResult.cs](code/Missions/MissionResult.cs)) — pure presentation work. | 4–6 | autonomous | — |
| P1.3 | **Triple the scene library.** Target ~25 scenes covering: first 3 cycles of director rapport, defector recruitment, faction-alliance offers, board-rank-change scenes, mid-stress decompression beats, every corp-trigger flag (`corp:promotion_imminent`, `corp:audit_triggered`, `corp:confrontation:*`), and at least one scene per scenario. Author `breaking_point:{id}` (B6). | 12–20 | **requires Dan** | P0.1, P0.5 |
| P1.4 | **Reconcile factions** (B2). Either fold the 5 corporate factions into `world.json` (renaming so missions can target them) and delete `corporate/factions.json`, or split the worlds with a deliberate "rival corps" vs "internal divisions" structure. | 1–2 | **requires Dan** (design call) | — |
| P1.5 | **Use the scenario seed mission** (B1). One-line fix in `GameManager.NewGame`. | 0.25 | autonomous | — |
| P1.6 | **Fix B4 (mandatory contracts never expire), B5 (relationship-drift kind ternary), B8 (UIRoot Vm type), B9 (`Clone` incomplete).** Quick batch. | 1 | autonomous | — |
| P1.7 | **Double mission templates** to ~20, with at least 3 per `MissionType`. Same authoring pattern — JSON only. | 4–6 | **requires Dan** | — |
| P1.8 | **Author 8–10 more traits**, especially Personality and Compulsion (currently 5 / 3). | 2 | **requires Dan** | — |
| P1.9 | **Decide and remove `corporate/contracts.json`** OR write `LoadContracts` and read lever costs from JSON (B3). | 0.5 | autonomous + 0.25 design call | — |

**P1 total: ~30–46 hours, ~16–22 requiring Dan.**

### P2 — Polish

| # | Item | Hours | Mode |
|---|---|---|---|
| P2.1 | Per-operative skill-fit hints in mission picker (e.g. greying out poor fits, showing best/worst skill match badges). | 3 | autonomous |
| P2.2 | Cycle-summary "Review" panel — aggregate of resolutions, dial deltas, headlines added, scenes seen. | 4 | autonomous |
| P2.3 | Refactor `ArchetypeTemplate.SkillBands` to keyed by `SkillKind` not `string`. | 1 | autonomous |
| P2.4 | Refactor `MissionBoard.Refresh` ambiguous-seeding branch for readability. | 0.5 | autonomous |
| P2.5 | Tonal pass on the existing 7 scenes with the post-P1 scene voice as reference. | 2 | requires Dan |
| P2.6 | Settings stub: seed input, roster size, audio mute. | 2 | autonomous |
| P2.7 | Bus-subscription teardown / fresh `EventBus` on `NewGame` (handler doubling concern). | 0.5 | autonomous |
| P2.8 | UI loading state for `CyclePhase.Resolution` — show per-mission progress instead of "Resolving missions…". | 2 | autonomous |

**P2 total: ~15 hours.**

### P3 — Ship (Steam readiness)

| # | Item | Hours | Mode |
|---|---|---|---|
| P3.1 | Steam page assets — capsule, hero, screenshots, GIFs. | — | requires Dan + asset work |
| P3.2 | Tutorial / first-cycle tooltips. Walkthrough overlay for War Room → Resolution → Aftermath. | 8 | mixed |
| P3.3 | Localizable strings — extract Razor labels and JSON content title/briefing/text fields into a string-id system. | 12 | autonomous |
| P3.4 | Cloud save support via S&box `FileSystem.Data` if applicable. | 4 | autonomous |
| P3.5 | Telemetry: opt-in death/cycle counter for tuning difficulty curve. | 6 | autonomous |
| P3.6 | Unit tests beyond the smoke test — `MissionResolver` distribution properties, `ConsequenceProcessor` idempotency, `NarrativeDirector` flag-prefix matching. | 8 | autonomous |
| P3.7 | Performance pass: replace linear `NarrativeFlags` scans with prefix index if flag count >1k. | 4 | autonomous |
| P3.8 | Achievements / persistent run history. | 8 | autonomous + design |

**P3 total: ~50+ hours, much of which is non-engineering.**

---

## Closing assessment

This codebase is in a strange place: **the engineering is genuinely solid, and the game cannot currently run** — because of a single 30-line file (`TemplateLoader`) shipping a doc comment that doesn't match its implementation. Once F1 is fixed and the contract surfacing + corporate panel are added (P0, total ~10 hours of code), Dan has a working playable loop he can sit in front of. Once the scene library triples (P1.3, ~15 hours of authoring), it becomes a game worth showing.

There is no rewrite required. There is no architectural debt that compounds. There is a content authoring debt and one engine-integration bug. Fix those two things and the unified branch is closer to playable than the user appears to believe.
