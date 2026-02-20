# DATA_MODEL_SPEC.md — Hunters & Collectors (AUTHORITATIVE)

Version: 0.1  
Status: DRAFT (Ready to Lock)  
Engine Target: Unity  
Networking: Server-Authoritative (Host MVP → Dedicated Later)  
Persistence: Server Writes Saves Only

---

# 0. Purpose

This document defines the authoritative runtime data contracts for:

- Items
- Item stacks
- Inventory
- Vendor chest storage
- Known item registry
- Currency

This specification exists to enable deterministic, machine-readable script generation.
No system may invent alternate data structures.

---

# 1. Item Definitions (Static Data)

## 1.1 ItemId (LOCKED)

Type: `string`  
Format: `IT_` prefix + PascalCase name

Examples:
- IT_Wood
- IT_Stone
- IT_Fiber
- IT_StoneAxe
- IT_WoodenClub

Rules:
- IDs are globally unique.
- IDs never change once released.
- IDs are never reused.

---

## 1.2 ItemDef (ScriptableObject)

Each ItemId has exactly one ItemDef asset.

### Required Fields (MVP)

- `string ItemId` (unique, required)
- `string DisplayName`
- `Sprite Icon`
- `int MaxStack`
- `ItemCategory Category`

### ItemCategory Enum (MVP)

- Resource
- Tool
- Crafted

### Stacking Rules

- Resources typically use large stack sizes (e.g., 99)
- Tools and crafted weapons use MaxStack = 1
- MaxStack must be >= 1

Future extensions (not in MVP):
- Durability
- Quality tier
- Material grade

---

# 2. Runtime Item Representation

## 2.1 ItemStack (LOCKED)

Represents a stack of identical items.

### Fields

- `string ItemId`
- `int Quantity`

### Validation Rules

- Quantity >= 1
- Quantity <= ItemDef.MaxStack
- ItemId must reference a valid ItemDef

If validation fails, the stack is invalid.

---

## 2.2 Unique Item Instances

MVP does NOT use unique instances.

No GUID.
No durability.
No per-item data.

Future versions may introduce `ItemInstance` for:
- Durability
- Random rolls
- Quality

When that happens, this spec will version to 0.2.

---

# 3. Inventory Model (Slot Grid System)

Inventory uses a fixed-size grid mapped internally to a 1D array.

---

## 3.1 InventoryGrid

### Fields

- `int Width`
- `int Height`
- `InventorySlot[] Slots` (length = Width * Height)

Grid index mapping:
index = y * Width + x

---

## 3.2 InventorySlot

Represents one slot in the grid.

### Fields

- `bool IsEmpty`
- `ItemStack Stack`

### Rules

If IsEmpty == true:
- Stack is ignored

If IsEmpty == false:
- Stack must be valid

A slot may only contain a single ItemStack type.

---

## 3.3 Standard Inventory Operations (Authoritative API Contract)

These methods must exist in Inventory implementation.

- `bool CanAdd(string itemId, int quantity, out int remainder)`
- `int Add(string itemId, int quantity)`
- `bool CanRemove(string itemId, int quantity)`
- `bool Remove(string itemId, int quantity)`
- `bool TryMoveSlot(int fromIndex, int toIndex)`
- `bool TrySplitStack(int index, int splitAmount, out int newSlotIndex)`

### Stacking Behavior

When moving items:

If destination slot contains same ItemId:
- Stack up to MaxStack
- Overflow remains in source

If destination slot is empty:
- Move stack

If destination contains different ItemId:
- Swap stacks

---

# 4. Vendor Chest Storage (MVP)

VendorChest uses the same InventoryGrid structure.

### Fields

- `InventoryGrid ChestInventory`

### Behavior

- All non-empty slots are considered "for sale".
- Vendor UI lists every non-empty stack.
- Vendor chest capacity is fixed size in MVP.

Future scaling:
- Warehouse skill modifies allowed chest count
- Chest tier modifies capacity

---

# 5. Known Item Registry (Per Player)

Tracks items the player has discovered.

---

## 5.1 KnownItemEntry

### Fields

- `string ItemId`
- `int BasePrice`

### Rules

- Entry is created when ItemId first enters inventory.
- BasePrice default = 1
- BasePrice must be >= 0

---

# 6. Currency Model

Currency Type: Coins

### Fields

- `int Coins`

Rules:
- Coins >= 0
- Stored per-player

---

# 7. MVP Item Definition Table

Resources:
- IT_Wood (MaxStack 99)
- IT_Stone (MaxStack 99)
- IT_Fiber (MaxStack 99)

Tools:
- IT_StoneAxe (MaxStack 1)

Crafted:
- IT_WoodenClub (MaxStack 1)

---

# 8. Persistence Requirements (Preview)

The save system must serialize:

Player:
- InventoryGrid (all slots)
- Known Items
- Wallet Coins

Shard:
- Vendor Chest InventoryGrid

Exact JSON schema defined in PERSISTENCE_SPEC.md (next phase).

---

END OF DATA_MODEL_SPEC v0.1
