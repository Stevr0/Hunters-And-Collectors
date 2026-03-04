using HuntersAndCollectors.Harvesting;
using HuntersAndCollectors.Items;
using HuntersAndCollectors.Players;
using HuntersAndCollectors.Vendors;
using TMPro;
using Unity.Netcode;
using UnityEngine;

public sealed class InteractPromptUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private CanvasGroup promptCanvas;
    [SerializeField] private TMP_Text promptText;

    [Header("Hide while harvesting")]
    [SerializeField] private HuntersAndCollectors.UI.HarvestProgressUI harvestUI;

    private PlayerInteract playerInteract;

    private void Awake()
    {
        // Fail fast if not wired.
        if (promptCanvas == null)
            promptCanvas = GetComponentInChildren<CanvasGroup>(true);

        if (promptText == null)
            promptText = GetComponentInChildren<TMP_Text>(true);

        SetVisible(false);
    }

    private void Update()
    {
        TryBind();
        if (playerInteract == null || promptCanvas == null || promptText == null)
            return;

        // If harvesting UI is currently visible, do not show prompts.
        if (harvestUI != null && harvestUI.IsVisible)
        {
            SetVisible(false);
            return;
        }

        // 1) Harvest node prompt comes from PlayerInteract focus (same rules as your interact system).
        var node = playerInteract.CurrentNodeFocus;
        if (node != null)
        {
            var text = node.ResourceType switch
            {
                ResourceType.Wood => "Hold E: Chop Tree",
                ResourceType.Stone => "Hold E: Mine Rock",
                ResourceType.Fiber => "Hold E: Gather",
                _ => "Hold E: Harvest"
            };

            SetPrompt(text);
            return;
        }

        // 2) Vendor / Drop prompt: raycast using PlayerInteract’s own settings (range + mask + trigger mode)
        if (!TryRaycastWithPlayerInteract(out var hit))
        {
            SetVisible(false);
            return;
        }

        if (hit.collider.GetComponentInParent<VendorInteractable>() != null)
        {
            SetPrompt("E: Open Vendor");
            return;
        }

        if (hit.collider.GetComponentInParent<ResourceDrop>() != null)
        {
            SetPrompt("E: Pick Up");
            return;
        }

        SetVisible(false);
    }

    private bool TryRaycastWithPlayerInteract(out RaycastHit hit)
    {
        hit = default;

        // We need access to the same camera/range/mask used by PlayerInteract.
        // Rather than duplicating those values here, expose simple getters on PlayerInteract (see below).
        var cam = playerInteract.InteractCamera;
        if (cam == null)
            return false;

        var ray = new Ray(cam.transform.position, cam.transform.forward);

        return Physics.Raycast(
            ray,
            out hit,
            playerInteract.InteractRange,
            playerInteract.InteractableMask,
            QueryTriggerInteraction.Collide);
    }

    private void TryBind()
    {
        if (playerInteract != null)
            return;

        if (NetworkManager.Singleton == null)
            return;

        var localPlayer = NetworkManager.Singleton.SpawnManager?.GetLocalPlayerObject();
        if (localPlayer == null)
            return;

        playerInteract = localPlayer.GetComponentInChildren<PlayerInteract>(true);
    }

    private void SetPrompt(string text)
    {
        promptText.text = text;
        SetVisible(true);
    }

    private void SetVisible(bool visible)
    {
        if (promptCanvas == null)
            return;

        promptCanvas.alpha = visible ? 1f : 0f;
        promptCanvas.blocksRaycasts = false;
        promptCanvas.interactable = false;
    }
}