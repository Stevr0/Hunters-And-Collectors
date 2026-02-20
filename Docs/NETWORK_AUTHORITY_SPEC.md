# NETWORK_AUTHORITY_SPEC.md — Hunters & Collectors (AUTHORITATIVE)

Version: 0.1  
Status: DRAFT (Ready to Lock)  
Engine Target: Unity  
Networking: Netcode for GameObjects (NGO)  
Authority: Server-Authoritative  
MVP Runtime: Host (Listen Server)  
Future Runtime: Dedicated Server

---

# 0. Purpose

This document defines the authoritative networking rules for:

- Ownership
- Server vs client responsibilities
- Replication strategy
- RPC contracts
- Validation rules (anti-cheat)

Codex must implement networking exactly as specified here.

---

# 1. Core Authority Rules (LOCKED)

## 1.1 Server is Truth

- Only the server/host may mutate authoritative game state.
- Clients may request actions.
- Server validates and applies changes.
- Server replicates resulting state to clients.

Authoritative state includes (MVP):
- Wallet Coins
- Skill XP/Level
- Player Inventory
- Vendor Chest Inventory
- Known Items (base price list)
- ShelterComplete flag
- Built pieces placement list

---

## 1.2 Clients are Presentation + Input

Clients are responsible for:
- Input collection
- UI presentation
- Local prediction ONLY if explicitly specified (not in MVP)

MVP rule:
- No client prediction.
- UI updates only after server state is received.

---

## 1.3 Ownership and Permissions

### Player Objects
- Each player has a `PlayerNetworkRoot` NetworkObject.
- Owner client has input authority (requests only).
- Server has state authority.

### World Objects
World objects are server-owned:
- Resource nodes (trees/rocks/plants)
- Build pieces
- Vendor
- Vendor chest

Rule:
- Clients never claim ownership of world objects.

---

# 2. Networked Objects (MVP)

The following MUST be NetworkObjects:

## 2.1 PlayerNetworkRoot
Contains networked components for:
- WalletNet
- SkillsNet
- InventoryNet
- KnownItemsNet

## 2.2 VendorChestNet
- One per shelter/vendor (MVP)
- Holds chest inventory state

## 2.3 VendorNPCNet
- Optional visual/interaction object
- Does NOT store inventory (chest does)

## 2.4 ResourceNodeNet (Optional MVP)
Two allowed MVP approaches:

A) Server-only logic + client visuals (no NetworkObject per node)
B) Fully networked nodes (each node is a NetworkObject)

MVP LOCK (choose now):
- Use approach A unless node interaction requires per-node networking.

---

# 3. Replication Strategy (MVP LOCK)

## 3.1 Snapshot Replication for Inventories

Inventories replicate as snapshots from server to clients.

Rationale:
- Simplifies correctness
- Avoids desync bugs
- Easier Codex generation

### Inventory Snapshot Payload
An inventory snapshot contains:
- Width
- Height
- Slot list (index → {itemId, quantity} or empty)

Applies to:
- Player inventory
- Vendor chest inventory

---

## 3.2 Event Replication for UX Feedback

Certain UX events replicate as lightweight ClientRpcs:
- TransactionSucceeded
- TransactionFailed (with reason)
- HarvestSucceeded
- HarvestFailed

These are UI-only and do not carry state.

---

# 4. RPC Contract Rules (LOCKED)

## 4.1 General RPC Rules

- Client -> Server: `ServerRpc`
- Server -> Clients: `ClientRpc`

Rules:
- All ServerRpcs must validate:
  - Caller is owner (where applicable)
  - Action is permitted
  - Inputs are sane (no negative quantities, no unknown itemIds)
- ServerRpcs must never trust client-provided inventory state.

---

## 4.2 Inventory Mutations

Clients NEVER directly modify inventory.

All inventory changes occur on server via one of:

### 4.2.1 RequestMoveSlotServerRpc
- Parameters: fromIndex, toIndex
- Server:
  - validates indices
  - applies TryMoveSlot on authoritative inventory
  - sends updated snapshot to owning client

### 4.2.2 RequestSplitStackServerRpc (Optional MVP)
- Parameters: index, splitAmount
- Server:
  - validates splitAmount
  - performs split if possible
  - sends updated snapshot

---

## 4.3 Harvesting

### RequestHarvestServerRpc
Parameters:
- resourceNodeId (stable identifier)

Server validation:
- node exists and is harvestable
- node not on cooldown
- player has required tool (if required)
- player in range

Server applies:
- remove/disable node
- award items to player inventory (Add)
- create KnownItem entries as needed
- schedule respawn timer (server-side)

Server replicates:
- player inventory snapshot
- node state change (visual disable) via ClientRpc or scene-state refresh

---

## 4.4 Crafting

### RequestCraftServerRpc
Parameters:
- recipeId
- craftCount (MVP default 1)

Server validation:
- recipe exists
- player has required ingredients
- output fits in inventory

Server applies:
- remove ingredients
- add output items
- update KnownItems

Server replicates:
- player inventory snapshot

---

## 4.5 Vendor / Commerce

### 4.5.1 RequestOpenVendorServerRpc
Parameters:
- vendorId

Server:
- validates vendor exists
- responds with vendor chest snapshot

### 4.5.2 RequestCheckoutServerRpc (Atomic Transaction)
Parameters:
- vendorId
- cartLines[] where each line = {slotIndex, quantity}

Important:
- cart references chest slots by index.
- client does NOT send itemId (server derives it from chest snapshot).

Server validation (all must pass):
1) vendor exists
2) each slotIndex is valid and non-empty
3) requested quantity <= slot quantity
4) buyer inventory can fit all resulting items
5) buyer has enough coins for total

Price calculation (MVP LOCK):
- UnitPrice = buyer KnownItemRegistry.BasePrice for that ItemId
- If buyer does not know item: UnitPrice defaults to 1 AND item becomes Known after purchase
- TotalPrice = sum(UnitPrice * quantity)

Server applies (atomic):
- subtract coins from buyer
- add coins to seller (vendor owner) OR vendor wallet (MVP decision below)
- remove items from vendor chest
- add items to buyer inventory
- grant XP:
  - Buyer Negotiation +1 per successful checkout
  - Seller Sales +1 per successful checkout

Replication:
- buyer wallet snapshot (or coins NetworkVariable)
- seller wallet snapshot
- buyer inventory snapshot
- vendor chest snapshot to all viewers (or all clients)
- TransactionSucceeded ClientRpc to buyer and seller

Failure:
- no state changes
- TransactionFailed ClientRpc with reason

---

# 5. State Containers (What Uses NetworkVariables)

MVP uses a hybrid:

## 5.1 NetworkVariables for Small Scalars
- Player Coins: `NetworkVariable<int>` (owner read + server write)
- Skill levels/xp: either NetworkVariables OR included in a "PlayerStateSnapshot"

MVP LOCK:
- Coins and Skills replicate via a PlayerStateSnapshot to keep networking consistent.

## 5.2 Custom Snapshot Messages for Grids
- Inventories are too large for NetworkVariables per slot.
- Use custom serialization + ClientRpc updates to push entire grid.

---

# 6. Snapshot Transport (Implementation Guidance)

## 6.1 Owner-Only vs Observers

Player inventory snapshots are sent only to the owning client.

Vendor chest snapshots are sent to:
- any client with vendor UI open

MVP simplification:
- broadcast vendor chest snapshots to all clients (acceptable for MVP)

Choose one:
A) Broadcast (simpler)
B) Only to viewers (more efficient)

MVP LOCK:
- Use A (Broadcast) for simplicity.

---

# 7. Anti-Cheat / Validation Rules (LOCKED)

Server rejects any request where:
- quantity <= 0
- indices out of range
- itemId unknown (where itemId is provided)
- caller is not owner for player-specific requests
- caller attempts to mutate non-owned inventory
- buyer attempts to purchase more than available
- buyer attempts to spend more than coins

All authoritative calculations occur server-side.

---

# 8. Error Codes (For UI)

Transaction/Action failure must return a reason enum.

## 8.1 FailureReason Enum (MVP)
- None
- InvalidRequest
- OutOfRange
- NotEnoughCoins
- NotEnoughInventorySpace
- OutOfStock
- VendorNotFound
- RecipeNotFound
- MissingIngredients
- NodeNotHarvestable
- OnCooldown

---

# 9. Out of Scope (MVP)

- Client prediction
- Reconciliation
- Lag compensation
- Encryption/obfuscation
- Dedicated server hardening

These may be addressed after MVP is validated.

---

END OF NETWORK_AUTHORITY_SPEC v0.1

