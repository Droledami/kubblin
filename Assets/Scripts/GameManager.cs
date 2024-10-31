using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class GameManager : NetworkBehaviour
{
    #region GAME_SETTINGS
    //Grid Settings
    public static readonly int GRID_WIDTH = 6; //X Axis
    public static readonly int GRID_LENGTH = 6; //Z Axis
    public static readonly float GRID_FLOOR = 0.4499999f;
    public static readonly Vector3 GRID_OFFSET = new Vector3(0.5f, 0, 0.5f);

    public static readonly Color SQUARE_RED = new Color(1, 0.1882353f, 0.2156862f, 1);
    public static readonly Color SQUARE_ORANGE = new Color(255, 0.454901f, 0.1882353f, 1);
    public static readonly Color SQUARE_YELLOW = new Color(255, 0.9568627f, 0.1882353f, 1);
    public static readonly Color SQUARE_GREEN = new Color(0.1882353f, 1, 0.4117647f, 1);

    //Player Settings
    public static readonly Vector3 PLAYER1_POSITION = new Vector3(GRID_OFFSET.x, GRID_FLOOR, GRID_LENGTH - GRID_OFFSET.z);
    public static readonly Vector3 PLAYER2_POSITION = new Vector3(GRID_WIDTH - GRID_OFFSET.x, GRID_FLOOR, GRID_OFFSET.z);
    public static readonly Color PLAYER1_COLOR = new Color(0, 1, 0);
    public static readonly Color PLAYER2_COLOR = new Color(1, 0, 0);
    #endregion

    public static GameManager Instance;

    public GameObject hostButton;
    public GameObject clientButton;
    public TMP_Text status;

    public TMP_Text notYourTurnText;
    public TMP_Text playerTurnText;
    public TMP_Text countDownText;
    public TMP_Text winnerText;

    public Button replayButton;

    public GameObject readyButton;

    public GridManager gridManager;

    public NetworkVariable<ulong> PlayerTurn = new NetworkVariable<ulong>();
    public NetworkVariable<bool> HasGameStarted = new NetworkVariable<bool>();

    #region UNITY_LIFECYCLE
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(this);
        }
    }

    public override void OnNetworkSpawn()
    {
        PlayerTurn.OnValueChanged += OnPlayerTurnChange;
        readyButton.gameObject.SetActive(true);
        if (IsServer && IsOwner)
        {
            PlayerTurn.Value = 1;
            HasGameStarted.Value = false;
        }
        else
        {
            UpdatePlayerTurnText();
        }
    }

    void Update()
    {
        UpdateUI();
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        PlayerTurn.OnValueChanged -= OnPlayerTurnChange;
    }
    #endregion

    #region NET_VARIABLES_CALLBACKS
    //Upon change of the variable determining whose player number it is the turn to choose a square, updates the according text
    private void OnPlayerTurnChange(ulong previous, ulong current)
    {
        if (PlayerTurn.Value != previous)
        {
            UpdatePlayerTurnText();
        }
    }
    #endregion

    #region UI_CHANGES
    public void OnHostButtonClicked() => NetworkManager.Singleton.StartHost();

    public void OnClientButtonClicked() => NetworkManager.Singleton.StartClient();

    //Used in Update flow, changes the UI depending on the connection status and game state
    void UpdateUI()
    {
        if (NetworkManager.Singleton == null)
        {
            SetStartButtons(false);
            SetStatusText("NetworkManager not found");
            return;
        }

        if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
        {
            SetStartButtons(true);
            SetStatusText("Not connected");
        }
        else
        {
            SetStartButtons(false);
            UpdateStatusLabels();
            playerTurnText.gameObject.SetActive(HasGameStarted.Value);
        }
    }

    void SetStartButtons(bool state)
    {
        hostButton.SetActive(state);
        clientButton.SetActive(state);
    }

    void SetStatusText(string text) => status.text = text;

    void UpdateStatusLabels()
    {
        var mode = NetworkManager.Singleton.IsHost ? "Host" : NetworkManager.Singleton.IsServer ? "Server" : "Client";
        string transport = "Transport: " + NetworkManager.Singleton.NetworkConfig.NetworkTransport.GetType().Name;
        string modeText = "Mode: " + mode;
        SetStatusText($"{transport} {modeText}");
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void ActivateEndOfGameElementsRpc(bool value, ulong clientId)
    {
        if (clientId != NetworkManager.LocalClientId)
            return;
        winnerText.gameObject.SetActive(value);
        replayButton.gameObject.SetActive(value);
    }

    private void UpdatePlayerTurnText()
    {
        playerTurnText.text = $"Au tour du joueur {PlayerTurn.Value}";
    }

    [Rpc(SendTo.ClientsAndHost)]
    public void NotifyNotYourTurnRpc(ulong clientId)
    {
        if (clientId == NetworkManager.Singleton.LocalClientId)
            StartCoroutine(NotYourTurnRoutine());
    }

    IEnumerator NotYourTurnRoutine()
    {
        notYourTurnText.gameObject.SetActive(true);
        yield return new WaitForSeconds(1);
        notYourTurnText.gameObject.SetActive(false);
    }
    #endregion

    #region PLAYER_READINESS
    public void SetReadyButton()
    {
        if (!NetworkManager.LocalClient.PlayerObject.GetComponent<PlayerManager>().IsPlayerReady.Value)
        {
            readyButton.GetComponent<Image>().color = Color.green;
            readyButton.GetComponentInChildren<TMP_Text>().text = "Prêt!";
            SendReadyStateRpc(true, NetworkManager.LocalClientId);
        }
        else
        {
            readyButton.GetComponent<Image>().color = Color.red;
            readyButton.GetComponentInChildren<TMP_Text>().text = "Pas Prêt.";
            SendReadyStateRpc(false, NetworkManager.LocalClientId);
        }
    }

    //Checks the ready state of every player (Can only be called by the server)
    private bool CheckIfEveryoneIsReady()
    {
        if (NetworkManager.Singleton.ConnectedClients.Count < 2)
            return false;
        bool isReady = true;
        foreach (var client in NetworkManager.Singleton.ConnectedClients)
        {
            if (!client.Value.PlayerObject.GetComponent<PlayerManager>().IsPlayerReady.Value)
            {
                isReady = false;
                break;
            }
        }
        return isReady;
    }

    //Send Ready State to the server. Every time it is done, the server will check if everyone is ready and launch the countdown if it's the case.
    [Rpc(SendTo.Server)]
    public void SendReadyStateRpc(bool isReady, ulong senderClientId)
    {
        NetworkManager.Singleton.ConnectedClients[senderClientId].PlayerObject.GetComponent<PlayerManager>().IsPlayerReady.Value = isReady;
        if (CheckIfEveryoneIsReady())
        {
            StartGame();
        }
    }

    [Rpc(SendTo.Everyone)]
    private void ResetReadyStateOfAllPlayersRpc()
    {
        foreach (var player in NetworkManager.Singleton.ConnectedClients)
        {
            player.Value.PlayerObject.GetComponent<PlayerManager>().SetPlayerReadyValueRpc(false);
        }
    }
    #endregion

    #region SQUARE_SELECTION
    //Upon receiving a request to find the hidden item with the clicked position, checks whether it is their turn to look for it
    //If it's the case, proceeds to changing the color of the selected square and pass the turn to the other player.
    [Rpc(SendTo.Server)]
    public void SendClickedObjectLocationToServerRpc(Vector3 clickedObjectPosition, ulong clientId)
    {
        if (clientId + 1 != PlayerTurn.Value)
        {
            //Click is not allowed since it's not the player's turn.
            NotifyNotYourTurnRpc(clientId);
            return;
        }
        int distance = (int)ManhattanDistance(clickedObjectPosition, gridManager.HiddenItemPosition.Value);
        ChangeSquareColorRpc(clickedObjectPosition, distance);
        if (distance == 0)
        {
            DeclareWinnerRpc(clientId);
            return;
        }
        SwapPlayerTurn();
    }

    [Rpc(SendTo.Everyone)]
    private void ChangeSquareColorRpc(Vector3 squareObjectPosition, int distanceFromItem)
    {
        int squareLayer = 64;
        GameObject targetedSquare = Physics.OverlapSphere(squareObjectPosition + GRID_OFFSET, 0.001f, squareLayer)[0].gameObject;
        if (targetedSquare == null)
            return;
        switch (distanceFromItem)
        {
            case 0:
                StartCoroutine(SquareColorChangeRoutine(targetedSquare, SQUARE_GREEN));
                break;
            case 1:
                StartCoroutine(SquareColorChangeRoutine(targetedSquare, SQUARE_YELLOW));
                break;
            case 2:
            case 3:
                StartCoroutine(SquareColorChangeRoutine(targetedSquare, SQUARE_ORANGE));
                break;
            default:
                StartCoroutine(SquareColorChangeRoutine(targetedSquare, SQUARE_RED));
                break;
        }
    }

    IEnumerator SquareColorChangeRoutine(GameObject squareObject, Color color)
    {
        Color originalColor = squareObject.GetComponent<MeshRenderer>().materials[0].color;
        squareObject.GetComponent<MeshRenderer>().materials[0].SetColor("_BaseColor", color);
        yield return new WaitForSeconds(0.5f);
        squareObject.GetComponent<MeshRenderer>().materials[0].SetColor("_BaseColor", originalColor);
    }
    #endregion

    #region GAME_STATE
    private void StartGame()
    {
        gridManager.SetRandomHiddenItemPosition();
        PlayerTurn.Value = 1;
        StartCountDownRpc(3);
    }

    [Rpc(SendTo.Server)]
    private void SetGameHasStartedRpc(bool value)
    {
        HasGameStarted.Value = value;
    }

    [Rpc(SendTo.Everyone)]
    public void StartCountDownRpc(int countFrom)
    {
        readyButton.SetActive(false);
        countDownText.gameObject.SetActive(true);
        foreach (var client in NetworkManager.Singleton.ConnectedClients)
        {
            client.Value.PlayerObject.GetComponent<PlayerManager>().DisplayReadyTextRpc(false);
        }
        StartCoroutine(CountDownCoroutine(countFrom));
    }

    IEnumerator CountDownCoroutine(int countFrom)
    {
        for (int i = countFrom; i >= 0; i--)
        {
            countDownText.fontSize = 2000;
            if (i > 0)
                countDownText.text = i.ToString();
            else
            {
                countDownText.text = "GO !";
                SetGameHasStartedRpc(true);
            }
            while (countDownText.fontSize > 500)
            {
                yield return new WaitForEndOfFrame();
                countDownText.fontSize -= 40;
            }
            yield return new WaitForSeconds(0.8f);
            countDownText.text = "";
            if (i <= 0)
                countDownText.gameObject.SetActive(false);
        }
    }

    private void SwapPlayerTurn()
    {
        if (PlayerTurn.Value == 2)
            PlayerTurn.Value = 1;
        else
            PlayerTurn.Value = 2;
    }

    [Rpc(SendTo.Everyone)]
    public void DeclareWinnerRpc(ulong winnerClientId)
    {
        ResetReadyStateOfAllPlayersRpc();
        foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            ActivateEndOfGameElementsRpc(true, clientId);
        }
        SetGameHasStartedRpc(false);
        if (winnerClientId == NetworkManager.Singleton.LocalClientId)
        {
            winnerText.text = "Gagné !";
            winnerText.color = Color.green;
        }
        else
        {
            winnerText.text = "Perdu...";
            winnerText.color = Color.red;
        }
    }

    public void OnClickedReplay()
    {
        RequestReplayRpc(NetworkManager.LocalClientId);
    }

    //Client wants to replay, thus requests the server to go back to its orginal position to wait for the other player.
    [Rpc(SendTo.Server)]
    private void RequestReplayRpc(ulong clientId)
    {
        PlayerManager playerManager = NetworkManager.ConnectedClients[clientId].PlayerObject.GetComponent<PlayerManager>();
        playerManager.PlayerInitialisation(true, clientId);
        ActivateEndOfGameElementsRpc(false, clientId);
        SendReadyStateRpc(true, clientId);
    }
    #endregion

    #region OTHER
    public static float ManhattanDistance(Vector3 a, Vector3 b)
    {
        checked
        {
            return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) + Mathf.Abs(a.z - b.z);
        }
    }
    #endregion
}
