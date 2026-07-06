# Cold War Cowboys — Fresh-Eyes Assessment

*July 2026. Full read of the codebase, all content JSON, the QA audit, and the authoring manual, plus exact-math enumeration of the resolver and a 500-seed simulation of the psychology loop. No hedging, as requested.*

**The one-paragraph version:** The prose is good, the architecture diagram is good, and the design thesis is genuinely worth building — but the game that exists contradicts the game that's described. The corruption arc is a mood ring, not a mechanic; cruelty is mathematically *worse* than kindness at every point in the run, which inverts the entire premise. The branching mission system — the Telltale half of the hybrid — is fully built and never ignited: `MissionNarrativeRunner.Begin()` has zero call sites. The scene library almost certainly fails to load at all (missing enum converter, silently swallowed). And the project has never demonstrably run inside the engine it targets. Do not spend 15–25 hours authoring content yet. Spend one focused week wiring and fixing first, or the content goes into a machine that can't load it, can't call back to it, and pays the player to ignore its theme.

---

## Reality check before either question

The self-description doesn't match the repo:

- **"21,774 C# LOC"** — actual: ~6,560 C# + ~2,410 Razor/SCSS. (~7,300 lines of JSON and the docs presumably padded the count.) This matters because 6.5k LOC is *good news*: the codebase is small enough to fix quickly.
- **"32 scene templates"** — accurate. But 26 of them are self-gating one-shots; effective replayable content is much thinner.
- **Mason, Kira, Ghost, Davi** — these characters do not exist. Not in code, not in data, not in the 386k-character authoring manual. The only named humans are Director Vale and Board Chair Okonkwo (scene cameos) and faction leaders who are single lines of JSON. Question 2's "procedural vs authored" comparison is between something that exists and something that was never built.
- **"9 sprints built every system the design calls for"** — 9 sprints built every system's *module*. Roughly a third of the public surface is never called (details below).

---

# Question 1: Technical / Systems

## 1.1 The three findings that gate everything else

**1. The scene library almost certainly never loads.** `SceneData.cs` declares `ScenePriority` and `CastSlotKind` as C# enums; `scenes.json` writes them as strings (`"Priority": "Pressing"`, `"Kind": "Role"`). `TemplateLoader` (code/Generation/Templates/TemplateLoader.cs:15-20) registers no `JsonStringEnumConverter`, and System.Text.Json throws on string→enum by default. `Deserialize<T>` swallows every exception and returns null (TemplateLoader.cs:49-51); `GameManager.cs:94` falls back to an empty list with zero logging. Net effect: the NarrativeDirector — the game's flagship system — most likely holds **zero scenes**, silently, in every runtime including the smoke test's own environment. The smoke test never loads scenes, so nothing catches it. Same failure applies to all six `narrative_missions.json` sequences (`"Phase": "Briefing"` → `NarrativePhase` enum). This is a one-line fix that nobody could see because every error path is `catch { return null; }`.

**2. The Telltale half of the game is built and unplugged.** `MissionNarrativeRunner` has a complete data model, a runner, resolver integration (`overrides` consumed at GameManager.cs:195-199), a UI panel, and six authored branching sequences with genuinely strong dilemmas (the assassination target's seven-year-old; the mole whose child needs the medical coverage). **`Begin()` is never called from anywhere.** `IsActive` is permanently false, the panel is unreachable, all six sequences are dead content. The single most Telltale-shaped thing in the project has no ignition wire.

**3. It has never run in-engine, and the current code plausibly can't.** Both `TemplateLoader.TryReadSandbox` and `SaveSystem`'s disk I/O use runtime reflection (`Type.GetType("Sandbox.FileSystem, Sandbox.System")` + `MethodInfo.Invoke`) with raw `System.IO` fallbacks — exactly the API categories s&box's compiler access-list is known to reject or neuter. The QA audit flagged this as the game-breaking blocker at Sprint 6; the "fix" reimplemented it in the same forbidden category, and no in-engine run has ever been demonstrated. Until someone opens the .sbproj and boots it, everything else here is theoretical.

## 1.2 Architecture: good skeleton, marketing comments

The core loop — generate → assign → resolve → consequences → scenes — is real, deterministic in-process, and cleanly phased. The seams the README brags about are each partially fiction:

- **"Single-writer WorldState" is false.** Eight distinct writers mutate it (`ConsequenceProcessor`, `CorporateConsequenceProcessor`, `NarrativeDirector.ApplyChoice`, `MissionNarrativeRunner.ApplyChoice`, `GameManager` decay/recovery, `ScenarioGenerator`, `FactionSystem` (which mixes immediate and queued mutation in the same method), `SaveSystem.Restore`).
- **The EventBus is 80% ceremony.** Three subscriptions exist in the whole codebase, all wired by GameManager to systems GameManager owns for an event GameManager publishes. Six published event types have zero subscribers.
- **Split-brain state is the worst structural problem.** `CorporateState.Factions` and `WorldState.Factions` hold *divergent duplicate objects* for the five rival factions from cycle 1 (CorporateDataLoader.cs:75 replaces shared references; the sync loop skips existing ids). Faction AI mutates the corp copies; the UI and narrative triggers read the world copies. Faction relationship changes are invisible to the player, and the `corp:confrontation:*` scene flags effectively cannot fire from AI behavior. Same duplication pattern: `Roster` vs `Operatives`, `Day` vs `Cycle`, `HeatLevel` vs `Heat`, three reputation dials.
- **A real ordering bug:** mission consequences to corporate state are queued during Resolution but applied at the *end* of the Corporate phase — after the board has already evaluated confidence. Promotion/demotion always runs against last cycle's numbers.

## 1.3 Does the math work?

Mid-band, yes; at the edges, no. Exact enumeration of the resolver:

- Outcome bands are 20 points wide; solo dice span 30. **At most 2–3 of the 4 outcomes are ever reachable from any given state**, and matched-difficulty fights land Partial 65% of the time. The game reads as "everything lands partial."
- **Success secretly costs `skill ≥ difficulty + 15`.** The UI fit score shows raw skill vs difficulty with no such offset — a "great fit" op has a 19% success rate.
- **The late game is mathematically unwinnable.** Difficulty scales +2/cycle (`MissionGenerator.cs:54`, capped +30); no mechanic anywhere increases a skill. By cycle ~13–16, a healthy elite operative has a 0% success chance on hard templates. The design says the late game demands ruthless optimization; the math says the late game demands nothing because nothing works.
- **The "48-point effective gap" is really 35–47** (`gap = 0.25·skill + 25`), hits 48 only for a 92+ skill operative at the absolute psychological floor, and a *realistically* broken op swings ~22 points. The honest version — "a broken operative loses one to two full outcome tiers" — is still a strong, felt mechanic. But only three of the five psychology dials participate: **conscience affects nothing in resolution and ambition affects nothing in the entire codebase.**
- **The stress loop death-spirals believably** (35% operative death by cycle 20 if worked continuously, 31% catastrophe rate) and rotation stabilizes it — that part of the human simulation genuinely works. Morale, however, has no recovery path except mission success, which the late game eliminates; morale → 0 is the long-run attractor. Conscience *never* recovers, by design or omission.
- **`tools/balance_test.py` validates a game that doesn't exist.** Its mirrored resolver uses thresholds 15 points more lenient, no effectiveness multiplier, different stress rates, and a 4× kill chance. Its PASS is meaningless; a faithful mirror shows the death spiral it hides.
- **Executives pollute every average.** The 8 hierarchy NPCs count as active operatives in corruption computation, decay, and every `avg_stat_below` scene trigger, diluting team math ~2.3×.

## 1.4 Code quality across 9 sprints

The pattern is consistent: each sprint shipped a well-formed, well-documented module and wired only its happy path. The dangling-entry-point list: `MissionNarrativeRunner.Begin`, `ContractSystem.Negotiate` (the political-capital economy has no player-facing sink), `PoliticsSystem.IssueDirective` (the board never issues directives mid-run), `ReputationSystem.Adjust`, `NarrativeDirector.EvaluateRoles` *and* `ReEvaluateRoles` (two competing role systems, both dead — "roles drift as stats shift" is fiction; roles are set once at generation), `OperativeGenerator.GenerateTeam` (team-variety enforcement, dead), the entire `MissionWeights` file (extracted in the most recent commit, wired to nothing, while the resolver keeps its private duplicate), mission `Reward` (success pays a flat +12,000 regardless of contract value — the economy is cosmetic), hidden-risk contract tags (parsed by nothing), directive compliance (completing a directive's mission never marks it complied — **you eat the non-compliance penalty even after succeeding**).

**Save/load is the biggest defect cluster**: active directives aren't saved, so every load fires the full stack of deadline penalties at once; corporate roster/faction views point at pre-restore phantom objects; missions lose their stat weights and narrative flags on round-trip; one-shot scene state and corruption milestones re-fire after every load; phase is parsed and discarded; RNG stream positions aren't serialized so the determinism claim dies at the first load.

**The QA audit is honest** — unusually so for a self-audit; it self-reported "NOT EXECUTED," found real bugs, and correctly called the game unplayable in-engine. But it audited structure, not data flow: it repeated the "single writer" and "no cycles" doc-comment claims without verifying either, and never noticed the unplugged narrative runner, the unpaid rewards, or the executive-polluted averages.

## 1.5 Are the 32 scene templates good?

As prose: yes, genuinely. As systems content: no.

The writing is the best asset in the repo. "The last one had a kid's drawing on his desk." "You used to ask." The Jenkins scene ("You didn't make it to hurt them. You didn't make it not to. You just made it. / This is Tuesday.") is the strongest single beat in the project and earns the milestone name. The vignette *shapes* are right: quiet moments, loyalty audits, the noodle bar off-grid.

Structurally, they're shallow and partially wired to nothing:

- Every scene is one text block + one choice. No follow-up beat, no reaction line, no depth-2 anywhere.
- **Zero cross-scene callbacks.** The engine supports "fire scene B only if the player picked X in scene A" via flags — not one of the 32 scenes uses it. All 26 flag-gates are self-gates (`scene:*_seen`). The Telltale promise ("X will remember that") is latent in the substrate and absent from the content.
- The "Competent" milestone (threshold 20) crosses on **cycle 1 before the player does anything** (typical starting index ≈ 22), wasting the first corruption beat. "The Machine" (80) and "Jenkins" (95) were reached in **0 of 100** simulated 20-cycle runs — the two best scenes in the game are unreachable, along with `final_confrontation`, which is gated on `corruption:the_machine`.
- Non-one-shot scenes gated on persistent flags (`defection_risk_pull`) re-fire **every cycle forever** because narrative flags are add-only and nothing clears them after resolution — the loyalty crisis becomes wallpaper.
- `{faction.name}` always resolves to `world.Factions.FirstOrDefault()` — the loyalty_test scene has a rival faction poaching your operative and will name *your own corporation* as the poacher.

## 1.6 What's broken — priority order

1. Enum deserialization → zero scenes/sequences load (one line + a validator).
2. `MissionNarrativeRunner` never started (one call site).
3. Never run in-engine; reflection/System.IO likely rejected by the s&box sandbox.
4. Corp/world faction split-brain.
5. Save/load defect cluster (directives, one-shots, milestones, weights, RNG).
6. Consequence-ordering bug in the Corporate phase.
7. Late-game difficulty scaling vs. static skills.
8. Executives in team averages.
9. Rewards unpaid / economy cosmetic; directive compliance unrewarded.
10. Silent-failure loader/validator layer (the reason 1–2 went unnoticed).

---

# Question 2: Creative / Design

## 2.1 Does the corruption arc work as game design, or is it just numbers?

It's less than numbers. **It's a gauge that watches you play a game whose incentives point the other way.**

Three separate failures, in ascending order of importance:

1. **It's presentation-only.** The index's complete consumer list: HUD color/desaturation/bleach, a CSS tone class (the "dialogue turns transactional" promise is literally an unstyled-difference stylesheet hook — text is identical at corruption 0 and 100), choice-button re-sorting at ≥80, and five milestone scene flags. The doc comment promises "harder directives at 40, rivals more aggressive at 60, darker archetype pool at 60" — none of it is implemented anywhere. Corruption changes what the game *looks* like, never what it *does*.

2. **It's a measurement, not a consequence.** The index is recomputed each cycle from current state — team wet-work counts, inverse conscience, board confidence, losses, heat. Two problems: your choices reach it only through stat drift (diffuse, illegible), and the `BoardConfidence × 0.2` term means *being good at your job humanely* adds up to 20 corruption while getting demoted launders your soul. Meanwhile ambient decay (−1 conscience 4 cycles out of 5, −1 morale/cycle, no recovery mechanic for either) means the world darkens *at* you regardless of anything you choose. A corruption arc the player doesn't author isn't an arc; it's weather.

3. **The math actively refutes the thesis.** "Early game humane play is effective, late game optimization requires treating people as expendable" — the codebase implements the opposite. Wet work pays the identical flat +12,000 as clean work, with triple heat, extra stress, and −7 conscience, on higher-difficulty templates. Resting broken operatives is strictly optimal at all times. There is no efficiency, payout, unlock, or pressure anywhere that cruelty buys. A player who ruthlessly optimizes will run a *humane* division, because that's what the numbers reward. The corruption fantasy is only purchasable by deliberately playing worse.

**Is the idea salvageable? Completely — and it's worth salvaging.** The presentation stack (desaturation, cold-choices-sort-first, the milestone vignettes) is a genuinely good expressive layer waiting for a mechanic underneath. What it needs is exactly one thing: **make the devil's bargain real.** Concretely: wet work and pushing broken operatives must pay — more money, faster board confidence, access to contracts clean divisions can't get — while the humane path pays in resilience (loyalty, stress recovery, defection resistance) that matters *because* difficulty scales. Then late-game scaling (currently a bug that makes success impossible) becomes the *design*: the wall that makes players reach for the cruel tools, which is when the desaturation starts meaning something. The pieces are all sitting in the repo unwired. That's the encouraging part: this is a wiring problem, not a vision problem.

## 2.2 Is CK/Telltale the right structure, or is there tonal whiplash?

There is no tonal whiplash — the register is impressively consistent from mission briefs to corporate events to scenes; everything speaks the same corporate-euphemism noir. The whiplash is **structural**: the two halves don't know about each other.

- Mission-sequence choices (the Telltale layer) emit **zero narrative flags** — a player who chose "proceed with the child present" leaves no trace the scene director can ever reference. The scene layer cannot say "you did that."
- Scene choices can't touch missions, factions, budget, or relationships — six psych/heat deltas and a global flag is the entire consequence vocabulary. The Telltale layer can't reach the CK layer.
- Scenes fire only at Aftermath, in a FIFO queue that accumulates duplicates. Nothing can interrupt a briefing, land mid-crisis, or react in the moment.

So the current game is neither CK nor Telltale: it's a **roguelite ops sim with a vignette reel appended to each turn**. That said, the hybrid *is* the right structure for this premise — the CK layer generates the situations no author would write, and the vignette layer prices them emotionally. What's missing is the handshake: sequence choices must write flags, scene choices need a bigger consequence vocabulary (kill/bench/transfer an operative, move a relationship, spawn or poison a mission), and at least a few scenes need to fire at decision time rather than after it. One more structural gap that matters for "Telltale-quality": **nothing records what you chose.** No choice log exists in WorldState or SaveData. The single cheapest high-value addition to the entire design is a choice-history list — it enables callbacks, an epilogue reel, and the "X will remember" affordance in one data structure.

## 2.3 Procedural archetypes vs authored characters

The question presumes a rivalry that doesn't exist yet: **there are no authored characters.** Mason, Kira, Ghost, and Davi are design-conversation ghosts. What exists is 25 archetypes with stat bands, trait pools, three flavor fragments each, and — the genuinely clever bit — a `NarrativeRole` tag (anchor / mirror / weapon / conscience / wildcard / innocent / survivor / climber) that scenes cast against. That's the correct middle path: it's how CK makes you care about procedurally generated people, and the role-cast scenes ("{operative.role:conscience} is waiting in the archive") already read as if written for a specific person.

But procedural characters earn attachment through *accumulated history*, and the engine currently cannot accumulate any: no per-operative memory of choices, no relationship-based casting, no role drift (the re-evaluation system is dead code — your "weapon" is whoever rolled that archetype at generation, forever), no way for a scene to reference an operative's past missions. Right now the most developed character in the game is *you* — the second-person director voice — and honestly that's the game's strongest emotional instrument; the corruption scenes about your own hollowing land harder than any operative scene.

**Which delivers more emotional impact?** For this premise, procedural — *if* the memory substrate gets built. The corruption arc's engine is your willingness to spend people; that only hurts when the people are yours, shaped by your run, not a canonical cast whose arcs an author already decided. The Telltale games' authored casts work because those are games about a fixed story; this is a game about *your* management pathology. Spend the authoring budget on: (a) role-cast vignettes with callbacks (they scale across every generated roster), and (b) one, maybe two authored fixtures — Vale already works as the mentor-tempter and costs nothing more. Do not build four authored operatives; they'd cannibalize the roster's reason to exist.

## 2.4 Does the cyberpunk register feel authentic or templated?

Split verdict: **the sentences are authentic; the furniture is templated.** The writing is at its best when it's corporate-procedural horror — "asset realignment," euphemisms that have their own euphemisms, an auditor whose paper notebook is the scariest object in the building, "concerned" doing a lot of work in a sentence. That register — HR-noir, the banality of corporate evil — is distinctive, consistent, and the project's actual voice.

The cyberpunk *set dressing* around it is stock-photo: neon bleeding through smog, rain-streaked smart-glass, ad-blimps, chrome jaw implants, a corp literally named Panopticon Holdings. None of it is bad; none of it is yours. Nothing in the world-building would be different if the genre furniture were swapped out, because the augments, the net, the street — none of it touches a single mechanic (the setting tokens are `{corp}`, `{location}`, `{year}`).

The honest move: accept that this is a game about middle management of the damned that happens to wear cyberpunk clothes, and either (a) let the clothes stay light and cheap, or (b) earn the genre by making one cyberpunk element mechanical — augment debt, memory editing, something the sim actually tracks. Option (a) is fine. "Cold War Cowboys," incidentally, is a title from a different game — nothing in the fiction is cold-war and nobody is a cowboy.

## 2.5 Is the five-dial psychology the right abstraction?

Three of the dials — stress, morale, loyalty — earn their place: they feed the resolver, they death-spiral believably, they generate the tripwire flags (`breaking_point`, `hollowed_out`, `defection_risk`) that drive the best scenes. That loop is the working heart of the human simulation and it's good.

The other two are decorative. **Conscience** modifies no behavior — it's an input to the corruption gauge and nothing else. **Ambition** appears in zero mechanics anywhere. And this points at the real abstraction problem: the system models *attrition*, not *people*. What's missing isn't more dials, it's dials that produce **behavior**: an operative with `comp_codes` ("won't cross certain lines") refusing a wet-work assignment; a high-ambition operative leaking to Finance Division to climb; a low-conscience operative *volunteering* for the jobs that break the others. One refusal event is worth a hundred points of silent stat drift, because the player *sees* it. Relationship data exists and seeds nicely but only nudges a ±5 resolution swing — the "friend died on your mission" case, the single most emotionally load-bearing event this genre offers, is representable in the data model and expressed nowhere.

Verdict: right dials (minus ambition), wrong outputs. Wire conscience to refusal/whistleblowing (the `final_confrontation` scene is literally this — make it systemic), give ambition one mechanic or delete it, and let psychology interrupt the player instead of just discounting their dice.

## 2.6 The minute-to-minute loop, and where boredom sets in

The actual loop as built: click Briefing (auto-decay, board refresh) → drag operatives onto 3–5 missions → click Resolution → read four outcome sentences → click Corporate (invisible; a log line or two) → click Aftermath → read 0–2 vignettes, pick a choice → click next cycle. Two to four minutes a cycle, of which the only *decision* is assignment.

Boredom sets in around **cycle 4–6**, for compounding reasons:

1. **Assignment is a solved puzzle.** Best-fit operative onto highest-fit mission, rest anyone above ~55 stress, is dominant from cycle 1 and never stops being dominant. No mission ever demands a trade-off the roster screen doesn't already display.
2. **65% of everything is Partial.** The outcome distribution means most resolutions read as the same shrug. Catastrophes — the story generators — are nearly unreachable for a rotated roster.
3. **The mid-mission layer is dark** (the unplugged runner), so missions are slot-machine pulls, not stories. This is the intended antidote to #1 and #2, already written, already built.
4. **The scene stream repeats and thins.** One-shots burn out in the first ~8 cycles, repeatable scenes re-fire identically forever, and nothing references your previous choices, so vignettes stop feeling like consequences and start feeling like a screensaver.
5. **The economy is fake** — money in, flat +12k out, nothing to buy. There is no recruitment, no training, no gear, no *build*. Between-mission play is pure triage.

The fix priority mirrors the causes: ignite the runner (it attacks 1, 2, and 3 simultaneously — approach choices change weights and difficulty, which unsolves assignment and moves outcomes off Partial), then callbacks, then a minimal economy (recruit/train/treat — all three are already fictional objects in the scenes: the recruitment_pitch scene *is* a recruitment mechanic with no mechanic behind it).

## 2.7 The gender token system

Mechanical swap, and a broken one. The full system is `{gender:m|A|B}` resolving against **the protagonist's** gender chosen at NewGame. But the content overwhelmingly applies it to *other people* — "Promote {gender:m|him|her}", "{gender:m|he|she} left me in the corridor" — so every operative and NPC in the game presents as a function of the *player's* gender: pick female and your whole roster becomes "her," structurally wrong pronouns for roughly half of all rendered text. Meanwhile operatives *have* a generated `Gender` field that the token resolver never reads. This isn't texture; it's a find-and-replace with a bug in it.

Meaningful texture is cheap from here: resolve gender tokens against the *cast operative* (the data already exists), and you're done — the system disappears into correctness, which is all a pronoun system should ever do. Anything more ambitious (gendered content differences) isn't worth authoring effort in a game whose theme has nothing to say about gender.

## 2.8 Who is the target player, and what are they playing right now?

The target player is the person who kept playing **Darkest Dungeon** after realizing the real game was deciding whom to feed to the Cove — plus the **Frostpunk** player who signed the child-labor law to see the "but was it worth it?" card, and the **XCOM** player who names soldiers and feels it. Adjacent fiction-side: Citizen Sleeper, Disco Elysium readers who'll tolerate systems for voice. It's a real, proven, mid-size niche: management games where the resource is people and the meter is your soul.

What CWC offers that those don't: **second-person managerial complicity**. Darkest Dungeon frames attrition as gothic inevitability; Frostpunk as civic emergency. CWC's pitch — *you*, personally, in performance reviews and noodle bars, redefining "acceptable losses" one reasonable decision at a time until it is Tuesday — is a genuinely unclaimed register. The Jenkins scene is the proof of concept. No shipped game does the corporate-HR-horror version of this.

Which is exactly why the math betrayal matters most for *this* player: they are the audience most likely to notice that the game never actually charges them for their soul — that kindness is free and optimal. This player came to be implicated. The current build never implicates them.

## 2.9 Is the premise worth 15–25 hours of content authoring, or does the design need to evolve first?

**The premise is worth it. The build, today, is not.** Neither "author now" nor "redesign" is the right call — the design doesn't need to evolve so much as the game needs to *exist* first. Content written this week goes into an engine that (a) very likely loads none of it, (b) cannot reference any previous choice, (c) resolves half its pronouns wrong, (d) never fires its two best scenes, and (e) rewards the opposite of its theme. Authoring into that is writing novels for a printing press with no ink.

The gate is roughly **one focused week of wiring, in this order**, before scene #33 gets written:

1. **Boot it in s&box.** One in-engine screenshot of a scene firing. Everything else is speculative until this exists. (Includes replacing the reflection/System.IO loader with the real `FileSystem.Mounted` API.)
2. **Fix the loader**: `JsonStringEnumConverter`, loud load errors, a startup validator (unique IDs, known trigger types, known resolvers, flag cross-reference), and a smoke-test assertion that 32 scenes loaded and one fired end-to-end.
3. **Call `MissionNarrativeRunner.Begin()`** when a mission with a sequence is assigned. Make sequence choices emit flags.
4. **Add the choice log** (id, scene, choice, cycle) to WorldState and SaveData.
5. **Invert the corruption economics**: pay for cruelty (money/confidence/exclusive contracts), price kindness as resilience, delete the BoardConfidence term, move "Competent" off cycle 1, and pull "The Machine"/"Jenkins" down into reach of an actual ruthless run.
6. **Gender tokens resolve against the cast operative.**
7. Fix the faction split-brain and the save/load cluster (or, cheaper for now: disable save mid-run and ship cycle-boundary saves only).
8. Wire conscience to refusal events; delete or wire ambition.

Then author — and author differently than planned: fewer standalone vignettes, more **callback pairs and triads** (choice in scene A, invoice arrives in scene B, cost lands in scene C), because the engine gap and the content gap are the same gap: nothing remembers. The 40–60 scene target is right; the shape of those scenes should be chains, not tiles.

The uncomfortable summary: nine autonomous sprints produced a well-written, well-documented, internally plausible artifact that has never been watched running by anyone — and it shows. Every individual module believes the module next to it is doing its job. The premise — corruption as the price of competence, rendered in performance-review prose — is the best thing here and deserves the week of honest wiring it takes to make the game stop contradicting it.
