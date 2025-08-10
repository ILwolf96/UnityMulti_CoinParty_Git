using System;
using System.Threading.Tasks;
using UnityEngine;
using Fusion;
using Fusion.Sockets;
using UnityEngine.SceneManagement;

public class NetworkManager : MonoBehaviour, INetworkRunnerCallbacks
{
    public static NetworkManager Instance { get; private set; }

    //[Header("Player prefab (NetworkObject)")]
    //[SerializeField] private NetworkPrefabRef playerPrefab;

    private NetworkRunner _runner;
    public NetworkRunner Runner => _runner;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private async Task EnsureRunnerCreated()
    {
        if (_runner != null && _runner.IsRunning)
            return;

        _runner = FindObjectOfType<NetworkRunner>();
        if (_runner == null)
        {
            GameObject go = new GameObject("NetworkRunner");
            _runner = go.AddComponent<NetworkRunner>();
        }

        if (_runner.GetComponent<NetworkSceneManagerDefault>() == null)
            _runner.gameObject.AddComponent<NetworkSceneManagerDefault>();

        _runner.ProvideInput = true;
        _runner.AddCallbacks(this);
    }

    public async Task<StartGameResult> StartHostAsync(string sessionName, SceneRef? scene = null, int maxPlayers = 6)
    {
        await EnsureRunnerCreated();

        var startArgs = new StartGameArgs
        {
            GameMode = GameMode.Host,
            SessionName = sessionName,
            Scene = scene ?? default,
            IsOpen = true,
            IsVisible = true,
            PlayerCount = 0,
            SessionProperties = null
        };

        var result = await _runner.StartGame(startArgs);
        return result;
    }

    public async Task<StartGameResult> StartClientAsync(string sessionName, SceneRef? scene = null)
    {
        await EnsureRunnerCreated();

        var startArgs = new StartGameArgs
        {
            GameMode = GameMode.Client,
            SessionName = sessionName,
            Scene = scene ?? default,
            IsOpen = true,
            IsVisible = true
        };

        var result = await _runner.StartGame(startArgs);
        return result;
    }

    public async Task<bool> HostLoadScene(SceneRef sceneRef, LoadSceneMode mode = LoadSceneMode.Single)
    {
        if (_runner == null || !_runner.IsRunning) return false;
        if (!_runner.IsServer) return false; // Only host can request load
        try
        {
            await _runner.LoadScene(sceneRef, mode);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"HostLoadScene exception: {e}");
            return false;
        }
    }

    public async Task ShutdownRunnerAsync()
    {
        if (_runner != null && _runner.IsRunning)
        {
            await _runner.Shutdown();
        }
    }

    #region INetworkRunnerCallbacks - basic forwarding / logging

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"OnPlayerJoined: {player.PlayerId}");
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"OnPlayerLeft: {player.PlayerId}");
        // If a player leaves, host should remove their PlayerNetwork entry (PlayerNetwork handles it on despawn).
        // Need to assign a bot here as well
    }

    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnShutdown(NetworkRunner runner, ShutdownReason reason)
    {
        Debug.Log($"Runner shutdown: {reason}");
    }
    public void OnConnectedToServer(NetworkRunner runner) { Debug.Log("Connected to server"); }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { Debug.LogError($"Connect failed: {reason}"); }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { Debug.Log($"Disconnected: {reason}"); }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnSessionListUpdated(NetworkRunner runner, System.Collections.Generic.List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, System.Collections.Generic.Dictionary<string, object> data) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, System.ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnSceneLoadDone(NetworkRunner runner) { Debug.Log("Scene load done."); }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectSpawned(NetworkRunner runner, NetworkObject obj) { }
    public void OnObjectDestroyed(NetworkRunner runner, NetworkObject obj) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest req, byte[] token) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }

    #endregion
}
