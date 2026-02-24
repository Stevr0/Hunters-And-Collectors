using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.UI
{
    /// <summary>
    /// UIInputGate
    /// --------------------------------------------------------------------
    /// Central "UI is open" gate.
    ///
    /// Responsibilities:
    /// - Manage cursor visibility + lock state.
    /// - Disable/enable gameplay scripts (movement + mouse look) on the LOCAL player.
    ///
    /// Why central:
    /// - Every window behaves consistently.
    /// - Prevents duplicated cursor code in each window/controller.
    /// - Easy to extend later (e.g., block interact, block hotkeys, etc).
    ///
    /// MVP behaviour:
    /// - Any window can call SetUiOpen(true/false).
    /// - We internally reference-count so multiple windows can be open without conflicts.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UIInputGate : MonoBehaviour
    {
        [Header("Gameplay Scripts To Disable While UI Open")]
        [Tooltip("Disable this component on local player when any UI window is open.")]
        [SerializeField] private string movementComponentTypeName = "HuntersAndCollectors.Players.PlayerMovement";

        // Reference count: how many UI windows currently claim the UI is open.
        // This prevents "close crafting" from re-locking cursor if, say, inventory is still open.
        private int _uiOpenCount = 0;

        /// <summary>
        /// Call when a window opens/closes.
        /// </summary>
        public void SetUiOpen(bool open)
        {
            if (open)
                _uiOpenCount++;
            else
                _uiOpenCount = Mathf.Max(0, _uiOpenCount - 1);

            Apply();
        }

        /// <summary>
        /// If you want an explicit "force closed" (e.g. on disconnect), call this.
        /// </summary>
        public void ForceClosed()
        {
            _uiOpenCount = 0;
            Apply();
        }

        private void Apply()
        {
            bool uiOpen = _uiOpenCount > 0;

            // Cursor rules
            Cursor.visible = uiOpen;
            Cursor.lockState = uiOpen ? CursorLockMode.None : CursorLockMode.Locked;

            // Disable movement/look on the local player
            var localPlayer = FindLocalPlayer();
            if (localPlayer == null)
                return;

            // We disable by type name so you don't have to hard-reference your movement script type here.
            // If you rename PlayerMovement, update movementComponentTypeName in inspector.
            var movement = localPlayer.GetComponent(movementComponentTypeName) as Behaviour;
            if (movement != null)
                movement.enabled = !uiOpen;
        }

        private GameObject FindLocalPlayer()
        {
            if (NetworkManager.Singleton == null)
                return null;

            foreach (var kvp in NetworkManager.Singleton.SpawnManager.SpawnedObjects)
            {
                var netObj = kvp.Value;
                if (netObj == null) continue;
                if (!netObj.IsOwner) continue;

                return netObj.gameObject;
            }
            return null;
        }
    }
}
