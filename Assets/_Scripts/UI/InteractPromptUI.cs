using HuntersAndCollectors.Players;
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

        if (playerInteract.TryGetPromptText(out string text))
        {
            SetPrompt(text);
            return;
        }

        SetVisible(false);
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
