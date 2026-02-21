using System.Collections.Generic;
using HuntersAndCollectors.Input;
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
    /// Clean, deterministic vendor UI implementation.
    ///
    /// Responsibilities:
    /// - Bind to a specific VendorInteractable
    /// - Subscribe/unsubscribe to chest snapshot updates
    /// - Render vendor stock rows
    /// - Send CheckoutRequest via ServerRpc
    /// - Lock/unlock gameplay input while active
    ///
    /// Rules:
    /// - Lock gameplay ONLY in OnEnable
    /// - Unlock gameplay ONLY in OnDisable
    /// - Never trust client price/itemId
    /// - Client sends SlotIndex + Quantity only
    /// </summary>
    public sealed class VendorWindowUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private Button closeButton;
        [SerializeField] private Transform contentRoot;
        [SerializeField] private VendorRowUI rowPrefab;

        private VendorInteractable currentVendor;
        private VendorChestNet currentChest;

        private readonly List<VendorRowUI> rows = new();

        private bool gameplayLockHeld;

        #region Unity Lifecycle

        private void Awake()
        {
            if (closeButton)
                closeButton.onClick.AddListener(Close);
        }

        private void OnEnable()
        {
            // Acquire gameplay lock exactly once.
            if (!gameplayLockHeld)
            {
                InputState.LockGameplay();
                gameplayLockHeld = true;
            }

            if (titleText)
                titleText.text = "Vendor";

            // Subscribe if already bound
            if (currentChest != null)
                currentChest.OnSnapshotChanged += HandleSnapshotChanged;
        }

        private void OnDisable()
        {
            // Unsubscribe
            if (currentChest != null)
                currentChest.OnSnapshotChanged -= HandleSnapshotChanged;

            ClearRows();

            currentVendor = null;
            currentChest = null;

            // Release gameplay lock exactly once.
            if (gameplayLockHeld)
            {
                InputState.UnlockGameplay();
                gameplayLockHeld = false;
            }
        }

        #endregion

        #region Public API

        /// <summary>
        /// Opens vendor UI for a specific vendor.
        /// </summary>
        public void Open(VendorInteractable vendor)
        {
            if (vendor == null)
            {
                Debug.LogWarning("[VendorWindowUI] Open called with null vendor.");
                return;
            }

            if (!vendor.IsSpawned)
            {
                Debug.LogWarning("[VendorWindowUI] Vendor not spawned.", vendor);
                return;
            }

            currentVendor = vendor;
            currentChest = vendor.Chest;

            if (currentChest == null)
            {
                Debug.LogWarning("[VendorWindowUI] Vendor has no chest assigned.", vendor);
                return;
            }

            // Activate UI (triggers OnEnable and input lock)
            gameObject.SetActive(true);

            // Subscribe AFTER activation to avoid double subscription
            currentChest.OnSnapshotChanged += HandleSnapshotChanged;

            // Request fresh snapshot from server
            currentVendor.RequestOpenVendorServerRpc();
        }

        /// <summary>
        /// Closes vendor UI.
        /// </summary>
        public void Close()
        {
            gameObject.SetActive(false);
        }

        #endregion

        #region Snapshot Rendering

        private void HandleSnapshotChanged(InventorySnapshot snapshot)
        {
            Render(snapshot);
        }

        private void Render(InventorySnapshot snapshot)
        {
            if (contentRoot == null || rowPrefab == null)
                return;

            ClearRows();

            if (snapshot.Slots == null)
                return;

            for (int i = 0; i < snapshot.Slots.Length; i++)
            {
                var slot = snapshot.Slots[i];
                if (slot.IsEmpty)
                    continue;

                int slotIndex = i;
                string itemId = slot.ItemId.ToString();
                int qty = slot.Quantity;

                string displayName = currentChest != null
                    ? currentChest.GetDisplayName(itemId)
                    : itemId;

                var row = Instantiate(rowPrefab, contentRoot);
                rows.Add(row);

                row.Bind(
                    slotIndex,
                    displayName,
                    qty,
                    onBuy1: () => SendBuy(slotIndex, 1),
                    onBuy5: () => SendBuy(slotIndex, 5)
                );
            }
        }

        private void ClearRows()
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i] != null)
                    Destroy(rows[i].gameObject);
            }

            rows.Clear();
        }

        #endregion

        #region Checkout

        private void SendBuy(int slotIndex, int quantity)
        {
            if (currentVendor == null)
            {
                Debug.LogWarning("[VendorWindowUI] Cannot buy: no vendor bound.");
                return;
            }

            if (!NetworkManager.Singleton || !NetworkManager.Singleton.IsClient)
                return;

            if (!currentVendor.IsSpawned)
            {
                Debug.LogWarning(
                    $"[VendorWindowUI] Vendor not spawned. name={currentVendor.name} netId={currentVendor.NetworkObjectId}",
                    currentVendor);
                return;
            }

            var request = new CheckoutRequest
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

            currentVendor.RequestCheckoutServerRpc(request);
        }

        #endregion
    }
}
