# HUNTERS & COLLECTORS
## Game Design Document (GDD)
Version: 2.0 – Basin Adaptation Direction
Engine: Unity 6 (URP)
Networking: Netcode for GameObjects (Server Authoritative)

---

# 1. HIGH CONCEPT

Hunters & Collectors is a multiplayer, skill‑based survival economy game set inside a sealed basin world known as a Shard.

A central settlement sits at the lowest point of a massive enclosed valley beneath an unnatural red sun. Players must explore outward, harvest resources, unlock ancient gates, stabilize the settlement, and survive escalating pressure from hostile factions.

The world grows more dangerous over time.
The settlement grows unstable if neglected.
Progression is capability-driven, not power-creep driven.

Core Pillars:
- Skill-based progression (no traditional classes)
- Economy-first rewards
- Server-authoritative multiplayer
- Settlement pressure & instability system
- Gate-driven exploration
- Player-driven trade and pricing

---

# 2. CORE GAME LOOP

Primary Loop:
1. Harvest resources (tools required)
2. Craft equipment, tools, and build pieces
3. Explore outward from settlement
4. Clear Points of Interest (POIs)
5. Recover artifacts and rare materials
6. Unlock progression gates
7. Settlement pressure increases over time
8. Players reinforce, stabilize, expand
9. Repeat at higher tiers

Secondary Loops:
- Trade via vendors and caravans
- Skill progression via action use
- Defensive building during raid windows
- Artifact research to unlock recipes

---

# 3. WORLD STRUCTURE

## 3.1 The Basin (Shard)

Each Shard contains:
- One primary buildable settlement zone (central)
- Surrounding wilderness zones
- Tiered dungeon POIs
- Environmental hazard zones

Players cannot build outside the central settlement area.

## 3.2 Zone Tiers

Tier 0 – Settlement Hub
Tier 1 – Starting Wilds
Tier 2 – Beastman Territories
Tier 3 – Swamp Regions
Tier 4 – Jungle Civilizations
Tier 5 – Fire Lands
Tier 6 – Deep Ancient Zones

Zones unlock via Gates (see section 8).

---

# 4. SETTLEMENT PRESSURE SYSTEM

The Shard contains a global pressure system representing environmental corruption.

## 4.1 Core Variables (Shard-Level)

BasinAge (increases with time + milestones)
SettlementStability (0–100)
MadnessStage (derived from stability)
RaidIntensity (derived from BasinAge + Stability)

## 4.2 Madness Stages

Stable → Irritated → Paranoid → Hostile → Collapse

Effects:
- Vendor price multipliers increase
- Craft success slightly reduced
- Raid frequency increases
- Enemy tiers escalate
- NPC dialogue shifts

Players restore stability by:
- Opening gates
- Completing artifact research
- Delivering supplies
- Building defensive structures
- Establishing trade routes
- Completing final ritual objective

---

# 5. SKILL SYSTEM

All actions are skill-driven.
Max Skill Level: 100
Success chance scales linearly (0% at 0 skill, 100% at 100 skill).

Core Skills:
- Lumberjacking
- Mining
- Foraging
- Running
- ToolCrafting
- EquipmentCrafting
- BuildingCrafting
- Research
- Negotiation

Higher skill provides:
- Reduced action time
- Increased yield
- Increased rare drop chance
- Reduced stamina cost
- Higher craft success chance

---

# 6. COMBAT SYSTEM

Combat is real-time with auto-attacks.
Attributes:
- Hit Chance
- Defense Chance
- Swing Speed
- Damage Increase
- Resistances

Combat is not the primary progression path.
Enemies exist to:
- Guard gates
- Pressure settlement
- Disrupt gathering
- Drop artifacts/resources

---

# 7. FACTIONS

## 7.1 Core Factions

Settlement
Beastmen
Troglodytes
Lizardmen
Azcan Civilization
Schattenalfen
Ancient Corruption (hidden force)

Each faction defines:
- Raid composition
- Resource preferences
- Artifact drops
- Territory control

Faction relationships are shard-level.

---

# 8. GATE SYSTEM

Progression is gate-based, not level-based.

Example Gate Chain:

GATE_GatewayPassage → unlock Swamp
GATE_AzcanArtifacts → unlock Jungle
GATE_FireRings → unlock Fire Lands
GATE_Schattenalfen → unlock Deep Zone
GATE_SummonRitual → stabilize Shard

Gate Requirements may include:
- Key item possession
- Artifact sets
- Completed POI
- Stability threshold

Server validates all gate unlocks.

---

# 9. ITEM SYSTEM

## 9.1 Categories

Resources
Tools
Equipment
Consumables
Artifacts
Key Items

## 9.2 Philosophy

Items grant capability, not exponential stat growth.

Examples:
- Ring of Fire Protection → access hazard zone
- Withered Vine → bypass plant gate
- Artifact Bundle → unlock research tier

Players set base prices for known items.
Vendor holds funds until player spawn.

---

# 10. POI DESIGN

POIs serve as progression anchors.

Types:
- Raider Camps (respawn, scaling)
- Resource Mines
- Artifact Ruins
- Boss Lairs
- Civilization Outposts

Clearing POIs may:
- Reduce raid frequency
- Unlock trade route
- Reveal gate requirement
- Drop rare materials

---

# 11. ECONOMY

## 11.1 Coins

Server-authoritative wallet.
All transactions validated server-side.

## 11.2 Vendors

Stock increases with progression.
Prices affected by MadnessStage.

## 11.3 Caravans

Unlocked after discoveries.
Provide:
- Safe transport
- Bulk hauling
- Stability boost

---

# 12. BUILDING

Building allowed only in central settlement.
Only shard owner (and co-owners) may build.

Build categories:
- Defensive (walls, towers)
- Utility (craft stations)
- Storage
- Vendor stalls

Defenses directly reduce raid impact.

---

# 13. RAID SYSTEM

Raids occur only when shard owner is active.
Raid intensity scales with:
- BasinAge
- MadnessStage
- Cleared POIs

Raid goals:
- Destroy structures
- Kill vendors
- Steal resources

Raid victory restores stability.

---

# 14. ARTIFACT RESEARCH SYSTEM

Artifacts recovered from POIs.
Delivered to research station.

Effects:
- Unlock recipes
- Unlock gates
- Reveal lore
- Increase stability

Artifacts drive narrative progression.

---

# 15. ENDGAME OBJECTIVE

Players uncover the source of corruption.
Final ritual event stabilizes Shard.
Stabilization resets BasinAge pressure.
Shard remains replayable with increased difficulty.

---

# 16. MVP SLICE

To reach playable basin loop:

1. Harvest + Craft + Save/Load
2. Implement Beastman Camp POI
3. Implement Settlement Pressure scaling
4. Implement Gateway gate unlock
5. Implement Artifact delivery

This produces core basin gameplay loop.

---

# 17. DESIGN PRINCIPLES

- Systems first, content second.
- Economy over XP grind.
- Capability over raw damage inflation.
- Pressure must escalate.
- Players must feel relief after stabilization.
- All gameplay actions are skill-based.
- All state changes are server-authoritative.

---

END OF DOCUMENT

