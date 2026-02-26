using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class SimpleNetUI : MonoBehaviour
{
    [Header("Connect Settings")]
    [Tooltip("If host + client are on the same PC, use 127.0.0.1")]
    [SerializeField] private string address = "127.0.0.1";

    [Tooltip("Must match host port.")]
    [SerializeField] private ushort port = 7777;

    [Header("UI Layout")]
    [SerializeField] private Rect window = new Rect(20, 20, 320, 160);

    private void OnGUI()
    {
        // If NetworkManager isn't present, show a warning.
        if (NetworkManager.Singleton == null)
        {
            GUI.Label(new Rect(20, 20, 600, 30), "No NetworkManager.Singleton found in scene.");
            return;
        }

        window = GUI.Window(0, window, DrawWindow, "Network");
    }

    private void DrawWindow(int id)
    {
        GUILayout.Label("Address (host IP):");
        address = GUILayout.TextField(address);

        GUILayout.Label("Port:");
        // Simple port edit (string conversion) for quick testing.
        string portStr = GUILayout.TextField(port.ToString());
        if (ushort.TryParse(portStr, out ushort parsedPort))
            port = parsedPort;

        GUILayout.Space(10);

        // Disable buttons if already running.
        bool isRunning = NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer;

        GUI.enabled = !isRunning;
        if (GUILayout.Button("Start Host"))
        {
            ConfigureTransport();
            NetworkManager.Singleton.StartHost();
            Debug.Log($"[NET] Host started on {address}:{port}");
        }

        if (GUILayout.Button("Start Client"))
        {
            ConfigureTransport();
            NetworkManager.Singleton.StartClient();
            Debug.Log($"[NET] Client connecting to {address}:{port}");
        }
        GUI.enabled = true;

        GUILayout.Space(6);

        // Useful status info
        GUILayout.Label($"IsServer: {NetworkManager.Singleton.IsServer}  IsClient: {NetworkManager.Singleton.IsClient}");
        GUILayout.Label($"LocalClientId: {NetworkManager.Singleton.LocalClientId}");

        // Allow dragging window
        GUI.DragWindow(new Rect(0, 0, 10000, 20));
    }

    private void ConfigureTransport()
    {
        // Make sure we're using UnityTransport (UTP).
        var utp = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (utp == null)
        {
            Debug.LogError("[NET] UnityTransport missing on NetworkManager.");
            return;
        }

        // IMPORTANT: Set connection data BEFORE starting host/client.
        utp.SetConnectionData(address, port);
    }
}