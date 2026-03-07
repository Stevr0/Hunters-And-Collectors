using UnityEngine;

namespace HuntersAndCollectors.Building
{
    /// <summary>
    /// BuildGhostView
    /// --------------------------------------------------------------------
    /// Local-only visual helper for placement ghost rendering.
    ///
    /// Responsibilities:
    /// - Apply green/red preview color state.
    /// - Avoid touching shared materials globally by using MaterialPropertyBlock.
    /// - Keep behavior simple and deterministic.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BuildGhostView : MonoBehaviour
    {
        [Header("Preview Colors")]
        [SerializeField] private Color validColor = new(0.1f, 1f, 0.2f, 0.45f);
        [SerializeField] private Color invalidColor = new(1f, 0.15f, 0.15f, 0.45f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private readonly MaterialPropertyBlock propertyBlock = new();

        private Renderer[] cachedRenderers;
        private bool isCurrentlyValid;
        private bool hasAppliedState;

        private void Awake()
        {
            CacheRenderers();
            ConfigureRenderersForGhost();
        }

        /// <summary>
        /// Sets the ghost color state.
        /// True = green/valid, false = red/invalid.
        /// </summary>
        public void SetPreviewValid(bool isValid)
        {
            if (hasAppliedState && isCurrentlyValid == isValid)
                return;

            isCurrentlyValid = isValid;
            hasAppliedState = true;

            Color target = isValid ? validColor : invalidColor;
            ApplyTint(target);
        }

        private void CacheRenderers()
        {
            cachedRenderers = GetComponentsInChildren<Renderer>(true);
        }

        private void ConfigureRenderersForGhost()
        {
            if (cachedRenderers == null)
                return;

            for (int i = 0; i < cachedRenderers.Length; i++)
            {
                Renderer renderer = cachedRenderers[i];
                if (renderer == null)
                    continue;

                // Ghosts are local visuals only; disable shadows to reduce visual noise.
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        private void ApplyTint(Color color)
        {
            if (cachedRenderers == null)
                return;

            for (int i = 0; i < cachedRenderers.Length; i++)
            {
                Renderer renderer = cachedRenderers[i];
                if (renderer == null)
                    continue;

                renderer.GetPropertyBlock(propertyBlock);
                propertyBlock.SetColor(BaseColorId, color);
                propertyBlock.SetColor(ColorId, color);
                renderer.SetPropertyBlock(propertyBlock);
            }
        }
    }
}
