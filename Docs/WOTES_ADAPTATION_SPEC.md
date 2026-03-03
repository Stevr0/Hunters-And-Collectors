# WOTES_ADAPTATION_SPEC.md
Version: 1.1 (Unity Adaptation)
Project: Hunters & Collectors (Unity 6 + NGO)
Status: Structural Design Reference

---

# 1. PURPOSE

This document formalizes how *Warriors of the Eternal Sun* (WotES) serves as a structural inspiration for Hunters & Collectors.

This is NOT a D&D conversion.
This is a systems adaptation for:

- Skill-based progression
- Server-authoritative multiplayer
- Economy-first rewards
- Basin-style shard world
- Escalating settlement pressure

The goal is to extract structure, not mechanics.

---

# 2. CORE ADAPTATION CONCEPT

## 2.1 Basin World Model

A central settlement exists inside a sealed valley (“basin”) under a hostile environment.

Gameplay Loop:

1. Explore outward
2. Clear hostile sites
3. Recover artifacts/resources
4. Unlock new regions
5. Settlement destabilizes over time
6. Pressure escalates
7. Stabilize via progression

This mirrors Hunters & Collectors’ shard pressure model.

---

# 3. NON-NEGOTIABLE SYSTEM ALIGNMENT

## 3.1 Authority Rules

- Server validates all actions.
- Clients send intent only.
- Inventory snapshots are server-driven.
- Gate unlocks are shard-level flags.

## 3.2 Economy-First Rewards

Combat should unlock:
- Resources
- Trade routes
- Craft recipes
- Capability upgrades

NOT raw power creep.

## 3.3 Persistence

Shard Save must include:
- BasinAge
- SettlementStability
- GateFlags
- DiscoveredPOIs

Player Save must include:
- Skills
- Coins
- KnownItems
- Inventory

---

# 4. PROGRESSION GATES

## 4.1 Gate Chain

GATE_GatewayPassage  → unlock Swamp
GATE_AzcanArtifacts  → unlock Jungle
GATE_FireRings       → unlock Fire Lands
GATE_Schattenalfen   → unlock City
GATE_SummonKa        → stabilize shard

## 4.2 Gate Validation (Server-Side)

Requirements may include:
- Required item in inventory
- Required artifact count
- Required shard flag
- Optional item consumption

On success:
- Set shard flag
- Broadcast unlock event
- Enable zone access

---

# 5. ZONE STRUCTURE

Core Zones:

ZN_CastleHub
ZN_StartingIsland
ZN_BeastmanCaves
ZN_GatewayPassage
ZN_MalpheggiSwamp
ZN_AzcanJungle
ZN_FireLands
ZN_SchattenalfenCity

Zones may contain:
- Resource nodes
- POIs
- Dungeon entrances
- Environmental hazards

---

# 6. SETTLEMENT PRESSURE SYSTEM

This replaces the “Burrower madness” concept.

## 6.1 Core Variables

BasinAge (int)
SettlementStability (0–100)
MadnessStage (derived enum)
RaidIntensity (derived value)

## 6.2 Madness Stages

Stage 0 – Stable
Stage 1 – Irritated
Stage 2 – Paranoid
Stage 3 – Hostile
Stage 4 – Collapse

## 6.3 Effects Per Stage

- Vendor price multipliers increase
- Raid frequency increases
- Raid strength scales
- NPC dialogue changes
- Trade routes become unreliable

## 6.4 Stability Recovery

Players restore stability by:
- Completing gates
- Delivering supplies
- Building defenses
- Opening trade routes
- Final ritual event

---

# 7. FACTION MODEL

Core Factions:

FCT_CastleHub
FCT_Beastmen
FCT_Troglodytes
FCT_Lizardmen
FCT_Azcan
FCT_Schattenalfen

Each faction defines:
- Raid roster
- Trade preferences
- Artifact drops
- Relationship state

Relationship is shard-level, not per-player.

---

# 8. ENEMY ARCHETYPES

Roles:

Raider      – attacks structures/vendors
Ambusher    – targets gatherers
Disruptor   – applies debuffs/disease
Sentinel    – guards gates
Guardian    – boss-tier

Starter Set:

EN_BeastmanHunter
EN_BeastmanWarrior
EN_BeastmanShaman
EN_BeastmanChieftain
EN_Troll
EN_Gargoyle
EN_LizardmanWarrior
EN_Hydra
EN_RedDragon

Enemies exist to create pressure, not XP grind.

---

# 9. ITEM STRUCTURE

## 9.1 Item Categories

Resources
Tools
Equipment
Consumables
Artifacts
Key Items

## 9.2 Key Items

IT_WitheredVine
IT_AzcanArtifact_A/B/C
IT_RingFireProtection
IT_SchattenalfenMedallion
IT_Scroll_SummonKa

## 9.3 Preferred Effects

- Access unlock
- Harvest speed bonus
- Yield bonus
- Craft success bonus
- Hazard immunity

Avoid raw stat inflation.

---

# 10. ARTIFACT RESEARCH LOOP

Players deliver cultural artifacts to research system.

Server flow:
- Validate items
- Remove from inventory
- Grant coin reward (optional)
- Grant skill XP
- Advance shard progression
- Unlock recipes or stock

Artifacts drive world knowledge.

---

# 11. CARAVAN / TRADE ROUTES

Routes unlock after discovery.

Functions:
- Safe travel
- Bulk hauling
- Stability increase
- Economic expansion

Routes scale in cost and risk.

---

# 12. POI MODEL

Examples adapted from WotES:

POI_BeastmanCamp
POI_HiddenForestHalls
POI_PassagewayCaverns
POI_AntNest
POI_WebPalace
POI_WindingCavern
POI_WellOfSouls
POI_TreeOfLife
POI_LizardmanVillage

POIs may provide:
- Boss encounters
- Rare resources
- Gate items
- Stability bonuses

---

# 13. MVP IMPLEMENTATION SLICE

Minimum WotES-feel integration:

1. Implement GATE_GatewayPassage
2. Add POI_BeastmanCamp (scaling respawn)
3. Add Artifact Delivery system
4. Add Settlement Pressure scaling

This creates basin structure without full dungeon suite.

---

# 14. AUTHORITATIVE ID CATALOG

Zones:
ZN_CastleHub
ZN_StartingIsland
ZN_BeastmanCaves
ZN_GatewayPassage
ZN_MalpheggiSwamp
ZN_AzcanJungle
ZN_FireLands
ZN_SchattenalfenCity

Gates:
GATE_GatewayPassage
GATE_AzcanArtifacts
GATE_FireRings
GATE_Schattenalfen
GATE_SummonKa

Shard Flags:
SF_GatewayOpened
SF_AzcanUnlocked
SF_FireUnlocked
SF_SchattenalfenUnlocked
SF_KaSummoned

---

# 15. DESIGN PRINCIPLES

Do:
- Use WotES structure
- Keep rewards economy-focused
- Tie pressure to shard state

Do Not:
- Import D&D stat math
- Build XP grind treadmill
- Let clients mutate shard state

---

END OF SPEC

