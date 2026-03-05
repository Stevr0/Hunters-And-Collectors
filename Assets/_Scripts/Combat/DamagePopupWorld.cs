using System.Collections;
using UnityEngine;

#if TMP_PRESENT
using TMPro;
#endif

namespace HuntersAndCollectors.Combat
{
    /// <summary>
    /// Lightweight world-space damage popup.
    /// This is purely visual and intentionally not networked.
    /// </summary>
    public sealed class DamagePopupWorld : MonoBehaviour
    {
        [SerializeField] private float lifetimeSeconds = 0.75f;
        [SerializeField] private float riseSpeed = 1.1f;

#if TMP_PRESENT
        private TextMeshPro textTmp;
#else
        private TextMesh textBasic;
#endif

        private float elapsed;
        private Color startColor = Color.yellow;

        /// <summary>
        /// Creates a popup in world space at the requested position.
        /// </summary>
        public static void Spawn(int amount, Vector3 worldPos)
        {
            GameObject go = new($"DamagePopup_{amount}");
            go.transform.position = worldPos;

            var popup = go.AddComponent<DamagePopupWorld>();
            popup.Initialize(amount);
        }

        private void Initialize(int amount)
        {
#if TMP_PRESENT
            textTmp = gameObject.AddComponent<TextMeshPro>();
            textTmp.text = amount.ToString();
            textTmp.fontSize = 4f;
            textTmp.alignment = TextAlignmentOptions.Center;
            textTmp.color = startColor;
#else
            textBasic = gameObject.AddComponent<TextMesh>();
            textBasic.text = amount.ToString();
            textBasic.fontSize = 48;
            textBasic.characterSize = 0.07f;
            textBasic.anchor = TextAnchor.MiddleCenter;
            textBasic.alignment = TextAlignment.Center;
            textBasic.color = startColor;
#endif

            StartCoroutine(LifeRoutine());
        }

        private IEnumerator LifeRoutine()
        {
            while (elapsed < lifetimeSeconds)
            {
                float dt = Time.deltaTime;
                elapsed += dt;

                // Float upward over time.
                transform.position += Vector3.up * (riseSpeed * dt);

                // Face camera so text is readable in world space.
                var cam = Camera.main;
                if (cam != null)
                {
                    Vector3 forward = transform.position - cam.transform.position;
                    if (forward.sqrMagnitude > 0.0001f)
                        transform.forward = forward.normalized;
                }

                float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, lifetimeSeconds));
                float alpha = 1f - t;
                SetAlpha(alpha);

                yield return null;
            }

            Destroy(gameObject);
        }

        private void SetAlpha(float alpha)
        {
            Color c = startColor;
            c.a = alpha;

#if TMP_PRESENT
            if (textTmp != null)
                textTmp.color = c;
#else
            if (textBasic != null)
                textBasic.color = c;
#endif
        }
    }
}
