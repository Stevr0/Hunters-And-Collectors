# DTO_SERIALIZATION_SPEC.md — Hunters & Collectors (AUTHORITATIVE)

Version: 0.1  
Status: REQUIRED FOR CODEX GENERATION  
Engine Target: Unity  
Networking: Netcode for GameObjects (NGO)  
Authority: Server-authoritative

---

# 0. Purpose

This document defines EXACT serialization rules for all Network DTOs used in:

- Inventory snapshots
- Checkout requests
- Action results

This prevents NGO runtime serialization errors and ensures Codex implements compatible network payloads.

If serialization conflicts with NGO constraints, THIS document overrides earlier loose assumptions.

---

# 1. NGO Serialization Strategy (LOCKED)

All DTO structs MUST:

- Implement `INetworkSerializable`
- Avoid managed arrays where possible
- Avoid nested dynamic lists
- Avoid UnityEngine.Object references
- Avoid nullable types

Strings must use NGO-compatible serialization.

---

# 2. String Serialization Rule (LOCKED)

## 2.1 Use FixedString

All ItemIds, SkillIds, and other identifiers in network DTOs MUST use:

- `FixedString64Bytes` (Unity.Collections)

NOT regular `string`.

Reason:
- NGO serializes FixedString efficiently and deterministically.
- Prevents heap allocation + GC spikes.

Conversion rule:
- At DTO boundary: convert between string <-> FixedString64Bytes
- Internal runtime logic may still use string.

---

# 3. InventorySnapshot DTO

### File:
`Networking/DTO/InventorySnapshot.cs`

### Struct Definition (Authoritative Shape)

Fields:

- `int W`
- `int H`
- `SlotDto[] Slots`

However, NGO constraint:

Slots MUST be serialized manually using `INetworkSerializable`.

---

## 3.1 SlotDto

Fields:

- `bool IsEmpty`
- `FixedString64Bytes ItemId`
- `int Quantity`

Rules:
- If IsEmpty == true:
  - ItemId must be default
  - Quantity = 0
- If IsEmpty == false:
  - Quantity >= 1

---

## 3.2 Serialization Implementation Pattern

InventorySnapshot MUST implement:

```csharp
public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
```

Serialization order (LOCKED):

1) W
2) H
3) slotCount (int)
4) Each SlotDto serialized sequentially

SlotDto serialization order:

1) IsEmpty
2) ItemId
3) Quantity

Order must never change.

---

# 4. CheckoutRequest DTO

### File:
`Networking/DTO/CheckoutRequest.cs`

### Struct Shape

Fields:

- `CheckoutLine[] Lines`

---

## 4.1 CheckoutLine

Fields:

- `int SlotIndex`
- `int Quantity`

Rules:
- Quantity >= 1
- SlotIndex >= 0

---

## 4.2 Serialization Pattern

CheckoutRequest implements `INetworkSerializable`.

Serialization order:

1) lineCount (int)
2) For each line:
   - SlotIndex
   - Quantity

No itemId is transmitted.
Server derives itemId from authoritative chest snapshot.

---

# 5. Action Result DTOs

### File:
`Networking/DTO/ActionResults.cs`

Must include:

- `TransactionResult`
- `HarvestResult`
- `CraftResult`

All must implement `INetworkSerializable`.

---

## 5.1 FailureReason Enum

Enum underlying type: `byte`

Reason:
- Minimizes network payload size
- Deterministic representation

---

## 5.2 TransactionResult

Fields:

- `bool Success`
- `FailureReason Reason`

Optional (MVP allowed but not required):
- `int TotalPrice`

Serialization order:

1) Success
2) Reason
3) TotalPrice (if included)

---

## 5.3 HarvestResult

Fields:

- `bool Success`
- `FailureReason Reason`

Serialization order:

1) Success
2) Reason

---

## 5.4 CraftResult

Fields:

- `bool Success`
- `FailureReason Reason`

Serialization order:

1) Success
2) Reason

---

# 6. Snapshot Transport Rules

## 6.1 InventorySnapshot RPC

Must use:

```csharp
[ClientRpc]
void ReceiveInventorySnapshotClientRpc(InventorySnapshot snapshot)
```

Player inventory snapshot:
- Sent only to owner client (ClientRpcParams)

Vendor chest snapshot:
- Broadcast to all clients (MVP simplification)

---

# 7. Capacity Limits (MVP LOCK)

To prevent excessive payload size:

- Player inventory max slots: 64
- Vendor chest max slots: 64
- Checkout max lines: 32

Codex must enforce these limits.

---

# 8. Validation Requirements

On server before serialization:

- Slot count must equal W * H
- No negative quantities
- No invalid slot indices

On client after deserialization:

- Snapshot must fully replace local UI cache
- No client-side mutation allowed

---

# 9. Out of Scope (MVP)

- Delta compression
- Bit-packing
- Custom FastBufferWriter manual optimization
- Encryption

---

# 10. Codex Enforcement Rule

When generating DTOs:

- Always implement `INetworkSerializable`
- Always follow exact serialization order specified
- Never rely on automatic NGO serialization of arrays
- Never use `string` in network DTOs (use FixedString64Bytes)

If NGO incompatibility occurs:
- STOP and report per CODEX_MASTER_PROMPT Stop Conditions

---

END OF DTO_SERIALIZATION_SPEC v0.1

