using HuntersAndCollectors.Items;
using HuntersAndCollectors.Players;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace HuntersAndCollectors.UI
{
    /// <summary>
    /// Lightweight HUD food widget.
    ///
    /// Displays up to 3 active foods from PlayerVitalsNet:
    /// - Item icon
    /// - Remaining duration countdown text
    ///
    /// This is intentionally view-only; consumption requests are triggered elsewhere (inventory double-click).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FoodWidgetUI : MonoBehaviour
    {
        [System.Serializable]
        private sealed class FoodSlotView
        {
            public GameObject Root;
            public Image Icon;
            public TMP_Text DurationText;
        }

        [Header("UI")]
        [SerializeField] private FoodSlotView[] slots = new FoodSlotView[3];

        [SerializeField] private Sprite emptyIcon;
        [SerializeField] private bool hideEmptySlots = false;

        [Header("Data")]
        [SerializeField] private ItemDatabase itemDatabase;

        [Header("Refresh")]
        [SerializeField, Min(0.05f)] private float refreshSeconds = 0.2f;

        private PlayerVitalsNet vitals;
        private float nextRefreshTime;

        private void OnEnable()
        {
            TryBindToLocalVitals();
            Refresh();
        }

        private void Update()
        {
            if (Time.unscaledTime < nextRefreshTime)
                return;

            nextRefreshTime = Time.unscaledTime + refreshSeconds;

            if (vitals == null)
                TryBindToLocalVitals();

            Refresh();
        }

        private void OnDisable()
        {
            vitals = null;
        }

        private void TryBindToLocalVitals()
        {
            if (itemDatabase == null)
                itemDatabase = FindFirstObjectByType<ItemDatabase>();

            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null)
                return;

            var localPlayer = nm.SpawnManager != null ? nm.SpawnManager.GetLocalPlayerObject() : null;
            if (localPlayer == null)
                return;

            vitals = localPlayer.GetComponent<PlayerVitalsNet>();
        }

        private void Refresh()
        {
            if (slots == null || slots.Length == 0)
                return;

            if (vitals == null)
            {
                for (int i = 0; i < slots.Length; i++)
                    RenderSlot(i, false, null, 0f);
                return;
            }

            var active = vitals.ActiveFoodSlots;

            for (int i = 0; i < slots.Length; i++)
            {
                bool has = active != null && i < active.Count && active[i].IsValid;
                if (!has)
                {
                    RenderSlot(i, false, null, 0f);
                    continue;
                }

                var buff = active[i];
                string itemId = buff.ItemId.ToString();
                ItemDef def = null;

                if (itemDatabase != null && !string.IsNullOrWhiteSpace(itemId))
                    itemDatabase.TryGet(itemId, out def);

                RenderSlot(i, true, def != null ? def.Icon : null, buff.RemainingSeconds);
            }
        }

        private void RenderSlot(int index, bool active, Sprite icon, float remainingSeconds)
        {
            if (index < 0 || index >= slots.Length)
                return;

            FoodSlotView slot = slots[index];
            if (slot == null)
                return;

            bool showRoot = active || !hideEmptySlots;
            if (slot.Root != null)
                slot.Root.SetActive(showRoot);

            if (slot.Icon != null)
            {
                slot.Icon.sprite = active ? (icon != null ? icon : emptyIcon) : emptyIcon;
                slot.Icon.enabled = slot.Icon.sprite != null;
            }

            if (slot.DurationText != null)
                slot.DurationText.text = active ? FormatDuration(remainingSeconds) : string.Empty;
        }

        private static string FormatDuration(float seconds)
        {
            int total = Mathf.Max(0, Mathf.CeilToInt(seconds));
            int h = total / 3600;
            int m = (total % 3600) / 60;
            int s = total % 60;

            if (h > 0)
                return $"{h}:{m:00}:{s:00}";

            return $"{m:00}:{s:00}";
        }
    }
}
