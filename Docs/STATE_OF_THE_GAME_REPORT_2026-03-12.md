# Hunters & Collectors State Of The Game Report

Date: March 12, 2026  
Project: Hunters & Collectors  
Engine: Unity 6 URP  
Networking: Netcode for GameObjects  
Assessment Type: Codebase and build-state review

## Executive Summary

Hunters & Collectors is currently in a strong pre-alpha or vertical-slice state.

This is no longer just a concept prototype. The project has a real server-authoritative gameplay backbone, a working menu-to-session loop, persistent saves, inventory and equipment systems, harvesting, crafting, storage, vendor trading, combat, AI actors, building placement, encumbrance, and death-grave persistence.

The codebase also compiles successfully as of March 12, 2026 with zero errors. The current technical condition is functional and promising, though not yet production-clean. There are still warning-level issues, several first-pass systems, and major design pillars from the GDD that remain only partially implemented or still exist mainly as documentation rather than live gameplay.

The clearest overall conclusion is:

Hunters & Collectors already has a playable systemic foundation, but it has not yet fully reached the intended game identity described in the design documents. The micro gameplay loops are increasingly real. The macro progression layer is still under construction.

## Current Development Stage

Recommended label: Pre-Alpha Vertical Slice

Why this label fits:

- The game has multiple connected gameplay systems rather than isolated prototypes.
- The project builds successfully and has an identifiable playable loop.
- Persistence and session flow are already present, which is a major maturity signal.
- Core survival-economy features are implemented in code, not just planned.
- Several high-level progression systems from the GDD are still not fully realized.
- Some systems explicitly describe themselves as first-pass implementations.

This means the project is beyond early prototyping, but still short of a content-complete alpha.

## What Is Clearly Working

### 1. Session Flow And Bootstrap

The game has a real menu-driven startup and return-to-menu flow.

Current code indicates:

- A bootstrapper starts host sessions.
- Player and shard keys are selected and passed into session startup.
- The gameplay scene is loaded additively.
- Returning to menu triggers a save before shutting the network session down.

This is important because it means the project is already structured around actual play sessions rather than editor-only testing.

### 2. Persistence And Save Architecture

Persistence is one of the strongest parts of the current project.

The save model already supports:

- Player save data
- Wallet data
- Skills
- Known items
- Inventory contents
- Equipment
- Player location
- Shard save data
- Placed buildings
- Placed storage chests
- Graves

There is also a save manager coordinating shard load, player load, autosave, and full save operations.

This is a major sign of maturity because persistence usually arrives after the core systems begin stabilizing. Hunters & Collectors is already operating with a persistent-world mindset rather than a disposable test-scene mindset.

### 3. Inventory, Items, Equipment, And Carry Weight

The inventory side of the game appears meaningfully developed.

Implemented or represented systems include:

- Inventory grid logic
- Stack and instance item handling
- Equipment slots
- Item instance data
- Durability support
- Drag-and-drop UI support
- Encumbrance and carry-weight calculations
- Known-items and pricing-related player data

Recent commit history also suggests these systems have been actively improved with weight and encumbrance work.

This means the project already supports a strong itemization foundation for survival, crafting, and economy gameplay.

### 4. Harvesting And Resource Loop

Harvesting is present as a real networked system, not just a design note.

The codebase contains:

- Harvesting network logic
- Resource node logic and registry
- Harvest animation support
- Auto-pickup support
- Tiered resource/node direction in recent commits

This strongly suggests the resource acquisition loop is one of the more advanced feature areas in the project.

### 5. Crafting

Crafting is implemented as a server-authoritative system.

Current crafting work includes:

- Crafting recipe definitions
- Crafting database
- Crafting UI
- Crafting station validation
- Ingredient consumption
- Inventory output checks
- Skill XP gain
- Server-rolled item-instance output for crafted gear

The crafting code comments also show a clear design direction: crafting no longer fails due to RNG once validation passes, but crafted gear can still vary in quality via controlled item-instance rolling.

That is a strong foundation for a capability-driven progression system.

### 6. Building And Structure Placement

Building is live, but still clearly in a first-pass state.

The project already supports:

- Placeable items as regular item definitions
- Server-authoritative placement validation
- Inventory consumption on placement
- Spawned placed structures as network objects
- HeartStone-based build-radius validation
- Shelter and structure requirement hooks
- Persistence support for placed buildings and placed storage

However, the code explicitly says some expected building features are not yet included, such as:

- Snapping
- Support systems
- Repair
- Demolition
- Refunds
- Client prediction

So the building feature set is real and usable, but not yet feature-complete.

### 7. Storage

Placed storage appears to be properly integrated into the world model.

The current chest/storage implementation includes:

- Server-authoritative chest inventory
- Snapshot replication to clients
- Store and take operations
- Stable persistent identifiers
- Save/load integration for placed chest contents

This is an especially good sign because storage frequently exposes weaknesses in persistence, authority, and UI synchronization. The presence of this system suggests the architecture is already fairly cohesive.

### 8. Vendors And Economy

The vendor system is more advanced than a basic placeholder.

The transaction service includes:

- Server-side validation
- Deterministic checkout planning
- Anti-exploit aggregation by slot index
- Pricing from persistent vendor stock data
- Buyer funds validation
- Inventory capacity checks
- Ordered commit logic

This suggests the economy-first pillar is already being supported by meaningful gameplay code rather than just design intent.

### 9. Combat

Combat is implemented as a server-authoritative melee system.

Current signals include:

- Player attack requests from client input
- Server validation against spoofed attacks
- Authoritative melee sweep checks
- Equipment-informed combat values
- Stamina costs
- Combat XP
- Damage application and animation replication

Combat does not appear to be the most content-rich system yet, but it is absolutely present as a functioning gameplay layer.

### 10. AI Actors And Creatures

AI is not just stubbed. There is a real server-authoritative actor brain.

The actor AI controller includes behavior states such as:

- Hold
- Patrol
- Chase
- Attack
- Retreat
- ReturnHome

The project also contains actor definitions, faction data, hostility logic, spawn zones, spawn points, loot tables, and loot dropping.

Recent commit history also mentions bears, which implies active expansion of creature content.

### 11. Vitals, Food, Rested State, And Survival Layer

A first-pass vitals system exists and already covers:

- Health
- Stamina
- Dynamic max vitals
- Regeneration
- Food buffs
- Rested state
- Consume-food requests from inventory
- Skill gain tied to regeneration

This is a strong survival-RPG signal.

One important limitation is that the vitals code explicitly describes food and rested effects as runtime-only for now, meaning some of that temporary state is not yet persisted between sessions.

## What Looks Stable Enough To Call Core Gameplay

At this point, the following can reasonably be treated as the game’s current core:

- Start a session
- Load into a persistent shard
- Move a player through a networked world
- Gather resources
- Manage inventory and equipment
- Craft items
- Place structures within HeartStone rules
- Use storage
- Trade with vendors
- Fight creatures
- Gain skills
- Save and load progress

That is a substantial amount of real game.

## What Is Still Clearly Early Or First-Pass

### 1. Settlement Pressure And Madness Systems

The GDD strongly centers the game around shard pressure, settlement stability, madness stages, raid intensity, and escalating world instability.

Those concepts are clearly documented, but they do not appear to be implemented as a fully active runtime gameplay layer yet.

They are currently much more visible in documentation than in the live codebase.

This is one of the largest gaps between the game’s design identity and the current playable implementation.

### 2. Raid Orchestration

Raid logic is part of the intended game fantasy, but current code comments indicate this is not yet fully included.

HeartStone-related code explicitly calls out raid orchestration as not included in the current first-pass scope.

This means the game has local survival and economy systems, but not yet the full basin-pressure conflict loop that the design documents describe as central.

### 3. Gate Progression And Macro World Progression

The GDD and adaptation docs describe gate-based exploration progression, shard-level gate flags, artifact requirements, and unlock chains.

Those systems appear to be largely design-side at the moment rather than fully represented in gameplay code.

This is another major missing piece of the intended long-term structure.

### 4. Artifact Research And Trade Route Progression

The design documents repeatedly mention artifact research, trade route stabilization, and settlement recovery mechanics.

Those are not yet visible as mature runtime systems in the current code sample.

### 5. Building Depth

Building exists, but the richer building loop is still incomplete.

Expected future additions likely include:

- Better placement UX
- More structural rules
- Repair loops
- Demolition and cleanup
- Better client responsiveness
- Potential shelter-state expansion

### 6. Temporary Survival State Persistence

Vitals are implemented, but some temporary states are explicitly runtime-only.

That means the survival layer exists, but it has not yet reached persistence completeness.

## Technical Health

### Build Status

As of March 12, 2026:

- `dotnet build Assembly-CSharp.csproj -nologo` succeeds
- Errors: 0
- Warnings: 40

This is a healthy sign overall. The project is not in a broken state.

### Main Warning Themes

The warnings are mostly technical debt rather than immediate blockers.

The main categories are:

- Deprecated NGO `ServerRpc(RequireOwnership = ...)` usage
- Deprecated Unity object lookup APIs like `FindObjectOfType` and `FindObjectsOfType`
- A few likely unassigned UI fields
- A small number of unused fields

This means the codebase is operational, but would benefit from a cleanup pass before larger scaling work continues.

### Repo State

At the time of review, the git working tree appeared clean.

That is useful because it suggests the current snapshot is not an unstable half-merge or heavily interrupted work state.

## Momentum From Recent Commits

Recent commit messages indicate clear development progress in active gameplay areas:

- World drop pass
- Encumbrance added
- Weight added
- Added six-tier nodes and crafting
- Updated actor definitions and saving state
- Added bears
- Storage bug fixes
- Saving system updates
- MVP core systems marked stable

This is a good sign. Development is not stalled, and recent work aligns with strengthening systemic gameplay rather than only cosmetic iteration.

## Best Current Description Of The Game

If the project needed a short honest description today, it would be:

Hunters & Collectors is currently a multiplayer server-authoritative survival-economy sandbox with persistence, crafting, building, inventory, storage, vendors, combat, and AI creatures already functioning at a meaningful level. Its intended shard-pressure, gate-progression, and settlement-instability meta-loop is designed and partially scaffolded, but not yet fully realized in live gameplay.

## Biggest Strengths Right Now

- Strong systemic foundation
- Real persistence architecture
- Server-authoritative multiplayer discipline
- Economy and inventory depth
- Broad feature coverage across many core loops
- Clear design direction in docs
- Active recent progress on meaningful systems

## Biggest Risks Right Now

- The game’s most distinctive macro systems are not yet fully live
- There is still a notable gap between GDD identity and current gameplay identity
- Several systems are explicitly first-pass and may need rework as production hardens
- Warning-level technical debt could compound if ignored for too long
- Survival and progression breadth may outpace polish if too many adjacent systems are expanded before the macro loop is locked

## Overall Conclusion

Hunters & Collectors is in a genuinely promising state.

The project already has enough working systems to prove that the game is real, structurally coherent, and moving in the right direction. It is not just a paper design and not just a pile of disconnected prototypes. The codebase shows a strong foundation for a multiplayer survival-economy game with persistence and systemic progression.

However, the game’s true differentiators, especially settlement pressure, shard instability, gate progression, raid escalation, and the larger basin meta-loop, still appear to be the main unfinished frontier.

So the honest state of the game is:

The foundation is strong. The core loops are increasingly playable. The signature game layer is not fully there yet.

That is a very good place to be for pre-alpha, provided the next phase focuses on turning the documented macro design into active gameplay rather than only broadening already-working subsystems.

## Recommended Next Milestone Focus

If this report is used for planning, the highest-value next milestone would likely be:

Lock and implement the first complete shard-pressure progression loop.

That would likely include:

- Settlement stability tracking
- At least one active madness or pressure stage effect
- One functioning gate unlock chain
- One raid or settlement-threat loop
- A direct connection between player action and shard stabilization

That milestone would move Hunters & Collectors from "feature-rich systems build" toward "the actual game as designed."
