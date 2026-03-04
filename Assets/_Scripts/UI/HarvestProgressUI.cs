using System;
using HuntersAndCollectors.Harvesting;
using HuntersAndCollectors.Players;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace HuntersAndCollectors.UI
{
    /// <summary>
    /// HarvestProgressUI (Repurposed to Node Health UI)
    /// --------------------------------------------------------------------
    /// Old behavior:
    /// - Displayed timed harvest "hold to harvest" progress by listening to HarvestingNet events.
    ///
    /// New behavior (Hit-to-Harvest):
    /// - Displays the HEALTH of the node the local player is currently aiming at.
    /// - Slider fill = CurrentHealth / MaxHealth (server-authoritative health replicated to clients).
    ///
    /// Why this is safe for networking:
    /// - UI only reads node state; it never writes or requests damage.
    /// - Damage remains server-side in HarvestingNet.HitNodeServerRpc.
    ///
    /// Requirements on ResourceNodeNet:
    /// - int MaxHealth { get; }
    /// - int CurrentHealth { get; } (replicated to clients)
    /// - bool IsHarvestableNow() (optional; used to hide during cooldown if enabled)
    /// - ResourceType ResourceType { get; } (for status label)
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class HarvestProgressUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private Slider progressSlider;
        [SerializeField] private TMP_Text statusText;

        [Header("Display Settings")]
        [Tooltip("Text shown if we cannot determine a better label.")]
        [SerializeField] private string defaultStatusText = "Node";

        [Tooltip("How fast the widget fades in/out.")]
        [SerializeField] private float fadeSpeed = 12f;

        [Tooltip("After you stop aiming at a node, keep the widget on screen briefly to avoid flicker.")]
        [Min(0f)]
        [SerializeField] private float lingerSeconds = 0.25f;

        [Tooltip("If true, hide the widget while node is on cooldown (not harvestable).")]
        [SerializeField] private bool hideIfOnCooldown = true;

        [Tooltip("If true, show 'Chopping Wood 3/5'. If false, only show action text.")]
        [SerializeField] private bool showHpText = true;

        // -------------------------
        // Runtime bindings
        // -------------------------

        private PlayerInteract playerInteract;       // local player interactor (source of focused node)
        private ResourceNodeNet activeNode;          // last known node we were displaying
        private float lingerTimer;                   // counts down after losing focus
        private bool targetVisible;                  // desired visible state (drives fade)

        public bool IsVisible => canvasGroup != null && canvasGroup.alpha > 0.01f;

        private void Awake()
        {
            // Ensure CanvasGroup exists (so we can fade cleanly).
            if (canvasGroup == null)
                canvasGroup = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();

            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            // Auto-find UI controls if not linked.
            if (progressSlider == null)
                progressSlider = GetComponentInChildren<Slider>(true);

            if (statusText == null)
                statusText = GetComponentInChildren<TMP_Text>(true);

            // Initialize slider range.
            if (progressSlider != null)
            {
                progressSlider.minValue = 0f;
                progressSlider.maxValue = 1f;
                progressSlider.value = 0f;
            }

            if (statusText != null)
                statusText.text = string.Empty;

            targetVisible = false;
        }

        private void OnEnable()
        {
            // Try binding immediately when enabled.
            TryBindLocalPlayerInteract();
        }

        private void Update()
        {
            if (playerInteract == null)
                TryBindLocalPlayerInteract();

            if (playerInteract == null)
            {
                // If we can't bind, hide UI.
                activeNode = null;
                lingerTimer = 0f;
                targetVisible = false;
                UpdateCanvasAlpha();
                return;
            }

            // Focused node is the one we're aiming at this frame.
            // This requires PlayerInteract to expose CurrentNodeFocus (you already have this in your project).
            var focused = playerInteract.CurrentNodeFocus;

            if (focused != null)
            {
                // We are actively aiming at a node.
                activeNode = focused;
                lingerTimer = lingerSeconds;

                RefreshFromNode(activeNode);
                targetVisible = true;
                UpdateCanvasAlpha();
                return;
            }

            // Not aiming at a node: linger briefly to avoid flicker.
            if (activeNode != null && lingerTimer > 0f)
            {
                lingerTimer -= Time.unscaledDeltaTime;
                RefreshFromNode(activeNode);
                targetVisible = true;
                UpdateCanvasAlpha();
                return;
            }

            // No focus and linger ended: hide.
            activeNode = null;
            targetVisible = false;
            UpdateCanvasAlpha();
        }

        /// <summary>
        /// Finds the local player object (owner) and binds to its PlayerInteract.
        /// This avoids you having to wire references in the inspector.
        /// </summary>
        private void TryBindLocalPlayerInteract()
        {
            if (playerInteract != null)
                return;

            if (NetworkManager.Singleton == null)
                return;

            var localPlayer = NetworkManager.Singleton.SpawnManager?.GetLocalPlayerObject();
            if (localPlayer == null)
                return;

            // PlayerInteract may be on the root or a child.
            var pi = localPlayer.GetComponentInChildren<PlayerInteract>(true);
            if (pi == null)
            {
                // Only warn once in practice; leaving simple warning here for now.
                Debug.LogWarning("[HarvestProgressUI] PlayerInteract not found on LocalPlayer (root or children).");
                return;
            }

            playerInteract = pi;
            Debug.Log("[HarvestProgressUI] Bound to PlayerInteract (Node health display mode).");
        }

        /// <summary>
        /// Updates slider + text based on node health.
        /// </summary>
        private void RefreshFromNode(ResourceNodeNet node)
        {
            if (node == null)
                return;

            // Optional: hide while on cooldown (if you want the bar to disappear when unharvestable).
            if (hideIfOnCooldown && !node.IsHarvestableNow())
            {
                targetVisible = false;
                return;
            }

            int max = Mathf.Max(1, node.MaxHealth);
            int cur = Mathf.Clamp(node.CurrentHealth, 0, max);

            // Slider fill.
            float t = cur / (float)max;
            if (progressSlider != null)
                progressSlider.value = Mathf.Clamp01(t);

            // Status label.
            if (statusText != null)
            {
                string action = node.ResourceType switch
                {
                    ResourceType.Wood => "Chopping Wood",
                    ResourceType.Stone => "Mining Stone",
                    ResourceType.Fiber => "Gathering Fiber",
                    _ => defaultStatusText
                };

                statusText.text = showHpText ? $"{action} {cur}/{max}" : $"{action}...";
            }
        }

        /// <summary>
        /// Handles smooth fade in/out of the UI panel.
        /// </summary>
        private void UpdateCanvasAlpha()
        {
            if (canvasGroup == null)
                return;

            canvasGroup.alpha = Mathf.MoveTowards(
                canvasGroup.alpha,
                targetVisible ? 1f : 0f,
                fadeSpeed * Time.unscaledDeltaTime);

            // We don't want this widget to block clicks.
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
    }
}