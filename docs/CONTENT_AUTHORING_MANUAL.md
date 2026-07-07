# Cold War Cowboys — Content Authoring Manual

**v2.0 — July 2026 — scene-writing reference for the NarrativeDirector template system**

A cyberpunk Crusader Kings / Telltale hybrid built in **s&box**.

> **This manual is a code artifact.** Every schema table is written from
> `code/Narrative/SceneData.cs` and `code/Narrative/NarrativeDirector.cs`, every
> threshold from `code/Core/CorruptionTracker.cs`, and every complete JSON
> example lives in [`docs/examples/`](examples/) as a fixture file that the
> smoke test loads, validates, and renders on every run. If the manual and the
> game ever disagree, the smoke test fails — that is the contract. (v1.0 of
> this manual documented a scene format that never existed in this repo; that
> is a mistake this project does not get to make twice.)

---

## Table of Contents

1. [Overview: How CWC's Narrative System Works](#1-overview)
2. [Scene File Format (JSON Reference)](#2-scene-file-format)
3. [The Trigger Vocabulary (Complete Reference)](#3-the-trigger-vocabulary)
4. [Use Case 1: Writing a Corruption Milestone Scene](#4-use-case-1-corruption-milestone)
5. [Use Case 2: Writing an Operative Psychology Scene](#5-use-case-2-psychology)
6. [Use Case 3: Writing a Mission Narrative Sequence](#6-use-case-3-mission-sequences)
7. [Use Case 4: Writing Relationship and Corporate Scenes](#7-use-case-4-relationship-and-corporate)
8. [The Corruption Arc: Design Philosophy](#8-the-corruption-arc)
9. [Cyberpunk Register Guide](#9-cyberpunk-register-guide)
10. [Gender Expression Guide](#10-gender-expression-guide)
11. [Quick Reference: Templates, Checklist, Workflow](#11-quick-reference)

---

<a name="1-overview"></a>
## 1. Overview: How CWC's Narrative System Works

Cold War Cowboys operates on three interlocking layers, each feeding the others.
Understanding this architecture is essential before writing a single line of
dialogue.

### 1.1 The Three Layers

| Layer | Function |
|---|---|
| **Strategy Sandbox** | The player manages operatives, assigns missions, allocates resources, and navigates corporate politics. This layer generates the raw state: stats, relationships, corruption indices, mission outcomes. |
| **Narrative Experience** | Scenes trigger based on sandbox state. These are the moments the player reads and makes choices within. Dialogue, confrontation, introspection. This is where meaning is created. |
| **Human Simulation** | Operatives are not units. They accumulate stress, form bonds, lose loyalty, break. The narrative layer makes this visible; the simulation layer makes it real. |

Your job as content author is the narrative layer. You write the scenes that
make the sandbox state legible and emotionally resonant. The engine handles
everything else.

### 1.2 How Scenes Trigger — the actual lifecycle

1. **Load.** All scene files under `Data/Templates/scenes/` load once at New
   Game. There is **no hot-reload**: to see a change in-engine you restart and
   start a new game. For the fast loop, use `tools/ScenePreview` (§11.4) —
   it renders any scene in seconds without booting s&box.
2. **Evaluate.** Once per cycle, at the **Aftermath** phase, the
   NarrativeDirector checks every template against the world: all
   `RequiredFlags` must match, all `Triggers` must pass, and no
   `ForbiddenFlags` may match.
3. **Queue.** Eligible scenes are materialized (cast resolved, tokens
   substituted) and queued in priority order: `Critical` > `Pressing` >
   `Routine` > `Background`. The queue never holds two copies of the same
   template.
4. **Fire.** The UI pops the next scene. At that moment the template's
   `FlagsOnFire` are written to the world, and a `OneShot` template is retired
   for the rest of the run. Repeatable templates (`"OneShot": false`) go on a
   **6-cycle cooldown** (a global constant — not configurable per scene).
5. **Choose.** The player picks a choice. Its five stat deltas apply to the
   scene's **primary operative** (the one who triggered it, or the first cast
   slot), `HeatDelta` applies to the corporation, `FlagsOnPick` are written to
   the world, the decision is recorded in the choice log, and the corruption
   tracker registers whether the choice was cold or humane (§8.4).

### 1.3 Role-Based Casting

Scenes do not reference specific operatives by name. They reference **cast
slots** that the NarrativeDirector fills at render time, so one template plays
out with entirely different casts across playthroughs.

Every operative carries at most one **narrative role**, seeded by their
archetype at generation and re-evaluated every cycle against their psychology
(this is the role-drift system — `NarrativeDirector.ReEvaluateRoles`):

| Role | Who holds it | Gained when | Lost when |
|---|---|---|---|
| `conscience` | The one who still feels it | Conscience > 65 | Conscience < 30 |
| `weapon` | The one who does the dirty work | Conscience < 30 and Ambition < 40 | Conscience > 70 |
| `climber` | The corporate ladder aspirant | Ambition > 70 | Ambition < 35 |
| `anchor` | The emotional tether | Loyalty > 70 | Loyalty < 35 |
| `innocent` | Not yet corrupted | *seeded by archetype only* | Conscience < 40 or Stress > 70 |
| `mirror` | The observer who names what's happening | *seeded by archetype only* | — |
| `survivor` | Been through it and endured | *seeded by archetype only* | — |
| `wildcard` | Unpredictable element | *seeded by archetype only* | — |

> **Design note.** Roles are tendencies, not fixed assignments. An operative
> who is the `conscience` in the early game might drift into `weapon` by the
> endgame. This is the game working as intended. An operative whose stats fit
> no band is narratively adrift — they hold no role, and `role:*` cast slots
> will not find them.

---

<a name="2-scene-file-format"></a>
## 2. Scene File Format (JSON Reference)

### 2.1 Files and directories

- One scene per file, anywhere under `Data/Templates/scenes/`. Category
  subdirectories (`corruption/`, `psychology/`, `corporate/`,
  `relationships/`, `missions/`, `atmosphere/`) are organization only — the
  loader scans recursively and doesn't care which folder a scene is in.
- A file may hold a single scene object `{...}` or an array `[...]` of scenes.
- The parser is writer-friendly: property names are **case-insensitive**,
  `// comments` are allowed, trailing commas are allowed, and enums are
  written as strings (`"Priority": "Pressing"`).
- **Typo'd field names are caught.** The loader re-parses every file in strict
  mode; an unknown property (`"FlagOnFire"` for `"FlagsOnFire"`) becomes a
  boot-time content warning and a ScenePreview/`--validate` failure. Nothing
  is silently ignored.

### 2.2 Annotated example

The complete worked example lives at
[`docs/examples/corruption_fourth_wet_job.json`](examples/corruption_fourth_wet_job.json)
— a loadable fixture, walked through step-by-step in §4. The shape at a glance:

```jsonc
{
  "Id": "corruption_fourth_wet_job",       // globally unique, snake_case
  "Title": "The Fourth Name",              // tokens allowed everywhere text is
  "Speaker": "{operative.role:mirror}",
  "Setting": "Parking structure, level 6, {location}",
  "RequiredFlags": [                        // AND — all must match
    "scene:third_kill_seen",                //   set by another scene (a chain)
    "any_op:third_kill"                     //   matches third_kill:{id}, binds that op
  ],
  "ForbiddenFlags": ["choice:pulled_from_rotation"],  // OR — any match blocks
  "Triggers": [                             // AND with RequiredFlags
    { "Type": "cycle_reached", "Threshold": 4 }
  ],
  "Priority": "Pressing",                   // Background | Routine | Pressing | Critical
  "OneShot": true,                          // default true; false = 6-cycle cooldown
  "Cast": [
    { "Name": "watcher", "Kind": "Role", "Resolver": "role:mirror" }
  ],
  "TextLines": [ "…" ],                     // the scene's prose, one string per beat
  "Choices": [
    {
      "Label": "…",
      "FlagsOnPick": ["choice:cold_streak"],
      "LoyaltyDelta": 0, "StressDelta": 0, "ConscienceDelta": -3,
      "HeatDelta": 0, "MoraleDelta": 0
    }
  ],
  "FlagsOnFire": ["scene:fourth_wet_job_seen"]  // written when the scene pops
}
```

### 2.3 Field reference (`SceneTemplate`)

| Field | Type | Description |
|---|---|---|
| `Id` | string | Unique scene identifier, snake_case, globally unique across all files. |
| `Title` | string | Scene header. Tokens resolve here too. |
| `Speaker` | string | Displayed speaker. Often `{operative.name}` or `"Director"`. |
| `Setting` | string | One line of place/atmosphere. |
| `RequiredFlags` | string[] | AND-conditions. Forms: `"flag:x"` or bare `"x"` (exact match), `"flag_prefix:x"` (any flag starts with x), `"any_op:x"` (matches `x:{operativeId}` and binds that operative as the trigger). |
| `ForbiddenFlags` | string[] | OR-block. If any matches (same three forms), the scene cannot fire. |
| `Triggers` | object[] | Structured predicates, ANDed with RequiredFlags. See §3. |
| `Cast` | object[] | Named slots resolved at render time. See §2.4. |
| `Priority` | string | `"Background"`, `"Routine"`, `"Pressing"`, `"Critical"`. Queue pops highest first. |
| `OneShot` | bool | Default `true`: fires at most once per run. `false`: repeatable with a fixed 6-cycle cooldown. |
| `TextLines` | string[] | The prose. Each string is one rendered line. |
| `Choices` | object[] | 2–4 recommended. See §2.5. |
| `FlagsOnFire` | string[] | Written to the world when the scene is popped for presentation (before the player chooses). |

### 2.4 Cast slots and resolvers

```jsonc
{ "Name": "witness", "Kind": "Role", "Resolver": "highest_conscience" }
```

| Field | Values |
|---|---|
| `Name` | The slot name. Text can reference it: `{operative.role:witness}`. |
| `Kind` | `"Role"` (resolved from world state — the normal case) or `"Identity"` (Resolver is a literal operative id — rare). |
| `Resolver` | One of the resolvers below. |

**Resolvers** (evaluated against the active, non-executive roster):

| Resolver | Resolution |
|---|---|
| `triggering_operative` | The operative bound by an `any_op:` flag or a stat/relationship trigger. **Requires** such a trigger — the validator flags a triggering slot with no operative source. |
| `highest_conscience` / `lowest_conscience` | Extremes of conscience. |
| `highest_social` | Highest Persuasion + Deception. |
| `lowest_loyalty` | The flight risk. |
| `highest_stress` | The one closest to breaking. |
| `highest_morale` / `lowest_morale` | Extremes of morale. |
| `first_mission_op` | Triggering op, falling back to first active. |
| `last_catastrophe_op` | Triggering op, falling back to highest stress. |
| `role:mirror` (etc.) | First operative currently holding that narrative role (§1.3). **Can fail** if nobody holds the role — the slot goes unfilled and tokens referencing it render as "someone". Prefer stat resolvers when the scene must always cast. |

The engine always adds a `triggering` slot automatically when a triggering
operative exists, even if you don't declare one.

### 2.5 Choices — the consequence vocabulary

```jsonc
{
  "Label": "\"You don't have to like it.\"",
  "FlagsOnPick": ["choice:human"],
  "LoyaltyDelta": 4, "StressDelta": -2, "ConscienceDelta": 2,
  "HeatDelta": 0, "MoraleDelta": 0
}
```

A choice carries exactly five named deltas plus flags. The four psychology
deltas (`Loyalty`, `Stress`, `Conscience`, `Morale`) apply to the scene's
**primary operative** — the triggering operative, or the first cast slot if
none. `HeatDelta` applies to the corporation. All values clamp to 0–100.

Honest constraints to design within (the roadmap plans a wider vocabulary, but
this is what ships today):

- A choice cannot kill, bench, or transfer an operative, move a relationship,
  spawn a mission, or touch a faction. Scenes *comment* on the sandbox;
  they act on it through one person's psychology, corporate heat, and flags.
- Deltas cannot target a cast member other than the primary operative.
- **Flags are your long game.** `FlagsOnPick` is how a choice echoes forward —
  a later scene requiring `choice:pulled_from_rotation` is how the game
  remembers. Chains cost nothing at runtime; use them liberally.
- **Choice flags feed corruption by keyword** (§8.4): a flag containing
  `cold`, `transactional`, or `machine` registers a cold choice (+4 corruption
  weight); `human` or `mercy` registers a humane one (−2). A negative
  `ConscienceDelta` also reads as cold; positive as humane. Name your flags
  with this in mind — it is a feature, not a trap.

### 2.6 Token reference

Tokens resolve at render time, in any text field (`Title`, `Speaker`,
`Setting`, `TextLines`, choice `Label`s):

| Token | Resolves to |
|---|---|
| `{operative.name}` | Primary operative's codename (falls back to name, then "the operative"). |
| `{operative.role:X}` | Name of the operative in cast slot `X`, or failing that, any operative holding narrative role `X`. Unresolvable → **"someone"** (the validator warns at load time). |
| `{op}` | Legacy alias for the primary operative. |
| `{corp}` | The host corporation's name. |
| `{location}` | The world's city/sprawl name. |
| `{year}` | The world's year. |
| `{faction.name}` | The most hostile non-host faction. A scene about a rival never names your own corp. |
| `{gender:m\|A\|B}` | Gender-conditional text. See below. |

**The gender token** `{gender:m|his|her}` renders `his` if the relevant person
is male, else `her`. Conditions: `m`, `f`, or `n` (non-binary). The token
reads the **cast operative's** gender — the content overwhelmingly uses it
about other people ("Promote {gender:m|him|her}") — and only falls back to the
protagonist's gender when the scene has no cast (e.g. the Director addressing
you). Full guidance in §10.

Anything else in braces is an error: it reaches the player as raw text, and
the validator says so at load time.

---

<a name="3-the-trigger-vocabulary"></a>
## 3. The Trigger Vocabulary (Complete Reference)

Structured triggers live in the `Triggers` array. Each is
`{ "Type": "...", "Key": "...", "Threshold": N }` — `Key` and `Threshold` are
used or ignored depending on the type. All triggers must pass (AND), and all
evaluate against the **active roster** (executives never count; if the roster
is empty, nothing fires).

| Type | Passes when | Uses |
|---|---|---|
| `flag` | Flag `Key` is present (exact). | Key |
| `flag_prefix` | Any world flag starts with `Key`. | Key |
| `any_op` | Any flag `Key:{id}` exists; binds operative `{id}` as the trigger. | Key |
| `avg_stat_below` | Team average of stat `Key` < Threshold. | Key, Threshold |
| `any_stat_below` | Any operative's stat `Key` < Threshold; binds that operative. | Key, Threshold |
| `stress_below` | Team average stress < Threshold (a calm team). | Threshold |
| `any_relationship_below` | Any relationship score < Threshold; binds the From operative. | Threshold |
| `any_relationship_above` | Any relationship score > Threshold; binds the From operative. | Threshold |
| `no_active_missions` | The board has no active missions. | — |
| `consecutive_successes` | Success streak ≥ Threshold. | Threshold |
| `board_confidence_below` | Board confidence < Threshold. | Threshold |
| `any_faction_relationship_below` | Any faction's relationship to you < Threshold. | Threshold |
| `cycle_reached` | Current cycle ≥ Threshold. | Threshold |
| `active_operatives_below` | Fewer than Threshold active operatives. | Threshold |
| `last_mission_catastrophe` | Most recent resolution had a catastrophe. | — |

Stat names for `avg_stat_below`/`any_stat_below`: `conscience`, `loyalty`,
`stress`, `morale`, `ambition`. Comparisons are strict (`<`, `>`).

### 3.1 Flags the engine emits (what you can trigger on)

**Per-operative tripwires** — form `name:{operativeId}`, so gate on them with
`"any_op:name"`. States, not events: they **clear when the operative
recovers** (at the next Briefing), so a crisis scene can't become wallpaper.

| Flag | Set when | Clears when |
|---|---|---|
| `third_kill:{id}` | WetWorkCount reaches exactly 3 | never (a record, not a state) |
| `cold_blooded:{id}` | WetWorkCount ≥ 7 | never |
| `hollowed_out:{id}` | Conscience ≤ 15 | Conscience > 25 |
| `breaking_point:{id}` | Stress ≥ 90 | Stress < 75 |
| `defection_risk:{id}` | Loyalty ≤ 20 | Loyalty > 30 |
| `refusal:{id}` | Operative refused a wet-work assignment | next Briefing |
| `ambition_leak:{id}` | Ambitious, disloyal operative leaked internally | next Briefing |

**World and corporate state:**

| Flag | Set when |
|---|---|
| `heat:critical` / `suspicion:critical` | Heat / Suspicion ≥ 80 (clear < 70) |
| `heat:body_found` | A wet-work catastrophe surfaced in the press |
| `last_mission:catastrophe` | Cleared and re-evaluated every Resolution |
| `corp:promotion_imminent` / `corp:demotion_imminent` | Board confidence ≥ 85 / ≤ 15 |
| `corp:audit_triggered` | Suspicion ≥ 75 |
| `corp:confrontation:{factionId}` | A faction's relationship ≤ −75 |
| `corp:promoted` / `corp:demoted` | Rank actually changed |
| `corp:threshold:{kind}` | A reputation threshold crossed (e.g. `corp:threshold:suspicion`) |
| `corruption:competent` … `corruption:jenkins` | Corruption milestones (§8), once per run |
| `seq:{missionTemplateId}:{approach}` | Every mission-sequence choice (e.g. `seq:extraction_highvalue:stealth`) |
| `seq:wetwork_chosen` | Any sequence choice marked WetWork |
| `scenario:{id}`, `first_cycle` | Campaign start |

**Author-set flags:** whatever you write in `FlagsOnFire` and `FlagsOnPick`.
Convention: `scene:*` for "this scene happened", `choice:*` for "the player
chose this". The validator cross-checks: a scene *requiring* a `scene:*` or
`choice:*` flag that no scene ever sets is reported as unfireable. Mission
templates also emit their authored `NarrativeFlagsOnSuccess` /
`...OnFailure` / `...OnCatastrophe` lists — check
`Data/Templates/missions.json` for the vocabulary in play.

---

<a name="4-use-case-1-corruption-milestone"></a>
## 4. Use Case 1: Writing a Corruption Milestone Scene

### 4.1 Pick your threshold

Every corruption milestone scene is anchored to a specific moment in the
division's moral descent. The five thresholds (these are the shipped values —
`CorruptionTracker.cs`):

| Corruption Index | Label | What it means |
|---|---|---|
| 20 | **Competent** | The division has proven effective. First kills behind it. Still processing. |
| 40 | **Effective** | Violence is becoming routine. Conscience is a voice, not a wall. Gray contracts unlock. |
| 55 | **Feared** | Reputation precedes you. Rivals treat you as a predator. |
| 70 | **The Machine** | The division is an instrument. Feelings are operational noise. The UI desaturates; choice ordering inverts. |
| 85 | **Jenkins** | The end state. The system made flesh. No announcement. It just feels different. |

Each threshold, crossed for the first time, sets a permanent flag:
`corruption:competent`, `corruption:effective`, `corruption:feared`,
`corruption:the_machine`, `corruption:jenkins`. The five shipped milestone
scenes (`Data/Templates/scenes/corruption/`) each gate on one of these.

### 4.2 Step-by-step: The Fourth Wet Job

The walkthrough below builds
[`docs/examples/corruption_fourth_wet_job.json`](examples/corruption_fourth_wet_job.json)
— open it alongside this section. It demonstrates the two idioms that carry
the corruption arc: **tripwire triggers** and **scene chaining**.

**Step 1: Define the trigger.** The engine emits `third_kill:{id}` when an
operative's wet-work count reaches three (§3.1). The shipped scene
`third_kill_intro` fires on it and marks itself seen. Our scene is the
*callback*: it requires both the tripwire **and** the earlier scene:

```jsonc
"RequiredFlags": [
  "scene:third_kill_seen",   // the chain: only after third_kill_intro played
  "any_op:third_kill"        // re-binds the same operative as the trigger
],
"Triggers": [
  { "Type": "cycle_reached", "Threshold": 4 }   // let at least a few cycles pass
],
```

The v1.0 manual imagined a trigger like `wet_work_count >= 3 AND conscience >=
40`. The real system expresses that intent differently: the *engine* owns the
wet-work threshold (it emits the tripwire), and if you want "still has a
conscience," chain from a scene whose choices recorded it, or add an
`avg_stat_below` guard in the negative. Design within the vocabulary of §3 —
the validator will tell you immediately if you invent a trigger type.

**Step 2: Cast the scene.** Two people: the operative who crossed the line
(bound by `any_op:third_kill`), and an observer perceptive enough to name it:

```jsonc
"Cast": [
  { "Name": "watcher", "Kind": "Role", "Resolver": "role:mirror" }
]
```

`role:mirror` fails gracefully — if nobody currently holds the mirror role, the
slot goes unfilled and `{operative.role:watcher}` renders as "someone". For a
scene that must always cast its observer, use `highest_social` instead. (The
fixture uses `role:mirror` deliberately, because the mirror *matters* here;
ScenePreview will show you both outcomes.)

**Step 3: Write the dialogue.** The observer opens. They name what the
protagonist can't. Keep it concrete, not philosophical. Cyberpunk register:
technology is embodied, language is clipped, observation is clinical.

> **Writing principle.** The observer doesn't judge. They describe. "Three
> names on your list now" is better than "You've killed three people." The
> first is a mirror; the second is a sermon.

**Step 4: Design choices.** The design rule: the cold option should be
mechanically optimal but morally costly; the humane option should cost
corporate standing (in this schema: `HeatDelta`, or loyalty from the wrong
people) but preserve the operative's soul.

| Choice | Mechanical effect | Narrative effect |
|---|---|---|
| "They were all necessary." | `ConscienceDelta: -8, StressDelta: -3` — relief through acceptance | The operative stops fighting it. Easier now. That's the point. |
| "Take a cycle. Talk to someone." | `StressDelta: -10, LoyaltyDelta: +3, HeatDelta: +2` | The operative reaches out. The corporation notices the hesitation. |
| "What's one more?" | `ConscienceDelta: -12, StressDelta: -5, MoraleDelta: -4` | Gallows humor. Cheapest coping mechanism. Costs the most long-term. |

Name the flags for the corruption ledger (§2.5): the cold choice picks
`choice:cold_acceptance` (contains `cold` → +4 corruption weight), the humane
one `choice:human_pause` (contains `human` → −2).

**Step 5: Test.** No debug console, no state override — the preview tool is
the loop:

```bash
# render it with a real generated roster, resolved cast, all warnings:
dotnet run --project tools/ScenePreview -- docs/examples/corruption_fourth_wet_job.json

# would it actually fire in this state?
dotnet run --project tools/ScenePreview -- --list-eligible \
    --flag scene:third_kill_seen --flag third_kill:1 --cycle 5

# everything still valid?
dotnet run --project tools/ScenePreview -- --validate
```

---

<a name="5-use-case-2-psychology"></a>
## 5. Use Case 2: Writing an Operative Psychology Scene

Psychology scenes make the human-simulation layer visible. They fire when an
operative's internal state crosses a threshold, and they exist to let the
player witness (and intervene in) a psychological trajectory.

### 5.1 The real trigger menu

| Scene type | Trigger | Narrative purpose |
|---|---|---|
| Stress break | `"any_op:breaking_point"` (stress ≥ 90) or `{ "Type": "any_stat_below", "Key": "morale", "Threshold": 25 }` | The operative is cracking. They may refuse orders, lash out, shut down. |
| Loyalty test | `"any_op:defection_risk"` (loyalty ≤ 20) | Considering betrayal, defection, going dark. A moment of decision. |
| Hollowing | `"any_op:hollowed_out"` (conscience ≤ 15) | The lights are on, nobody's home. Different from stress: this is meaning, not pressure. |
| Refusal fallout | `"any_op:refusal"` | Someone said no to wet work. The scene is what you do about it. |
| Team-wide slump | `{ "Type": "avg_stat_below", "Key": "morale", "Threshold": 40 }` | The whole floor has gone quiet. |

Remember these tripwires are **states**: `breaking_point:{id}` clears when the
operative's stress drops below 75 (§3.1). Make crisis scenes `OneShot: false`
where re-occurrence is the point — the 6-cycle cooldown plus the
clear-on-recovery rule keeps them from becoming wallpaper.

### 5.2 The Mirror Role

Psychology scenes almost always include a mirror character: someone who
observes and names what the protagonist is experiencing. The mirror's function
is narrative, not therapeutic. They don't fix anything. They make the internal
external.

Good mirror dialogue:

- "You haven't slept in three days. I can see it in how you're holding your sidearm."
- "The last time someone looked the way you look, they walked into the Baikonur wastes and didn't come back."
- "You're scaring the junior analysts. I don't think you know that."

Bad mirror dialogue (avoid):

- "Are you okay? You seem stressed." (Too generic, too therapeutic)
- "You need to talk to someone about your feelings." (Anachronistic; wrong register)
- "The mission is taking a toll on all of us." (Deflection; loses the individual focus)

### 5.3 Voice by Background

An operative's **background** should inflect how they express distress. The
six real background categories (`archetypes.json` — 25 archetypes across
these; the per-archetype `FlavorFragments` in that file are the ground truth
for each one's texture):

| Background | Voice under pressure |
|---|---|
| **Street** | Terse. Profane. Physical. Stress manifests as aggression, restlessness, picking fights. "I didn't crawl out of the Reclamation Zones to die in a conference room." |
| **CorpoClimber** | Controlled until it isn't. Stress manifests as over-performance, then sudden collapse. "My quarterly review is in six days. I can hold it together for six days." |
| **Spook** | Detached. Clinical. Stress manifests as hyper-analysis, paranoia, trust erosion. "I've run the scenarios. Every exit has a watcher. Every watcher has a handler." |
| **ExMilitary** | Stoic. Brief. Stress manifests as withdrawal, ritual behavior, cleaning weapons. "Weapon's clean. Weapon's clean. Weapon's always clean." |
| **Hacker** | Dissociates into systems. Stress manifests as marathon sessions in the mesh, talking to the code instead of people. "The intrusion logs make sense. The intrusion logs are the only thing that makes sense." |
| **Academic** | Intellectualizes. Stress manifests as method — footnoting their own breakdown. "I've been keeping a dataset on my sleep latency. The trend line is not encouraging." |

You can't gate a scene on background directly, but you can cast by role and
write the register neutrally enough to survive any casting — or write the
scene's texture around the *situation* rather than the person. When a specific
voice is essential, keep the operative's lines short and let the **setting and
your own dialogue** carry the register.

---

<a name="6-use-case-3-mission-sequences"></a>
## 6. Use Case 3: Writing a Mission Narrative Sequence

Mission sequences are the bridge between the strategy sandbox and the
narrative layer: 3–5 interactive nodes that play **between Assignment and
Resolution** for high-stakes missions, modifying the final resolution roll
based on choices. They live in `Data/Templates/narrative_missions.json`, as a
`NarrativeSequence` attached to a mission template.

### 6.1 Schema (`NarrativeSequence`, `NarrativeNode`, `NarrativeChoice`)

```jsonc
"NarrativeSequence": {
  "Nodes": [
    {
      "Phase": "Briefing",                  // Briefing | Complication | ResolutionModifier
      "Text": "The target is a data courier. {leader} reads the brief twice.",
      "RequiresApproach": null,             // only fires if a prior choice had this approach
      "RequiresStat": null,                 // "Skill:60" — node only fires if an assigned op qualifies
      "RollThreshold": null,                // 0.0-1.0 — probabilistic complication
      "Choices": [
        {
          "Text": "Go in quiet.",
          "Approach": "stealth",            // stealth | social | force | cyber | compromise | abort
          "SkillWeightOverride": { "Stealth": 15 },  // added to the resolution weights
          "DifficultyModifier": 0,          // + harder / - easier
          "StressModifier": 5,              // applied to all assigned ops immediately
          "WetWork": false,                 // true = corruption +5, conscience erosion path
          "Aftermath": "The team goes dark at the perimeter.",
          "FlagsOnPick": ["chose_quiet"],   // world flags for later scenes to read
          "RequiresStat": "Stealth:50"      // choice only OFFERED if an assigned op qualifies
        }
      ]
    }
  ]
}
```

Node tokens: `{leader}` (highest Combat+Stealth among assigned), `{target}`,
`{client}` (faction names), `{location}`, `{corp}`.

Every sequence choice also auto-emits `seq:{missionTemplateId}:{approach}` as
a world flag — the scene director reads these later ("you did that"). Skills
are the real six: `Combat`, `Stealth`, `Hacking`, `Deception`, `Intimidation`,
`Persuasion`.

### 6.2 The 3–5 node structure

| Node | Function | Example |
|---|---|---|
| **Briefing** (1) | Set the scene. Stakes, target, location. The player should understand what they're walking into. | "The target is a data courier. Neural implant, encrypted payload. She'll be at the Kharkov exchange at 2200." |
| **Complication** (1–2) | Something goes wrong or an unexpected element appears. Forces a choice that modifies mission parameters. | "There's a child in the room. The courier's daughter. She wasn't in the intel package." |
| **ResolutionModifier** (1–2) | The choices here determine which skill weights apply to the final resolution roll. | "If you go loud, combat. If you talk your way past, deception and persuasion." |

### 6.3 The approach system

Player choices shift which skills determine success — this is how narrative
choices get mechanical teeth:

| Approach | Skills to weight | Texture |
|---|---|---|
| `stealth` | Stealth, Hacking | Lowest stress gain on success. Highest on failure — blown cover is terrifying. |
| `social` | Deception, Persuasion | Moderate stress. Requires the operative to maintain a persona. Registers as humane (−1 corruption weight). |
| `force` | Combat, Intimidation | Fastest resolution. Highest collateral. Guaranteed stress gain. |
| `cyber` | Hacking, Deception | Remote leverage. Gate it with `RequiresStat` so it's only offered when someone can actually do it. |
| `compromise` | — | Take the worse deal to keep hands clean. Registers as humane. |
| `abort` | — | Walking away is always an option. It just costs. |

The approach isn't always a clean choice. The best mission narratives present
situations where the optimal approach is ambiguous or where circumstances
force a switch mid-mission (`RequiresApproach` on a later node builds exactly
this: a complication that only exists because you went loud).

### 6.4 Wet-work decisions

The most important narrative moments in missions are wet-work decisions:
moments where the player must choose whether to kill. These are the bridge
between strategy and narrative — a `"WetWork": true` choice is where the
corruption arc actually moves (+5 weight, `seq:wetwork_chosen`, and the
assigned operatives' wet-work counts feed the index every cycle).

Design rules for wet-work decisions:

- **Never make killing the only option.** There must always be an alternative,
  even if it's costly.
- **The alternative should cost something real.** More stress, a
  `DifficultyModifier`, a worse contract.
- **Name the target.** Even a one-line detail ("she has a data-port scar
  behind her left ear") makes the decision heavier.
- **Show the aftermath.** Use the choice's `Aftermath` text — not a lecture, a
  detail. Blood on a sleeve. A name on a list.

> **Design principle.** The player should never feel that killing was
> inevitable. They should feel that it was the easiest option. That
> distinction is the entire game.

---

<a name="7-use-case-4-relationship-and-corporate"></a>
## 7. Use Case 4: Writing Relationship and Corporate Scenes

### 7.1 Relationship triggers

Relationships are directed edges with a score; scenes fire on threshold
crossings via two trigger types:

| Trigger | Scene type | Function |
|---|---|---|
| `{ "Type": "any_relationship_above", "Threshold": 60 }` | Loyalty/partnership scene | Two operatives who trust each other. Creates vulnerability: one can be leveraged through the other. |
| `{ "Type": "any_relationship_below", "Threshold": -30 }` | Confrontation scene | Open conflict. Affects team cohesion. May force the player to pick a side. |
| `{ "Type": "any_relationship_below", "Threshold": -50 }` | Breaking point | One will act against the other. A forcing function the player must resolve. |

The operative on the *From* end of the matched edge becomes the triggering
operative, so `triggering_operative` casts the person who feels it, and
`{operative.role:...}` slots can cast the other party by role.

### 7.2 Corporate confrontation triggers

Corporate scenes trigger on organizational state, not individual stats — the
moments when the boardroom becomes a battlefield:

| State | Gate with | Scene |
|---|---|---|
| Board losing faith | `{ "Type": "board_confidence_below", "Threshold": 30 }` | A hearing. Justify your division or face restructuring. |
| Board too pleased | `"flag:corp:promotion_imminent"` (confidence ≥ 85) | You're too successful. Other divisions notice. Jealousy is a weapon. |
| Audit incoming | `"flag:corp:audit_triggered"` (suspicion ≥ 75) | An oversight committee appears. Consequences are coming. |
| Faction vendetta | `"flag_prefix:corp:confrontation:"` or `{ "Type": "any_faction_relationship_below", "Threshold": -75 }` | A rival moves against you: sabotage, raids, poached operatives. |
| Public exposure | `"flag:heat:body_found"` | The press has a name and a body. |

### 7.3 Writing the board hearing

The board hearing is a signature CWC scene type. It inverts the power dynamic:
the player, who commands operatives and authorizes wet work, is now answering
to people who command them.

Structure:

1. **Opening**: a board member states the concern. Clinical corporate
   language. No emotion.
2. **Evidence**: specific incidents cited. Missions that went sideways.
   Collateral. Budget overruns. (Pull from what the flags can tell you:
   `last_mission:catastrophe`, `heat:critical`, `seq:wetwork_chosen`.)
3. **Player response**: 2–4 choices. Defend aggressively (`HeatDelta` up,
   loyalty preserved). Accept blame (standing down, relationships preserved).
   Deflect to a subordinate (`LoyaltyDelta` and `MoraleDelta` down hard on the
   scapegoat — remember the deltas hit the scene's primary operative, so cast
   the scapegoat deliberately).
4. **Resolution**: the board's verdict arrives through the sandbox — board
   confidence and directives — not through this scene's text claiming an
   outcome the simulation doesn't deliver.

> **Writing principle.** The board hearing should feel like the inverse of a
> mission briefing. Same sterile language, same clinical detachment. The
> difference is that you're the target now.

---

<a name="8-the-corruption-arc"></a>
## 8. The Corruption Arc: Design Philosophy

The corruption arc is CWC's central thesis: systems corrupt the people who
operate them, and the corruption is invisible from the inside. The player
should never receive a notification that says "You are now corrupt." They
should realize it themselves, looking back.

### 8.1 The five milestones — what actually changes

| Index | Name | Presentation (as shipped) | Narrative shift |
|---|---|---|---|
| 20 | Competent | Full color. | The game has taught that caring works. First milestone scene asks whether it still does. |
| 40 | Effective | HUD saturation begins draining (1.0 → 0.7 across this band). | The board notices. Gray contracts unlock. The observer roles start commenting. |
| 55 | Feared | Saturation 0.7 → 0.25. Brightness starts washing out. Operative dialogue turns **Guarded** — hedged, fewer personal details. | Deference replaces collaboration. Fear replaces respect. |
| 70 | The Machine | Near-monochrome (0.25 → 0.08), washed out. **Choice ordering inverts** — the ruthless option now lists first. Dialogue turns **Transactional** — clipped, professional, emotionally flat. | What was rationalization becomes statement of fact. |
| 85 | Jenkins | Ghost UI (saturation 0.05, brightness 1.55). Dialogue **Hollow** — monosyllabic, affectless. | The protagonist has become the system. No more internal conflict. Efficiency is its own justification. |

### 8.2 Show, don't tell

There is no morality meter on screen, no alignment notification, no
achievement for crossing a threshold. Instead:

- **Choices reorder.** At corruption 0 the humane choice is first. Past The
  Machine, the ruthless choice is first. The player notices they're clicking
  the top option more often. Then they realize the top option has *changed*.
- **Language shifts.** Early-game choice labels say "Eliminate the target."
  Late-game labels say "Clean up." The euphemism treadmill works on the player
  just as it works on the operative.
- **The world reacts.** Junior operatives flinch. Allies stop making small
  talk. The canteen goes quiet when you enter. These are atmosphere scenes
  (`Background` priority), not plot scenes.
- **The UI desaturates.** Color drains at each threshold, slowly enough that
  the player adapts. That adaptation IS the point.

### 8.3 Writing across the arc

When a scene can trigger at multiple corruption levels, write variants gated
on the milestone flags (`ForbiddenFlags` on the higher ones), or lean on the
tone system, which the engine applies automatically. A stress break at
corruption 20 is not a stress break at corruption 80:

| Corruption | Same scene, different expression |
|---|---|
| 20 (Competent) | "I can't sleep. Every time I close my eyes I see his face. The way he looked at me when he knew." |
| 55 (Feared) | "I can't sleep. Operational review is at 0800 and the after-action report has inconsistencies I need to resolve before the committee sees it." |
| 70+ (The Machine) | "I can't sleep. Suboptimal. I'll requisition pharmacological intervention and be operational by 0600." |

Same trigger. Same surface behavior (insomnia). Completely different
interiority. The first operative is haunted. The third has replaced their
humanity with process.

### 8.4 How the index moves (write with the ledger in mind)

The index recomputes every cycle from: the division's total wet-work record
(30%), how hollowed the team's conscience is (30%), operatives dead or
defected (10%), heat + suspicion (10%) — **plus the player's own decisions**,
up to 25 points: scene choices register cold (+4) or humane (−2) by
`ConscienceDelta` sign or by flag keyword (`cold`/`transactional`/`machine` vs
`human`/`mercy`), and sequence choices register wet work (+5) or a
social/compromise approach (−1). Board confidence is deliberately **not** a
component: being good at your job humanely must never read as corruption.

Two consequences for authors: name choice flags honestly (§2.5), and remember
the arc is *authored by decisions*, not weather — a player who never picks the
cold option should stay below Effective, and the shipped balance harness
(`tools/PolicySim`) enforces exactly that on every run.

---

<a name="9-cyberpunk-register-guide"></a>
## 9. Cyberpunk Register Guide

CWC's dialogue should feel like it exists in a world where neural implants are
as mundane as smartphones, where corporations have replaced nation-states as
the primary organizing force, and where language has evolved to reflect both
of those realities.

### 9.1 Technology is embodied

In CWC's world, technology is not external. It's installed. Characters don't
"use" a computer; they have cortical stacks. They don't "check their phone";
they parse their feed overlay. Write technology as body, not tool.

| Instead of | Write |
|---|---|
| "She checked her phone" | "Her optic overlay flickered with incoming traffic" |
| "He accessed the database" | "He dropped into the datawell, cortex-first" |
| "The security system activated" | "The building's immune response kicked in" |
| "She sent a message" | "She squirted a compressed burst through the mesh" |
| "He turned off his implant" | "He went dark. Like unplugging a limb." |

### 9.2 Corporate euphemism

The corporation never says what it means. This is both realistic and
thematically essential: the euphemism treadmill is how the system hides its
own violence from itself.

| Real meaning | Corporate language |
|---|---|
| Assassination | Asset reallocation / Permanent contract termination |
| Firing an employee | Workforce optimization event |
| Covering up a murder | Narrative management / Incident containment |
| Torture / interrogation | Enhanced information extraction |
| Civilian casualties | Collateral inefficiency |
| Blackmail | Leverage-based negotiation |
| Betrayal | Strategic realignment of loyalties |

### 9.3 Street-level contrast

Operatives from street backgrounds don't use corporate euphemism. They call
killing "killing." This contrast is deliberately designed: it makes the
corporate language feel more alien, more sanitized, more complicit.

**Street register**: short sentences, concrete nouns, physical verbs. "I
dropped him in the alley behind the noodle place. He bled out before his
immune system could close the wound."

**Corporate register**: complex clauses, abstract nouns, passive voice. "The
asset was neutralized in a low-visibility environment. Biological termination
occurred prior to automated medical intervention."

### 9.4 Architecture of power

Describe spaces by access level, not aesthetics. A beautiful office is less
important than the fact that it requires Level 4 clearance to enter. Security
tiers, access codes, and surveillance coverage tell the player more about
power dynamics than any description of marble floors.

### 9.5 Information as currency

In CWC's world, data has weight. Blackmail files are assets. Access codes are
leverage. A conversation isn't just dialogue; it's a negotiation where
information is the commodity. Write scenes where characters trade knowledge,
withhold it strategically, or weaponize it.

---

<a name="10-gender-expression-guide"></a>
## 10. Gender Expression Guide

CWC generates operatives (and lets the player be) male, female, or non-binary.
The gender system is designed to add texture, not double the content.

### 10.1 Token syntax and semantics

```
{gender:m|male text|other text}
{gender:f|female text|other text}
{gender:n|non-binary text|other text}
```

The condition letter names a gender; if the relevant person matches, the first
option renders, otherwise the second. **The token reads the cast operative's
gender** — the scene's primary operative — because the content overwhelmingly
uses it about other people ("Promote {gender:m|him|her}. {gender:m|He's|She's}
earned it."). Only when a scene has no cast operative (the Director addressing
you directly) does it fall back to the protagonist's gender.

Tokens can appear in any text field. For non-binary coverage with three-way
pronouns, nest or restructure the sentence to avoid pronouns — often the
stronger line anyway.

### 10.2 Where gender matters

Gender expression should inflect scenes at 15–20 key moments across the full
game, not in every line of dialogue. The moments where it matters are moments
of **power dynamics**: how authority is asserted, how it's perceived, how
resistance manifests.

- **Authority assertion**: a male operative commands; a female operative
  commands and is questioned about it. Not because the game is making a
  statement, but because the world is.
- **Power dynamics**: corporate hierarchy perceives gender. A male director is
  "decisive"; a female director is "aggressive." The same behavior, different
  reception.
- **Physical descriptions**: sparing, but gendered where it adds character.
  Posture, movement, how space is occupied.

### 10.3 The two protagonist modes

CWC's protagonist has two expressive modes, loosely modeled on archetypes from
prestige television:

| Mode | Description |
|---|---|
| **Draper energy** (male default) | Command through mastery. The room orients around the protagonist because competence is assumed. Power is quiet, gravitational. The danger is complacency: the world bends, and bending the world becomes invisible. |
| **Stout energy** (female default) | Weaponized competence. The protagonist has armor made of being better than everyone in the room, and she knows it. The system wasn't built for her, so she became the system. The danger is the same: becoming the thing you learned to beat. |

Both modes reach the same destination: the corruption arc doesn't change based
on gender. What changes is the texture of the journey. The system corrupts
differently, but it corrupts completely.

### 10.4 Don't double content

The gender system is designed for texture, not bifurcation. Do not write
separate scenes for male and female characters. Write one scene with gender
tokens at the moments where the difference creates meaning. Most dialogue is
gender-neutral. Most choices are gender-neutral. The tokens exist for the
15–20 moments where the gendered experience of power adds something the
gender-neutral version can't.

---

<a name="11-quick-reference"></a>
## 11. Quick Reference: Templates, Checklist, Workflow

### 11.1 Starter template

Copy [`docs/examples/minimal_scene.json`](examples/minimal_scene.json) into
the right category folder, rename it, and go. It is the smallest scene the
validator accepts, with every optional field noted in comments.

### 11.2 Pre-commit checklist

| Check | Question | If no |
|---|---|---|
| Trigger | Is the trigger specific enough to fire at the right moment? | Add flags or a `cycle_reached` guard to narrow the window (§3). |
| Casting | Can every cast slot actually resolve? | `role:*` slots fail when nobody holds the role — prefer stat resolvers for must-cast scenes (§2.4). |
| Chain | Does the scene mark itself (`FlagsOnFire: ["scene:x_seen"]`) so later scenes can call back? | Add it. Chains are free. |
| Consequences | Do all choices produce meaningfully different deltas/flags? | If two choices lead to the same outcome, merge them or differentiate. |
| Cost | Does at least one choice cost the player something real? | Add a `HeatDelta`, a loyalty cost, or a stress gain. |
| Corruption ledger | Do flag names reflect intent (`cold`/`human` keywords, §8.4)? | Rename — the tracker reads them. |
| Gender tokens | Placed at power-dynamic moments only (§10)? | Add or remove accordingly. |
| Re-fire | `OneShot` right for this scene? Repeatables get a 6-cycle cooldown automatically. | Flip it. |
| Priority | `Critical` reserved for milestone/crisis, `Background` for atmosphere? | Adjust — priority is queue order, not importance to you. |
| Voice | Does the dialogue match the register (§5.3, §9)? | Revise. |
| **Preview** | Does `ScenePreview` render it clean — cast resolved, no raw tokens, no warnings? | Fix what it reports; it names the problem and the field. |

### 11.3 File layout and naming

```
Data/Templates/scenes/
  corruption/       corruption milestones, wet-work reckonings
  psychology/       stress, loyalty, hollowing, refusal fallout
  corporate/        board, audit, factions, press, promotion
  relationships/    bonds and tensions between operatives
  missions/         debriefs and mission-adjacent scenes
  atmosphere/       ambient texture, Background priority
```

One scene per file, file named after the scene id:
`corruption/fourth_wet_job.json` holds `"Id": "corruption_fourth_wet_job"` —
or keep id and filename identical; the loader doesn't care, but greppability
does. The id namespace is flat and global regardless of folder.

### 11.4 The authoring workflow

```bash
# 1. Write. Copy the starter, write the scene.

# 2. Preview — seconds, not minutes. Real director, real generated roster:
dotnet run --project tools/ScenePreview -- Data/Templates/scenes/corruption/my_scene.json
#    (or by id, once the file is in place:)
dotnet run --project tools/ScenePreview -- my_scene_id

# 3. Check when it fires:
dotnet run --project tools/ScenePreview -- --list-eligible --flag third_kill:1 --cycle 8

# 4. Validate the whole library (also what CI/the smoke test enforce):
dotnet run --project tools/ScenePreview -- --validate

# 5. Full pipeline proof before committing:
cd tests/SmokeTest && dotnet run     # includes: render EVERY scene, catch every trap
cd tools/PolicySim && dotnet run     # 40 seeds × 20 cycles balance targets, real content
```

In-engine, templates load once at New Game — restart s&box and start a new
game to see changes in context. There is no hot-reload and no debug console;
the preview tool exists so you rarely miss them.
