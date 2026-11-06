using UnityEngine;

public class BoatClickable : MonoBehaviour, IClickable
{
    [SerializeField] private BoatData boatData;
    [SerializeField] private BoatDataEventChannel channel; // kéo asset BoatClicked vào

    public void OnClicked(RaycastHit hit)
    {
        channel.Raise(boatData); 
    }
}