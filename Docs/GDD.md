# HUNTERS AND COLLECTORS — GAME DESIGN DOCUMENT (GDD)
Version: 0.3 (Structured Ecology Edition)
Engine Target: Unity 6 (URP)
Networking: Multiplayer, Server-Authoritative
Core Philosophy: Skill Determines Everything

---

# 1. HIGH CONCEPT

Hunters and Collectors is a multiplayer, skill-driven exploration and economy game where each player develops their own shard through gathering, crafting, building, ecological mastery, and trade.

The game is not combat-centric.
Danger exists, but it is environmental.
Creatures are not killed — they are managed, harvested responsibly, or worked around.

Progression is determined entirely by skill capability.

---

# 2. CORE DESIGN PILLARS

1. Everything Requires a Skill  
2. Skill Determines Access  
3. Ecology Over Combat  
4. Physical World Over UI Abstraction  
5. Exploration Feeds Economy  
6. Deterministic, Server-Authoritative Systems  

---

# 3. WORLD STRUCTURE

Each player owns a shard.

Initial shard scenes:
- SCN_Village (Safe zone, building allowed)
- SCN_Wildlands (Exploration, renewable creatures, reactive hazards)
- SCN_Cave (Layered territorial guardians and high-tier materials)

Players may visit other shards.
Items must be physically transported.
There is no global auction house.

---

# 4. PLAYER START & EARLY LOOP

Players spawn:
- Naked
- No tools
- Empty land plot
- No vendor present

Early Gameplay Loop:
1. Gather Tier 1 resources
2. Craft primitive tools
3. Build enclosed shelter
4. Attract vendor NPC
5. Begin trade and specialization

If this loop works, the foundation of the game is validated.

---

# 5. SKILL SYSTEM (FOUNDATION)

No levels. No XP bars. Use-based growth.

Every meaningful action performs a skill check.
Skills improve through successful use.

## 5.1 Skill Categories

Gathering:
- Foraging
- Mining
- Lumbering
- Fishing
- Trapping
- Excavation

Fieldcraft & Creature Skills:
- Tracking
- Animal Handling
- Preservation
- Herbalism
- Trophy Preservation

Crafting:
- Carpentry
- Smithing
- Restoration
- Decoration
- Appraisal

Commerce:
- Negotiation (Player)
- Sales (Vendor)
- Warehouse (Vendor)

## 5.2 Skill Determines

- Safe passage through hazard zones
- Aggro radius reduction
- Harvest yield
- Harvest quality
- Access to higher-tier resources
- Vendor container capacity
- Trade price margins

Skill is territorial access.

---

# 6. RESOURCE TIERS

Higher skill reveals and enables safe access to higher-tier resources.

Example Visibility Thresholds:
- Tier 1 → Skill 0
- Tier 2 → Skill 20
- Tier 3 → Skill 40
- Tier 4 → Skill 70
- Tier 5 → Skill 90

Higher tiers are often protected by territorial guardians.

---

# 7. CREATURE ECOLOGY SYSTEM

Creatures are part of the ecosystem.
They are never killed for progression.

## 7.1 Ecological Types

### Type A — Renewable Harvest Creatures

Examples:
- Sheep (Wool)
- Goats (Milk)
- Bees (Honey)

These creatures:
- Are non-lethal
- Regenerate resources over time
- Require Animal Handling skill

Low skill:
- Reduced yield
- Lower quality
- Increased stress

High skill:
- Higher yield
- Premium materials
- Faster regeneration


### Type B — Territorial Guardians

Examples:
- Spider (Silk Zone)
- Snake (Venom Zone)
- Boar (Root Territory)

These creatures:
- Anchor valuable resource zones
- Must be suppressed or safely navigated
- Do not drop loot directly

They create Safe Work Zones when managed correctly.

---

# 8. HOSTILITY MODEL

Hostility defines temperament, not combat.

## 8.1 Passive
- Never hostile
- May flee or become stressed
- Yield affected by handling skill

## 8.2 Reactive
- Neutral unless provoked
- Have Warning and Aggro zones
- Can be safely bypassed with sufficient skill

## 8.3 Aggressive
- Always defend territory
- Function as environmental progression gates
- Require high skill or suppression to work nearby

---

# 9. HAZARD & SAFE PASSAGE SYSTEM

Hazards replace combat encounters.

Each hazard defines:
- RequiredSkill
- MinSafeSkill
- BaseAggroRadius
- WarningRadius
- StatusEffect

If PlayerSkill < MinSafeSkill:
- Entering AggroRadius applies status

If PlayerSkill >= MinSafeSkill:
- Aggro radius reduced
- Safe passage possible

EffectiveAggroRadius = BaseAggroRadius × (1 - SkillScalingFactor)

Higher skill physically reduces danger radius.

---

# 10. SAFE WORK ZONE SYSTEM

To harvest within a territorial guardian’s zone:

Requirements:
- PlayerSkill >= MinSafeSkill
- Suppression interaction performed

Safe Work Zone:
- Temporary radius
- Duration scales with skill
- Enables harvest nodes

Failure may:
- Collapse zone
- Trigger status
- Reduce yield

Creatures recover and regenerate.

---

# 11. STATUS EFFECT SYSTEM

Hazards apply Conditions instead of combat damage.

Examples:
- Venom
- Infection
- Hallucination
- Fatigue
- Reduced Vision
- Reduced Carry Capacity

Statuses impact:
- Skill checks
- Movement
- Crafting quality
- Negotiation outcomes

Untreated statuses may escalate.

---

# 12. KNOWN ITEM SYSTEM

When a player:
- Finds
- Crafts
- Identifies

An item becomes Known.

Registry stores:
- ItemId
- Player-defined BasePrice
- TimesCrafted
- TimesSold
- Discovery Tier

BasePrice is not final price.

---

# 13. VENDOR & TRADE SYSTEM

Vendor is attracted by valid shelter:
- Enclosed space
- Door
- Light
- Storage container

Vendor Skills:
- Sales
- Warehouse
- Appraisal (future)

## 13.1 Vendor Chests

- Crafted physical containers
- Linked via Warehouse skill
- Physical storage is source of truth

Items placed in linked containers appear automatically in shop UI.

No abstract inventory listing.

---

# 14. TRADE FLOW

1. Buyer interacts with vendor
2. Adds items to cart
3. Checkout triggers server validation

Price Model:

SkillDelta = VendorSales - BuyerNegotiation  
FinalPrice = BasePrice × (1 + TradeModifier)

Clamp:
- 70% minimum
- 150% maximum

All calculations are server-authoritative.

---

# 15. SPECIALIZATION PATHS

Emergent roles include:
- Master Carpenter
- Merchant Tycoon
- Master Animal Handler
- Hazard Specialist
- Deep-Cave Extractor
- Silk Artisan
- Venom Distiller

Skill investment defines identity.

---

# 16. FAILURE STATES

Failure does not primarily mean death.

Possible consequences:
- Status escalation
- Forced retreat
- Tool damage
- Resource loss
- Economic downtime

Death is rare and meaningful.

---

# 17. CORE DESIGN LOCK

Creatures are not defeated.

Some provide.
Some warn.
Some defend their territory.

The world does not become weaker.

The player becomes more capable.

---

END OF VERSION 0.3

