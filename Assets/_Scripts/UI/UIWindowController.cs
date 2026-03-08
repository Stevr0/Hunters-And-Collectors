using System;
using System.Collections.Generic;
using HuntersAndCollectors.Inventory.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HuntersAndCollectors.UI
{
    /// <summary>
    /// UIWindowController
    /// --------------------------------------------------------------------
    /// Centralized client-side UI window toggle manager.
    ///
    /// Design goals:
    /// - Keep keybind-driven window toggles in one place.
    /// - Let each window remain UI-only (no gameplay authority mutation).
    /// - Support special inventory behavior where only lower rows expand/collapse.
    /// </summary>
    public sealed class UIWindowController : MonoBehaviour
    {
        [Serializable]
        public sealed class ManagedWindow
        {
            [Tooltip("Stable window id used by Open/Close/Toggle APIs.")]
            public string Id;

            [Tooltip("Root GameObject that is shown/hidden.")]
            public GameObject Root;

            [Tooltip("Optional input action used to toggle this window.")]
            public InputActionReference ToggleAction;

            [Tooltip("If true, this window starts open on enable.")]
            public bool StartOpen;

            [Tooltip("If true, opening this window will close other exclusive windows.")]
            public bool Exclusive;

            [Tooltip("If false, CloseAllWindows() will ignore this window.")]
            public bool IncludedInCloseAll = true;
        }

        [Header("Managed Popup Windows")]
        [SerializeField] private List<ManagedWindow> managedWindows = new();

        [Header("Inventory Special Case")]
        [Tooltip("Special inventory presenter that keeps hotbar row visible at all times.")]
        [SerializeField] private PlayerInventoryWindowUI inventoryWindow;

        [Tooltip("Window id used when controlling inventory expansion via generic APIs.")]
        [SerializeField] private string inventoryWindowId = "inventory";

        [Tooltip("Action that toggles inventory expanded rows (default: I).")]
        [SerializeField] private InputActionReference toggleInventoryExpandedAction;

        [Tooltip("If true, inventory rows 1..3 start expanded.")]
        [SerializeField] private bool inventoryStartExpanded;

        [Tooltip("If true, CloseAllWindows() also collapses inventory rows 1..3.")]
        [SerializeField] private bool closeInventoryExpansionInCloseAll = true;

        private readonly Dictionary<string, ManagedWindow> windowsById = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<InputAction, Action<InputAction.CallbackContext>> actionCallbacks = new();

        private void Awake()
        {
            if (inventoryWindow == null)
                inventoryWindow = FindFirstObjectByType<PlayerInventoryWindowUI>(FindObjectsInactive.Include);

            RebuildWindowLookup();
        }

        private void OnEnable()
        {
            RebuildWindowLookup();
            ApplyInitialWindowStates();
            SubscribeAllInputActions();
        }

        private void OnDisable()
        {
            UnsubscribeAllInputActions();
        }

        /// <summary>
        /// Toggle a registered window by id.
        /// </summary>
        public bool ToggleWindow(string id)
        {
            if (TryHandleInventorySpecialCase(id, WindowOperation.Toggle, out bool inventoryHandled))
                return inventoryHandled;

            if (!windowsById.TryGetValue(id, out ManagedWindow managed) || managed == null || managed.Root == null)
                return false;

            bool next = !managed.Root.activeSelf;
            SetWindowOpenState(managed, next);
            return true;
        }

        /// <summary>
        /// Open a registered window by id.
        /// </summary>
        public bool OpenWindow(string id)
        {
            if (TryHandleInventorySpecialCase(id, WindowOperation.Open, out bool inventoryHandled))
                return inventoryHandled;

            if (!windowsById.TryGetValue(id, out ManagedWindow managed) || managed == null || managed.Root == null)
                return false;

            SetWindowOpenState(managed, true);
            return true;
        }

        /// <summary>
        /// Close a registered window by id.
        /// </summary>
        public bool CloseWindow(string id)
        {
            if (TryHandleInventorySpecialCase(id, WindowOperation.Close, out bool inventoryHandled))
                return inventoryHandled;

            if (!windowsById.TryGetValue(id, out ManagedWindow managed) || managed == null || managed.Root == null)
                return false;

            SetWindowOpenState(managed, false);
            return true;
        }

        /// <summary>
        /// Close all registered windows.
        /// </summary>
        public void CloseAllWindows()
        {
            foreach (ManagedWindow managed in managedWindows)
            {
                if (managed == null || managed.Root == null || !managed.IncludedInCloseAll)
                    continue;

                managed.Root.SetActive(false);
            }

            if (closeInventoryExpansionInCloseAll && inventoryWindow != null)
                inventoryWindow.SetExpanded(false);
        }

        /// <summary>
        /// Returns true if a registered window is currently open.
        /// </summary>
        public bool IsWindowOpen(string id)
        {
            if (string.Equals(id, inventoryWindowId, StringComparison.OrdinalIgnoreCase))
                return inventoryWindow != null && inventoryWindow.IsExpanded();

            if (!windowsById.TryGetValue(id, out ManagedWindow managed) || managed == null || managed.Root == null)
                return false;

            return managed.Root.activeSelf;
        }

        private enum WindowOperation
        {
            Open,
            Close,
            Toggle
        }

        private bool TryHandleInventorySpecialCase(string id, WindowOperation op, out bool handled)
        {
            handled = false;
            if (!string.Equals(id, inventoryWindowId, StringComparison.OrdinalIgnoreCase))
                return false;

            if (inventoryWindow == null)
                return true;

            switch (op)
            {
                case WindowOperation.Open:
                    inventoryWindow.SetExpanded(true);
                    break;
                case WindowOperation.Close:
                    inventoryWindow.SetExpanded(false);
                    break;
                case WindowOperation.Toggle:
                    inventoryWindow.ToggleExpanded();
                    break;
            }

            handled = true;
            return true;
        }

        private void RebuildWindowLookup()
        {
            windowsById.Clear();

            for (int i = 0; i < managedWindows.Count; i++)
            {
                ManagedWindow managed = managedWindows[i];
                if (managed == null || string.IsNullOrWhiteSpace(managed.Id))
                    continue;

                string key = managed.Id.Trim();
                if (!windowsById.ContainsKey(key))
                    windowsById.Add(key, managed);
            }
        }

        private void ApplyInitialWindowStates()
        {
            for (int i = 0; i < managedWindows.Count; i++)
            {
                ManagedWindow managed = managedWindows[i];
                if (managed == null || managed.Root == null)
                    continue;

                managed.Root.SetActive(managed.StartOpen);
            }

            if (inventoryWindow != null)
                inventoryWindow.SetExpanded(inventoryStartExpanded);
        }

        private void SubscribeAllInputActions()
        {
            UnsubscribeAllInputActions();

            for (int i = 0; i < managedWindows.Count; i++)
            {
                ManagedWindow managed = managedWindows[i];
                if (managed == null || managed.ToggleAction == null || string.IsNullOrWhiteSpace(managed.Id))
                    continue;

                InputAction action = managed.ToggleAction.action;
                if (action == null)
                    continue;

                action.actionMap?.Enable();

                string id = managed.Id.Trim();
                Action<InputAction.CallbackContext> callback = _ => ToggleWindow(id);
                action.performed += callback;
                actionCallbacks[action] = callback;
            }

            if (toggleInventoryExpandedAction != null)
            {
                InputAction action = toggleInventoryExpandedAction.action;
                if (action != null)
                {
                    action.actionMap?.Enable();

                    Action<InputAction.CallbackContext> callback = _ => ToggleWindow(inventoryWindowId);
                    action.performed += callback;
                    actionCallbacks[action] = callback;
                }
            }
        }

        private void UnsubscribeAllInputActions()
        {
            foreach (KeyValuePair<InputAction, Action<InputAction.CallbackContext>> kvp in actionCallbacks)
            {
                if (kvp.Key == null || kvp.Value == null)
                    continue;

                kvp.Key.performed -= kvp.Value;
            }

            actionCallbacks.Clear();
        }

        private void SetWindowOpenState(ManagedWindow managed, bool open)
        {
            if (managed == null || managed.Root == null)
                return;

            if (open && managed.Exclusive)
            {
                for (int i = 0; i < managedWindows.Count; i++)
                {
                    ManagedWindow other = managedWindows[i];
                    if (other == null || other == managed || !other.Exclusive || other.Root == null)
                        continue;

                    other.Root.SetActive(false);
                }
            }

            managed.Root.SetActive(open);
        }
    }
}
