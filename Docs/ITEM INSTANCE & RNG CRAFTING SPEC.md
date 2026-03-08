# HUNTERS & COLLECTORS
## ITEM INSTANCE & RNG CRAFTING SPEC
Version: 1.0 — LOCKED
Status: Authoritative Design Update

---

# 1. DESIGN GOAL

Crafting must always produce an item.

The gameplay loop should reward skill progression by producing better rolled equipment rather than preventing crafting attempts.

Crafting failure is removed.

Instead, item stats are generated using RNG influenced by the player's crafting skill.

Low skill produces weaker items on average.
High skill produces stronger items on average.

This maintains:

- Positive progression
- Player-driven economy
- Crafting excitement
- Stat variation between items

---

# 2. CORE RULES

## Crafting Never Fails

If the player has:

- Required materials
- Required crafting station
- Inventory space

Crafting always succeeds.

## Crafted Items Roll Stats

When crafted, an item receives rolled stat values.

Example:

Stone Axe A
Damage: 6
Durability: 41
SwingSpeed: 1.03

Stone Axe B
Damage: 9
Durability: 57
SwingSpeed: 0.97

Both are the same item type but have different stat rolls.

## Skill Influences RNG

Skill does not guarantee specific stats.

Instead, skill biases the distribution.

| Skill Level | Result |
|-------------|--------|
| Low | Rolls skew toward minimum values |
| Medium | Wide stat spread |
| High | Higher average stats |
| Master | Low rolls become rare |

## No Visible Quality Number

The game does not display a quality score.

Players judge items by their actual stat values.

Examples:

- Damage
- Durability
- Defence
- Swing Speed
- Movement Speed

---

# 3. ITEM CATEGORIES

Items are divided into two major types.

---

# STACK ITEMS

Examples:

- Wood
- Stone
- Fiber
- Ore
- Food ingredients

Characteristics:

- Stackable
- No RNG stats
- No durability
- Simple ItemStack data

---

# INSTANCE ITEMS

Examples:

- Tools
- Weapons
- Armor
- Shields
- Advanced crafted equipment

Characteristics:

- Non stackable
- Have durability
- Have RNG stat rolls
- Stored as ItemInstance

Inventory slot limit: 1 per slot.

---

# 4. ITEMDEF ROLE (TEMPLATE)

ItemDef remains a static template.

It defines possible stat ranges rather than final values.

Example:

ItemDef: StoneAxe

DamageMin = 4
DamageMax = 10

DurabilityMin = 30
DurabilityMax = 60

SwingSpeedMin = 0.9
SwingSpeedMax = 1.2

These ranges define what can be rolled.

---

# 5. ITEM INSTANCE MODEL

Crafted equipment becomes runtime instances.

Example model:

ItemInstance
{
    ItemId
    DamageRolled
    DefenceRolled
    SwingSpeedRolled
    MovementSpeedRolled

    MaxDurability
    CurrentDurability
}

This object represents a specific crafted item.

Two items of the same type may have completely different values.

---

# 6. CRAFTING STAT ROLL PROCESS

When crafting occurs:

### Step 1 — Server Validation

Server validates:

- Recipe
- Materials
- Inventory capacity
- Craft station

### Step 2 — Determine Skill Level

Example:

Blacksmithing = 32

### Step 3 — Convert Skill To Roll Bias

Normalized value:

skillFactor = skillLevel / skillMax

Example:

32 / 100 = 0.32

### Step 4 — Adjust Roll Range

Low skill:

- Rolls biased toward minimum values

High skill:

- Rolls biased toward maximum values

### Step 5 — Roll Stats

Example:

Damage = Random(DamageMin .. DamageMax)
Durability = Random(DurabilityMin .. DurabilityMax)
SwingSpeed = Random(SwingSpeedMin .. SwingSpeedMax)

Rolls are influenced by skill bias.

### Step 6 — Create ItemInstance

Server creates ItemInstance.

### Step 7 — Insert Into Inventory

ItemInstance inserted into inventory.

---

# 7. DURABILITY SYSTEM

Durability becomes instance state.

MaxDurability (rolled)
CurrentDurability (runtime)

Durability decreases through use.

When durability reaches 0:

Item becomes broken or unusable until repaired.

Repair mechanics will be defined in a later system.

---

# 8. INVENTORY MODEL UPDATE

Inventory slots now support two item types.

Possible states:

- Empty
- StackItem
- InstanceItem

Conceptual structure:

InventorySlot
{
    IsEmpty

    StackItem
    {
        ItemId
        Quantity
    }

    InstanceItem
    {
        ItemInstance
    }
}

Rules:

- Stack items may stack
- Instance items may not stack

---

# 9. NETWORK AUTHORITY

All stat rolls occur server side.

Clients never generate items.

Client flow:

Client → RequestCraftServerRpc

Server flow:

- Validate recipe
- Remove ingredients
- Roll stats
- Create ItemInstance
- Insert into inventory
- Send updated snapshot

---

# 10. ECONOMY IMPACT

This system supports a player driven economy.

Better crafters produce better gear.

Example marketplace scenario:

Stone Axe (Damage 6)
Price: 12 coins

Stone Axe (Damage 9)
Price: 48 coins

Item value becomes tied to actual stat quality rather than arbitrary rarity tiers.

---

# 11. FUTURE SYSTEMS ENABLED

This architecture supports:

Repair System

Repair cost based on durability loss.

Master Crafting

Skill levels may unlock higher stat caps.

Rare Craft Events

Small chance to exceed normal stat ranges.

Enchanting

Modifiers applied to instance items.

Salvaging

Breaking items into materials.

Item Affixes

Later expansion.

---

# 12. DESIGN PRINCIPLES PRESERVED

This system maintains core pillars of Hunters & Collectors.

## Skill Determines Everything

Skill directly affects crafting outcomes.

## Player Economy

Better crafted items command higher value.

## Server Authority

All stat generation and item creation occurs server side.

## Deterministic Systems

Stat ranges defined in ItemDef ensure predictable balance.

---

# 13. IMPLEMENTATION PHASE

Recommended implementation order:

1. Introduce ItemInstance data model
2. Update InventorySlot structure
3. Update Inventory snapshot DTO
4. Update CraftingNet to roll stats
5. Update ItemDef stat ranges
6. Introduce durability system

---

# 14. FINAL LOCK

The following rules are now project locked.

- Crafting never fails.
- Crafting generates items with RNG stats.
- Crafting skill biases RNG outcomes.
- Crafted equipment becomes non-stackable ItemInstances.
- ItemDef defines stat ranges.
- Actual stats exist only on ItemInstance.
- All rolls occur server side.

