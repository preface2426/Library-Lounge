using System;
using System.Collections;
using UnityEngine;

// Runtime regression against the *built* room's CharacterController and floor.
public sealed class CafeFloorSmoke : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-floorSmoke") < 0) return;
        var cafe = FindFirstObjectByType<CafePrototype>();
        if (cafe == null) { Debug.LogError("FLOOR_SMOKE_FAIL no player"); Application.Quit(1); return; }
        cafe.gameObject.AddComponent<CafeFloorSmoke>();
    }

    IEnumerator Start()
    {
        var cafe = GetComponent<CafePrototype>();
        yield return null;
        bool success = false;
        try
        {
            var floor = cafe.walkableFloor!=null?cafe.walkableFloor.gameObject:GameObject.Find("Warm plank floor");
            var collider = floor == null ? null : floor.GetComponent<BoxCollider>();
            if (collider == null || !collider.enabled || !floor.activeInHierarchy)
                throw new Exception("Visible floor has no enabled collider");
            if (Physics.GetIgnoreLayerCollision(cafe.body.gameObject.layer, floor.layer))
                throw new Exception("Player/floor collision pair is ignored");
            Physics.SyncTransforms();
            var body = cafe.body;
            float fallSpeed = 0;
            float bottom = body.transform.position.y;
            bool touched = false;
            for (int i=0;i<240;i++)
            {
                float dt = 1f / 60f;
                fallSpeed = body.isGrounded ? -2 : Mathf.Max(fallSpeed - 20 * dt, -30);
                var flags = body.Move(Vector3.up * (fallSpeed * dt));
                touched |= (flags & CollisionFlags.Below) != 0;
                bottom = Mathf.Min(bottom,body.transform.position.y);
                if (bottom < -.3f) throw new Exception("Player fell through floor at y=" + bottom);
            }
            if (!touched || !body.isGrounded) throw new Exception("No grounded contact after gravity test");
            if (Mathf.Abs(cafe.eyes.transform.localPosition.y - cafe.WalkingEyeHeight) > .01f)
                throw new Exception("Walking camera moved off capsule anchor");
            cafe.OpenMenu(); cafe.Continue();
            if (cafe.CurrentSeat != -1 || cafe.Mode != CafePrototype.InputMode.Walking ||
                body.transform.position.y < -.3f)
                throw new Exception("Returning to room did not keep player grounded");
            for (int i=0;i<cafe.seats.Length;i++)
            {
                cafe.Sit(i); cafe.Stand();
                for (int j=0;j<10;j++) body.Move(Vector3.down * .025f);
                if (body.transform.position.y < -.3f)
                    throw new Exception("Seat exit "+i+" unsupported by floor");
            }
            success = true;
            Debug.Log("FLOOR_SMOKE_PASS: actual capsule ground contact 240 frames, 8 exits, menu and walking camera");
        }
        catch(Exception ex) { Debug.LogException(ex); Debug.LogError("FLOOR_SMOKE_FAIL: " + ex.Message); }
        Application.Quit(success ? 0 : 1);
    }
}
