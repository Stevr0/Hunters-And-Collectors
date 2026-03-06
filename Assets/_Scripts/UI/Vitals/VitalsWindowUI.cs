using HuntersAndCollectors.Combat;
using HuntersAndCollectors.Stats;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace HuntersAndCollectors.UI
{
    /// <summary>
    /// Displays local-player vitals using the actor stats pipeline.
    ///
    /// Max values come from IStatsProvider (ActorStatsProvider).
    /// Current health comes from HealthNet when available.
    /// Stamina/mana current values currently mirror derived max values.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VitalsWindowUI : MonoBehaviour
    {
        [Header("Health")]
        [SerializeField] private Image healthFill;
        [SerializeField] private TMP_Text healthValueText;

        [Header("Stamina")]
        [SerializeField] private Image staminaFill;
        [SerializeField] private TMP_Text staminaValueText;

        [Header("Mana")]
        [SerializeField] private Image manaFill;
        [SerializeField] private TMP_Text manaValueText;

        [Header("Refresh")]
        [SerializeField, Min(0.05f)] private float pollIntervalSeconds = 0.25f;

        private NetworkObject boundPlayerObject;
        private IStatsProvider boundStatsProvider;
        private HealthNet boundHealth;

        private float nextPollTime;
        private bool warnedMissingStatsProvider;

        private void OnEnable()
        {
            TryBindToLocalPlayer();
            Refresh();
        }

        private void Update()
        {
            if (Time.unscaledTime < nextPollTime)
                return;

            nextPollTime = Time.unscaledTime + pollIntervalSeconds;

            TryBindToLocalPlayer();
            Refresh();
        }

        private void OnDisable()
        {
            boundPlayerObject = null;
            boundStatsProvider = null;
            boundHealth = null;
            warnedMissingStatsProvider = false;
        }

        private void TryBindToLocalPlayer()
        {
            NetworkObject localPlayer = GetLocalPlayerObject();
            if (localPlayer == null)
                return;

            if (boundPlayerObject == localPlayer)
                return;

            boundPlayerObject = localPlayer;
            boundStatsProvider = localPlayer.GetComponentInParent<IStatsProvider>();
            boundHealth = localPlayer.GetComponent<HealthNet>();
            warnedMissingStatsProvider = false;
        }

        private static NetworkObject GetLocalPlayerObject()
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null)
                return null;

            if (nm.LocalClient != null && nm.LocalClient.PlayerObject != null)
                return nm.LocalClient.PlayerObject;

            return nm.SpawnManager != null ? nm.SpawnManager.GetLocalPlayerObject() : null;
        }

        private void Refresh()
        {
            if (boundPlayerObject == null)
            {
                RenderChannel(healthFill, healthValueText, 0f, 0f);
                RenderChannel(staminaFill, staminaValueText, 0f, 0f);
                RenderChannel(manaFill, manaValueText, 0f, 0f);
                return;
            }

            if (boundStatsProvider == null)
            {
                if (!warnedMissingStatsProvider)
                {
                    warnedMissingStatsProvider = true;
                    Debug.LogWarning("[VitalsWindowUI] Missing IStatsProvider on local player; showing 0 vitals.", this);
                }

                RenderChannel(healthFill, healthValueText, 0f, 0f);
                RenderChannel(staminaFill, staminaValueText, 0f, 0f);
                RenderChannel(manaFill, manaValueText, 0f, 0f);
                return;
            }

            EffectiveStats stats = boundStatsProvider.GetEffectiveStats();

            float healthMax = Mathf.Max(0f, stats.MaxHealth);
            float staminaMax = Mathf.Max(0f, stats.MaxStamina);
            float manaMax = Mathf.Max(0f, stats.MaxMana);

            float healthCurrent = boundHealth != null ? boundHealth.CurrentHealth : healthMax;
            float staminaCurrent = staminaMax;
            float manaCurrent = manaMax;

            RenderChannel(healthFill, healthValueText, healthCurrent, healthMax);
            RenderChannel(staminaFill, staminaValueText, staminaCurrent, staminaMax);
            RenderChannel(manaFill, manaValueText, manaCurrent, manaMax);
        }

        private static void RenderChannel(Image fill, TMP_Text text, float current, float max)
        {
            float safeCurrent = Mathf.Max(0f, current);
            float safeMax = Mathf.Max(0f, max);
            float ratio = safeMax > 0f ? Mathf.Clamp01(safeCurrent / safeMax) : 0f;

            if (fill != null)
                fill.fillAmount = ratio;

            if (text != null)
                text.text = $"{safeCurrent:0} / {safeMax:0}";
        }
    }
}
