using UnityEngine;

public sealed class CafeInteractable : MonoBehaviour
{
    public enum Action { Seat, Sofa, Light, Display }
    public Action action;
    public int seatIndex;
    public string label;
}
