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

        [Tooltip("VendorChestNet used by the vendor. If empty, we will try to resolve from VendorInteractable.")]
        [SerializeField] private VendorChestNet vendorChest;

        [Header("UI References")]
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private Button closeButton;

        [Tooltip("Parent transform where row prefabs will be spawned (e.g., ScrollView/Viewport/Content).")]
        [SerializeField] private Transform contentRoot;

        [Tooltip("Row prefab with VendorRowUI + TMP + Buttons.")]
        [SerializeField] private VendorRowUI rowPrefab;

        // Keeps track of which vendor we are currently talking to.
        private VendorInteractable currentVendor;

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
        }

        private void OnDisable()
        {
            if (vendorChest != null)
                vendorChest.OnSnapshotChanged -= HandleSnapshotChanged;

            currentVendor = null;
            vendorChest = null;

            gameObject.SetActive(false);
        }

        /// <summary>
        /// Opens the vendor window and binds it to a specific VendorInteractable.
        /// This is IMPORTANT: the UI must know which vendor to call RPCs on.
        /// </summary>
        public void Open(VendorInteractable vendor)
        {
            if (vendor == null)
            {
                Debug.LogWarning("[VendorWindowUI] Open() called with null vendor.");
                return;
            }

            currentVendor = vendor;

            // Bind to the exact chest for this vendor.
            vendorChest = vendor.Chest;

            if (vendorChest == null)
            {
                Debug.LogWarning("[VendorWindowUI] Vendor has no VendorChestNet.", vendor);
                return;
            }

            // Subscribe AFTER we know the right chest.
            vendorChest.OnSnapshotChanged += HandleSnapshotChanged;

            // Show UI
            gameObject.SetActive(true);

            // Ask server for the snapshot
            currentVendor.RequestOpenVendorServerRpc();
        }

        /// <summary>
        /// Called when UI wants the server to send us the current vendor stock.
        /// </summary>
        private void RequestOpenVendorSnapshot()
        {
            if (currentVendor == null)
            {
                Debug.LogWarning("[VendorWindowUI] No current vendor bound. Did you call Open(vendor)?");
                return;
            }

            // This RPC runs on the vendor NetworkBehaviour.
            currentVendor.RequestOpenVendorServerRpc();
        }

        // Optional: When closing, clear the vendor reference.
        public void Close()
        {
            if (vendorChest != null)
                vendorChest.OnSnapshotChanged -= HandleSnapshotChanged;

            currentVendor = null;
            vendorChest = null;

            gameObject.SetActive(false);
        }

        private void TryResolveRefs()
        {
            // Resolve VendorInteractable if not assigned.
            if (currentVendor == null)
                currentVendor = FindObjectOfType<VendorInteractable>();

            // Resolve VendorChestNet if not assigned.
            // VendorInteractable has a [SerializeField] vendorChest, but it is private,
            // so we can either assign it manually in inspector OR FindObjectOfType.
            if (vendorChest == null)
                vendorChest = FindObjectOfType<VendorChestNet>();

            if (titleText)
                titleText.text = "Vendor";
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

            var currentChest = vendorChest;

            // Create a row for every non-empty slot.
            for (int i = 0; i < snapshot.Slots.Length; i++)
            {
                var slot = snapshot.Slots[i];
                if (slot.IsEmpty)
                    continue;

                var itemId = slot.ItemId.ToString();
                var qty = slot.Quantity;

                // Ask VendorChestNet for a friendly name (fallback to itemId).
                var displayName = currentChest != null
                    ? currentChest.GetDisplayName(itemId)
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
            if (currentVendor == null)
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

            if (!currentVendor.IsSpawned)
            {
                var hasNetObj = currentVendor.NetworkObject != null;
                var netIdText = hasNetObj ? currentVendor.NetworkObjectId.ToString() : "n/a";
                Debug.LogWarning($"[VendorWindowUI] Vendor is not spawned. name={currentVendor.gameObject.name} netId={netIdText}", currentVendor);
            }

            currentVendor.RequestCheckoutServerRpc(req);
        }
    }
}
