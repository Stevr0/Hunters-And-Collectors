# PERSISTENCE_SPEC.md — Hunters & Collectors (AUTHORITATIVE)

Version: 0.1  
Status: DRAFT (Ready to Lock)  
Engine Target: Unity  
Persistence Authority: Server-only writes  
Format: JSON (MVP)  
Save Location: Unity `Application.persistentDataPath`

---

# 0. Purpose

Defines the authoritative persistence model for Hunters & Collectors MVP:

- What is saved
- Where it is saved
- File naming and layout
- JSON schemas (exact)
- When saves occur
- Versioning and migration rules

Codex must implement persistence exactly as specified.

---

# 1. Persistence Authority (LOCKED)

## 1.1 Server Writes Only

- Only the server/host writes save files.
- Clients never write save data.
- Clients may cache UI state locally, but not game progression.

## 1.2 Load Happens on Server

- On shard start, server loads shard save.
- On player join, server loads that player save.

---

# 2. Save Domains (LOCKED)

There are two save domains:

1) Player Save (per account/player identity)
2) Shard Save (world state for the current shard)

---

# 3. Identity Keys (MVP)

## 3.1 PlayerKey

MVP PlayerKey format:
- `Client_{OwnerClientId}` (e.g., Client_0)

Future:
- Replace with AccountId from login/steam

Rule:
- Persistence code must treat PlayerKey as an abstract string.

---

## 3.2 ShardKey

MVP ShardKey format:
- `Shard_Default`

Future:
- Replace with per-shard seed or shard id

Rule:
- Persistence code must treat ShardKey as an abstract string.

---

# 4. File Layout (LOCKED)

Root folder:
- `Application.persistentDataPath/HuntersAndCollectors/Saves/`

Subfolders:
- `Players/`
- `Shards/`

File naming:
- Player: `Players/{PlayerKey}.json`
- Shard:  `Shards/{ShardKey}.json`

Examples:
- `.../Saves/Players/Client_0.json`
- `.../Saves/Shards/Shard_Default.json`

---

# 5. Versioning Rules (LOCKED)

Every save file MUST contain:

- `schemaVersion` (int)

MVP schemaVersion:
- 1

Migration:
- If schemaVersion < current, server attempts migration.
- If migration is not supported, server archives the file and creates a fresh save.

Archive rule:
- Move old file to same folder with suffix `.bak_{timestamp}`

---

# 6. JSON Schemas (EXACT)

All JSON must be UTF-8.
All numeric values are integers.

---

## 6.1 Shared Schema: InventoryGrid

InventoryGrid is serialized as:

```json
{
  "w": 6,
  "h": 4,
  "slots": [
    null,
    {"id": "IT_Wood", "q": 12},
    null,
    {"id": "IT_Stone", "q": 5}
  ]
}
```

Rules:
- `slots.length` MUST equal `w * h`
- Each slot is either:
  - `null` (empty)
  - `{ "id": string, "q": int }`

Validation:
- q >= 1
- id must be known ItemDef at runtime or rejected (see Load Validation)

---

## 6.2 Player Save Schema (schemaVersion = 1)

```json
{
  "schemaVersion": 1,
  "playerKey": "Client_0",
  "wallet": {
    "coins": 100
  },
  "skills": [
    {"id": "Sales", "lvl": 0, "xp": 0},
    {"id": "Negotiation", "lvl": 0, "xp": 0}
  ],
  "knownItems": [
    {"id": "IT_Wood", "base": 1},
    {"id": "IT_Stone", "base": 1}
  ],
  "inventory": {
    "w": 6,
    "h": 4,
    "slots": []
  }
}
```

### Field Notes
- `playerKey` must match file key.
- `wallet.coins` >= 0
- `skills[].id` are string identifiers (MVP: Sales, Negotiation)
- `knownItems[].base` >= 0

---

## 6.3 Shard Save Schema (schemaVersion = 1)

```json
{
  "schemaVersion": 1,
  "shardKey": "Shard_Default",
  "shelters": [
    {
      "shelterId": "SHELTER_001",
      "isComplete": true,
      "vendor": {
        "vendorId": "VENDOR_001",
        "ownerPlayerKey": "Client_0",
        "chest": {
          "w": 4,
          "h": 4,
          "slots": []
        }
      }
    }
  ],
  "buildPieces": [
    {
      "id": "BP_Floor",
      "pos": {"x": 0, "y": 0, "z": 0},
      "rotY": 0
    }
  ]
}
```

### Field Notes
- `shelterId` / `vendorId` are stable world identifiers.
- `ownerPlayerKey` is used to route seller wallet updates.
- `buildPieces` are stored as a lightweight placement list.

---

# 7. Save Timing (MVP LOCK)

## 7.1 Save Triggers

Server saves player data when:
- Player disconnects
- Successful checkout completes (buyer and seller)
- Successful craft completes
- Successful harvest completes
- Periodic autosave tick

Server saves shard data when:
- Build piece placed/removed
- ShelterComplete changes
- Vendor chest contents change
- Periodic autosave tick

## 7.2 Autosave

- Autosave interval: 60 seconds
- Autosave runs on server only

---

# 8. Load Validation Rules (LOCKED)

On load, server validates:

- schemaVersion supported
- inventory slot counts match w*h
- item ids exist in ItemDef registry
- quantities are within MaxStack
- coins >= 0
- base prices >= 0

If invalid data is detected:
- Clamp safe values where possible (e.g., quantity to MaxStack)
- Remove unknown items (slot becomes null)
- Log warnings

If file is severely corrupted:
- Archive and create fresh save

---

# 9. Determinism and Safety

- Save/Load must be idempotent.
- No client-provided data is trusted.
- All persistence operations occur on server main thread context (Unity safety).

---

END OF PERSISTENCE_SPEC v0.1

