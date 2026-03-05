using System;
using System.Collections.Generic;
using System.Reflection;
using HuntersAndCollectors.Combat;
using HuntersAndCollectors.Items;
using HuntersAndCollectors.Players;
using HuntersAndCollectors.Skills;
using HuntersAndCollectors.Stats;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace HuntersAndCollectors.UI
{
    /// <summary>
    /// VitalsWindowUI
    /// -----------------------------------------------------------------------------
    /// Displays Health/Stamina/Mana bars + values for the local player.
    ///
    /// Key behavior:
    /// - Current values come from the runtime vitals component when available.
    /// - Max values always come from EffectiveStatsCalculator (derived STR/DEX/INT).
    ///
    /// This UI is read-only and never writes authoritative gameplay state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VitalsWindowUI : MonoBehaviour
    {
        [Header("Health")]
        [SerializeField] private Image healthFill;
        [SerializeField] private TMP_Text healthLabel;
        [SerializeField] private TMP_Text healthValueText;

        [Header("Stamina")]
        [SerializeField] private Image staminaFill;
        [SerializeField] private TMP_Text staminaLabel;
        [SerializeField] private TMP_Text staminaValueText;

        [Header("Mana")]
        [SerializeField] private Image manaFill;
        [SerializeField] private TMP_Text manaLabel;
        [SerializeField] private TMP_Text manaValueText;

        [Header("Data")]
        [SerializeField] private ItemDatabase itemDatabase;

        [Header("Refresh")]
        [SerializeField, Min(0.05f)] private float pollIntervalSeconds = 0.25f;

        private NetworkObject boundPlayerObject;
        private PlayerEquipmentNet boundEquipment;
        private SkillsNet boundSkills;
        private PlayerBaseStats boundBaseStats;
        private IVitalsReadOnly vitalsReader;

        private float nextPollTime;

        private float lastHealthFill = float.NaN;
        private float lastStaminaFill = float.NaN;
        private float lastManaFill = float.NaN;

        private string lastHealthText = string.Empty;
        private string lastStaminaText = string.Empty;
        private string lastManaText = string.Empty;

        private readonly HashSet<string> warned = new(StringComparer.Ordinal);

        private static readonly string[] CandidateVitalsTypeNames =
        {
            "PlayerVitalsNet",
            "VitalsNet",
            "PlayerVitals",
            "Vitals"
        };

        private void OnEnable()
        {
            ApplyPlaceholders();
            RunSelfDiagnosisOnce();

            TryBindToLocalPlayer();
            RefreshFromBoundData();
        }

        private void Update()
        {
            if (Time.unscaledTime < nextPollTime)
                return;

            nextPollTime = Time.unscaledTime + pollIntervalSeconds;

            if (!IsBound())
                TryBindToLocalPlayer();

            RefreshFromBoundData();
        }

        private void OnDisable()
        {
            boundPlayerObject = null;
            boundEquipment = null;
            boundSkills = null;
            boundBaseStats = null;
            vitalsReader = null;
        }

        private bool IsBound()
        {
            return boundPlayerObject != null;
        }

        private void RunSelfDiagnosisOnce()
        {
            if (healthFill == null || staminaFill == null || manaFill == null ||
                healthLabel == null || staminaLabel == null || manaLabel == null ||
                healthValueText == null || staminaValueText == null || manaValueText == null)
            {
                WarnOnce("ui_refs", "VitalsWindowUI: one or more UI references are null.");
            }

            if (!gameObject.activeInHierarchy)
                WarnOnce("inactive", "VitalsWindowUI: GameObject is inactive in hierarchy.");

            CanvasGroup[] groups = GetComponentsInParent<CanvasGroup>(true);
            for (int i = 0; i < groups.Length; i++)
            {
                CanvasGroup g = groups[i];
                if (g != null && g.alpha <= 0f)
                {
                    WarnOnce("cg_zero", $"VitalsWindowUI: Hidden by CanvasGroup alpha=0 on {GetPath(g.transform)}");
                    break;
                }
            }

            CheckFillType(healthFill, "fill_type_health");
            CheckFillType(staminaFill, "fill_type_stamina");
            CheckFillType(manaFill, "fill_type_mana");
        }

        private void CheckFillType(Image img, string key)
        {
            if (img != null && img.type != Image.Type.Filled)
                WarnOnce(key, $"VitalsWindowUI: Image '{img.name}' is not Filled. fillAmount is still written, but visual fill may not change.");
        }

        private void ApplyPlaceholders()
        {
            SetLabel(healthLabel, "Health");
            SetLabel(staminaLabel, "Stamina");
            SetLabel(manaLabel, "Mana");

            SetFillIfChanged(healthFill, ref lastHealthFill, 0f);
            SetFillIfChanged(staminaFill, ref lastStaminaFill, 0f);
            SetFillIfChanged(manaFill, ref lastManaFill, 0f);

            SetTextIfChanged(healthValueText, ref lastHealthText, "-- / --");
            SetTextIfChanged(staminaValueText, ref lastStaminaText, "-- / --");
            SetTextIfChanged(manaValueText, ref lastManaText, "-- / --");
        }

        private void TryBindToLocalPlayer()
        {
            NetworkObject localPlayer = GetLocalPlayerObject();
            if (localPlayer == null)
            {
                WarnOnce("no_local_player", "VitalsWindowUI: LocalClient.PlayerObject not available yet (waiting for spawn).");
                return;
            }

            if (boundPlayerObject == localPlayer)
                return;

            boundPlayerObject = localPlayer;
            boundEquipment = localPlayer.GetComponent<PlayerEquipmentNet>();
            boundSkills = localPlayer.GetComponent<SkillsNet>();
            boundBaseStats = localPlayer.GetComponent<PlayerBaseStats>();

            if (boundBaseStats == null)
                WarnOnce("no_base_stats", "VitalsWindowUI: PlayerBaseStats not found on local player. Using safe defaults for max vitals.");

            if (localPlayer.TryGetComponent(out HealthNet healthNet))
            {
                vitalsReader = new HealthNetReader(healthNet, WarnOnce);
                LogBindMappingOnce(localPlayer.gameObject.name, "HealthNet", "CurrentHealth/MaxHealth", "(missing -> calculator fallback)", "(missing -> calculator fallback)");
                return;
            }

            Component namedVitals = FindNamedVitalsComponent(localPlayer.gameObject);
            if (namedVitals != null)
            {
                ReflectionVitalsReader reflectionReader = new ReflectionVitalsReader(namedVitals, WarnOnce);
                if (reflectionReader.HasAnyChannel)
                {
                    vitalsReader = reflectionReader;
                    LogBindMappingOnce(
                        localPlayer.gameObject.name,
                        namedVitals.GetType().Name,
                        reflectionReader.HealthMemberInfo,
                        reflectionReader.StaminaMemberInfo,
                        reflectionReader.ManaMemberInfo);
                    return;
                }
            }

            vitalsReader = null;
            WarnOnce("no_vitals", "VitalsWindowUI: No runtime vitals component found on local player. Using derived max vitals as fallback values.");
            LogBindMappingOnce(localPlayer.gameObject.name, "Fallback", "(calculator)", "(calculator)", "(calculator)");
        }

        private static NetworkObject GetLocalPlayerObject()
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null)
                return null;

            if (nm.LocalClient != null && nm.LocalClient.PlayerObject != null)
                return nm.LocalClient.PlayerObject;

            if (nm.SpawnManager != null)
                return nm.SpawnManager.GetLocalPlayerObject();

            return null;
        }

        private static Component FindNamedVitalsComponent(GameObject go)
        {
            if (go == null)
                return null;

            Component[] all = go.GetComponents<Component>();
            for (int n = 0; n < CandidateVitalsTypeNames.Length; n++)
            {
                string wanted = CandidateVitalsTypeNames[n];
                for (int i = 0; i < all.Length; i++)
                {
                    Component c = all[i];
                    if (c != null && string.Equals(c.GetType().Name, wanted, StringComparison.Ordinal))
                        return c;
                }
            }

            return null;
        }

        private void RefreshFromBoundData()
        {
            if (!IsBound())
                return;

            // Always derive max values from the shared calculator so STR/DEX/INT rules
            // are consistent with StatsWindow and future gameplay systems.
            EffectiveStats effective = EffectiveStatsCalculator.Compute(boundBaseStats, boundEquipment, boundSkills, itemDatabase);

            float hCur = effective.MaxHealth;
            float sCur = effective.MaxStamina;
            float mCur = effective.MaxMana;

            float hMax = effective.MaxHealth;
            float sMax = effective.MaxStamina;
            float mMax = effective.MaxMana;

            if (vitalsReader != null)
            {
                // We read only CURRENT values from the runtime vitals source.
                // We intentionally ignore vitals-source max values in this phase.
                if (vitalsReader.TryGetHealth(out float readCur, out _))
                {
                    hCur = readCur;
                }
                else
                {
                    WarnOnce("missing_health", "VitalsWindowUI: Health current not found; using derived max as fallback current.");
                }

                if (vitalsReader.TryGetStamina(out readCur, out _))
                {
                    sCur = readCur;
                }
                else
                {
                    WarnOnce("missing_stamina", "VitalsWindowUI: Stamina current not found; using derived max as fallback current.");
                }

                if (vitalsReader.TryGetMana(out readCur, out _))
                {
                    mCur = readCur;
                }
                else
                {
                    WarnOnce("missing_mana", "VitalsWindowUI: Mana current not found; using derived max as fallback current.");
                }
            }

            UpdateSingle(healthFill, healthValueText, ref lastHealthFill, ref lastHealthText, hCur, hMax, "max_health_zero");
            UpdateSingle(staminaFill, staminaValueText, ref lastStaminaFill, ref lastStaminaText, sCur, sMax, "max_stamina_zero");
            UpdateSingle(manaFill, manaValueText, ref lastManaFill, ref lastManaText, mCur, mMax, "max_mana_zero");
        }

        private void UpdateSingle(Image fill, TMP_Text valueText, ref float cachedFill, ref string cachedText, float current, float max, string zeroWarnKey)
        {
            float c = Mathf.Max(0f, current);
            float m = max;

            // If derived max is zero (attribute total is zero), keep UI stable and explicit.
            if (m <= 0f)
            {
                WarnOnce(zeroWarnKey, $"VitalsWindowUI: max <= 0 for {valueText?.name ?? "vital"}; showing current / 0 with empty bar.");
                SetFillIfChanged(fill, ref cachedFill, 0f);
                SetTextIfChanged(valueText, ref cachedText, $"{c:0} / 0");
                return;
            }

            float fillValue = Mathf.Clamp01(c / m);
            SetFillIfChanged(fill, ref cachedFill, fillValue);
            SetTextIfChanged(valueText, ref cachedText, $"{c:0} / {m:0}");
        }

        private void LogBindMappingOnce(string playerName, string componentType, string hMembers, string sMembers, string mMembers)
        {
            if (!warned.Add("bind_info"))
                return;

            Debug.Log($"[VitalsWindowUI] Bound player='{playerName}' source='{componentType}' health='{hMembers}' stamina='{sMembers}' mana='{mMembers}'", this);
        }

        private void WarnOnce(string key, string message)
        {
            if (!warned.Add(key))
                return;

            Debug.LogWarning(message, this);
        }

        private static void SetLabel(TMP_Text label, string text)
        {
            if (label == null)
                return;

            if (!string.Equals(label.text, text, StringComparison.Ordinal))
                label.text = text;
        }

        private static void SetFillIfChanged(Image image, ref float cache, float value)
        {
            if (image == null)
                return;

            if (!Mathf.Approximately(cache, value))
            {
                image.fillAmount = value;
                cache = value;
            }
        }

        private static void SetTextIfChanged(TMP_Text text, ref string cache, string value)
        {
            if (text == null)
                return;

            if (!string.Equals(cache, value, StringComparison.Ordinal))
            {
                text.text = value;
                cache = value;
            }
        }

        private static string GetPath(Transform t)
        {
            if (t == null)
                return "<null>";

            string path = t.name;
            Transform p = t.parent;
            while (p != null)
            {
                path = p.name + "/" + path;
                p = p.parent;
            }
            return path;
        }

        private interface IVitalsReadOnly
        {
            bool TryGetHealth(out float cur, out float max);
            bool TryGetStamina(out float cur, out float max);
            bool TryGetMana(out float cur, out float max);
        }

        private sealed class HealthNetReader : IVitalsReadOnly
        {
            private readonly HealthNet health;
            private readonly Action<string, string> warnOnce;

            public HealthNetReader(HealthNet health, Action<string, string> warnOnce)
            {
                this.health = health;
                this.warnOnce = warnOnce;
            }

            public bool TryGetHealth(out float cur, out float max)
            {
                if (health == null)
                {
                    cur = 0f;
                    max = 0f;
                    return false;
                }

                cur = health.CurrentHealth;
                max = health.MaxHealth;
                return true;
            }

            public bool TryGetStamina(out float cur, out float max)
            {
                cur = 0f;
                max = 0f;
                warnOnce?.Invoke("healthnet_no_stamina", "VitalsWindowUI: HealthNet has no stamina members; using calculator fallback for stamina.");
                return false;
            }

            public bool TryGetMana(out float cur, out float max)
            {
                cur = 0f;
                max = 0f;
                warnOnce?.Invoke("healthnet_no_mana", "VitalsWindowUI: HealthNet has no mana members; using calculator fallback for mana.");
                return false;
            }
        }

        private sealed class ReflectionVitalsReader : IVitalsReadOnly
        {
            private readonly object source;
            private readonly Action<string, string> warnOnce;

            private readonly FloatAccessor healthCur;
            private readonly FloatAccessor healthMax;
            private readonly FloatAccessor staminaCur;
            private readonly FloatAccessor staminaMax;
            private readonly FloatAccessor manaCur;
            private readonly FloatAccessor manaMax;

            public bool HasAnyChannel => healthCur.IsBound || staminaCur.IsBound || manaCur.IsBound;

            public string HealthMemberInfo => $"{(healthCur.IsBound ? healthCur.MemberName : "(missing)")}/{(healthMax.IsBound ? healthMax.MemberName : "(max missing -> current)")}";
            public string StaminaMemberInfo => $"{(staminaCur.IsBound ? staminaCur.MemberName : "(missing)")}/{(staminaMax.IsBound ? staminaMax.MemberName : "(max missing -> current)")}";
            public string ManaMemberInfo => $"{(manaCur.IsBound ? manaCur.MemberName : "(missing)")}/{(manaMax.IsBound ? manaMax.MemberName : "(max missing -> current)")}";

            public ReflectionVitalsReader(object source, Action<string, string> warnOnce)
            {
                this.source = source;
                this.warnOnce = warnOnce;

                healthCur = new FloatAccessor(source, "HealthCurrent", "CurrentHealth", "Health");
                healthMax = new FloatAccessor(source, "HealthMax", "MaxHealth");

                staminaCur = new FloatAccessor(source, "StaminaCurrent", "CurrentStamina", "Stamina", "Dexterity");
                staminaMax = new FloatAccessor(source, "StaminaMax", "MaxStamina");

                manaCur = new FloatAccessor(source, "ManaCurrent", "CurrentMana", "Mana");
                manaMax = new FloatAccessor(source, "ManaMax", "MaxMana");
            }

            public bool TryGetHealth(out float cur, out float max)
            {
                cur = 0f;
                max = 0f;

                if (source == null || !healthCur.TryRead(out cur))
                    return false;

                if (!healthMax.TryRead(out max))
                {
                    max = Mathf.Max(1f, cur);
                    warnOnce?.Invoke("health_max_missing", "VitalsWindowUI: MaxHealth not found; using current as max.");
                }

                return true;
            }

            public bool TryGetStamina(out float cur, out float max)
            {
                cur = 0f;
                max = 0f;

                if (source == null || !staminaCur.TryRead(out cur))
                    return false;

                if (!staminaMax.TryRead(out max))
                {
                    max = Mathf.Max(1f, cur);
                    warnOnce?.Invoke("stamina_max_missing", "VitalsWindowUI: MaxStamina not found; using current as max.");
                }

                return true;
            }

            public bool TryGetMana(out float cur, out float max)
            {
                cur = 0f;
                max = 0f;

                if (source == null || !manaCur.TryRead(out cur))
                    return false;

                if (!manaMax.TryRead(out max))
                {
                    max = Mathf.Max(1f, cur);
                    warnOnce?.Invoke("mana_max_missing", "VitalsWindowUI: MaxMana not found; using current as max.");
                }

                return true;
            }

            private sealed class FloatAccessor
            {
                private readonly object source;
                private readonly FieldInfo field;
                private readonly PropertyInfo property;

                public string MemberName { get; }
                public bool IsBound => source != null && (field != null || property != null);

                public FloatAccessor(object source, params string[] names)
                {
                    this.source = source;
                    if (source == null || names == null || names.Length == 0)
                        return;

                    Type t = source.GetType();
                    const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                    for (int i = 0; i < names.Length; i++)
                    {
                        string n = names[i];

                        FieldInfo f = t.GetField(n, flags);
                        if (f != null)
                        {
                            field = f;
                            MemberName = n;
                            return;
                        }

                        PropertyInfo p = t.GetProperty(n, flags);
                        if (p != null && p.CanRead)
                        {
                            property = p;
                            MemberName = n;
                            return;
                        }
                    }
                }

                public bool TryRead(out float value)
                {
                    value = 0f;
                    if (!IsBound)
                        return false;

                    object raw = field != null ? field.GetValue(source) : property.GetValue(source, null);
                    return TryConvertToFloat(raw, out value);
                }

                private static bool TryConvertToFloat(object raw, out float value)
                {
                    value = 0f;
                    if (raw == null)
                        return false;

                    switch (raw)
                    {
                        case float f: value = f; return true;
                        case double d: value = (float)d; return true;
                        case int i: value = i; return true;
                        case long l: value = l; return true;
                        case short s: value = s; return true;
                        case uint ui: value = ui; return true;
                        case ulong ul: value = ul; return true;
                        case byte b: value = b; return true;
                    }

                    PropertyInfo valueProp = raw.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
                    if (valueProp != null && valueProp.CanRead)
                    {
                        object inner = valueProp.GetValue(raw, null);
                        return TryConvertToFloat(inner, out value);
                    }

                    return false;
                }
            }
        }
    }
}
