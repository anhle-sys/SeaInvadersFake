using UnityEngine;

public class MeshStateController : MonoBehaviour
{
    [SerializeField] private MeshFilter meshFilter;

    [SerializeField] private Mesh[] meshStates;

    private int currentState;

    public int CurrentState => currentState;

    public void SetState(int index)
    {
        if (meshFilter == null)
            return;

        if (meshStates == null ||
            index < 0 ||
            index >= meshStates.Length)
            return;

        if (meshStates[index] == null)
            return;

        meshFilter.sharedMesh = meshStates[index];

        currentState = index;
    }

    public void NextState()
    {
        SetState(currentState + 1);
    }

    public void PreviousState()
    {
        SetState(currentState - 1);
    }

    public void ResetState()
    {
        SetState(0);
    }
}