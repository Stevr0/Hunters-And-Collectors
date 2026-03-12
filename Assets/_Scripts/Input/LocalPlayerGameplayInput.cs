using HuntersAndCollectors.Building;
using HuntersAndCollectors.Combat;
using HuntersAndCollectors.Players;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace HuntersAndCollectors.Input
{
    /// <summary>
    /// LocalPlayerGameplayInput
    /// --------------------------------------------------------------------
    /// Single owner-side gameplay input entry point for attack + interact.
    ///
    /// Why this exists:
    /// - Movement already reads owner input separately for server-authoritative movement.
    /// - Camera already reads owner input separately for local-only presentation.
    /// - Gameplay actions should have ONE clear owner-side path so attack/interact
    ///   do not compete with each other or create duplicate InputAction listeners.
    ///
    /// Authority model:
    /// - This script only reads local input on the owning client.
    /// - It never mutates authoritative gameplay state directly.
    /// - It only forwards requests to PlayerInteract / PlayerAttackNet, which then
    ///   use their existing validated ServerRpc flows.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerInteract))]
    [RequireComponent(typeof(PlayerAttackNet))]
    public sealed class LocalPlayerGameplayInput : NetworkBehaviour
    {
        private enum PrimaryRoute
        {
            None,
            Interaction,
            Combat
        }

        [Header("Optional References")]
        [Tooltip("Used to temporarily suppress gameplay actions while local build placement mode is active.")]
        [SerializeField] private BuildPlacementController buildPlacement;

        [SerializeField] private PlayerInteract playerInteract;
        [SerializeField] private PlayerAttackNet playerAttack;

        private PlayerInputActions input;
        private bool primaryHeld;
        private PrimaryRoute activePrimaryRoute;

        private void Awake()
        {
            if (playerInteract == null)
                playerInteract = GetComponent<PlayerInteract>();

            if (playerAttack == null)
                playerAttack = GetComponent<PlayerAttackNet>();

            if (buildPlacement == null)
                buildPlacement = GetComponent<BuildPlacementController>();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsOwner || !IsClient)
            {
                enabled = false;
                return;
            }

            input = new PlayerInputActions();
            input.Player.Primary.started += OnPrimaryStarted;
            input.Player.Primary.canceled += OnPrimaryCanceled;
            input.Player.Interact.performed += OnInteractPerformed;
            input.Enable();
        }

        private void OnDisable()
        {
            primaryHeld = false;
            activePrimaryRoute = PrimaryRoute.None;

            if (playerInteract != null)
                playerInteract.EndPrimaryInput();

            if (playerAttack != null)
                playerAttack.EndPrimaryInput();

            if (input == null)
                return;

            input.Player.Primary.started -= OnPrimaryStarted;
            input.Player.Primary.canceled -= OnPrimaryCanceled;
            input.Player.Interact.performed -= OnInteractPerformed;
            input.Disable();
            input.Dispose();
            input = null;
        }

        private void Update()
        {
            if (!IsOwner || !IsClient)
                return;

            playerInteract?.RefreshFocus();

            if (!primaryHeld)
                return;

            if (!CanProcessGameplayInput())
                return;

            switch (activePrimaryRoute)
            {
                case PrimaryRoute.Interaction:
                    playerInteract?.TickHeldPrimaryInput();
                    break;

                case PrimaryRoute.Combat:
                    playerAttack?.TickHeldPrimaryInput();
                    break;
            }
        }

        private void OnPrimaryStarted(InputAction.CallbackContext context)
        {
            if (!context.started)
                return;

            primaryHeld = true;
            activePrimaryRoute = PrimaryRoute.None;

            if (!CanProcessGameplayInput())
                return;

            if (playerInteract != null)
                playerInteract.RefreshFocus();

            // Interaction gets first chance so harvesting / pickup can cleanly win over combat
            // when the reticle is sitting on an interaction target.
            if (playerInteract != null && playerInteract.BeginPrimaryInput())
            {
                activePrimaryRoute = PrimaryRoute.Interaction;
                return;
            }

            if (playerAttack != null && playerAttack.BeginPrimaryInput())
                activePrimaryRoute = PrimaryRoute.Combat;
        }

        private void OnPrimaryCanceled(InputAction.CallbackContext context)
        {
            if (!context.canceled)
                return;

            primaryHeld = false;

            playerInteract?.EndPrimaryInput();
            playerAttack?.EndPrimaryInput();

            activePrimaryRoute = PrimaryRoute.None;
        }

        private void OnInteractPerformed(InputAction.CallbackContext context)
        {
            if (!context.performed)
                return;

            if (!CanProcessGameplayInput())
                return;

            playerInteract?.RefreshFocus();
            playerInteract?.TryInteractPressed();
        }

        private bool CanProcessGameplayInput()
        {
            if (InputState.GameplayLocked)
                return false;

            if (buildPlacement != null && buildPlacement.IsPlacementActive)
                return false;

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return false;

            return true;
        }
    }
}
