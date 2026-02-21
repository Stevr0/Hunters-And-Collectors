using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Players
{
    /// <summary>
    /// Keeps a scene camera attached to the local player's NetworkObject after join/spawn/scene load.
    /// Attach this to the Camera that should follow the owning client's player.
    /// </summary>
    public sealed class LocalPlayerCameraFollower : MonoBehaviour
    {
        [Header("Follow")]
        [SerializeField] private Vector3 followOffset = new(0f, 12f, -8f);
        [SerializeField] private float followLerpSpeed = 12f;

        private Transform _target;
        private Coroutine _bindRoutine;

        private void OnEnable()
        {
            PlayerNetworkRoot.LocalOwnerSpawned += HandleLocalOwnerSpawned;

            var net = NetworkManager.Singleton;
            if (net != null)
            {
                net.OnClientConnectedCallback += HandleClientConnected;

                if (net.SceneManager != null)
                    net.SceneManager.OnLoadEventCompleted += HandleLoadEventCompleted;
            }

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

            var desired = _target.position + followOffset;
            transform.position = Vector3.Lerp(transform.position, desired, followLerpSpeed * Time.deltaTime);
            transform.LookAt(_target.position);
        }

        private void HandleClientConnected(ulong clientId)
        {
            var net = NetworkManager.Singleton;
            if (net == null || clientId != net.LocalClientId)
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

            if (clientsCompleted.Contains(net.LocalClientId))
                BeginBindRoutine();
        }

        private void HandleLocalOwnerSpawned(PlayerNetworkRoot localOwner)
        {
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
            var elapsed = 0f;

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

        private bool TryBindToLocalPlayer()
        {
            var net = NetworkManager.Singleton;
            if (net == null || !net.IsListening)
                return false;

            if (!net.ConnectedClients.TryGetValue(net.LocalClientId, out var localClient))
                return false;

            if (localClient.PlayerObject == null)
                return false;

            Bind(localClient.PlayerObject.transform);
            return true;
        }

        private void Bind(Transform target)
        {
            _target = target;
            transform.position = _target.position + followOffset;
            transform.LookAt(_target.position);

            Debug.Log($"[LocalPlayerCameraFollower] Bound camera to local player netId={_target.GetComponent<NetworkObject>()?.NetworkObjectId} owner={NetworkManager.Singleton?.LocalClientId}");
        }
    }
}
