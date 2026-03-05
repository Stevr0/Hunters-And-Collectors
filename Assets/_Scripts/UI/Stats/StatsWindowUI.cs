using System;
using System.Text;
using HuntersAndCollectors.Items;
using HuntersAndCollectors.Players;
using HuntersAndCollectors.Skills;
using HuntersAndCollectors.Stats;
using TMPro;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.UI
{
    /// <summary>
    /// StatsWindowUI (Totals text only)
    /// -----------------------------------------------------------------------------
    /// Renders read-only totals for the local player:
    /// - Combat totals
    /// - Attribute totals (Strength/Dexterity/Intelligence)
    /// - Derived max vitals from attributes
    ///
    /// This script does not write authoritative state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StatsWindowUI : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private TMP_Text totalsText;

        [Header("Data")]
        [Tooltip("Assign your ItemDatabase so equipped item ids can resolve to ItemDef.")]
        [SerializeField] private ItemDatabase itemDatabase;

        [Header("Refresh")]
        [SerializeField, Min(0.05f)] private float pollIntervalSeconds = 0.25f;

        private readonly StringBuilder textBuilder = new(320);

        private NetworkObject boundPlayerObject;
        private PlayerEquipmentNet boundEquipment;
        private SkillsNet boundSkills;
        private PlayerBaseStats boundBaseStats;

        private bool equipmentSubscribed;
        private bool warnedMissingEquipment;
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
            Unbind();
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
                    Unbind();
                    SetTotalsTextIfChanged("No player bound");
                }
                return;
            }

            if (boundPlayerObject == localPlayer)
                return;

            Bind(localPlayer);
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

        private void Bind(NetworkObject playerObject)
        {
            Unbind();

            boundPlayerObject = playerObject;
            boundEquipment = boundPlayerObject.GetComponent<PlayerEquipmentNet>();
            boundSkills = boundPlayerObject.GetComponent<SkillsNet>();
            boundBaseStats = boundPlayerObject.GetComponent<PlayerBaseStats>();

            if (boundEquipment != null)
            {
                boundEquipment.OnEquipmentChanged += HandleEquipmentChanged;
                equipmentSubscribed = true;
            }
            else if (!warnedMissingEquipment)
            {
                warnedMissingEquipment = true;
                Debug.LogWarning("[StatsWindowUI] PlayerEquipmentNet missing on local player.");
            }
        }

        private void Unbind()
        {
            if (equipmentSubscribed && boundEquipment != null)
                boundEquipment.OnEquipmentChanged -= HandleEquipmentChanged;

            equipmentSubscribed = false;
            boundPlayerObject = null;
            boundEquipment = null;
            boundSkills = null;
            boundBaseStats = null;
        }

        private void HandleEquipmentChanged()
        {
            Refresh();
        }

        private void Refresh()
        {
            if (boundPlayerObject == null)
            {
                SetTotalsTextIfChanged("No player bound");
                return;
            }

            EffectiveStats effective = EffectiveStatsCalculator.Compute(boundBaseStats, boundEquipment, boundSkills, itemDatabase);

            textBuilder.Clear();
            textBuilder.AppendLine("== TOTALS ==");
            textBuilder.Append("Move Speed: ").Append(effective.MoveSpeedMult.ToString("0.##")).AppendLine("x");
            textBuilder.Append("Damage: ").AppendLine(effective.Damage.ToString("0.0#"));
            textBuilder.Append("Defence: ").AppendLine(effective.Defence.ToString("0.0#"));
            textBuilder.Append("Swing Speed: ").AppendLine(effective.SwingSpeed.ToString("0.##"));
            textBuilder.AppendLine();

            textBuilder.AppendLine("== ATTRIBUTES (BASE + EQUIP) ==");
            textBuilder.Append("Strength: ").AppendLine(effective.Strength.ToString());
            textBuilder.Append("Dexterity: ").AppendLine(effective.Dexterity.ToString());
            textBuilder.Append("Intelligence: ").AppendLine(effective.Intelligence.ToString());
            textBuilder.AppendLine();

            textBuilder.AppendLine("== VITALS (DERIVED) ==");
            textBuilder.Append("Health Max: ").AppendLine(effective.MaxHealth.ToString());
            textBuilder.Append("Stamina Max: ").AppendLine(effective.MaxStamina.ToString());
            textBuilder.Append("Mana Max: ").Append(effective.MaxMana.ToString());

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
