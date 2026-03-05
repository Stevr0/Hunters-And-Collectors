using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace HuntersAndCollectors.Combat
{
    /// <summary>
    /// World-space health bar view bound to HealthNet.
    ///
    /// Read-only UI role:
    /// - Subscribes to replicated health changes.
    /// - Updates local visual fill amount.
    /// - Never writes gameplay health.
    /// </summary>
    public sealed class DummyHealthBarWorld : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private HealthNet health;
        [SerializeField] private Image fillImage;

        [Header("View")]
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
            if (health == null)
                health = GetComponentInParent<HealthNet>() ?? GetComponent<HealthNet>();

            if (fillImage == null)
                fillImage = GetComponentInChildren<Image>(true);

            if (health == null)
            {
                Debug.LogWarning("[Combat] DummyHealthBarWorld could not find HealthNet.", this);
                return;
            }

            if (fillImage == null)
            {
                Debug.LogWarning("[Combat] DummyHealthBarWorld is missing fill Image reference.", this);
                return;
            }

            if (!health.IsSpawned)
            {
                if (waitForSpawnRoutine == null)
                    waitForSpawnRoutine = StartCoroutine(WaitForHealthSpawnRoutine());
                return;
            }

            Subscribe();
            Refresh();
        }

        private IEnumerator WaitForHealthSpawnRoutine()
        {
            while (health != null && !health.IsSpawned)
                yield return null;

            waitForSpawnRoutine = null;

            if (health != null)
            {
                Subscribe();
                Refresh();
            }
        }

        private void Subscribe()
        {
            if (subscribed || health == null)
                return;

            health.CurrentHealthNetVar.OnValueChanged += OnHealthChanged;
            subscribed = true;
        }

        private void Unbind()
        {
            if (!subscribed || health == null)
                return;

            health.CurrentHealthNetVar.OnValueChanged -= OnHealthChanged;
            subscribed = false;
        }

        private void OnHealthChanged(int previousValue, int newValue)
        {
            Refresh();
        }

        private void Refresh()
        {
            if (health == null || fillImage == null)
                return;

            float ratio = health.Health01;
            fillImage.fillAmount = ratio;

            if (fillImage.type != Image.Type.Filled)
            {
                Vector3 s = fillImage.rectTransform.localScale;
                s.x = Mathf.Max(0.0001f, ratio);
                fillImage.rectTransform.localScale = s;
            }
        }
    }
}
