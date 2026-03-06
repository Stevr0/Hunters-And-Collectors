using System;
using System.Text;
using HuntersAndCollectors.Stats;
using TMPro;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.UI
{
    /// <summary>
    /// Renders read-only local-player totals from the actor stats pipeline.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StatsWindowUI : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private TMP_Text totalsText;

        [Header("Refresh")]
        [SerializeField, Min(0.05f)] private float pollIntervalSeconds = 0.25f;

        private readonly StringBuilder textBuilder = new(320);

        private NetworkObject boundPlayerObject;
        private IStatsProvider boundStatsProvider;

        private bool warnedMissingStatsProvider;
        private float nextPollTime;
        private string lastRenderedText = string.Empty;

        private void OnEnable()
        {
            SetTotalsTextIfChanged("No player bound");
            TryBindToLocalPlayer();
            Refresh();
        }

        private void OnDisable()
        {
            boundPlayerObject = null;
            boundStatsProvider = null;
            warnedMissingStatsProvider = false;
        }

        private void Update()
        {
            if (Time.unscaledTime < nextPollTime)
                return;

            nextPollTime = Time.unscaledTime + pollIntervalSeconds;
            TryBindToLocalPlayer();
            Refresh();
        }

        private void TryBindToLocalPlayer()
        {
            NetworkObject localPlayer = GetLocalPlayerObject();

            if (localPlayer == null)
            {
                if (boundPlayerObject != null)
                {
                    boundPlayerObject = null;
                    boundStatsProvider = null;
                    warnedMissingStatsProvider = false;
                    SetTotalsTextIfChanged("No player bound");
                }
                return;
            }

            if (boundPlayerObject == localPlayer)
                return;

            boundPlayerObject = localPlayer;
            boundStatsProvider = localPlayer.GetComponentInParent<IStatsProvider>();
            warnedMissingStatsProvider = false;
        }

        private static NetworkObject GetLocalPlayerObject()
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsClient)
                return null;

            NetworkObject fromLocalClient = nm.LocalClient != null ? nm.LocalClient.PlayerObject : null;
            if (fromLocalClient != null)
                return fromLocalClient;

            return nm.SpawnManager != null ? nm.SpawnManager.GetLocalPlayerObject() : null;
        }

        private void Refresh()
        {
            if (boundPlayerObject == null)
            {
                SetTotalsTextIfChanged("No player bound");
                return;
            }

            if (boundStatsProvider == null)
            {
                if (!warnedMissingStatsProvider)
                {
                    warnedMissingStatsProvider = true;
                    Debug.LogWarning("[StatsWindowUI] Missing IStatsProvider on local player.", this);
                }

                SetTotalsTextIfChanged("Missing stats provider");
                return;
            }

            EffectiveStats effective = boundStatsProvider.GetEffectiveStats();

            textBuilder.Clear();
            textBuilder.AppendLine("== TOTALS ==");
            textBuilder.Append("Move Speed: ").Append(effective.MoveSpeedMult.ToString("0.##")).AppendLine("x");
            textBuilder.Append("Damage: ").AppendLine(effective.Damage.ToString("0.0#"));
            textBuilder.Append("Defence: ").AppendLine(effective.Defence.ToString("0.0#"));
            textBuilder.Append("Swing Speed: ").AppendLine(effective.SwingSpeed.ToString("0.##"));
            textBuilder.AppendLine();

            textBuilder.AppendLine("== ATTRIBUTES ==");
            textBuilder.Append("Strength: ").AppendLine(effective.Strength.ToString());
            textBuilder.Append("Dexterity: ").AppendLine(effective.Dexterity.ToString());
            textBuilder.Append("Intelligence: ").AppendLine(effective.Intelligence.ToString());
            textBuilder.AppendLine();



            SetTotalsTextIfChanged(textBuilder.ToString());
        }

        private void SetTotalsTextIfChanged(string value)
        {
            if (totalsText == null)
                return;

            if (!string.Equals(lastRenderedText, value, StringComparison.Ordinal))
            {
                totalsText.text = value;
                lastRenderedText = value;
            }
        }
    }
}
