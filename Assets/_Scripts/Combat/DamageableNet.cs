using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Combat
{
    /// <summary>
    /// Adapter that makes a NetworkObject damageable through IDamageableNet.
    ///
    /// Responsibilities:
    /// - Delegates server damage to HealthNet.
    /// - Hosts local-only hit reaction visuals (anim trigger + flash).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(HealthNet))]
    public sealed class DamageableNet : NetworkBehaviour, IDamageableNet
    {
        [Header("References")]
        [SerializeField] private HealthNet health;

        [Tooltip("Optional animator to trigger local hit reaction.")]
        [SerializeField] private Animator animator;

        [Tooltip("Optional explicit renderers to flash. If empty, children are auto-used.")]
        [SerializeField] private Renderer[] flashRenderers;

        [Header("Hit Reaction")]
        [SerializeField] private string hitTriggerName = "Hit";
        [SerializeField] private Color flashColor = Color.white;
        [Min(0.01f)]
        [SerializeField] private float flashDuration = 0.1f;

        private Coroutine flashRoutine;

        private void Awake()
        {
            if (health == null)
                health = GetComponent<HealthNet>();

            if (animator == null)
                animator = GetComponentInChildren<Animator>();
        }

        /// <summary>
        /// SERVER ONLY: applies damage through HealthNet.
        /// </summary>
        public bool ServerTryApplyDamage(int amount, ulong attackerClientId, Vector3 hitPoint)
        {
            if (!IsServer)
                return false;

            if (health == null)
                health = GetComponent<HealthNet>();

            if (health == null)
                return false;

            return health.ServerApplyDamage(amount, attackerClientId, hitPoint);
        }

        /// <summary>
        /// LOCAL ONLY: called by HealthNet's ClientRpc to play visual hit feedback.
        /// </summary>
        public void PlayHitReactionLocal()
        {
            if (animator != null && !string.IsNullOrWhiteSpace(hitTriggerName))
                animator.SetTrigger(hitTriggerName);

            if (flashRoutine != null)
                StopCoroutine(flashRoutine);

            flashRoutine = StartCoroutine(FlashRoutine());
        }

        private IEnumerator FlashRoutine()
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
                Renderer r = renderers[i];
                if (r == null)
                    continue;

                Material mat = r.material;
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
    }
}
