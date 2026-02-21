using System.Collections.Generic;
using HuntersAndCollectors.Networking.DTO;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace HuntersAndCollectors.Vendors.UI
{
    /// <summary>
    /// VendorWindowUI
    /// --------------------------------------------------------------------
    /// Real vendor window (uGUI).
    ///
    /// Responsibilities:
    /// - Find/assign VendorInteractable + VendorChestNet
    /// - Subscribe to chest snapshot updates
    /// - Render a list of rows (item name + qty + buy buttons)
    /// - On Buy: send CheckoutRequest (slotIndex + qty) via VendorInteractable ServerRpc
    ///
    /// Notes:
    /// - We never trust the client for price or itemId.
    /// - We send ONLY slot index + desired quantity (server validates).
    /// </summary>
    public sealed class VendorWindowUI : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("VendorInteractable in the scene. If empty, we will try to FindObjectOfType.")]
        [SerializeField] private VendorInteractable vendorInteractable;

        [Tooltip("VendorChestNet used by the vendor. If empty, we will try to resolve from VendorInteractable.")]
        [SerializeField] private VendorChestNet vendorChest;

        [Header("UI References")]
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private Button closeButton;

        [Tooltip("Parent transform where row prefabs will be spawned (e.g., ScrollView/Viewport/Content).")]
        [SerializeField] private Transform contentRoot;

        [Tooltip("Row prefab with VendorRowUI + TMP + Buttons.")]
        [SerializeField] private VendorRowUI rowPrefab;

        // Keep spawned rows so we can reuse/clear them on refresh.
        private readonly List<VendorRowUI> rows = new();

        private void Awake()
        {
            if (closeButton)
                closeButton.onClick.AddListener(Close);

            // Start closed by default (optional)
            // gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            TryResolveRefs();

            // Subscribe to snapshot changes so UI updates when stock changes.
            if (vendorChest != null)
                vendorChest.OnSnapshotChanged += HandleSnapshotChanged;

            // If we already have a cached snapshot, render immediately.
            if (vendorChest != null && vendorChest.LastSnapshot.Slots != null)
                Render(vendorChest.LastSnapshot);

            // Ask server for the latest snapshot when we open the window.
            RequestOpenVendorSnapshot();
        }

        private void OnDisable()
        {
            if (vendorChest != null)
                vendorChest.OnSnapshotChanged -= HandleSnapshotChanged;
        }

        private void TryResolveRefs()
        {
            // Resolve VendorInteractable if not assigned.
            if (vendorInteractable == null)
                vendorInteractable = FindObjectOfType<VendorInteractable>();

            // Resolve VendorChestNet if not assigned.
            // VendorInteractable has a [SerializeField] vendorChest, but it is private,
            // so we can either assign it manually in inspector OR FindObjectOfType.
            if (vendorChest == null)
                vendorChest = FindObjectOfType<VendorChestNet>();

            if (titleText)
                titleText.text = "Vendor";
        }

        /// <summary>
        /// Call this when the player opens the vendor (later: via proximity interact).
        /// For now, it just ensures server sends snapshots.
        /// </summary>
        private void RequestOpenVendorSnapshot()
        {
            if (vendorInteractable == null)
            {
                Debug.LogWarning("[VendorWindowUI] No VendorInteractable found to request snapshot.");
                return;
            }

            // Only clients should request server snapshot.
            if (!NetworkManager.Singleton || !NetworkManager.Singleton.IsClient)
                return;

            vendorInteractable.RequestOpenVendorServerRpc();
        }

        private void HandleSnapshotChanged(InventorySnapshot snapshot)
        {
            Render(snapshot);
        }

        private void Render(InventorySnapshot snapshot)
        {
            if (contentRoot == null || rowPrefab == null)
                return;

            // Clear existing rows (simple MVP approach).
            // Later we can pool them for performance.
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i] != null)
                    Destroy(rows[i].gameObject);
            }
            rows.Clear();

            if (snapshot.Slots == null)
                return;

            // Create a row for every non-empty slot.
            for (int i = 0; i < snapshot.Slots.Length; i++)
            {
                var slot = snapshot.Slots[i];
                if (slot.IsEmpty)
                    continue;

                var itemId = slot.ItemId.ToString();
                var qty = slot.Quantity;

                // Ask VendorChestNet for a friendly name (fallback to itemId).
                var displayName = vendorChest != null
                    ? vendorChest.GetDisplayName(itemId)
                    : itemId;

                var row = Instantiate(rowPrefab, contentRoot);
                rows.Add(row);

                // Capture slot index for button callbacks.
                var slotIndex = i;

                row.Bind(
                    slotIndex,
                    $"{displayName}",
                    qty,
                    onBuy1: () => SendBuy(slotIndex, 1),
                    onBuy5: () => SendBuy(slotIndex, 5)
                );
            }
        }

        private void SendBuy(int slotIndex, int quantity)
        {
            if (vendorInteractable == null)
            {
                Debug.LogWarning("[VendorWindowUI] Cannot buy - VendorInteractable missing.");
                return;
            }

            if (!NetworkManager.Singleton || !NetworkManager.Singleton.IsClient)
                return;

            // Build the checkout request with ONE line.
            var req = new CheckoutRequest
            {
                Lines = new[]
                {
                    new CheckoutRequest.CheckoutLine
                    {
                        SlotIndex = slotIndex,
                        Quantity = Mathf.Max(1, quantity)
                    }
                }
            };

            vendorInteractable.RequestCheckoutServerRpc(req);
        }

        public void Open()
        {
            gameObject.SetActive(true);
        }

        public void Close()
        {
            gameObject.SetActive(false);
        }
    }
}
