using UnityEngine;
using UnityEngine.UI;

namespace HuntersAndCollectors.UI
{
    /// <summary>
    /// UIDragGhost
    /// --------------------------------------------------------------------
    /// Simple UI "ghost" image that follows the mouse while dragging.
    ///
    /// - Client-only visual.
    /// - Does NOT change inventory state.
    /// - Drag/drop state changes happen only via ServerRpc elsewhere.
    /// </summary>
    public sealed class UIDragGhost : MonoBehaviour
    {
        [SerializeField] private Canvas rootCanvas;
        [SerializeField] private Image iconImage;

        private RectTransform _rt;

        private void Awake()
        {
            _rt = (RectTransform)transform;

            if (rootCanvas == null)
                rootCanvas = GetComponentInParent<Canvas>();

            Hide();
        }

        private void Update()
        {
            // Follow mouse every frame while active.
            if (!gameObject.activeSelf)
                return;

            if (rootCanvas == null)
                return;

            // Screen -> Canvas local point
            RectTransform canvasRt = (RectTransform)rootCanvas.transform;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRt,
                    Input.mousePosition,
                    rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : rootCanvas.worldCamera,
                    out Vector2 localPos))
            {
                _rt.anchoredPosition = localPos;
            }
        }

        public void Show(Sprite icon)
        {
            if (iconImage != null)
            {
                iconImage.sprite = icon;
                iconImage.enabled = icon != null;
            }

            gameObject.SetActive(true);
        }

        public void Hide()
        {
            gameObject.SetActive(false);

            if (iconImage != null)
            {
                iconImage.sprite = null;
                iconImage.enabled = false;
            }
        }
    }
}
