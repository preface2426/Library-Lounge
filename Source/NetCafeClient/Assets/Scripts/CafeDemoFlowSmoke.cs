using System;
using System.Collections;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Deterministic state/permission regression. No synthetic OS clicks or network join.
// Only runs on an explicitly requested development diagnostic command line.
public sealed class CafeDemoFlowSmoke : MonoBehaviour
{
    IEnumerator Start()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-demoFlowSmoke") < 0)
        {
            Destroy(this);
            yield break;
        }
        var run = Run();
        Exception failure = null;
        while (true)
        {
            bool more = false;
            try { more = run.MoveNext(); }
            catch (Exception error) { failure = error; }
            if (failure != null || !more) break;
            yield return run.Current;
        }
        (run as IDisposable)?.Dispose();
        if (failure != null) Debug.LogException(failure);
        Application.Quit(failure == null ? 0 : 1);
    }

    IEnumerator Run()
    {
        var entry = GetComponent<CafeEntryUi>();
        var cafe = GetComponent<CafePrototype>();
        var network = GetComponent<NetCafeNetworkSession>();
        var voice = GetComponent<CafeSpatialVoice>();
        yield return null; yield return null;
        {
            Require(entry != null && entry.Ready && entry.BlocksRoomMenu, "Home not shown");
            Require(cafe.Mode == CafePrototype.InputMode.Menu &&
                Cursor.visible && Cursor.lockState == CursorLockMode.None,
                "Home must show the unlocked OS cursor");
            Require(voice != null && !voice.MicrophoneActive &&
                !voice.MicrophoneBusy && voice.RemoteAudioCount == 0,
                "Microphone must not auto-publish on app startup");
            string before = network.RoomServiceUrl;
            Require(!network.SetRoomServiceUrl("http://192.0.2.99:8787"),
                "HTTP non-loopback must not carry bearer credentials");
            Require(network.SetRoomServiceUrl("https://rooms.example.test"),
                "Valid HTTPS room endpoint rejected");
            Require(network.SetRoomServiceUrl(before), "Original room URL restore failed");
            Debug.Log("DEMO_SECURITY_PREFLIGHT_PASS: microphone opt-in; HTTPS/loopback URL policy");

            Click(entry,"进入大厅"); yield return null;
            Click(entry, "创建房间"); yield return null;
            Require(Cursor.visible && cafe.Mode == CafePrototype.InputMode.Menu,
                "Create page must retain OS pointer");
            Click(entry, "返回大厅"); yield return null;
            foreach(var input in entry.ScreenshotOverlayRoot.GetComponentsInChildren<TMP_InputField>())
                if(input.name=="Input_邀请码")input.text="ABC123";
            Click(entry, "加入房间"); yield return null;
            Require(Cursor.visible, "Join page must retain OS pointer");
            Click(entry, "返回大厅"); yield return null;

            Click(entry, "单人体验"); yield return new WaitForSecondsRealtime(3.1f);
            Require(!entry.BlocksRoomMenu && cafe.Mode == CafePrototype.InputMode.Walking &&
                !Cursor.visible && Cursor.lockState == CursorLockMode.Locked,
                "Walking room should lock camera look and hide cursor");
            Debug.Log("DEMO_CURSOR_ROOM_PASS: home/create/join visible; walking hidden/locked");
            Vector3 spawn = cafe.body.transform.position;
            Quaternion facing = cafe.eyes.transform.rotation;
            var avatar = cafe.body.GetComponent<CafeAvatarVisual>();
            int eyesMatched = 0;
            foreach (string id in CafeAvatarLibrary.Ids)
            {
                avatar.SetAvatar(id);
                var bones = avatar.visual.GetComponentsInChildren<Transform>();
                var left = bones.First(bone => bone.name == "LeftEye");
                var right = bones.First(bone => bone.name == "RightEye");
                yield return null;
                if (Mathf.Abs(cafe.eyes.transform.position.y - (left.position.y + right.position.y) * .5f) < .10f)
                    eyesMatched++;
            }
            avatar.SetAvatar(CafeAvatarLibrary.SelectedId);
            yield return null;
            Vector3 firstPerson = cafe.eyes.transform.position;
            cafe.TogglePerspective(); yield return null;
            Require(cafe.ThirdPerson &&
                Vector3.Distance(cafe.eyes.transform.position, firstPerson)>.30f &&
                (cafe.eyes.cullingMask&(1<<28))!=0,
                "Third person must move the camera and show the own animal model");
            cafe.TogglePerspective(); yield return null;
            Require(!cafe.ThirdPerson &&
                Vector3.Distance(cafe.eyes.transform.position, firstPerson)<.01f &&
                (cafe.eyes.cullingMask&(1<<28))==0,
                "First person must restore head camera and hide own model");
            Debug.Log("LOCAL_AVATAR_PERSPECTIVE_PASS: V first/third person visibility + camera return");

            MoveBody(cafe, spawn + new Vector3(3, -8, 0));
            yield return null; yield return null;
            bool fallRecovered = (cafe.body.transform.position - spawn).sqrMagnitude < .0025f &&
                Quaternion.Angle(facing, cafe.eyes.transform.rotation) < .1f && cafe.body.enabled &&
                !entry.BlocksRoomMenu && cafe.Mode == CafePrototype.InputMode.Walking;
            Debug.Log("PLAYER_FALL_RECOVERY " + fallRecovered);

            cafe.OpenMenu(); yield return null;
            MoveBody(cafe, spawn + new Vector3(3, -8, 0));
            yield return null; yield return null;
            bool pausedRecovered = (cafe.body.transform.position - spawn).sqrMagnitude < .0025f &&
                cafe.Mode == CafePrototype.InputMode.Menu;
            Debug.Log("PLAYER_PAUSED_RECOVERY " + pausedRecovered);
            MoveBody(cafe, spawn); // Isolate the manual control check from the fall check.
            cafe.Continue();
            cafe.Sit(0); yield return null;
            cafe.OpenMenu(); yield return null;
            var lounge = GetComponent<CafeLoungeHud>();
            var more = lounge.Dock.GetComponentInParent<Canvas>().GetComponentsInChildren<Button>()
                .FirstOrDefault(button => button.name == "HudMoreButton");
            more?.onClick.Invoke();
            yield return null;
            var recover = lounge.MorePanelRoot.GetComponentsInChildren<Button>()
                .FirstOrDefault(button => button.name == "ReturnToSpawnButton");
            recover?.onClick.Invoke();
            yield return null;
            bool menuRecovered = recover != null && cafe.CurrentSeat == -1 && cafe.body.enabled &&
                (cafe.body.transform.position - spawn).sqrMagnitude < .0025f &&
                cafe.Mode == CafePrototype.InputMode.Walking && !entry.BlocksRoomMenu;
            Debug.Log($"PLAYER_RECOVERY_RESULTS eyes={eyesMatched}/{CafeAvatarLibrary.Ids.Length} menu={menuRecovered}");
            Require(eyesMatched == CafeAvatarLibrary.Ids.Length && fallRecovered && pausedRecovered && menuRecovered,
                "Player eye height or room recovery regression");
            cafe.OpenMenu(); yield return null;
            Require(Cursor.visible && Cursor.lockState == CursorLockMode.None,
                "In-room menu must display OS cursor");
            entry.ReturnHome(); yield return new WaitForSecondsRealtime(3.1f);
            Require(entry.BlocksRoomMenu && Cursor.visible, "Return home must restore visible OS cursor");

            Click(entry, "单人体验"); yield return new WaitForSecondsRealtime(3.1f);
            for (int i = 0; i < cafe.screens.Length; i++)
            {
                cafe.Sit(i); yield return null;
                Require(cafe.CurrentSeat == i, "Seat " + i + " failed");
                cafe.Stand(); yield return null;
                Require(cafe.CurrentSeat == -1 &&
                    cafe.Mode == CafePrototype.InputMode.Walking &&
                    !Cursor.visible, "Stand from seat " + i + " must hide OS pointer");
            }
            Debug.Log("DEMO_SIX_SEATS_PASS: sit/stand six PCs; walking invisible cursor");
            cafe.OpenMenu(); yield return null;
            Debug.Log("DEMO_FLOW_SMOKE_PASS: menu, room cursor, entry pages, six seats, security defaults");
        }
    }

    static void MoveBody(CafePrototype cafe, Vector3 position)
    {
        cafe.body.enabled = false;
        cafe.body.transform.position = position;
        cafe.body.enabled = true;
    }

    static void Click(CafeEntryUi entry, string name)
    {
        foreach (var button in entry.GetComponentsInChildren<Button>(true))
        {
            if (!button.isActiveAndEnabled || !button.gameObject.activeInHierarchy) continue;
            var text = button.GetComponentInChildren<TMP_Text>(true);
            if (text != null && text.text == name)
            {
                button.onClick.Invoke();
                return;
            }
        }
        throw new InvalidOperationException("Entry button missing: " + name);
    }
    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
