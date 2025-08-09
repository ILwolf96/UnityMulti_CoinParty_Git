using Fusion;
using UnityEngine;

public class PlayerNetwork : NetworkBehaviour
{
    [Networked]
    public NetworkString<_16> PlayerName { get; set; }

    public override void Spawned()
    {
        if (Object.HasStateAuthority)
        {
            string assignedName = LobbyManager.Instance != null
                ? LobbyManager.Instance.GetPlayerName(Object.InputAuthority)
                : $"Player {Object.InputAuthority.PlayerId}";

            PlayerName = assignedName;
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Object.HasStateAuthority && LobbyManager.Instance != null)
        {
            LobbyManager.Instance.ReleasePlayerName(PlayerName.ToString());
        }
    }

    public static bool TryGetPlayerName(PlayerRef player, out string playerName)
    {
        playerName = null;
        if (LobbyManager.Instance != null)
        {
            playerName = LobbyManager.Instance.GetPlayerName(player);
            return !string.IsNullOrEmpty(playerName);
        }
        return false;
    }
}
