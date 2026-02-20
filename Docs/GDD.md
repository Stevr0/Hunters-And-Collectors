# Hunters & Collectors — Game Design Document (Authoritative)
Version: 1.0
Status: DESIGN LOCKED (MVP + Full System Direction)
Engine Target: Unity (Server-Authoritative, Host Mode MVP → Dedicated Server Later)

---

# 1. Core Vision

Hunters & Collectors is a skill-driven, player-shaped economic world where:

• Every action is governed by a skill  
• Every item can be sold  
• Players define item value through Base Price  
• World progression is shaped by crafting, ecology, and trade  
• Infrastructure quality determines economic capability  

The MVP validates the economic loop.  
The full vision expands into a living skill-locked society.

---

# 2. Design Pillars

## 2.1 Skill Determines Everything

Every system is governed by skill level:

• Harvest yield  
• Craft quality  
• Vendor capacity  
• Warehouse size  
• Buildable structures  
• Economic efficiency  
• Ecology interaction  

There are no hard character classes.
Progression is entirely skill-based.

---

## 2.2 Player-Defined Economy

• Players set Base Price per Known Item  
• Vendor listings use that Base Price  
• Trade outcomes increase Sales / Negotiation skills  
• Economic identity emerges from pricing decisions

The game does not dictate value — players do.

---

## 2.3 Infrastructure Drives Capability

Better skills unlock:

• Larger vendor chests  
• More vendor chest placements (Warehouse skill)  
• Higher-tier build pieces  
• Advanced crafting stations  

Skill gates scale opportunity — not arbitrary level locks.

---

# 3. MVP Vertical Slice (Locked Scope)

The MVP proves the foundational loop:

1. Harvest Tier‑1 resources  
2. Craft tool + sellable item  
3. Build shelter  
4. Vendor spawns  
5. List item via vendor chest  
6. Player purchases item  
7. Currency transfers  
8. Skills increase  
9. Save → Load restores state

---

# 4. Core Systems (MVP + Expansion Direction)

---

# 4.1 Currency System

Currency: Coins  
Integer values only  
Player starts with 100 coins (MVP)

Future:
• Coins enter economy via NPC demand, trade, services  
• Economic scarcity influences price behavior  

Coins are stored per-player in Wallet.

---

# 4.2 Skill System

## MVP Skills
• Sales  
• Negotiation

Starting Level: 0  
XP Curve: XP needed = 10 × (level + 1)  
XP per successful transaction: +1

## Planned Skill Categories

Harvesting Skills:
• Woodcutting  
• Mining  
• Foraging

Crafting Skills:
• Carpentry  
• Smithing  
• Tailoring  
• Toolmaking

Commerce Skills:
• Sales  
• Negotiation  
• Warehouse Management

Building Skills:
• Construction  
• Engineering

Ecology Skills (future):
• Animal Handling  
• Suppression  
• Environmental Management

Skill effects scale:
• Yield  
• Craft efficiency  
• Item quality (future)  
• Vendor slot count  
• Chest placement limit

---

# 4.3 Items

Items use stable string IDs.

MVP Items:
Resources:
• IT_Wood  
• IT_Stone  
• IT_Fiber

Tool:
• IT_StoneAxe

Crafted:
• IT_WoodenClub

Future expansion:
• Quality tiers  
• Tool durability  
• Material grades  
• Crafted item variations

All items are sellable if placed in a vendor chest.

---

# 4.4 Inventory System

Slot Grid system (locked).

Transport rule:
Only carried inventory transfers between shards.

Future expansion:
• Weight system  
• Encumbrance  
• Specialized storage containers

---

# 4.5 Crafting System

MVP:
• Always succeeds  
• Ingredient removal + output add

Recipes (MVP):
• Stone Axe  
• Wooden Club

Future expansion:
• Skill-based success chance  
• Quality tiers  
• Craft speed scaling  
• Workstation requirements  
• Advanced recipe trees

---

# 5. Building System

## MVP Build Pieces
• Floor  
• Wall  
• Door  
• Roof  
• (Optional Light)

ShelterComplete triggers vendor spawn.

## Future Building Expansion
• Structural tiers  
• Multi-tile foundations  
• Advanced housing  
• Workshops  
• Specialized vendor stalls  
• Decorative value affecting NPC attraction

Building skill unlocks:
• Larger structures  
• Improved efficiency  
• Advanced economic buildings

---

# 6. Vendor & Commerce System

## Vendor Behavior (MVP)
• Spawns when shelter complete  
• Linked to one Vendor Chest  
• Lists all items in chest

## Base Price System
• Per itemId  
• Default 1 coin  
• Editable via Known Items UI

## Checkout Rules
Atomic transaction:
• Validate stock  
• Validate coins  
• Validate inventory space  
• Transfer coins + items  
• Apply XP

## Future Commerce Expansion
• Buyer NPC AI  
• Negotiation modifiers  
• Market demand modifiers  
• Supply scarcity  
• Regional price variation  
• Player-to-player trade UI

---

# 7. Known Item Registry

Item becomes Known when first entering inventory.

Per item stores:
• itemId  
• basePrice

Future:
• Knowledge tiers  
• Appraisal skill  
• Hidden value discovery  
• Market intelligence systems

---

# 8. Ecology & Resource Systems

## MVP
• Fixed spawn points  
• Simple respawn timers  
• No stress system

Respawn:
• Tree 60s  
• Rock 90s  
• Plant 45s

## Future Ecology Expansion
• Overharvesting stress  
• Yield reduction  
• Regrowth cycles  
• Guardian creatures  
• Hazard zones  
• Suppression mechanics  
• Skill-based environmental mitigation

Ecology is intended to become a strategic layer.

---

# 9. Infrastructure Scaling (Future Direction)

Vendor capacity is determined by:
• Warehouse skill  
• Vendor chest tier  
• Crafted container quality

Carpentry skill unlocks:
• Larger chests  
• Improved storage  
• Advanced structures

Every upgrade path is skill-based — not arbitrary unlock-based.

---

# 10. Networking Model

Authority: Server-authoritative

MVP Runtime: Host (listen server)

Future:
• Dedicated shard servers  
• Player-owned shards  
• Cross-shard visitation  
• Inventory-based trade transport

Clients never write save data.

---

# 11. Persistence

Player Save:
• Coins  
• Skills  
• Known Items + Base Prices  
• Inventory

Shard Save:
• ShelterComplete  
• Placed Build Pieces  
• Vendor Chest Contents

Future:
• Ecology states  
• Vendor AI state  
• Market conditions

---

# 12. Explicitly Out of MVP Scope

• Guardian creatures  
• Hazard systems  
• Advanced negotiation math  
• PvP systems  
• Crime / theft systems  
• Multi-vendor economies  
• Dynamic markets

These are expansion layers beyond v1.0.

---

# 13. MVP Completion Criteria

MVP is complete when:

1. Harvesting works  
2. Crafting works  
3. Shelter triggers vendor  
4. Vendor chest lists items  
5. Checkout transfers coins/items  
6. Skills gain XP correctly  
7. Save/Load restores full state

---

# 14. Long-Term Identity

Hunters & Collectors is not just a crafting game.
It is a skill-driven economic simulation where infrastructure, pricing decisions, and environmental stewardship determine prosperity.

The MVP proves the engine.
The expansion builds the society.

---

END OF GDD v1.0 (Authoritative)

