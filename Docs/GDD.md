# HUNTERS AND COLLECTORS — GAME DESIGN DOCUMENT (GDD)
Version: 0.1 (First Draft)
Engine Target: Unity 6 (URP)
Networking: Multiplayer, Server-Authoritative
Core Philosophy: Skill Determines Everything

---

# 1. HIGH CONCEPT

Hunters and Collectors is a multiplayer, skill-driven exploration and economy game where every player owns and develops their own shard.

Players begin naked on an empty plot of land. Through gathering, crafting, building, trading, and specialization, they construct a house that becomes a shop, museum, workshop, and prestige showcase.

All systems are governed by skills. There are no character levels. There are no hard gates. Progression emerges through use.

Core Fantasy:
- Explore your world
- Discover and collect rare items
- Build your home
- Attract a vendor
- Run a shop
- Become a specialist
- Build prestige through display and trade

---

# 2. CORE DESIGN PILLARS

1. Everything Requires a Skill
2. Physical World Over UI Abstractions
3. Player-Driven Economy
4. Deterministic, Server-Authoritative Systems
5. Exploration Feeds Economy

---

# 3. WORLD STRUCTURE

Each Player Owns a Shard.

Shard Structure (Initial):
- SCN_Village (Safe zone, building allowed)
- SCN_Wildlands (Resource gathering, hunting, rare finds)

Players may visit other shards.
Production occurs locally.
Items must be physically transported between shards.

---

# 4. STARTING EXPERIENCE

Players spawn:
- Naked
- No tools
- Empty land plot
- No vendor

Early Gameplay Loop:
1. Gather primitive resources (wood scraps, stones, fiber, berries)
2. Craft primitive tools (stone hatchet, knife, hammer)
3. Build basic shelter
4. Attract vendor NPC
5. Begin trading

---

# 5. SHELTER REQUIREMENT (VENDOR ATTRACTION SYSTEM)

Vendor does not spawn by default.

Basic Shelter Requirements:
- Enclosed space
- Door
- Light source
- Storage container

Once conditions met:
- Vendor arrival event triggers after delay

Vendor NPC becomes permanent shard resident.

---

# 6. SKILL SYSTEM (FOUNDATION)

No Levels. No XP Bars. Use-Based Growth.

Every meaningful action performs a skill check.
Skill improves on successful use.

Skill Categories (Initial):

Gathering:
- Foraging
- Mining
- Lumbering
- Fishing
- Trapping
- Excavation

Hunting:
- Tracking
- Skinning
- Butchery
- Trophy Preservation

Crafting:
- Carpentry
- Smithing
- Preservation
- Restoration
- Decoration
- Appraisal

Commerce:
- Negotiation (Player)
- Sales (Vendor)
- Warehouse (Vendor)

Skill Determines:
- Success chance
- Yield
- Access to higher tier resources
- Container tiers
- Vendor capacity
- Trade margins

---

# 7. RESOURCE TIERS

Higher skill reveals higher tier resources.

Low skill players cannot see high tier nodes.

Example Tier Visibility:
- Tier 1 visible at 0 skill
- Tier 2 at 20
- Tier 3 at 40
- Tier 4 at 70
- Tier 5 at 90

---

# 8. KNOWN ITEM SYSTEM

When a player:
- Finds an item
- Crafts an item
- Identifies an item

It becomes "Known".

Each player maintains a Known Item Registry.

Registry Stores:
- ItemId
- BasePrice (player-defined)
- TimesCrafted
- TimesSold
- Discovery Tier

Base Price is defined per ItemId per player.
Base Price is NOT final transaction price.

---

# 9. VENDOR SYSTEM

Vendor is an NPC attracted to a valid shelter.
Vendor belongs to shard owner.

Vendor Has Skills:
- Sales (affects upward price pressure)
- Warehouse (affects number of linked vendor containers)
- Appraisal (future expansion)

Vendor Skill improves on successful trade activity.

---

# 10. VENDOR CHESTS (PHYSICAL TRADE SYSTEM)

Vendor Chests are:
- Crafted world objects
- Placed game objects
- Fixed slot capacity

Players place physical containers in world.

Carpentry determines:
- Which container tiers can be crafted
- Slot capacity per container

Warehouse skill determines:
- How many vendor containers can be linked to vendor

Only linked containers contribute to shop inventory.

If an item is inside a linked vendor container:
It automatically appears in shop UI.

No manual listing.
No separate shop inventory.

Physical storage is the source of truth.

---

# 11. TRADE FLOW

Buyer Interaction:
1. Approach vendor
2. Open shop UI
3. Add items to cart
4. Checkout

Price is calculated at checkout.

Trade Resolution:
- Skill differential calculated
- Price modified per item
- Bulk bonus optional
- Server validates inventory
- Items transfer
- Currency transfers
- Skills gain XP

All calculations are server-authoritative.

---

# 12. PRICE CALCULATION MODEL (INITIAL)

SkillDelta = VendorSales - BuyerNegotiation

TradeModifier = SkillDelta × Coefficient

FinalPrice = BasePrice × (1 + TradeModifier)

Clamp Range:
- Minimum 70% of BasePrice
- Maximum 150% of BasePrice

Each item calculated individually at checkout.

---

# 13. SPECIALIZATION PATHS

Players may specialize in:
- Master Carpenter
- Rare Artifact Collector
- Trophy Hunter
- Fossil Restorer
- Luxury Furniture Maker
- Traveling Negotiator
- Merchant Tycoon

Emergent roles arise from skill investment.

---

# 14. ECONOMIC DESIGN PRINCIPLES

- All items sellable
- Physical stock only
- No global auction house
- Price diversity between players
- Skill-driven margin
- Deterministic trade resolution

Optional Future Systems:
- Reputation
- Market saturation
- Global average pricing
- Prestige bonuses for display

---

# 15. FIRST PLAYABLE SLICE (MVP TARGET)

Must Support:
- Spawn naked
- Gather Tier 1 resources
- Craft primitive tools
- Build small shelter
- Attract vendor
- Craft small vendor chest
- Link chest
- Set base price for one known item
- Another player visits shard
- Buy item
- Price adjusted via skill differential
- Skills increase

If this loop works, core game is validated.

---

# 16. CORE DESIGN STATEMENT

Hunters and Collectors is a multiplayer, skill-driven world where exploration feeds economy, economy feeds prestige, and every system is governed by use-based progression.

Everything requires a skill.
Skill determines everything.

---

END OF FIRST DRAFT

