# Cold War Cowboys

A cyberpunk strategy game blending the dynastic, sandbox simulation of *Crusader Kings* with the choice-driven narrative beats of a Telltale adventure. You run a shadow operations cell inside a fractured megacorp, dispatching operatives on missions while corporate politics, personal relationships, and your own creeping corruption reshape the world around you.

Built on **s&box** (Facepunch's source-available engine), with a Razor-based UI layer.

---

## What the game is

Each night you balance two pressures:

- **The board.** A procedurally-generated world of corporate factions, internal hierarchies, and contract opportunities. You build a roster of operatives — each with traits, relationships, and a stake in the outcome — and assign them to missions resolved by a hybrid stat/dice math model with four-state outcomes (success / partial / fail / catastrophe).
- **The story.** Mission outcomes feed a flag-driven narrative director that selects scenes from a content library. Scenes cast operatives by role, resolve gendered tokens, branch on player choices, and write back to world state. A corruption index tracks the moral cost of your decisions and inverts choice text once you've crossed certain thresholds — your operatives literally start reading the world differently.

The intended feel: every cycle should produce a story you'd want to tell someone.

---

## Getting started

### Running the game

This is an s&box project. To play:

1. Install the [s&box editor](https://sbox.game).
2. Open `cold_war_cowboys.sbproj` from the editor's project list.
3. Hit Play. The startup scene is `scenes/minimal.scene`; the `CwcGame` component in [code/MyComponent.cs](code/MyComponent.cs) bootstraps the game systems and mounts the Razor UI.

### Running the smoke test

The engine-independent layers (Core, Domain, Game, Generation, Missions, Narrative, Corporate) compile against plain .NET 8 so they can be exercised without the editor:

```sh
cd tests/SmokeTest
dotnet run
```

This drives world generation, mission resolution, narrative scene selection, and corporate simulation through a deterministic seed and prints a summary. Useful for verifying logic changes without booting s&box.

---

## Architecture overview

Three loosely-coupled layers sit on top of a small core, communicating through an `EventBus` and a shared `WorldState` (mutated by the consequence processors, the narrative layer, and save-restore — coordinated by `GameManager`'s phase loop rather than a single writer):

### 1. Strategy sandbox
Procedural world generation, operative rosters, mission boards, and resolution math.
- [code/Generation/](code/Generation/) — `WorldGenerator`, `OperativeGenerator`, `CorporateHierarchyGenerator`, `RelationshipSeeder`, `ScenarioGenerator`. JSON-driven via [code/Generation/Templates/](code/Generation/Templates/).
- [code/Missions/](code/Missions/) — `MissionBoard` (offer pool), `MissionResolver` (pure hybrid stat/dice math), `ConsequenceProcessor` (the **only** writer to `WorldState` for mission outcomes), `MissionNarrativeRunner` (per-mission narrative sequences).
- [code/Domain/](code/Domain/) — `Operative`, `Mission`, `Faction`, `Stats`, `Trait`, `Relationship`, and supporting enums.

### 2. Narrative engine
Flag-driven scene selection and presentation.
- [code/Narrative/NarrativeDirector.cs](code/Narrative/NarrativeDirector.cs) selects scenes from the content library based on world flags, mission results, and operative state.
- [code/Narrative/SceneData.cs](code/Narrative/SceneData.cs) defines the JSON schema: roles, conditions, lines, choices, and consequences.
- Scenes cast operatives by role at runtime, resolve `{token}` substitutions (names, gendered pronouns, faction names), and route choices through `ConsequenceProcessor`.

### 3. Human simulation (corporate layer)
Autonomous corporate AI that runs in parallel with the player's actions.
- [code/Corporate/](code/Corporate/) — `FactionSystem`, `PoliticsSystem`, `ContractSystem`, `ReputationSystem`, `CorporateEventGenerator`, `CorporateConsequenceProcessor`. Subscribes to `EventBus` to react to player moves and emits its own contract offers, political shifts, and reputation effects.

### Core
- [code/Core/EventBus.cs](code/Core/EventBus.cs) — pub/sub seam between layers.
- [code/Core/PhaseManager.cs](code/Core/PhaseManager.cs) — turn structure (Corporate → Mission Board → Resolution → Review).
- [code/Core/WorldState.cs](code/Core/WorldState.cs), [code/Core/CorporateState.cs](code/Core/CorporateState.cs), [code/Core/CorruptionTracker.cs](code/Core/CorruptionTracker.cs) — game state.
- [code/Core/Rng.cs](code/Core/Rng.cs) — deterministic seeded RNG with seed-forking for reproducibility.

### UI
[code/UI/](code/UI/) is s&box's Razor system (not Blazor WebAssembly — the file extensions are the same but the runtime is s&box's). `UIRoot.razor` mounts phase-specific panels (`CorporatePhasePanel`, `MissionBoardPanel`, `OperativeRosterPanel`, `NarrativeSequencePanel`, `ResolutionResultsPanel`, `CycleReviewPanel`) and binds them to `GameViewModel` through the `IGameViewModel` interface — keeping the UI testable and the game logic engine-independent.

### Save / load
[code/Save/SaveSystem.cs](code/Save/SaveSystem.cs) serializes the full game state to `Data/Saves/`. Saves round-trip through `SaveData.cs`'s versioned schema.

---

## Current status

**Systems: built and wired** (July 2026 wiring pass). Nine autonomous nightly sprints built every system's module; a fresh-eyes assessment (see git history for `ASSESSMENT.md`) then found that roughly a third of the public surface was never called, the scene library silently failed to load, and the math contradicted the theme. The July 2026 wiring pass fixed all of it: the loader is loud and validated, the mission narrative runner ignites, save/load round-trips completely (including RNG streams), corp/world faction state is unified, the economy pays, corruption is choice-driven and its endgame milestones are reachable, and gender tokens resolve against the operative the scene is about.

Two validation harnesses run against the real game code (no mirrored logic):

```sh
cd tests/SmokeTest && dotnet run     # 84 checks: loaders, wiring, round-trips
cd tools/PolicySim && dotnet run     # 40-seed × 20-cycle humane-vs-ruthless balance targets
```

**Not yet done:** the project has not been booted inside the s&box editor. The sandbox-unsafe reflection/System.IO code has been replaced with a provider seam (`CwcFiles` + `SandboxFileProvider` over `FileSystem.Mounted`/`FileSystem.Data`), which is the API the sandbox documents — but until someone opens the .sbproj and sees a scene fire, in-engine behavior remains unverified.

**Phase: content authoring.** With the wiring pass done, the bottleneck is genuinely content: callback scene chains, wet-work mission variety, and depth-2 scenes. See `CWC_DESIGN_DOCUMENT.md` for the full content plan and roadmap.

---

## Content authoring

All gameplay content is JSON, loaded from [Data/Templates/](Data/Templates/) at startup:

- `missions.json` / `narrative_missions.json` — mission templates with stat checks, role requirements, and outcome branches.
- `scenes.json` — narrative scenes consumed by `NarrativeDirector`. Each scene declares roles, selection conditions, line text with token slots, and player choices that route through `ConsequenceProcessor`.
- `archetypes.json`, `traits.json`, `names.json` — operative generation.
- `world.json`, `scenarios.json` — world setting and scenario seeds.
- `corporate/` — faction, directive, and corporate-event templates.

For the comprehensive scene-writing guide covering JSON format reference, use-case walkthroughs, and the cyberpunk register guide, see the [Content Authoring Manual](docs/CWC_Content_Authoring_Manual.pdf).

---

## Project structure

```
cold_war_cowboys/
├── cold_war_cowboys.sbproj   # s&box project descriptor
├── code/                     # game code (compiled by s&box)
│   ├── Assembly.cs           # global usings
│   ├── MyComponent.cs        # CwcGame scene component (engine entry point)
│   ├── Core/                 # EventBus, PhaseManager, WorldState, RNG
│   ├── Domain/               # Operative, Mission, Faction, Stats, Trait
│   ├── Generation/           # World/operative/hierarchy generation + Templates loader
│   ├── Missions/             # Resolver, board, consequence processor, narrative runner
│   ├── Narrative/            # NarrativeDirector, SceneData
│   ├── Corporate/            # Faction politics, contracts, reputation, events
│   ├── Game/                 # GameManager (top-level orchestrator)
│   ├── Save/                 # Save/load
│   └── UI/                   # Razor (.razor + .scss) phase panels and HUD
├── Editor/                   # s&box editor extensions
├── Assets/scenes/            # s&box scenes (minimal.scene = startup)
├── Data/
│   ├── Templates/            # JSON content (missions, scenes, archetypes, traits, world, corporate/)
│   └── Saves/                # save files
├── tests/SmokeTest/          # engine-independent .NET 8 smoke test
├── tools/                    # auxiliary scripts (e.g., balance_test.py)
└── docs/
    ├── CWC_Content_Authoring_Manual.pdf
    └── QA_AUDIT.md
```
