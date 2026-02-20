# SYSTEM_SCRIPT_MAP.md — Hunters & Collectors (AUTHORITATIVE)

Version: 0.1  
Status: DRAFT (Ready to Lock)  
Engine Target: Unity  
Networking: Netcode for GameObjects (NGO)  
Authority: Server-authoritative

---

# 0. Purpose

Defines the authoritative script/class map for Hunters & Collectors MVP.

This is the "no-guess" blueprint Codex uses to generate code:

- Exact class names
- File names
- Responsibilities
- Required components
- Prefab attachment points
- Public APIs
- RPC endpoints
- Dependencies between scripts

If a system is not listed here, it does not exist in MVP.

---

# 1. Folder Structure (LOCKED)

Unity project scripts folder root:

- `Assets/_Scripts/HuntersAndCollectors/`

Subfolders:

- `Bootstrap/`
- `Data/`
- `Networking/`
- `Players/`
- `Inventory/`
- `Items/`
- `Skills/`
- `Crafting/`
- `Harvesting/`
- `Vendors/`
- `Building/`
- `Persistence/`
- `UI/`
- `Shared/`

---

# 2. Core Data (ScriptableObjects)

## 2.1 Items

### File: `Items/ItemDef.cs`
Class: `ItemDef : ScriptableObject`
Responsibilities:
- Defines ItemId, DisplayName, Icon, MaxStack, Category

### File: `Items/ItemCategory.cs`
Enum: `ItemCategory`
Values: Resource, Tool, Crafted

### File: `Items/ItemDatabase.cs`
Class: `ItemDatabase : ScriptableObject`
Responsibilities:
- Holds list of ItemDef
- Builds runtime dictionary ItemId -> ItemDef
- Provides lookup API for validation

Public API:
- `bool TryGet(string itemId, out ItemDef def)`
- `ItemDef GetOrThrow(string itemId)`

---

## 2.2 Crafting

### File: `Crafting/RecipeDef.cs`
Class: `RecipeDef : ScriptableObject`
Responsibilities:
- recipeId
- ingredient list
- output list

### File: `Crafting/RecipeDatabase.cs`
Class: `RecipeDatabase : ScriptableObject`
Responsibilities:
- recipe lookup by recipeId

Public API:
- `bool TryGet(string recipeId, out RecipeDef def)`

---

# 3. Shared Data Structures

## 3.1 Inventory Structs

### File: `Inventory/ItemStack.cs`
Struct: `ItemStack`
Fields:
- `string ItemId`
- `int Quantity`

### File: `Inventory/InventorySlot.cs`
Struct: `InventorySlot`
Fields:
- `bool IsEmpty`
- `ItemStack Stack`

### File: `Inventory/InventoryGrid.cs`
Class: `InventoryGrid`
Responsibilities:
- Stores width/height and slots
- Provides operations (Add/Remove/Move/Split)

Public API (authoritative contract):
- `bool CanAdd(string itemId, int quantity, out int remainder)`
- `int Add(string itemId, int quantity)`
- `bool CanRemove(string itemId, int quantity)`
- `bool Remove(string itemId, int quantity)`
- `bool TryMoveSlot(int fromIndex, int toIndex)`
- `bool TrySplitStack(int index, int splitAmount, out int newSlotIndex)`

Dependencies:
- ItemDatabase (for MaxStack)

---

## 3.2 Known Items

### File: `Players/KnownItemEntry.cs`
Struct: `KnownItemEntry`
Fields:
- `string ItemId`
- `int BasePrice`

---

## 3.3 Skills

### File: `Skills/SkillId.cs`
Enum-like constants (MVP):
- Sales
- Negotiation

Implementation:
- Use string IDs to match persistence schema (Sales, Negotiation)

### File: `Skills/SkillEntry.cs`
Struct: `SkillEntry`
Fields:
- `string Id`
- `int Level`
- `int Xp`

---

# 4. Bootstrap / Scene Wiring

## 4.1 GameBootstrapper

### File: `Bootstrap/GameBootstrapper.cs`
Class: `GameBootstrapper : MonoBehaviour`
Scene: First loaded scene
Responsibilities:
- Loads databases (ItemDatabase, RecipeDatabase)
- Provides global service locator (MVP) OR references via serialized fields
- Starts/ensures NGO NetworkManager exists

Dependencies:
- NetworkManager
- ItemDatabase
- RecipeDatabase

---

# 5. Networking Root Objects

## 5.1 PlayerNetworkRoot

### File: `Players/PlayerNetworkRoot.cs`
Class: `PlayerNetworkRoot : NetworkBehaviour`
Attached to: Player prefab root (NetworkObject)
Responsibilities:
- Holds references to player sub-components:
  - WalletNet
  - SkillsNet
  - InventoryNet
  - KnownItemsNet
- Exposes PlayerKey for persistence

Public API:
- `string PlayerKey { get; }` (server-assigned)

RPC:
- None directly (delegated to sub-components)

---

# 6. Player State Components (Networked)

## 6.1 WalletNet

### File: `Players/WalletNet.cs`
Class: `WalletNet : NetworkBehaviour`
Attached to: Player prefab (child ok)
Responsibilities:
- Server-authoritative coin balance
- Provides methods to add/subtract coins
- Integrates with persistence

Public API (Server):
- `int Coins { get; }`
- `bool TrySpend(int amount)`
- `void AddCoins(int amount)`

Replication:
- Part of PlayerStateSnapshot (see PlayerStateSync)

---

## 6.2 SkillsNet

### File: `Skills/SkillsNet.cs`
Class: `SkillsNet : NetworkBehaviour`
Responsibilities:
- Stores skill entries (Sales, Negotiation)
- Applies XP and level-up curve

Public API (Server):
- `SkillEntry Get(string id)`
- `void AddXp(string id, int amount)`

XP Curve (from GDD):
- XP needed = 10 * (level + 1)
- XP per successful transaction: +1

---

## 6.3 KnownItemsNet

### File: `Players/KnownItemsNet.cs`
Class: `KnownItemsNet : NetworkBehaviour`
Responsibilities:
- Tracks known items and base prices
- Creates entry when item first acquired

Public API (Server):
- `bool IsKnown(string itemId)`
- `int GetBasePriceOrDefault(string itemId, int defaultPrice = 1)`
- `void EnsureKnown(string itemId)`
- `bool TrySetBasePrice(string itemId, int basePrice)`

Client Requests:
- ServerRpc to change base price

RPC:
- `RequestSetBasePriceServerRpc(string itemId, int basePrice)`

---

## 6.4 PlayerInventoryNet

### File: `Inventory/PlayerInventoryNet.cs`
Class: `PlayerInventoryNet : NetworkBehaviour`
Responsibilities:
- Owns authoritative InventoryGrid for player
- Applies inventory mutation requests via ServerRpc
- Sends inventory snapshots to owner client

Public API (Server):
- `InventoryGrid Grid { get; }`
- `void ForceSendSnapshotToOwner()`

RPC:
- `RequestMoveSlotServerRpc(int fromIndex, int toIndex)`
- `RequestSplitStackServerRpc(int index, int splitAmount)` (optional MVP)

ClientRpc:
- `ReceiveInventorySnapshotClientRpc(InventorySnapshot snapshot)` (owner only)

Dependencies:
- ItemDatabase
- KnownItemsNet (EnsureKnown when items added)

---

# 7. Vendor System

## 7.1 VendorChestNet

### File: `Vendors/VendorChestNet.cs`
Class: `VendorChestNet : NetworkBehaviour`
Attached to: VendorChest prefab root (NetworkObject)
Responsibilities:
- Owns chest InventoryGrid
- Broadcasts chest snapshots

Public API (Server):
- `InventoryGrid Grid { get; }`
- `void ForceBroadcastSnapshot()`

ClientRpc:
- `ReceiveChestSnapshotClientRpc(InventorySnapshot snapshot)`

---

## 7.2 VendorInteractable

### File: `Vendors/VendorInteractable.cs`
Class: `VendorInteractable : NetworkBehaviour`
Attached to: Vendor NPC prefab (NetworkObject)
Responsibilities:
- Handles interaction requests
- Returns chest snapshot
- Routes checkout

RPC:
- `RequestOpenVendorServerRpc()`
- `RequestCheckoutServerRpc(CheckoutRequest request)`

Dependencies:
- VendorChestNet
- VendorTransactionService

---

## 7.3 VendorTransactionService (Server-only)

### File: `Vendors/VendorTransactionService.cs`
Class: `VendorTransactionService` (pure C#)
Responsibilities:
- Implements atomic checkout algorithm
- Performs validations
- Applies wallet changes, inventory changes, skill XP

Public API:
- `TransactionResult TryCheckout(PlayerNetworkRoot buyer, VendorContext vendor, CheckoutRequest request)`

Dependencies:
- ItemDatabase
- KnownItemsNet
- PlayerInventoryNet
- WalletNet
- SkillsNet

---

# 8. Harvesting System

## 8.1 ResourceNodeRegistry

### File: `Harvesting/ResourceNodeRegistry.cs`
Class: `ResourceNodeRegistry : MonoBehaviour`
Responsibilities:
- Maintains runtime registry of resource nodes
- Provides stable node ids

Public API:
- `bool TryGet(string nodeId, out ResourceNode node)`

---

## 8.2 ResourceNode

### File: `Harvesting/ResourceNode.cs`
Class: `ResourceNode : MonoBehaviour`
Responsibilities:
- Represents harvestable node in scene
- Has nodeId
- Has drop definition (itemId + quantity)
- Has respawn timer values

Server-only state:
- harvestable / cooldown tracking

---

## 8.3 HarvestingNet

### File: `Harvesting/HarvestingNet.cs`
Class: `HarvestingNet : NetworkBehaviour`
Attached to: Player prefab
Responsibilities:
- Receives harvest requests
- Validates and awards drops server-side

RPC:
- `RequestHarvestServerRpc(string nodeId)`

ClientRpc:
- `HarvestResultClientRpc(HarvestResult result)`

Dependencies:
- ResourceNodeRegistry
- PlayerInventoryNet
- KnownItemsNet

---

# 9. Crafting System

## 9.1 CraftingNet

### File: `Crafting/CraftingNet.cs`
Class: `CraftingNet : NetworkBehaviour`
Attached to: Player prefab
Responsibilities:
- Receives craft requests
- Validates ingredients
- Mutates inventory server-side

RPC:
- `RequestCraftServerRpc(string recipeId, int craftCount)`

ClientRpc:
- `CraftResultClientRpc(CraftResult result)`

Dependencies:
- RecipeDatabase
- PlayerInventoryNet
- KnownItemsNet

---

# 10. Building System (MVP Minimal)

## 10.1 BuildPieceDef

### File: `Building/BuildPieceDef.cs`
Class: `BuildPieceDef : ScriptableObject`
Responsibilities:
- buildPieceId
- prefab reference

---

## 10.2 BuildingNet

### File: `Building/BuildingNet.cs`
Class: `BuildingNet : NetworkBehaviour`
Attached to: Player prefab
Responsibilities:
- Receives place/remove build piece requests
- Spawns build pieces as server-owned NetworkObjects
- Updates shard save model

RPC:
- `RequestPlaceBuildPieceServerRpc(string buildPieceId, Vector3 pos, float rotY)`

Dependencies:
- BuildPieceDatabase (future) OR serialized list
- ShardSaveService

---

# 11. Shelter + Vendor Spawn Logic

## 11.1 ShelterState

### File: `Building/ShelterState.cs`
Class: `ShelterState : NetworkBehaviour`
Attached to: Shelter root object (NetworkObject) OR scene singleton
Responsibilities:
- Tracks whether shelter is complete
- Spawns VendorNPC + VendorChest when complete

Public API (Server):
- `void MarkComplete(string ownerPlayerKey)`

---

# 12. Persistence Services

## 12.1 SaveManager

### File: `Persistence/SaveManager.cs`
Class: `SaveManager : MonoBehaviour`
Responsibilities:
- Server-only coordinator for save/load
- Periodic autosave tick
- Orchestrates PlayerSaveService and ShardSaveService

---

## 12.2 PlayerSaveService

### File: `Persistence/PlayerSaveService.cs`
Class: `PlayerSaveService`
Responsibilities:
- Load/save Player Save JSON
- Apply to player components (WalletNet, SkillsNet, KnownItemsNet, PlayerInventoryNet)

---

## 12.3 ShardSaveService

### File: `Persistence/ShardSaveService.cs`
Class: `ShardSaveService`
Responsibilities:
- Load/save Shard Save JSON
- Apply to world (build pieces, shelters, vendor chest contents)

---

# 13. UI Scripts (MVP Minimal)

UI is client-only. UI never mutates state directly.

## 13.1 InventoryUI

### File: `UI/Inventory/InventoryWindowUI.cs`
Class: `InventoryWindowUI : MonoBehaviour`
Responsibilities:
- Renders owner inventory snapshot
- Sends drag/move requests to PlayerInventoryNet

---

## 13.2 KnownItemsUI

### File: `UI/KnownItems/KnownItemsWindowUI.cs`
Class: `KnownItemsWindowUI : MonoBehaviour`
Responsibilities:
- Shows known items + base prices
- Sends base price changes via KnownItemsNet ServerRpc

---

## 13.3 VendorUI

### File: `UI/Vendor/VendorWindowUI.cs`
Class: `VendorWindowUI : MonoBehaviour`
Responsibilities:
- Displays chest snapshot
- Allows cart selection
- Sends checkout request to VendorInteractable

---

# 14. Network DTOs (Snapshots + Requests)

DTOs are serializable structs compatible with NGO.

## 14.1 InventorySnapshot

### File: `Networking/DTO/InventorySnapshot.cs`
Struct: `InventorySnapshot`
Fields:
- `int W`
- `int H`
- `SlotDto[] Slots`

SlotDto is:
- `bool IsEmpty`
- `string ItemId`
- `int Quantity`

---

## 14.2 CheckoutRequest

### File: `Networking/DTO/CheckoutRequest.cs`
Struct: `CheckoutRequest`
Fields:
- `CheckoutLine[] Lines`

CheckoutLine:
- `int SlotIndex`
- `int Quantity`

---

## 14.3 Results

### File: `Networking/DTO/ActionResults.cs`
Structs:
- `TransactionResult`
- `HarvestResult`
- `CraftResult`

Includes:
- Success bool
- FailureReason enum

---

# 15. Required Prefabs (MVP)

- Player prefab:
  - NetworkObject
  - PlayerNetworkRoot
  - WalletNet
  - SkillsNet
  - KnownItemsNet
  - PlayerInventoryNet
  - HarvestingNet
  - CraftingNet
  - BuildingNet

- VendorChest prefab:
  - NetworkObject
  - VendorChestNet

- VendorNPC prefab:
  - NetworkObject
  - VendorInteractable

- BuildPiece prefabs:
  - NetworkObject
  - (optional) marker script

---

END OF SYSTEM_SCRIPT_MAP v0.1

