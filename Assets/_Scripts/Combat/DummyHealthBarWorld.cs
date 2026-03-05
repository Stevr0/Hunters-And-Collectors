using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace HuntersAndCollectors.Combat
{
    /// <summary>
    /// World-space health bar view for TrainingDummyNet.
    ///
    /// This script is read-only visual logic:
    /// - Subscribes to dummy health NetworkVariable changes.
    /// - Updates a fill image ratio for all clients.
    /// - Never changes gameplay state or health authority.
    /// </summary>
    public sealed class DummyHealthBarWorld : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Dummy source. If null, auto-finds on this object/parents.")]
        [SerializeField] private TrainingDummyNet dummy;

        [Tooltip("UI image used as the fill bar.")]
        [SerializeField] private Image fillImage;

        [Header("View")]
        [Tooltip("If true, rotates this transform to face Camera.main.")]
        [SerializeField] private bool billboardToCamera = true;

        private bool subscribed;
        private Coroutine waitForSpawnRoutine;

        private void Start()
        {
            TryBind();
            Refresh();
        }

        private void OnEnable()
        {
            TryBind();
        }

        private void LateUpdate()
        {
            if (!billboardToCamera)
                return;

            var cam = Camera.main;
            if (cam == null)
                return;

            Vector3 forward = transform.position - cam.transform.position;
            if (forward.sqrMagnitude > 0.0001f)
                transform.forward = forward.normalized;
        }

        private void OnDisable()
        {
            Unbind();

            if (waitForSpawnRoutine != null)
            {
                StopCoroutine(waitForSpawnRoutine);
                waitForSpawnRoutine = null;
            }
        }

        private void OnDestroy()
        {
            Unbind();
        }

        private void TryBind()
        {
            if (dummy == null)
                dummy = GetComponentInParent<TrainingDummyNet>() ?? GetComponent<TrainingDummyNet>();

            if (fillImage == null)
                fillImage = GetComponentInChildren<Image>(true);

            if (dummy == null)
            {
                Debug.LogWarning("[Combat] DummyHealthBarWorld could not find TrainingDummyNet.", this);
                return;
            }

            if (fillImage == null)
            {
                Debug.LogWarning("[Combat] DummyHealthBarWorld is missing fill Image reference.", this);
                return;
            }

            if (!dummy.IsSpawned)
            {
                if (waitForSpawnRoutine == null)
                    waitForSpawnRoutine = StartCoroutine(WaitForDummySpawnRoutine());
                return;
            }

            Subscribe();
            Refresh();
        }

        private IEnumerator WaitForDummySpawnRoutine()
        {
            while (dummy != null && !dummy.IsSpawned)
                yield return null;

            waitForSpawnRoutine = null;

            if (dummy != null)
            {
                Subscribe();
                Refresh();
            }
        }

        private void Subscribe()
        {
            if (subscribed || dummy == null)
                return;

            // Read-only subscription to replicated server health.
            dummy.HealthNetVar.OnValueChanged += OnHealthChanged;
            subscribed = true;
        }

        private void Unbind()
        {
            if (!subscribed || dummy == null)
                return;

            dummy.HealthNetVar.OnValueChanged -= OnHealthChanged;
            subscribed = false;
        }

        private void OnHealthChanged(int previousValue, int newValue)
        {
            Refresh();
        }

        private void Refresh()
        {
            if (dummy == null || fillImage == null)
                return;

            int max = Mathf.Max(1, dummy.MaxHealth);
            int current = Mathf.Clamp(dummy.CurrentHealth, 0, max);
            float ratio = Mathf.Clamp01((float)current / max);

            // Preferred path for standard UI fill images.
            fillImage.fillAmount = ratio;

            // Fallback when image type is not Filled.
            if (fillImage.type != Image.Type.Filled)
            {
                Vector3 s = fillImage.rectTransform.localScale;
                s.x = Mathf.Max(0.0001f, ratio);
                fillImage.rectTransform.localScale = s;
            }
        }
    }
}
