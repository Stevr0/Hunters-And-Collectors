using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Players
{
    /// <summary>
    /// LocalPlayerCameraFollower
    /// ------------------------------------------------------------
    /// Keeps a scene camera attached to the LOCAL player's object.
    ///
    /// Why this exists:
    /// - In NGO, the local player is spawned asynchronously.
    /// - In builds (especially), timing differs between host/client.
    /// - Additive scene loads can also cause temporary unbinding.
    ///
    /// Attach this to the Camera that should follow the owning client's player.
    /// IMPORTANT: This should run on *every client*, and bind only to that client's player.
    /// </summary>
    public sealed class LocalPlayerCameraFollower : MonoBehaviour
    {
        [Header("Follow")]
        [SerializeField] private Vector3 followOffset = new(0f, 12f, -8f);

        [Tooltip("Higher = tighter follow, lower = floaty follow.")]
        [SerializeField] private float followLerpSpeed = 12f;

        private Transform _target;
        private Coroutine _bindRoutine;

        private void OnEnable()
        {
            // Best signal: your own deterministic "local owner spawned" event.
            PlayerNetworkRoot.LocalOwnerSpawned += HandleLocalOwnerSpawned;

            var net = NetworkManager.Singleton;
            if (net != null)
            {
                // When the local client finishes connecting, try to bind.
                net.OnClientConnectedCallback += HandleClientConnected;

                // When scene loads complete (including additive), try to re-bind.
                if (net.SceneManager != null)
                    net.SceneManager.OnLoadEventCompleted += HandleLoadEventCompleted;
            }

            // Try immediately (works for host, and for clients when running single scene).
            TryBindToLocalPlayer();
        }

        private void OnDisable()
        {
            PlayerNetworkRoot.LocalOwnerSpawned -= HandleLocalOwnerSpawned;

            var net = NetworkManager.Singleton;
            if (net != null)
            {
                net.OnClientConnectedCallback -= HandleClientConnected;

                if (net.SceneManager != null)
                    net.SceneManager.OnLoadEventCompleted -= HandleLoadEventCompleted;
            }

            // Clean up any running retry coroutine.
            if (_bindRoutine != null)
            {
                StopCoroutine(_bindRoutine);
                _bindRoutine = null;
            }
        }

        private void LateUpdate()
        {
            if (_target == null)
                return;

            // Smooth follow. (LateUpdate reduces jitter if target moves in Update.)
            Vector3 desired = _target.position + followOffset;
            transform.position = Vector3.Lerp(transform.position, desired, followLerpSpeed * Time.deltaTime);

            // Simple look-at. (If you want pitch limits later, do it here.)
            transform.LookAt(_target.position);
        }

        private void HandleClientConnected(ulong clientId)
        {
            var net = NetworkManager.Singleton;
            if (net == null)
                return;

            // Only react when THIS machine's local client has connected.
            if (clientId != net.LocalClientId)
                return;

            BeginBindRoutine();
        }

        private void HandleLoadEventCompleted(
            string sceneName,
            UnityEngine.SceneManagement.LoadSceneMode loadSceneMode,
            List<ulong> clientsCompleted,
            List<ulong> clientsTimedOut)
        {
            var net = NetworkManager.Singleton;
            if (net == null)
                return;

            // Only rebind when THIS client completed the load.
            if (clientsCompleted.Contains(net.LocalClientId))
                BeginBindRoutine();
        }

        private void HandleLocalOwnerSpawned(PlayerNetworkRoot localOwner)
        {
            // This is the most reliable path: the player's own OnNetworkSpawn fired with IsOwner.
            Bind(localOwner.transform);
        }

        private void BeginBindRoutine()
        {
            if (_bindRoutine != null)
                StopCoroutine(_bindRoutine);

            _bindRoutine = StartCoroutine(BindWhenReady());
        }

        private IEnumerator BindWhenReady()
        {
            const float timeoutSeconds = 5f;
            float elapsed = 0f;

            while (elapsed < timeoutSeconds)
            {
                if (TryBindToLocalPlayer())
                {
                    _bindRoutine = null;
                    yield break;
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            _bindRoutine = null;
            Debug.LogWarning("[LocalPlayerCameraFollower] Timed out waiting for local player object; camera target not set.");
        }

        /// <summary>
        /// Attempts to bind camera to the local player's NetworkObject.
        /// Returns true if successful.
        /// </summary>
        private bool TryBindToLocalPlayer()
        {
            var net = NetworkManager.Singleton;

            // Not started yet? Nothing to bind to.
            if (net == null || !net.IsListening)
                return false;

            // CLIENT-SAFE WAY:
            // SpawnManager keeps track of the local player object on clients.
            NetworkObject localPlayer = null;

            if (net.SpawnManager != null)
                localPlayer = net.SpawnManager.GetLocalPlayerObject();

            // HOST/SERVER FALLBACK (optional):
            // ConnectedClients is reliable on host/server, but not always on pure clients.
            if (localPlayer == null && net.IsServer)
            {
                if (net.ConnectedClients.TryGetValue(net.LocalClientId, out var localClient))
                    localPlayer = localClient.PlayerObject;
            }

            if (localPlayer == null)
                return false;

            Bind(localPlayer.transform);
            return true;
        }

        private void Bind(Transform target)
        {
            if (target == null)
                return;

            // Optional: if you ever bind to a child target (CameraTarget), you can pass that here instead.
            _target = target;

            // Snap camera instantly on bind to avoid one-frame weirdness from old position.
            transform.position = _target.position + followOffset;
            transform.LookAt(_target.position);

            // Useful debug info (NetworkObject may not exist if someone passes a non-network target by mistake).
            var no = _target.GetComponent<NetworkObject>();
            Debug.Log($"[LocalPlayerCameraFollower] Bound camera to target='{_target.name}' netId={no?.NetworkObjectId} ownerLocalClientId={NetworkManager.Singleton?.LocalClientId}");
        }
    }
}
