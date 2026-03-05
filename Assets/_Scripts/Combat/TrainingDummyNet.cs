using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Combat
{
    /// <summary>
    /// Minimal server-authoritative combat target for combat prototyping.
    ///
    /// Authority rules:
    /// - Health is replicated read-only to clients.
    /// - Only the server can modify health.
    /// - Hit feedback is broadcast from server via ClientRpc.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class TrainingDummyNet : NetworkBehaviour
    {
        [Header("Dummy Health")]
        [Tooltip("Initial health value set by server on spawn.")]
        [Min(1)]
        [SerializeField] private int maxHealth = 100;

        [Header("Hit Feedback")]
        [Tooltip("World offset used for hit popup spawn position when no exact hit point is supplied.")]
        [SerializeField] private Vector3 popupOffset = new(0f, 1.6f, 0f);

        [Tooltip("Optional animator for local hit trigger playback on each client.")]
        [SerializeField] private Animator dummyAnimator;

        [Tooltip("Animator trigger name for a hit reaction.")]
        [SerializeField] private string hitTriggerName = "Hit";

        [Tooltip("Optional explicit renderers to flash. If empty, renderers in children are used.")]
        [SerializeField] private Renderer[] flashRenderers;

        [Tooltip("Flash tint color shown briefly when hit.")]
        [SerializeField] private Color flashColor = Color.white;

        [Tooltip("Duration of the local hit flash effect.")]
        [Min(0.01f)]
        [SerializeField] private float flashDuration = 0.1f;

        // Replicated health value. Everyone can read, only server can write.
        private readonly NetworkVariable<int> healthNet =
            new(100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private Coroutine flashRoutine;

        public int CurrentHealth => healthNet.Value;
        public int MaxHealth => Mathf.Max(1, maxHealth);

        /// <summary>
        /// Exposes the replicated health variable for read-only subscriptions (UI, bars, etc).
        /// Clients must never assign to Value; server is the only writer.
        /// </summary>
        public NetworkVariable<int> HealthNetVar => healthNet;

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                maxHealth = Mathf.Max(1, maxHealth);
                healthNet.Value = maxHealth;
            }
        }

        /// <summary>
        /// SERVER ONLY: Applies validated damage to this dummy and broadcasts feedback.
        /// </summary>
        public void ServerApplyDamage(int damage)
        {
            if (!IsServer)
            {
                Debug.LogWarning("[Combat] ServerApplyDamage called on client; ignored.", this);
                return;
            }

            if (!IsSpawned || damage <= 0 || healthNet.Value <= 0)
                return;

            int previous = healthNet.Value;
            int nextHealth = Mathf.Max(0, previous - damage);
            int appliedDamage = Mathf.Max(0, previous - nextHealth);
            healthNet.Value = nextHealth;

            Vector3 popupWorldPoint = transform.position + popupOffset;
            HitFeedbackClientRpc(appliedDamage, popupWorldPoint, nextHealth, MaxHealth);

            Debug.Log($"[Combat] Dummy hit for {appliedDamage} damage", this);
            Debug.Log($"[Combat] Dummy health = {nextHealth}", this);

            if (nextHealth <= 0)
                ServerDestroyDummy();
        }

        /// <summary>
        /// Backward-compatible alias used by any existing call sites.
        /// </summary>
        public bool ApplyDamageServer(int damage)
        {
            int before = healthNet.Value;
            ServerApplyDamage(damage);
            return IsServer && healthNet.Value < before;
        }

        /// <summary>
        /// SERVER -> ALL CLIENTS: Play local-only visuals for the hit.
        /// No gameplay state is modified here.
        /// </summary>
        [ClientRpc]
        private void HitFeedbackClientRpc(int damageAmount, Vector3 worldHitPoint, int newHealth, int maxHealthValue)
        {
            PlayLocalHitReaction();

            if (damageAmount > 0)
                DamagePopupWorld.Spawn(damageAmount, worldHitPoint);
        }

        /// <summary>
        /// LOCAL VISUALS ONLY: animation trigger + short material flash.
        /// </summary>
        private void PlayLocalHitReaction()
        {
            if (dummyAnimator != null && !string.IsNullOrWhiteSpace(hitTriggerName))
                dummyAnimator.SetTrigger(hitTriggerName);

            if (flashRoutine != null)
                StopCoroutine(flashRoutine);

            flashRoutine = StartCoroutine(FlashRenderersRoutine());
        }

        private IEnumerator FlashRenderersRoutine()
        {
            Renderer[] renderers = flashRenderers;
            if (renderers == null || renderers.Length == 0)
                renderers = GetComponentsInChildren<Renderer>(true);

            if (renderers == null || renderers.Length == 0)
                yield break;

            const string baseColorName = "_BaseColor";
            const string colorName = "_Color";

            var states = new List<(Material mat, int propId, Color original)>(renderers.Length);

            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null)
                    continue;

                var mat = r.material;
                if (mat == null)
                    continue;

                if (mat.HasProperty(baseColorName))
                {
                    int pid = Shader.PropertyToID(baseColorName);
                    states.Add((mat, pid, mat.GetColor(pid)));
                    mat.SetColor(pid, flashColor);
                    continue;
                }

                if (mat.HasProperty(colorName))
                {
                    int pid = Shader.PropertyToID(colorName);
                    states.Add((mat, pid, mat.GetColor(pid)));
                    mat.SetColor(pid, flashColor);
                }
            }

            yield return new WaitForSeconds(Mathf.Max(0.01f, flashDuration));

            for (int i = 0; i < states.Count; i++)
            {
                var state = states[i];
                if (state.mat != null)
                    state.mat.SetColor(state.propId, state.original);
            }

            flashRoutine = null;
        }

        /// <summary>
        /// SERVER ONLY: Despawns this networked dummy when defeated.
        /// </summary>
        private void ServerDestroyDummy()
        {
            if (!IsServer)
                return;

            Debug.Log("[Combat] Dummy destroyed", this);

            if (NetworkObject != null && NetworkObject.IsSpawned)
            {
                NetworkObject.Despawn(true);
                return;
            }

            Destroy(gameObject);
        }
    }
}
