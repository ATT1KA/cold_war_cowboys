# Cold War Cowboys — Design & Project Document

*July 2026. Supersedes the original design docs and the September README claims. Incorporates the full findings of the fresh-eyes assessment (ASSESSMENT.md, July 2026) and the wiring pass that followed it. This document describes the game as it actually exists in code, what changed, and what happens next.*

---

## 1. Vision statement

**Cold War Cowboys is a game about middle management of the damned.** You run a shadow-operations cell inside a fractured megacorp: a Crusader Kings-style human simulation generates the people and the pressures, a Telltale-style narrative layer prices your decisions emotionally, and a corruption arc measures — in performance-review prose — what running the numbers does to you.

The assessment sharpened the vision by identifying what actually works:

- **The unclaimed register is corporate-HR horror, not cyberpunk.** The game's best writing is "asset realignment," euphemisms with their own euphemisms, an auditor whose paper notebook is the scariest object in the building. The neon and chrome are set dressing; the banality of corporate evil is the voice. We lean into that.
- **The strongest character in the game is *you*.** The second-person director voice — performance reviews, noodle bars, redefining "acceptable losses" one reasonable decision at a time — lands harder than any operative vignette. The corruption scenes about your own hollowing are the emotional spine.
- **The target player came to be implicated.** This is the Darkest Dungeon player who realized the real game was deciding whom to feed to the Cove; the Frostpunk player who signed the child-labor law to see the "but was it worth it?" card. That player notices immediately if kindness is free. The game must genuinely charge them for their soul — and now, mechanically, it does (see §3).
- **Procedural people over authored cast.** There are no authored operatives (Mason, Kira, Ghost, and Davi never existed outside design conversations), and that is the right call: the corruption arc's engine is your willingness to spend people, and that only hurts when the people are yours, shaped by your run. Authoring budget goes to role-cast vignettes with callbacks, plus at most one or two authored fixtures (Director Vale as mentor-tempter).

**The one-line pitch:** every cycle should produce a story you'd want to tell someone — and by cycle twenty, the story should be about what you became.

---

## 2. Architecture overview — as it actually exists

Built on **s&box** (Facepunch), Razor UI, all game logic engine-independent C# (~7k LOC) with JSON content (~4.4k lines). Three layers over a small core, coordinated by `GameManager`'s six-phase cycle loop:

```
Briefing → Assignment → Resolution → Corporate → Aftermath → Review
```

### The honest state of each seam (post-wiring-pass)

| Seam | Pre-assessment reality | Now |
|---|---|---|
| Scene loading | **Silently loaded zero scenes** (string enums + no converter + `catch { return null; }`) | `JsonStringEnumConverter` registered; every load failure recorded and logged; `TemplateValidator` checks ids, trigger types, resolvers, flag cross-references, and stat bands at boot; smoke test asserts the full library loads |
| Mission narrative runner (the Telltale half) | Fully built, **`Begin()` had zero call sites** — all six authored sequences dead | Ignites automatically when leaving Assignment with a sequence mission staffed; phase advance holds until the sequence completes; choices emit narrative flags (`seq:<template>:<approach>`), write the choice log, and feed corruption |
| "Single-writer WorldState" | False — eight writers | Still multiple writers, but now *documented as such*; mutation is coordinated by phase ordering rather than pretending to a single-writer rule |
| EventBus | 80% ceremony; six event types with zero subscribers | Corporate events feed `World.Headlines`; rank changes and reputation thresholds emit narrative flags; mission resolution still fans out to the three corporate systems |
| Corp/world faction state | **Split-brain** — divergent duplicate objects; faction AI mutations invisible to UI and narrative | `CorporateDataLoader` merges into the world's faction objects; `corp.Factions[id]` and `World.Factions` are reference-identical (smoke-test asserted); views rebuilt after save restore |
| Consequence ordering | Board evaluated confidence *before* the cycle's mission fallout applied | Mission fallout applies first; board evaluates last, against current numbers |
| Save/load | Defect cluster: directives, one-shots, milestones, weights, RNG all lost on round-trip | Full round-trip (asserted): directives + pending pool, fired one-shots, crossed milestones + choice weight, mission weights/flags/contract metadata, choice log, executive flags, and the four corporate RNG stream states (xoshiro256\*\* with serializable state — determinism survives a load) |
| Sandbox compatibility | Reflection (`Type.GetType("Sandbox.FileSystem…")`) + raw `System.IO` — the exact API classes the s&box allowlist rejects | All file access behind `CwcFiles`/`ICwcFileProvider`; in-engine provider (`SandboxFileProvider`) uses `FileSystem.Mounted`/`FileSystem.Data` directly; tests use a disk provider; **zero reflection or System.IO in game code** |
| The economy | Cosmetic — flat +12,000 regardless of contract | Contracts pay their stated `Reward`; wet work carries a ~60% premium; negotiation (political capital) is a player-facing button on the board |

### Layer 1 — Strategy sandbox (`code/Generation`, `code/Missions`, `code/Domain`)

World generation seeds setting, factions, a six-operative roster (now via `GenerateTeam` with enforced variety: distinct archetypes, a conscience-keeper, a cynic, a climber, and a flight risk on every roster), a corporate hierarchy (8 executive NPCs, now flagged `IsExecutive` and excluded from every team average, decay pass, and corruption computation), and relationships. The board offers 3–5 template missions per cycle **plus** corporate contracts as a separate channel (a starvation bug where contracts filled the template quota — killing all authored missions after cycle 1 — is fixed). The resolver is pure hybrid stat/dice math (see §3 for the numbers).

**Newly discovered and fixed during the wiring pass** (bugs even the assessment missed, because nothing had ever run):

- `IntRange` was a getter-only struct: System.Text.Json silently deserialized **every archetype stat band as (0,0)** — archetype-generated operatives had near-zero skills and psychology whenever archetypes.json loaded at all.
- The smoke test had not compiled since Sprint 6 (its csproj never included `code/Save`, which `GameManager` references). Every "passing" claim after that point was stale.
- Heat had no decay sink — every 20-cycle run pinned at ~100 heat regardless of playstyle, polluting the corruption index for humane players.
- Two factions could poach the same operative in the same cycle (the second paid 8k for a ghost).
- The narrative sequence panel applied every choice **twice** (double stress, double node advance).

### Layer 2 — Narrative engine (`code/Narrative`)

Flag-driven scene selection with role-based casting, stat triggers, and token resolution. Now with:

- **Choice log** (`WorldState.ChoiceLog`) — every scene and sequence decision recorded (cycle, source, label, flags, operative). This is the substrate for callbacks, epilogues, and "X will remember that." Serialized in saves.
- **Repeatable-scene cooldown** (6 cycles) + queue dedup — a persistent loyalty crisis is a beat, not wallpaper.
- **Tripwire flags are states, not events** — `defection_risk:{id}` clears when loyalty recovers; same for `breaking_point`, `hollowed_out`, `heat:critical`.
- **Gender tokens resolve against the cast operative** (the person the scene is about), falling back to protagonist gender only when a scene has no cast. Pick a female director and your roster no longer becomes uniformly "her."
- `{faction.name}` resolves to the most hostile rival — never your own corporation.
- **Role drift is live**: `ReEvaluateRoles` runs each Briefing; your "weapon" is whoever's psychology currently earns the tag, not whoever rolled it at generation.

### Layer 3 — Human simulation / corporate layer (`code/Corporate`)

Faction AI (weighted actions per agenda), board politics (with **directive compliance** — completing a directive's mission now marks it complied and pays confidence; the board **drip-feeds** directives from a pool instead of dumping the stack on cycle 1), contracts (per-faction offers, poisoned fine print via `hidden_risk`/`hidden_exposure` tags that the resolver now actually consumes, **gray-market wet contracts** unlocked at corruption ≥ 40), reputation with asymmetric decay, and random corporate events.

Psychology produces **behavior**, not just penalties:
- **Refusal:** an operative with conscience ≥ 75 will not take a wet-work assignment. The assignment fails, a `refusal:{id}` flag fires, and the roster log records it.
- **Ambition:** a hungry, disloyal operative (ambition ≥ 75, loyalty < 40) has a 15%/cycle chance to leak documents — suspicion rises, `ambition_leak:{id}` fires. Ambition is no longer a dead dial.

### Validation harnesses (both compile the real game code — no mirrored logic)

- `tests/SmokeTest` — **84 checks**: RNG determinism, loaders, faction identity, end-to-end scene firing, runner ignition, economy, refusal, full save/load round-trip, corruption reachability.
- `tools/PolicySim` — replaces the deleted `balance_test.py` (whose hand-mirrored resolver had drifted ~15 points and validated nothing). Drives 40 seeds × 20 cycles under HUMANE and RUTHLESS policies through the actual `GameManager` and asserts the design targets in §3.

### Known remaining gap

**The project has still never been booted inside the s&box editor.** The sandbox-hostile code is gone and the provider seam uses the documented sandbox APIs, but until someone opens `cold_war_cowboys.sbproj` and watches a scene fire, in-engine behavior is unverified. This is the single highest-priority manual task (§7, step 1).

---

## 3. The corruption arc

### How it's supposed to work

Corruption is the price of competence. Early game, humane management is effective; late game, the pressure mounts until you reach for the cruel tools — and the game desaturates around you as you do. The arc has five milestones: **Competent (20) → Effective (40) → Feared (55) → The Machine (70) → Jenkins (85)**, each unlocking scenes, ending in "It is Tuesday."

### How it actually worked (assessment findings)

It was a mood ring. The index was recomputed from ambient state, so the world darkened *at* you regardless of choices; `BoardConfidence × 0.2` meant being good at your job humanely added up to 20 corruption while getting demoted laundered your soul; "Competent" crossed on cycle 1 before you did anything; "The Machine" and "Jenkins" were reached in **0 of 100** simulated runs — the two best scenes in the game were unreachable. Worst of all, the math refuted the thesis: wet work paid the identical flat +12,000 as clean work with triple heat and extra stress, so a ruthless optimizer would run a *humane* division. The corruption fantasy was only purchasable by deliberately playing worse.

### How it works now (implemented and simulation-verified)

The devil's bargain is real, in both directions:

**Cruelty pays.**
- Wet work pays a **~60% cash premium** at generation.
- Wet-work success pays **bonus board confidence** (+2 on top of the standard +5).
- At corruption ≥ 40 the **gray market opens**: off-ledger wet contracts (~35–50k) clean divisions never see.

**Kindness pays in resilience.**
- Resting operatives recover stress at double rate, regain morale (morale has a recovery path now), and — below corruption 40 — slowly recover conscience.
- Ambient conscience erosion **only exists inside a corrupt culture** (index ≥ 40). The world darkens because you darkened it.
- A humane roster refuses less, defects less, and keeps its psych penalties near zero — which matters, because difficulty scales.

**The index is choice-driven.**
- `CorruptionTracker.ChoiceWeight` accumulates from decisions: cold scene choices +4, humane −2, wet-work sequence choices +5. It contributes up to 25 points of index and is saved.
- The state terms: division-lifetime wet-work practice ×0.3 (counted across everyone who *ever* served — the record doesn't resign when the killer does), inverse conscience ×0.3, operatives lost ×0.1, heat+suspicion ×0.1. **The BoardConfidence term is deleted.**
- A wiped division keeps its corruption. It doesn't reset to innocence because everyone who did the work is dead.

**Verified by PolicySim (40 seeds × 20 cycles each policy):**

| | Humane | Ruthless |
|---|---|---|
| Outcomes S/P/F/C | 38 / 34 / 20 / 8 % | 23 / 33 / 22 / 23 % |
| Late-game success (cyc 14+) | 33% | 12% |
| Deaths per run | 0.00 | 0.47 |
| Final corruption (avg) | 17 | 66 |
| Reaches The Machine | 0/40 | **18/40** |
| Reaches Jenkins | 0/40 | **5/40** |
| Final morale (avg) | 75 | 34 |

Humane play is viable, keeps its people, and never accidentally trips the arc. Deliberately ruthless play reaches the endgame scenes half the time and pays in morale and bodies. The late game is harder than the early game (difficulty +2/cycle capped +26 vs. fieldwork skill growth +1/mission) without becoming the old dead wall — and all four outcomes actually occur.

### What still needs to change (design work, not wiring)

1. **The squeeze needs teeth in the mid-game.** Late-game pressure exists, but nothing yet *forces* the reach for cruel tools — a patient humane player can grind. Candidate levers: board directives with wet-only solutions; cash crunches (payroll) that make the wet premium tempting; rival escalation keyed to your success.
2. **Corruption should change what the game *does*, not only how it looks.** The doc-comment promises (harder directives at 40, more aggressive rivals at 60, darker archetype pool at 60) are still unimplemented beyond the gray market. Each is a small, well-scoped hook now that the seams work.
3. **The desaturation stack needs an in-engine eyeball pass** — saturation/brightness bands were retuned to the new milestone values but have never been seen on screen.

---

## 4. Content plan

### What exists

- **32 scene templates** — the writing is the project's best asset ("The last one had a kid's drawing on his desk." / "You didn't make it to hurt them. You didn't make it not to. You just made it. / This is Tuesday."). Structurally: one text block + one choice each, 26 self-gating one-shots, zero cross-scene callbacks.
- **6 narrative mission sequences** (3–5 branching nodes each) — genuinely strong dilemmas (the assassination target's seven-year-old; the mole whose child needs the medical coverage). Now reachable in play.
- **14 mission templates** (3 wet), 25 archetypes with role tags, ~40 traits, corporate factions/directives/events.

### Authoring principles (from the assessment, now enforceable)

1. **Chains, not tiles.** The engine gap and the content gap were the same gap: nothing remembered. Now the choice log and `FlagsOnPick`/`seq:*` flags exist — so author **callback pairs and triads**: the choice in scene A, the invoice in scene B, the cost in scene C. The 40–60 scene target stands; the shape changes.
2. **Write against the choice log.** Every sequence choice emits `seq:<mission_template>:<approach>` automatically; scenes can require them (`flag:seq:extraction_highvalue:force`). "You did that" is now a one-line trigger.
3. **Depth-2 beats.** At least the milestone scenes need a follow-up beat — a reaction line keyed to the choice, even if it's just one more text block gated on `choice:*`.
4. **Feed the refusal/ambition/poach events.** `refusal:{id}`, `ambition_leak:{id}`, `corp:promoted/demoted`, `corp:threshold:*` are new trigger surface with zero scenes written against them. These are cheap, high-yield vignettes — the system already produces the drama.
5. **Wet-work variety.** 3 wet templates is too thin now that wet work is a real economy. Target ~8, with a spread of moral weights (15 to 90) so the gray market escalates rather than plateaus.

### Priorities (in order)

| Priority | Content | Why |
|---|---|---|
| P0 | Callback pairs for the 6 existing sequences (6–12 scenes) | Proves the Telltale handshake players actually feel |
| P0 | Refusal / ambition-leak / poach vignettes (4–6 scenes) | Systems now generate these events; they're invisible without scenes |
| P1 | Depth-2 beats on the five milestone scenes | The arc's spine deserves more than one block |
| P1 | 5 new wet templates + 2 gray-market sequence missions | The devil's bargain needs a menu |
| P2 | Faction-confrontation scenes (`corp:confrontation:*` now actually fires) | Split-brain fix made these reachable |
| P2 | Epilogue reel from the choice log | Cheap now; the payoff for the whole run |

---

## 5. Technical debt — ordered, with estimates

The assessment's ten-item broken list is **fully cleared** (items 1–10: enum loading, runner ignition, sandbox APIs, faction split-brain, save/load cluster, consequence ordering, late-game math, executive pollution, unpaid rewards/compliance, silent-failure loader). Remaining debt, ordered:

| # | Item | Effort | Notes |
|---|---|---|---|
| 1 | **Boot in s&box** — open .sbproj, verify `SandboxFileProvider` against the real allowlist, screenshot a scene firing | 0.5–1 day, manual | Everything else is code-complete but engine-unverified; Razor panels have never rendered |
| 2 | UI polish for new affordances — refusal feedback (currently a silent failed assign + log line), negotiate button styling, fit-tier tags | 0.5 day | The seams exist; the presentation is minimal |
| 3 | Corruption gameplay hooks — harder directives at 40, rival aggression at 60, darker archetype pool at 60 | 1–2 days | Small, well-scoped now; each is a one-system change |
| 4 | Mid-cycle manual saves restore to Briefing of the same cycle (by design; assignments survive but the player re-locks) | 0.5 day if full phase restore wanted | Documented behavior; auto-saves are always coherent |
| 5 | `ActiveCrises` / `PublicTrust` are vestigial (saved, never driven) | delete or wire, 0.5 day | Decide with the crisis-content question |
| 6 | Relationship data only nudges a ±12 resolution swing — "friend died on your mission" is representable and unexpressed | 1 day + content | The single most load-bearing emotional event in the genre |
| 7 | `RelationshipSeeder`/scene casting could use the choice log for relationship-aware casting | 1–2 days | Post-boot, pre-content-push |
| 8 | Smoke test and PolicySim are custom runners, not a test framework | optional | They work and they're honest; migrate to xUnit only if the team grows |

---

## 6. Creative direction

### The register: HR-noir, not neon

The authentic voice is **corporate-procedural horror**: euphemism as violence, process as menace. "Asset realignment." "Concerned" doing a lot of work in a sentence. The auditor's paper notebook. Write the horror in the language of performance reviews, and let the reader supply the blood.

The cyberpunk furniture (smog, smart-glass, ad-blimps, chrome) is *rented, not owned* — nothing mechanical touches it. Per the assessment, that's acceptable **option (a)**: keep the clothes light and cheap. If we ever want to earn the genre, the move is **one** cyberpunk element made mechanical (augment debt, memory editing) — not more set dressing. Until then: every sentence should work if the genre were swapped out; if a line only works because of the neon, cut the neon and keep the line.

(The title is acknowledged legacy — nothing in the fiction is cold-war and nobody is a cowboy. Revisit at ship.)

### The protagonist: second person, two modes

The player-director is the game's most developed character, and the prose addresses them as *you*. The voice runs in two registers — the **Draper mode** and the **Stout mode** — and choice text should almost always offer both:

- **Draper mode** — the performance. Polished, warm, persuasive; the manager who makes the cut feel like a promotion, who buys the journalist instead of burying him, who says "you've earned a rest" and means "you're a liability this quarter." Draper choices read kind and cost conscience later; they're how corruption arrives wearing a good suit. Mechanically these map to transactional/cold choices with soft labels.
- **Stout mode** — the ledger. Blunt, unvarnished, pragmatic; the manager who says the number out loud, who tells the operative exactly what the job is, who refuses the euphemism even when performing it. Stout choices are honest in both directions — the cruel ones read cruel ("You want a bonus or a transfer?"), the humane ones read plain ("You don't have to like it.").

The corruption arc's tone modifiers (Normal → Guarded → Transactional → Hollow) apply to the *operatives'* dialogue; the Draper/Stout split applies to *yours*. At Jenkins, the two modes converge — the performance and the ledger say the same thing, which is the point.

### Character voice rules

- Operatives earn attachment through **accumulated history**, not backstory dumps: reference their missions, their choice-log entries, their drifted role. Three flavor fragments at generation is seed, not soil.
- **Vale is the only authored fixture** — the mentor-tempter who was Jenkins once. Okonkwo appears for board-level dread. Nobody else gets a canonical arc.
- The Jenkins scene's cadence — short declaratives, no adjectives, present tense — is the endgame style guide. Early-game scenes may breathe; late-game scenes contract.
- Choices must never moralize at the player. The game keeps score; the text keeps a straight face.

---

## 7. Roadmap

**Phase 0 — done (this pass).** All ten assessment blockers fixed, plus five newly found bugs (IntRange zero-stats, board starvation, heat sink, double-poach, double-applied sequence choices). 84 smoke checks + 9 balance targets green. Fix-first-then-author: the fixing is done.

**Phase 1 — Boot it (next, ~1 day, manual).**
Open the .sbproj in the s&box editor. Verify the provider seam against the real sandbox. Watch a scene fire. Screenshot it. Fix whatever the allowlist actually rejects (the seam localizes any breakage to `SandboxFileProvider`). Until this happens, everything else is theoretical — this was the assessment's loudest point and it remains true.

**Phase 2 — Feel pass in-engine (2–3 days).**
Play ten cycles by hand. Check: the runner panel flow, refusal legibility, the negotiate button, HUD desaturation bands at the new thresholds, scene pacing with cooldowns. File and fix the inevitable presentation gaps (debt items 2–3).

**Phase 3 — Author against the wiring (1–2 weeks).**
The §4 priority table, top-down: callback chains for the six sequences, event vignettes (refusal/ambition/poach), milestone depth-2 beats, wet-work menu. Target: 40–60 scenes where every third scene can reference something the player did.

**Phase 4 — The squeeze (design + 1 week).**
Make the mid-game force the choice the theme is about: payroll pressure, wet-only directives, rival escalation keyed to success. Re-run PolicySim; the target is humane-viable-but-tempted, not humane-optimal.

**Phase 5 — Epilogue + ship loop.**
Choice-log epilogue reel, run-end summary, title decision, content freeze, balance lock.

---

## Appendix: verification snapshot (2026-07-06)

```
tests/SmokeTest  — 84 checks, 84 pass, 0 fail
tools/PolicySim  — 40 seeds × 20 cycles × 2 policies, 9/9 design targets PASS
```

Key wiring-pass commits land as one change set on `main` (see git history). The assessment document (`ASSESSMENT.md`) is preserved in history as the baseline this document measures against.
