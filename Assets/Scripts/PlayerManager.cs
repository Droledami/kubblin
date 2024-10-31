using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;

public class PlayerManager : NetworkBehaviour
{
    public NetworkVariable<Color> PlayerColor = new NetworkVariable<Color>();
    public NetworkVariable<bool> IsPlayerReady = new NetworkVariable<bool>();
    private InputSystem_Actions playerActions;

    [SerializeField] private float maxMovementSpeed = 5;
    [SerializeField] private float acceleration = 0.08f;
    [SerializeField] public TMP_Text readyText;

    private float currentSpeed = 0;

    private Vector2 moveInput;
    Vector3 finalMovement;

    bool isColorSet = false;
    bool isReadyStateSet = false;

    bool listensToInput = true;

    #region UNITY_LIFECYCLE
    public override void OnNetworkSpawn()
    {
        PlayerColor.OnValueChanged += OnColorChanged;
        IsPlayerReady.OnValueChanged += OnPlayerReadyChanged;
        if (IsOwner)
        {
            PlayerInitialisation(false, NetworkManager.LocalClientId);
            SetPlayerReadyValueRpc(false);
        }
    }

    private void Start()
    {
        playerActions = new InputSystem_Actions();
        playerActions.Enable();
    }

    private void Update()
    {
        //Ensure every instance of the player prefab is assigned its color on every client.
        if (!isColorSet)
        {
            gameObject.GetComponent<MeshRenderer>().materials[0].color = PlayerColor.Value;
            isColorSet = true;
        }
        //Ensure every instance of the player prefab displays its readiness status on every client.
        if (!isReadyStateSet)
        {
            ChangeReadyText(IsPlayerReady.Value);
            isReadyStateSet = true;
        }
        if (!IsOwner || !GameManager.Instance.HasGameStarted.Value)
            return;

        if (IsHost && listensToInput) //Host = Player 1 and MovePlayer1 have WASD controls from input system
            moveInput = playerActions.FindAction("MovePlayer1").ReadValue<Vector2>();
        else if (listensToInput) //Not host = Player 2 and MovePlayer2 have arrow controls from input system
            moveInput = playerActions.FindAction("MovePlayer2").ReadValue<Vector2>();
        else
            moveInput = Vector2.zero;

        CalculateCurrentSpeed(moveInput);

        finalMovement = new Vector3(moveInput.x, 0, moveInput.y) * currentSpeed * Time.deltaTime;
        MovePlayer(finalMovement);

        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;
            if (Physics.Raycast(ray, out hit, float.PositiveInfinity, 64))
            {
                GameManager.Instance.SendClickedObjectLocationToServerRpc(hit.collider.gameObject.transform.position - GameManager.GRID_OFFSET, NetworkManager.LocalClientId);
            }
        }

        if (transform.position.y < -2)
        {
            DeclareSelfOutOfBoundsRpc(NetworkManager.LocalClientId);
        }
    }

    //On collision, calculates the knockBackDirection and request every clients and server to apply knockback on their respective objects instance
    private void OnCollisionEnter(Collision collision)
    {
        if (collision.collider.CompareTag("Player"))
        {
            Vector3 knockbackDirection = collision.transform.position - transform.position;
            knockbackDirection.Normalize();

            ApplyCollisionRpc(NetworkManager.Singleton.LocalClientId,
                collision.transform.GetComponent<NetworkObject>().OwnerClientId,
                knockbackDirection);
            //Reset the speed of the player pushing the other
            SetPlayerSpeedValueRpc(0);
            //Prevents pushing indefinetely the other player
            StartCoroutine(StunCoroutine(0.2f));
        }
    }

    public override void OnNetworkDespawn()
    {
        PlayerColor.OnValueChanged -= OnColorChanged;
        IsPlayerReady.OnValueChanged -= OnPlayerReadyChanged;
    }
    #endregion

    #region NET_VARIABLES_CALLBACKS
    //When fired, changes the color of the player prefab instance.
    public void OnColorChanged(Color previous, Color current)
    {
        if (previous != PlayerColor.Value)
        {
            gameObject.GetComponent<MeshRenderer>().materials[0].color = PlayerColor.Value;
        }
    }

    public void OnPlayerReadyChanged(bool previous, bool current)
    {
        if (previous != IsPlayerReady.Value)
        {
            ChangeReadyText(IsPlayerReady.Value);
        }
    }
    #endregion

    #region PLAYER_SETUP_METHODS
    //Assigns a player color based on the clientId and assigns it to them.
    //The server changes the PlayerColor NetworkVariable for every instance of the Player Prefab, firing the OnColorChanged event.
    [Rpc(SendTo.Server)]
    void RequestPlayerColorRpc()
    {
        foreach (var connectedClientsId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            switch (connectedClientsId)
            {
                case 0:
                    PlayerColor.Value = GameManager.PLAYER1_COLOR;
                    break;
                case 1:
                    PlayerColor.Value = GameManager.PLAYER2_COLOR;
                    break;
            }
        }
    }

    private void ChangeReadyText(bool isReady)
    {
        if (isReady)
        {
            readyText.text = "Prêt!";
            readyText.color = Color.green;
        }
        else
        {
            readyText.text = "Pas Prêt.";
            readyText.color = Color.red;
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    public void DisplayReadyTextRpc(bool value)
    {
        readyText.gameObject.SetActive(value);
    }

    public void PlayerInitialisation(bool isReplay, ulong clientId)
    {
        RequestStartingLocationRpc(clientId);
        if (!isReplay)
            RequestPlayerColorRpc();
    }

    [Rpc(SendTo.Server)]
    void RequestStartingLocationRpc(ulong clientId)
    {
        if (clientId == 0)
        {
            MoveToLocationRpc(GameManager.PLAYER1_POSITION, clientId);
        }
        else if (clientId == 1)
        {
            MoveToLocationRpc(GameManager.PLAYER2_POSITION, clientId);
        }
    }

    [Rpc(SendTo.Owner)]
    void MoveToLocationRpc(Vector3 location, ulong clientId)
    {
        transform.position = location;
    }

    [Rpc(SendTo.Server)]
    public void SetPlayerReadyValueRpc(bool value)
    {
        IsPlayerReady.Value = value;
    }

    [Rpc(SendTo.Server)]
    private void SetPlayerSpeedValueRpc(float speed)
    {
        currentSpeed = speed;
    }
    #endregion

    #region MOVEMENT_AND_COLLISIONS_METHODS
    //Tell the server that the current player object fell off the platform, the server will then declare the other player as the winner.
    [Rpc(SendTo.Server)]
    private void DeclareSelfOutOfBoundsRpc(ulong loserClientId)
    {
        foreach (ulong connectedClientId in NetworkManager.ConnectedClientsIds)
        {
            if (connectedClientId != loserClientId)
                GameManager.Instance.DeclareWinnerRpc(connectedClientId);
        }
    }

    //For use in Update flow, calculates the current speed of the player by adding the acceleration value to the current speed each call.
    private void CalculateCurrentSpeed(Vector2 move)
    {
        if (move.magnitude != 0)
            if (currentSpeed < maxMovementSpeed)
                currentSpeed += acceleration;
            else
                currentSpeed = maxMovementSpeed;
        else
        {
            if (currentSpeed > 0)
                currentSpeed -= acceleration * 2;
            else
                currentSpeed = 0;
        }
    }

    public void MovePlayer(Vector3 movementVector)
    {
        transform.Translate(movementVector);
    }
    IEnumerator StunCoroutine(float stunTime)
    {
        listensToInput = false;
        yield return new WaitForSeconds(stunTime);
        listensToInput = true;
    }

    [Rpc(SendTo.Everyone)]
    private void ApplyCollisionRpc(ulong meId, ulong otherId, Vector3 knockBack)
    {
        NetworkManager.Singleton.ConnectedClients[meId].PlayerObject.transform.Translate(-(knockBack / 2));
        NetworkManager.Singleton.ConnectedClients[otherId].PlayerObject.transform.Translate(knockBack);
    }
    #endregion
}