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

        private static int lockCount;

        public static void LockGameplay()
        {
            lockCount++;
            GameplayLocked = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public static void UnlockGameplay()
        {
            lockCount = Mathf.Max(0, lockCount - 1);
            GameplayLocked = lockCount > 0;

            if (!GameplayLocked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        /// <summary>
        /// Hard reset for gameplay locks.
        ///
        /// Use sparingly for explicit UX transitions where gameplay must be enabled
        /// even if one or more UI systems previously requested a lock.
        /// </summary>
        public static void ForceUnlockGameplay()
        {
            lockCount = 0;
            GameplayLocked = false;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}
