using System.Collections.Generic;
using UnityEngine;
using Fusion;
using Fusion.Sockets;
using TMPro;
using UnityEngine.UI;
using System.Threading.Tasks;
using UnityEngine.SceneManagement;

public class LobbyManager : MonoBehaviour, INetworkRunnerCallbacks
{
    [Header("Lobby UI Elements")]
    [SerializeField] private Button joinLobbyButton = null;

    [SerializeField] private Transform sessionListContainer = null;
    [SerializeField] private GameObject sessionButtonPrefab = null;

    [Header("Session Creation UI")]
    [SerializeField] private TMP_InputField sessionNameInput = null;
    [SerializeField] private Button createSessionButton = null;

    [Header("Room UI")]
    [SerializeField] private GameObject roomPanel = null;
    [SerializeField] private Transform playerListContainer = null;
    [SerializeField] private GameObject playerTextPrefab = null;
    [SerializeField] private Button leaveRoomButton = null;

    [Header("Leave Lobby UI")]
    [SerializeField] private Button leaveLobbyButton = null;

    [Header("Confirmation UI")]
    [SerializeField] private GameObject confirmationPanel = null;
    [SerializeField] private TMP_Text confirmationText = null;
    [SerializeField] private Button confirmationYesButton = null;
    [SerializeField] private Button confirmationNoButton = null;

    [Header("Scene to load on game start")]
    [SerializeField] private SceneRef gameScene;

    [Header("Settings")]
    [SerializeField] private int maxPlayersPerSession = 6;

    [SerializeField]
    private List<string> allowedPlayerNames = new List<string>()
    {
        "Player 1", "Player 2", "Player 3", "Player 4", "Player 5", "Player 6"
    };

    private NetworkRunner _runner;
    public NetworkRunner Runner => _runner;

    private bool _inRoom = false;
    private string _desiredSessionName = "";

    private Dictionary<PlayerRef, string> _playerNameMap = new Dictionary<PlayerRef, string>();

    public static LobbyManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        _runner = NetworkManager.Instance?.Runner;
        if (_runner == null)
        {
            Debug.LogError("LobbyManager: Could not find NetworkRunner from NetworkManager.");
            return;
        }

        _runner.ProvideInput = true;
        _runner.AddCallbacks(this);

        joinLobbyButton.interactable = true;
        createSessionButton.interactable = true;
        leaveRoomButton.interactable = false;
        leaveLobbyButton.interactable = false;

        sessionListContainer.gameObject.SetActive(false);
        roomPanel.SetActive(false);
        confirmationPanel.SetActive(false);

        joinLobbyButton.onClick.AddListener(() => OnJoinOrCreateClicked(false));
        createSessionButton.onClick.AddListener(() => OnJoinOrCreateClicked(true));
        leaveRoomButton.onClick.AddListener(LeaveRoomAsync);
        leaveLobbyButton.onClick.AddListener(LeaveLobbyAsync);

        confirmationYesButton.onClick.AddListener(OnConfirmationYes);
        confirmationNoButton.onClick.AddListener(OnConfirmationNo);
    }

    public string GetPlayerName(PlayerRef player)
    {
        if (_playerNameMap.TryGetValue(player, out var name))
            return name;
        return "Unknown";
    }

    public void ReleasePlayerName(string playerName)
    {
        if (!allowedPlayerNames.Contains(playerName))
        {
            allowedPlayerNames.Add(playerName);
            allowedPlayerNames.Sort();
        }
    }

    private async void OnJoinOrCreateClicked(bool isCreate)
    {
        _desiredSessionName = sessionNameInput.text.Trim();
        if (string.IsNullOrEmpty(_desiredSessionName))
        {
            ShowConfirmationDialog("Session name cannot be empty.", null, () => HideConfirmationDialog());
            return;
        }

        if (isCreate)
            await TryCreateSession(_desiredSessionName);
        else
            await TryJoinSession(_desiredSessionName);
    }

    private void ShowConfirmationDialog(string message, UnityEngine.Events.UnityAction onYes, UnityEngine.Events.UnityAction onNo)
    {
        confirmationPanel.SetActive(true);
        confirmationText.text = message;

        ClearConfirmationActions();

        if (onYes != null)
        {
            confirmationYesButton.onClick.AddListener(onYes);
            confirmationYesButton.onClick.AddListener(HideConfirmationDialog);
            confirmationYesButton.gameObject.SetActive(true);
        }
        else
        {
            confirmationYesButton.gameObject.SetActive(false);
        }

        if (onNo != null)
        {
            confirmationNoButton.onClick.AddListener(onNo);
            confirmationNoButton.onClick.AddListener(HideConfirmationDialog);
            confirmationNoButton.gameObject.SetActive(true);
        }
        else
        {
            confirmationNoButton.gameObject.SetActive(false);
        }
    }

    private void ClearConfirmationActions()
    {
        confirmationYesButton.onClick.RemoveAllListeners();
        confirmationNoButton.onClick.RemoveAllListeners();
    }

    private void HideConfirmationDialog()
    {
        ClearConfirmationActions();
        confirmationPanel.SetActive(false);
    }

    private void OnConfirmationYes()
    {
        // Intentionally empty, handled dynamically by added listeners.
    }

    private void OnConfirmationNo()
    {
        HideConfirmationDialog();
    }

    private async Task TryJoinSession(string sessionName)
    {
        joinLobbyButton.interactable = false;
        createSessionButton.interactable = false;

        if (_runner.IsRunning)
        {
            await _runner.Shutdown();
            // After shutdown, will get runner from NetworkManager again
            _runner = NetworkManager.Instance?.Runner;
            if (_runner == null)
            {
                Debug.LogError("LobbyManager: Could not find NetworkRunner from NetworkManager after shutdown.");
                return;
            }
            _runner.ProvideInput = true;
            _runner.AddCallbacks(this);
        }

        var startArgs = new StartGameArgs()
        {
            GameMode = GameMode.Client,
            SessionName = sessionName,
            Scene = gameScene,
            IsOpen = true,
            IsVisible = true
        };

        var result = await _runner.StartGame(startArgs);
        if (result.Ok)
        {
            _inRoom = true;
            Debug.Log($"Joined session '{sessionName}' as Client.");
            ShowRoomUI();
        }
        else
        {
            Debug.LogWarning($"Failed to join session '{sessionName}': {result.ErrorMessage}");
            ShowConfirmationDialog(
                $"Session '{sessionName}' does not exist. Do you want to create it?",
                async () => await TryCreateSession(sessionName),
                () => HideConfirmationDialog());
        }
    }

    private async Task TryCreateSession(string sessionName)
    {
        joinLobbyButton.interactable = false;
        createSessionButton.interactable = false;

        if (_runner.IsRunning)
        {
            await _runner.Shutdown();
            _runner = NetworkManager.Instance?.Runner;
            if (_runner == null)
            {
                Debug.LogError("LobbyManager: Could not find NetworkRunner from NetworkManager after shutdown.");
                return;
            }
            _runner.ProvideInput = true;
            _runner.AddCallbacks(this);
        }

        var startArgs = new StartGameArgs()
        {
            GameMode = GameMode.Host,
            SessionName = sessionName,
            Scene = gameScene,
            IsOpen = true,
            IsVisible = true
        };

        var result = await _runner.StartGame(startArgs);
        if (result.Ok)
        {
            _inRoom = true;
            Debug.Log($"Created and joined session '{sessionName}' as Host.");
            ShowRoomUI();
        }
        else
        {
            Debug.LogWarning($"Failed to create session '{sessionName}': {result.ErrorMessage}");
            ShowConfirmationDialog(
                $"Session '{sessionName}' already exists. Do you want to join it?",
                async () => await TryJoinSession(sessionName),
                () => HideConfirmationDialog());
        }
    }

    private void ShowRoomUI()
    {
        sessionListContainer.gameObject.SetActive(false);
        roomPanel.SetActive(true);

        createSessionButton.interactable = false;
        joinLobbyButton.interactable = false;
        leaveLobbyButton.interactable = true;
        leaveRoomButton.interactable = true;

        UpdatePlayerList();
    }

    private async void LeaveRoomAsync()
    {
        if (_runner != null && _runner.IsRunning)
        {
            await _runner.Shutdown();
        }
        ResetUI();
    }

    private async void LeaveLobbyAsync()
    {
        if (_runner != null && _runner.IsRunning)
        {
            await _runner.Shutdown();
        }
        ResetUI();
    }

    private void ResetUI()
    {
        _inRoom = false;

        joinLobbyButton.interactable = true;
        createSessionButton.interactable = true;
        leaveLobbyButton.interactable = false;
        leaveRoomButton.interactable = false;

        sessionListContainer.gameObject.SetActive(false);
        roomPanel.SetActive(false);
        confirmationPanel.SetActive(false);

        foreach (Transform child in sessionListContainer)
            Destroy(child.gameObject);

        foreach (Transform child in playerListContainer)
            Destroy(child.gameObject);

        _playerNameMap.Clear();
    }

    private void UpdatePlayerList()
    {
        foreach (Transform child in playerListContainer)
            Destroy(child.gameObject);

        if (_runner != null && _runner.IsRunning)
        {
            foreach (PlayerRef player in _runner.ActivePlayers)
            {
                string playerName = GetOrAssignPlayerName(player);
                GameObject playerText = Instantiate(playerTextPrefab, playerListContainer);
                TMP_Text textComp = playerText.GetComponent<TMP_Text>();
                textComp.text = player == _runner.LocalPlayer ? $"{playerName} (YOU)" : playerName;
            }
        }
    }

    private string GetOrAssignPlayerName(PlayerRef player)
    {
        if (_playerNameMap.TryGetValue(player, out string name))
            return name;

        HashSet<string> usedNames = new HashSet<string>(_playerNameMap.Values);
        foreach (var candidate in allowedPlayerNames)
        {
            if (!usedNames.Contains(candidate))
            {
                _playerNameMap[player] = candidate;
                return candidate;
            }
        }

        string fallbackName = $"Player {player.PlayerId}";
        _playerNameMap[player] = fallbackName;
        return fallbackName;
    }

    #region INetworkRunnerCallbacks

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"Player {player.PlayerId} joined the session.");
        if (_inRoom)
            UpdatePlayerList();
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"Player {player.PlayerId} left the session.");

        if (_playerNameMap.ContainsKey(player))
            _playerNameMap.Remove(player);

        if (_inRoom)
            UpdatePlayerList();
    }

    public void OnConnectedToServer(NetworkRunner runner)
    {
        Debug.Log("Connected to server.");
    }

    public void OnShutdown(NetworkRunner runner, ShutdownReason reason)
    {
        Debug.Log($"Runner Shutdown: {reason}");
        ResetUI();
    }

    public void OnConnectFailed(NetworkRunner runner, NetAddress addr, NetConnectFailedReason reason)
    {
        Debug.LogError($"Connection failed to {addr}: {reason}");
        ResetUI();
    }

    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        Debug.Log($"Disconnected from server: {reason}");
        ResetUI();
    }

    public void OnSceneLoadDone(NetworkRunner runner)
    {
        Debug.Log($"Scene loaded successfully by NetworkRunner.");
        roomPanel.SetActive(true);
        leaveRoomButton.interactable = true;
        UpdatePlayerList();
    }

    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessions)
    {
        Debug.Log($"OnSessionListUpdated called. Sessions count: {sessions.Count}");
        // will need to Update session list UI here
    }

    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest req, byte[] token) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken token) { }
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, System.ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }

    #endregion
}
