using UnityEngine;

// Visual only. The local CharacterController and Windows desktop input remain independent.
public sealed class CafeAvatarVisual : MonoBehaviour
{
    public CafePrototype cafe;
    public CafeAvatarLibrary library;
    public Transform visual;
    public Animator animator;

    int previousSeat = -2;
    bool walking;
    string selected;
    Vector3 standingOffset;
    internal float EyeHeight { get; private set; } = .85f;

    void Awake() => standingOffset = visual != null ? visual.localPosition : Vector3.zero;
    void Start() => SetAvatar(CafeAvatarLibrary.SelectedId);

    public void SetAvatar(string id)
    {
        id = CafeAvatarLibrary.Valid(id);
        if (library == null || (selected == id && visual != null)) return;
        var replacement = library.Create(id, transform);
        if (replacement == null) return;
        // Exclude only the local avatar from its owner's first-person camera, not remote peers.
        const int localAvatarLayer = 28;
        Transform leftEye = null, rightEye = null;
        foreach (var child in replacement.GetComponentsInChildren<Transform>(true))
        {
            child.gameObject.layer = localAvatarLayer;
            if (child.name == "LeftEye") leftEye = child;
            else if (child.name == "RightEye") rightEye = child;
        }
        // Sample the standing pose once; animation must not shake the player's view.
        replacement.transform.localPosition = standingOffset;
        EyeHeight = leftEye != null && rightEye != null
            ? transform.InverseTransformPoint((leftEye.position + rightEye.position) * .5f).y : .85f;
        if (cafe != null && cafe.eyes != null)
            cafe.eyes.cullingMask &= ~(1 << localAvatarLayer);
        if (visual != null) Destroy(visual.gameObject);
        visual = replacement.transform;
        animator = replacement.GetComponentInChildren<Animator>();
        selected = id;
        previousSeat = -2;
        walking = false;
    }

    void LateUpdate()
    {
        if (cafe == null || visual == null || animator == null) return;
        // Show the player's selected animal only when walking in third-person.
        const int localAvatarLayer = 28;
        if (cafe.ThirdPerson) cafe.eyes.cullingMask |= 1 << localAvatarLayer;
        else cafe.eyes.cullingMask &= ~(1 << localAvatarLayer);
        int seat = cafe.CurrentSeat;
        if (seat != previousSeat)
        {
            if (seat >= 0 && seat < cafe.seats.Length)
            {
                visual.SetParent(cafe.seats[seat], false);
                var cinema=cafe.GetComponent<CafeCinemaView>();
                visual.localPosition = cinema!=null&&cinema.ContainsSeat(seat) ?
                    cafe.seats[seat].InverseTransformPoint(cinema.AvatarPosition(seat)) :
                    new Vector3(0, seat < cafe.screens.Length ? -.67f : -.32f, -.12f);
                visual.localRotation = Quaternion.identity;
            }
            else
            {
                visual.SetParent(transform, false);
                visual.localPosition = standingOffset;
                visual.localRotation = Quaternion.identity;
            }
            previousSeat = seat;
        }
        if (seat < 0 && cafe.eyes != null)
            visual.rotation = Quaternion.Euler(0, cafe.eyes.transform.eulerAngles.y, 0);
        bool moving = seat < 0 && cafe.body != null && cafe.body.enabled && cafe.body.velocity.sqrMagnitude > .03f;
        if (moving != walking || seat != previousSeat)
        {
            walking = moving;
            animator.Play(walking ? "Walk" : "Idle", 0, 0);
        }
    }
}
