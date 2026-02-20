# CODEX_MASTER_PROMPT.md — Hunters & Collectors (AUTHORITATIVE)

Version: 0.1  
Status: READY FOR CODEX (After Lock)  
Engine Target: Unity  
Networking: Netcode for GameObjects (NGO)  
Authority: Server-authoritative  
MVP Runtime: Host (Listen Server)

---

# 0. How to Use

1) Ensure these specs are present and treated as authoritative:
   - GDD v1.0
   - DATA_MODEL_SPEC.md v0.1
   - NETWORK_AUTHORITY_SPEC.md v0.1
   - PERSISTENCE_SPEC.md v0.1
   - SYSTEM_SCRIPT_MAP.md v0.1

2) Paste this prompt into Codex.

3) Codex must generate ONLY the scripts listed in SYSTEM_SCRIPT_MAP.md, and implement them exactly to spec.

---

# 1. CODING CONSTRAINTS (NON-NEGOTIABLE)

## 1.1 Do Not Invent Architecture

- Do NOT add new gameplay systems.
- Do NOT rename classes or files.
- Do NOT change public APIs.
- Do NOT introduce alternative data models.

If something is missing, implement the simplest stub that compiles and is clearly marked TODO.

## 1.2 Unity + NGO Constraints

- All NetworkBehaviours must guard server-only logic using `IsServer` / `IsHost`.
- All ServerRpc methods must set `RequireOwnership = true` when appropriate (player-owned actions).
- All client requests MUST be validated server-side.
- Clients never write persistence.

## 1.3 Commenting Style

- Include detailed XML doc comments on every public class and public method.
- Include inline comments explaining intent and validation.
- Add "Editor wiring checklist" comments at the top of MonoBehaviours.

## 1.4 Determinism + Safety

- Never trust client-provided inventory state.
- Validate indices, quantities, and IDs.
- Prevent negative coins.
- Clamp invalid save data per PERSISTENCE_SPEC.

---

# 2. GENERATION OUTPUT REQUIREMENTS

Codex must output:

1) A file tree listing of all created scripts (paths).
2) The full source code for each file.
3) A short wiring checklist for prefabs and ScriptableObjects.

Do not output Unity scene files or prefabs.

---

# 3. BUILD ORDER (MUST FOLLOW)

Implement in this exact order to avoid circular dependencies:

1) Shared data + databases
   - Items/ItemCategory.cs
   - Items/ItemDef.cs
   - Items/ItemDatabase.cs
   - Crafting/RecipeDef.cs
   - Crafting/RecipeDatabase.cs

2) Inventory core
   - Inventory/ItemStack.cs
   - Inventory/InventorySlot.cs
   - Inventory/InventoryGrid.cs

3) Skills + Known Items data
   - Skills/SkillEntry.cs
   - Skills/SkillId.cs
   - Players/KnownItemEntry.cs

4) Networking DTOs
   - Networking/DTO/InventorySnapshot.cs
   - Networking/DTO/CheckoutRequest.cs
   - Networking/DTO/ActionResults.cs

5) Player network components
   - Players/PlayerNetworkRoot.cs
   - Players/WalletNet.cs
   - Skills/SkillsNet.cs
   - Players/KnownItemsNet.cs
   - Inventory/PlayerInventoryNet.cs

6) Vendor system
   - Vendors/VendorChestNet.cs
   - Vendors/VendorTransactionService.cs
   - Vendors/VendorInteractable.cs

7) Harvesting system
   - Harvesting/ResourceNodeRegistry.cs
   - Harvesting/ResourceNode.cs
   - Harvesting/HarvestingNet.cs

8) Crafting system
   - Crafting/CraftingNet.cs

9) Building + Shelter
   - Building/BuildPieceDef.cs
   - Building/BuildingNet.cs
   - Building/ShelterState.cs

10) Persistence
   - Persistence/SaveManager.cs
   - Persistence/PlayerSaveService.cs
   - Persistence/ShardSaveService.cs

11) UI (minimal, compile-safe)
   - UI/Inventory/InventoryWindowUI.cs
   - UI/KnownItems/KnownItemsWindowUI.cs
   - UI/Vendor/VendorWindowUI.cs

12) Bootstrap
   - Bootstrap/GameBootstrapper.cs

---

# 4. IMPLEMENTATION DETAILS (MUST MATCH SPECS)

## 4.1 DATA MODEL

Follow DATA_MODEL_SPEC.md exactly:

- ItemId is a stable string.
- ItemStack = (ItemId, Quantity)
- InventoryGrid = width/height + slots
- VendorChest uses same InventoryGrid
- KnownItems map ItemId -> BasePrice (default 1)
- Currency is int coins

## 4.2 NETWORK AUTHORITY

Follow NETWORK_AUTHORITY_SPEC.md exactly:

- Server is truth, clients request.
- Inventories replicate via snapshots.
- Player inventory snapshots go to owner only.
- Vendor chest snapshots broadcast to all clients (MVP simplification).
- All mutations are server-side.

## 4.3 PERSISTENCE

Follow PERSISTENCE_SPEC.md exactly:

- JSON format
- Server-only write
- File layout under Application.persistentDataPath/HuntersAndCollectors/Saves
- schemaVersion = 1
- Validate on load; clamp or drop invalid entries

## 4.4 VENDOR CHECKOUT (ATOMIC)

Implement checkout rules:

Inputs:
- Cart lines reference chest slotIndex + quantity

Server derives:
- itemId and available quantity from chest snapshot

Price:
- unit price = buyer KnownItems base price for ItemId
- unknown item => unit price 1 AND item becomes known

Validation order:
1) vendor exists
2) slot indices valid and non-empty
3) quantities <= available
4) buyer inventory has space for all items
5) buyer coins >= total

Apply atomically:
- subtract buyer coins
- add seller coins (vendor owner playerKey)
- remove from chest
- add to buyer inventory
- XP:
  - buyer Negotiation +1 per successful checkout
  - seller Sales +1 per successful checkout

On failure:
- no state changes
- return FailureReason

---

# 5. PREFAB / EDITOR WIRING CHECKLIST (MVP)

## 5.1 ScriptableObjects

Create assets:

- ItemDatabase asset:
  - Add ItemDef assets for MVP item IDs

- RecipeDatabase asset:
  - Add RecipeDef assets for Stone Axe and Wooden Club

## 5.2 Player Prefab

Must include:

- NetworkObject
- PlayerNetworkRoot
- WalletNet
- SkillsNet
- KnownItemsNet
- PlayerInventoryNet
- HarvestingNet
- CraftingNet
- BuildingNet

## 5.3 Vendor Prefabs

- VendorChest prefab:
  - NetworkObject
  - VendorChestNet

- VendorNPC prefab:
  - NetworkObject
  - VendorInteractable
  - Reference to its VendorChestNet

## 5.4 Scene Singletons

- GameBootstrapper in first scene
- SaveManager in server scene
- ResourceNodeRegistry in any scene with harvest nodes

---

# 6. TEST / VERIFICATION CHECKLIST (MVP)

Codex must ensure these runtime flows exist and compile:

1) Host start → spawns player → player has coins and empty inventory
2) Harvest request adds items to player inventory (server validated)
3) Craft request consumes ingredients and produces output
4) Known items auto-register on first acquisition
5) Vendor chest snapshot shows items placed in chest
6) Checkout transfers items + coins atomically, applies XP
7) Save/Load persists coins, skills, known items, inventories, chest contents

---

# 7. STOP CONDITIONS

If any of the following occurs, STOP and report:

- A spec conflict is detected
- A required class/file is missing from SYSTEM_SCRIPT_MAP
- NGO cannot serialize a DTO as designed (then propose minimal adjustment)

Never silently change the spec.

---

# 8. BEGIN IMPLEMENTATION

Generate the scripts now, following the build order.

Output:
1) File tree
2) All code files
3) Wiring checklist

---

END OF CODEX_MASTER_PROMPT v0.1

