using Unity.Netcode;
using UnityEngine;

public class GridManager : NetworkBehaviour
{
    //Coordinates: Y is ignored. X and Z for item position.
    public NetworkVariable<Vector3> HiddenItemPosition = new NetworkVariable<Vector3>();

    public void SetRandomHiddenItemPosition()
    {
        HiddenItemPosition.Value = new Vector3(Random.Range(0, GameManager.GRID_WIDTH-1), 0, Random.Range(0, GameManager.GRID_LENGTH-1));
    }
}
