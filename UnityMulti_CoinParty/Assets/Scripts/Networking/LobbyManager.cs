using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Fusion;
using Fusion.Sockets;
using UnityEngine.SceneManagement;

public class LobbyManager : MonoBehaviour, INetworkRunnerCallbacks
{
    [Header("Intro Panel (first screen)")]
    [SerializeField] private GameObject introPanel = null;           
    [SerializeField] private Button joinLobbyButton = null;

    [Header("Lobby Panel (session list and create/join UI)")]
    [SerializeField] private GameObject lobbyPanel = null;           // Panel that holds session list ScrollView and other session UI
    [SerializeField] private Transform sessionListContainer = null;  // ScrollView Content container for session buttons
    [SerializeField] private GameObject sessionButtonPrefab = null;  // Prefab for session buttons
    [SerializeField] private TMP_InputField sessionNameInput = null; // Input field for new session name
    [SerializeField] private Button createSessionButton = null;

    [Header("Session Panel (in-session UI)")]
    [SerializeField] private GameObject sessionPanel = null;         // Panel shown when inside a session/room
    [SerializeField] private Transform playerListContainer = null;   // Container for player names list inside session
    [SerializeField] private GameObject playerTextPrefab = null;     // Prefab for displaying player names
    [SerializeField] private Button leaveSessionButton = null;       // Button to leave the session

    [Header("Start/Waiting Button (Session Panel)")]
    [SerializeField] private Button startGameButton = null;          // Visible only for Host, triggers game start
    [SerializeField] private Button waitingForHostButton = null;     // Visible only for Clients, does nothing

    [Header("Leave Lobby UI")]
    [SerializeField] private Button leaveLobbyButton = null;         

    [Header("Confirmation UI (modal)")]
    [SerializeField] private GameObject confirmationPanel = null;    
    [SerializeField] private TMP_Text confirmationText = null;
    [SerializeField] private Button confirmationYesButton = null;
    [SerializeField] private Button confirmationNoButton = null;

    [Header("Scene to load on game start")]
    [SerializeField] private string gameSceneName = "GameScene"; 

    [Header("Settings")]
    [SerializeField] private int maxPlayersPerSession = 6;

    [Header("Allowed player names (editable in Inspector)")]
    [SerializeField]
    private List<string> allowedPlayerNames = new List<string>()
    {
        "Player 1", "Player 2", "Player 3", "Player 4", "Player 5", "Player 6"
    };

    private NetworkRunner _runner;
    public NetworkRunner Runner => _runner;

    private bool _inSession = false;
    private string _desiredSessionName = "";

    private readonly Dictionary<PlayerRef, string> _playerNameMap = new Dictionary<PlayerRef, string>();

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

    private async void Start()
    {
        await EnsureRunner();

        if (_runner != null)
        {
            _runner.ProvideInput = true;
            _runner.AddCallbacks(this);
        }

        // Initial UI state
        if (introPanel != null) introPanel.SetActive(true);
        if (lobbyPanel != null) lobbyPanel.SetActive(false);
        if (sessionPanel != null) sessionPanel.SetActive(false);
        if (confirmationPanel != null) confirmationPanel.SetActive(false);

        // Button listeners
        if (joinLobbyButton != null) joinLobbyButton.onClick.AddListener(OnJoinLobbyClicked);
        if (createSessionButton != null) createSessionButton.onClick.AddListener(() => OnJoinOrCreateClicked(true));
        if (leaveSessionButton != null) leaveSessionButton.onClick.AddListener(LeaveSessionAsync);
        if (leaveLobbyButton != null) leaveLobbyButton.onClick.AddListener(LeaveLobbyAsync);

        if (confirmationYesButton != null) confirmationYesButton.onClick.AddListener(OnConfirmationYes);
        if (confirmationNoButton != null) confirmationNoButton.onClick.AddListener(OnConfirmationNo);

        if (startGameButton != null) startGameButton.onClick.AddListener(OnStartGameClicked);
    }

    private async Task EnsureRunner()
    {
        _runner = FindObjectOfType<NetworkRunner>();

        if (_runner == null)
        {
            GameObject go = new GameObject("NetworkRunner");
            _runner = go.AddComponent<NetworkRunner>();

            if (_runner.GetComponent<NetworkSceneManagerDefault>() == null)
                _runner.gameObject.AddComponent<NetworkSceneManagerDefault>();
        }

        await Task.Yield();
    }

    // UI Actions

    private void OnJoinLobbyClicked()
    {
        if (introPanel != null) introPanel.SetActive(false);
        if (lobbyPanel != null) lobbyPanel.SetActive(true);

        if (createSessionButton != null) createSessionButton.interactable = true;
        if (leaveLobbyButton != null) leaveLobbyButton.interactable = true;
    }

    private async void OnJoinOrCreateClicked(bool isCreate)
    {
        _desiredSessionName = sessionNameInput != null ? sessionNameInput.text.Trim() : "";
        if (string.IsNullOrEmpty(_desiredSessionName))
        {
            ShowConfirmationDialog("Session name cannot be empty.", null, HideConfirmationDialog);
            return;
        }

        if (isCreate)
            await TryCreateSession(_desiredSessionName);
        else
            await TryJoinSession(_desiredSessionName);
    }

    private async Task TryJoinSession(string sessionName)
    {
        if (_runner == null)
        {
            ShowConfirmationDialog("Network runner not available.", null, HideConfirmationDialog);
            return;
        }

        if (joinLobbyButton != null) joinLobbyButton.interactable = false;
        if (createSessionButton != null) createSessionButton.interactable = false;

        if (_runner.IsRunning)
        {
            await _runner.Shutdown();
            _runner = gameObject.AddComponent<NetworkRunner>();
            if (_runner.GetComponent<NetworkSceneManagerDefault>() == null)
                _runner.gameObject.AddComponent<NetworkSceneManagerDefault>();
            _runner.ProvideInput = true;
            _runner.AddCallbacks(this);
        }

        var startArgs = new StartGameArgs
        {
            GameMode = GameMode.Client,
            SessionName = sessionName,
            Scene = default, 
            IsOpen = true,
            IsVisible = true
        };

        var result = await _runner.StartGame(startArgs);
        if (result.Ok)
        {
            _inSession = true;
            Debug.Log($"Joined session '{sessionName}' as Client.");
            ShowSessionUI();
        }
        else
        {
            Debug.LogWarning($"Failed to join session '{sessionName}': {result.ErrorMessage}");
            ShowConfirmationDialog(
                $"Session '{sessionName}' does not exist. Do you want to create it?",
                async () => await TryCreateSession(sessionName),
                HideConfirmationDialog);
        }
    }

    private async Task TryCreateSession(string sessionName)
    {
        if (_runner == null)
        {
            ShowConfirmationDialog("Network runner not available.", null, HideConfirmationDialog);
            return;
        }

        if (joinLobbyButton != null) joinLobbyButton.interactable = false;
        if (createSessionButton != null) createSessionButton.interactable = false;

        if (_runner.IsRunning)
        {
            await _runner.Shutdown();
            _runner = gameObject.AddComponent<NetworkRunner>();
            if (_runner.GetComponent<NetworkSceneManagerDefault>() == null)
                _runner.gameObject.AddComponent<NetworkSceneManagerDefault>();
            _runner.ProvideInput = true;
            _runner.AddCallbacks(this);
        }

        var startArgs = new StartGameArgs
        {
            GameMode = GameMode.Host,
            SessionName = sessionName,
            Scene = default, // Host won't load scene here; will load when Start Game clicked
            IsOpen = true,
            IsVisible = true
        };

        var result = await _runner.StartGame(startArgs);
        if (result.Ok)
        {
            _inSession = true;
            Debug.Log($"Created and joined session '{sessionName}' as Host.");
            ShowSessionUI();
        }
        else
        {
            Debug.LogWarning($"Failed to create session '{sessionName}': {result.ErrorMessage}");
            ShowConfirmationDialog(
                $"Session '{sessionName}' already exists. Do you want to join it?",
                async () => await TryJoinSession(sessionName),
                HideConfirmationDialog);
        }
    }

    private void ShowSessionUI()
    {
        if (lobbyPanel != null) lobbyPanel.SetActive(false);
        if (introPanel != null) introPanel.SetActive(false);
        if (sessionPanel != null) sessionPanel.SetActive(true);

        if (createSessionButton != null) createSessionButton.interactable = false;
        if (joinLobbyButton != null) joinLobbyButton.interactable = false;
        if (leaveLobbyButton != null) leaveLobbyButton.interactable = true;
        if (leaveSessionButton != null) leaveSessionButton.interactable = true;

        UpdatePlayerList();

        // Show start game button only if host, else show waiting button
        bool isHost = _runner != null && _runner.IsServer;

        if (startGameButton != null)
            startGameButton.gameObject.SetActive(isHost);

        if (waitingForHostButton != null)
            waitingForHostButton.gameObject.SetActive(!isHost);
    }

    private void ShowConfirmationDialog(string message, UnityEngine.Events.UnityAction onYes, UnityEngine.Events.UnityAction onNo)
    {
        if (confirmationPanel == null) return;

        confirmationPanel.SetActive(true);
        if (confirmationText != null) confirmationText.text = message;

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
        if (confirmationYesButton != null) confirmationYesButton.onClick.RemoveAllListeners();
        if (confirmationNoButton != null) confirmationNoButton.onClick.RemoveAllListeners();
    }

    private void HideConfirmationDialog()
    {
        ClearConfirmationActions();
        if (confirmationPanel != null) confirmationPanel.SetActive(false);
    }

    private void OnConfirmationYes() {/* listeners handle actual actions */}
    private void OnConfirmationNo() { HideConfirmationDialog(); }

    private async void LeaveSessionAsync()
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
        _inSession = false;

        if (introPanel != null) introPanel.SetActive(true);
        if (lobbyPanel != null) lobbyPanel.SetActive(false);
        if (sessionPanel != null) sessionPanel.SetActive(false);
        if (confirmationPanel != null) confirmationPanel.SetActive(false);

        if (joinLobbyButton != null) joinLobbyButton.interactable = true;
        if (createSessionButton != null) createSessionButton.interactable = true;
        if (leaveSessionButton != null) leaveSessionButton.interactable = false;
        if (leaveLobbyButton != null) leaveLobbyButton.interactable = false;

        if (sessionListContainer != null)
        {
            foreach (Transform t in sessionListContainer) Destroy(t.gameObject);
        }
        if (playerListContainer != null)
        {
            foreach (Transform t in playerListContainer) Destroy(t.gameObject);
        }

        _playerNameMap.Clear();
    }

    private void UpdatePlayerList()
    {
        if (playerListContainer == null || playerTextPrefab == null) return;

        foreach (Transform child in playerListContainer) Destroy(child.gameObject);

        if (_runner == null || !_runner.IsRunning) return;

        foreach (PlayerRef p in _runner.ActivePlayers)
        {
            string name = GetOrAssignPlayerName(p);
            GameObject entry = Instantiate(playerTextPrefab, playerListContainer);
            TMP_Text textComp = entry.GetComponentInChildren<TMP_Text>();
            if (textComp != null) textComp.text = p == _runner.LocalPlayer ? $"{name} (YOU)" : name;
        }
    }

    private string GetOrAssignPlayerName(PlayerRef player)
    {
        if (_playerNameMap.TryGetValue(player, out string already)) return already;

        var used = new HashSet<string>(_playerNameMap.Values);
        foreach (var candidate in allowedPlayerNames)
        {
            if (!used.Contains(candidate))
            {
                _playerNameMap[player] = candidate;
                return candidate;
            }
        }

        string fallback = $"Player {player.PlayerId}";
        _playerNameMap[player] = fallback;
        return fallback;
    }

    public void ReleasePlayerName(string playerName)
    {
        if (string.IsNullOrEmpty(playerName)) return;

        var toRemove = new List<PlayerRef>();
        foreach (var kv in _playerNameMap)
        {
            if (kv.Value == playerName)
                toRemove.Add(kv.Key);
        }
        foreach (var pr in toRemove)
            _playerNameMap.Remove(pr);
    }

    public string GetPlayerName(PlayerRef player)
    {
        if (_playerNameMap.TryGetValue(player, out var n)) return n;
        return $"Player {player.PlayerId}";
    }

    private void OnStartGameClicked()
    {
        if (_runner == null)
        {
            Debug.LogWarning("NetworkRunner not initialized!");
            return;
        }

        if (!_runner.IsServer)
        {
            Debug.LogWarning("Only the Host can start the game.");
            return;
        }

        Debug.Log("Host started the game.");
        SceneManager.LoadScene(gameSceneName);
    }

    #region INetworkRunnerCallbacks

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"Player {player.PlayerId} joined.");
        GetOrAssignPlayerName(player);
        if (_inSession) UpdatePlayerList();
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"Player {player.PlayerId} left.");
        if (_playerNameMap.ContainsKey(player)) _playerNameMap.Remove(player);
        if (_inSession) UpdatePlayerList();
    }

    public void OnConnectedToServer(NetworkRunner runner) { Debug.Log("Connected to server."); }
    public void OnShutdown(NetworkRunner runner, ShutdownReason reason) { Debug.Log($"Runner Shutdown: {reason}"); ResetUI(); }
    public void OnConnectFailed(NetworkRunner runner, NetAddress addr, NetConnectFailedReason reason) { Debug.LogError($"Connect failed: {reason}"); ResetUI(); }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { Debug.Log($"Disconnected: {reason}"); ResetUI(); }

    public void OnSceneLoadDone(NetworkRunner runner) { Debug.Log("Scene loaded."); if (sessionPanel != null) sessionPanel.SetActive(true); UpdatePlayerList(); }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessions)
    {
        Debug.Log($"Session list updated: {sessions.Count} sessions.");
        if (sessionListContainer == null || sessionButtonPrefab == null) return;

        foreach (Transform c in sessionListContainer) Destroy(c.gameObject);

        foreach (var s in sessions)
        {
            if (!s.IsOpen || !s.IsVisible || s.PlayerCount >= s.MaxPlayers) continue;

            GameObject btnObj = Instantiate(sessionButtonPrefab, sessionListContainer);
            TMP_Text t = btnObj.GetComponentInChildren<TMP_Text>();
            if (t != null) t.text = $"{s.Name} ({s.PlayerCount}/{s.MaxPlayers})";
            Button b = btnObj.GetComponent<Button>();
            if (b != null)
            {
                string nameCopy = s.Name;
                b.onClick.AddListener(() => { _ = TryJoinSession(nameCopy); });
            }
        }
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
