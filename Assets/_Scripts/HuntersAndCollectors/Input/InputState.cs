using UnityEngine;

namespace HuntersAndCollectors.Input
{
    /// <summary>
    /// Global input state flags.
    /// ------------------------------------------------------
    /// Used to temporarily disable gameplay input when UI is open.
    /// 
    /// This avoids tightly coupling movement scripts to specific UI windows.
    /// </summary>
    public static class InputState
    {
        /// <summary>
        /// When true, gameplay look/move input should be ignored.
        /// </summary>
        public static bool GameplayLocked;

        public static void LockGameplay()
        {
            GameplayLocked = true;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public static void UnlockGameplay()
        {
            GameplayLocked = false;

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}
