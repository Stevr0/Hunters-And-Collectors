using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

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
            if (!gameObject.activeSelf)
                return;

            if (rootCanvas == null)
                return;

            // If no mouse device (rare but safe check)
            if (Mouse.current == null)
                return;

            Vector2 mousePos = Mouse.current.position.ReadValue();

            RectTransform canvasRt = (RectTransform)rootCanvas.transform;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRt,
                    mousePos,
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