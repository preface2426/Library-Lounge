using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Single source of truth for the newest Library Lounge client.
// Explicitly does not overwrite the stable player launcher or old builds.
public static class LibraryLoungeBuild
{
    public const string ScenePath = "Assets/CozyLoungeGreybox/Scenes/LibraryLounge_Furnished.unity";

    [MenuItem("NetCafe/Library Lounge/Validate merged scene")]
    public static void Validate()
    {
        var enabled = EditorBuildSettings.scenes.Where(x => x.enabled).ToArray();
        if (enabled.Length != 1 || enabled[0].path != ScenePath)
            throw new InvalidOperationException("The furnished scene must be the sole active Build Settings scene.");
        if (!File.Exists(Path.Combine(Application.dataPath,
            "CozyLoungeGreybox/Scenes/LibraryLounge_Furnished.unity")))
            throw new FileNotFoundException("Missing furnished Lounge scene", ScenePath);
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        if (!scene.IsValid() || !scene.isLoaded)
            throw new InvalidOperationException("Failed to load furnished scene");
        var objects = scene.GetRootGameObjects();
        var cafes = objects.SelectMany(root => root.GetComponentsInChildren<CafePrototype>(true)).ToArray();
        if (cafes.Length != 1)
            throw new InvalidOperationException("Expected a single CafePrototype controller");
        var cafe = cafes[0];
        var stations = objects.SelectMany(root=>root.GetComponentsInChildren<CafeInteractable>(true)).ToArray();
        if (cafe.body == null || cafe.eyes == null ||
            cafe.GetComponent<CafeHud>() == null ||
            cafe.GetComponent<CafeDesktopCapture>() == null ||
            cafe.GetComponent<CafeDesktopInput>() == null ||
            cafe.GetComponent<CafeScreenshot>() == null ||
            cafe.GetComponent<CafeEntryUi>() == null ||
            cafe.GetComponent<CafeRoomSync>() == null ||
            cafe.GetComponent<NetCafeNetworkSession>() == null ||
            cafe.GetComponent<CafeA2SmokeTest>() == null)
            throw new InvalidOperationException("Missing integrated movement/UI/input/capture/B1/screenshot component");
        if (cafe.seats == null || cafe.exits == null || cafe.screens == null ||
            cafe.seats.Length < 8 || cafe.exits.Length < 8 || cafe.screens.Length < 6)
            throw new InvalidOperationException("Invalid seat/screen arrays");
        for (int i=0; i<6; i++)
        {
            if (cafe.seats[i] == null || cafe.exits[i] == null ||
                cafe.screens[i] == null || !cafe.screens[i].gameObject.activeInHierarchy)
                throw new InvalidOperationException("Computer station " + i + " is not connected");
        }
        if (stations.Count(x=>x.action==CafeInteractable.Action.Seat)<6 ||
            stations.Count(x=>x.action==CafeInteractable.Action.Sofa)<2 ||
            stations.Count(x=>x.action==CafeInteractable.Action.Display)!=1 ||
            cafe.publicScreen==null)
            throw new InvalidOperationException("Computer, rest seat or public display interaction mismatch");
        int cameraCount = objects.Sum(root=>root.GetComponentsInChildren<Camera>(true)
            .Count(camera=>camera.isActiveAndEnabled));
        if (cameraCount!=1)
            throw new InvalidOperationException("Expected one active gameplay camera, got " + cameraCount);
        // A prior build accidentally kept the visible floor's BoxCollider disabled.
        // The retained old floors are inactive; a six-seat smoke alone misses this.
        CozyLoungeFloorRepair.Verify(scene);        CozyLoungeCollisionCoverage.Verify(scene);        CozyLoungeCameraClipAudit.Verify(scene);
        Debug.Log("LIBRARY_LOUNGE_MERGE_VALIDATE_PASS: furnished default scene; six interactive computers; " +
            "two rest seats; B1 and capture/input/HUD/screenshot components; one active camera; " +
            "player_room_capacity=6; computer_station_count_can_exceed_player_capacity=true; " +
            "not a runtime or real network test");
    }

    [MenuItem("NetCafe/Library Lounge/Build merged Windows QA")]
    public static void RunQA() => Build("LibraryLounge-Merged-QA-20261031", true);

    [MenuItem("NetCafe/Library Lounge/Build merged Windows candidate")]
    public static void RunCandidate() => Build("LibraryLounge-Merged-20261031", false);
    [MenuItem("NetCafe/Library Lounge/Build full audit Windows QA")]
    public static void RunAuditQA() => Build("LibraryLounge-Audit-QA-20261031", true);

    [MenuItem("NetCafe/Library Lounge/Build full audit Windows candidate")]
    public static void RunAuditCandidate() => Build("LibraryLounge-Audit-Candidate-20261031", false);
    // Final smoke harness safety update, preserving all earlier audited binaries.
    [MenuItem("NetCafe/Library Lounge/Build final audit QA")]
    public static void RunFinalAuditQA() => Build("LibraryLounge-Audit-QA-Final-20261031", true);

    [MenuItem("NetCafe/Library Lounge/Build final audited candidate")]
    public static void RunFinalAuditCandidate() => Build("LibraryLounge-Audit-Final-20261031", false);
    [MenuItem("NetCafe/Library Lounge/Build demo integration QA")]
    public static void RunDemoQA() => Build("LibraryLounge-Demo-QA-20261031", true);

    [MenuItem("NetCafe/Library Lounge/Build demo integration candidate")]
    public static void RunDemoCandidate() => Build("LibraryLounge-Demo-Candidate-20261031", false);
    [MenuItem("NetCafe/Library Lounge/Build demo acceptance QA")]
    public static void RunDemoAcceptanceQA() => Build("LibraryLounge-Demo-Acceptance-QA-20261031", true);

    [MenuItem("NetCafe/Library Lounge/Build demo acceptance candidate")]
    public static void RunDemoAcceptanceCandidate() => Build("LibraryLounge-Demo-Acceptance-20261031", false);
    [MenuItem("NetCafe/Library Lounge/Build demo soak QA")]
    public static void RunDemoSoakQA() => Build("LibraryLounge-Demo-Soak-QA-20261031", true);

    [MenuItem("NetCafe/Library Lounge/Build demo soak candidate")]
    public static void RunDemoSoakCandidate() => Build("LibraryLounge-Demo-Soak-20261031", false);
    [MenuItem("NetCafe/Library Lounge/Build demo voice acceptance QA")]
    public static void RunDemoVoiceQA() => Build("LibraryLounge-Demo-Voice-QA-20261031", true);

    [MenuItem("NetCafe/Library Lounge/Build demo voice acceptance candidate")]
    public static void RunDemoVoiceCandidate() => Build("LibraryLounge-Demo-Voice-20261031", false);
    [MenuItem("NetCafe/Library Lounge/Build demo playthrough QA")]
    public static void RunDemoPlaythroughQA() => Build("LibraryLounge-Demo-Playthrough-QA-20261031", true);

    [MenuItem("NetCafe/Library Lounge/Build demo playthrough release")]
    public static void RunDemoPlaythroughRelease() => Build("LibraryLounge-Demo-Playthrough-20261031", false);
    [MenuItem("NetCafe/Library Lounge/Build demo full acceptance QA")]
    public static void RunDemoFullAcceptanceQA() => Build("LibraryLounge-Demo-Full-QA-20261031", true);

    [MenuItem("NetCafe/Library Lounge/Build demo full candidate")]
    public static void RunDemoFullCandidate() => Build("LibraryLounge-Demo-Full-20261031", false);
    [MenuItem("NetCafe/Library Lounge/Build ground-fixed QA")]
    public static void RunGroundFixedQA() => Build("LibraryLounge-GroundFixed-QA-20261031", true);

    [MenuItem("NetCafe/Library Lounge/Build ground-fixed candidate")]
    public static void RunGroundFixedCandidate() => Build("LibraryLounge-GroundFixed-20261031", false);
    [MenuItem("NetCafe/Library Lounge/Build walkable-room QA")]
    public static void RunWalkableRoomQA() => Build("LibraryLounge-Walkable-QA-20261031", true);

    [MenuItem("NetCafe/Library Lounge/Build walkable-room candidate")]
    public static void RunWalkableRoomCandidate() => Build("LibraryLounge-Walkable-20261031", false);
    [MenuItem("NetCafe/Library Lounge/Build complete collision QA")]
    public static void RunCompleteCollisionQA() => Build("LibraryLounge-CollisionComplete-QA-20261031", true);

    [MenuItem("NetCafe/Library Lounge/Build complete collision candidate")]
    public static void RunCompleteCollisionCandidate() => Build("LibraryLounge-CollisionComplete-20261031", false);
    [MenuItem("NetCafe/Library Lounge/Build collision plus correct facing QA")]
    public static void RunCollisionFacingQA() => Build("LibraryLounge-CollisionFacing-QA-20261031", true);

    [MenuItem("NetCafe/Library Lounge/Build collision plus correct facing candidate")]
    public static void RunCollisionFacingCandidate() => Build("LibraryLounge-CollisionFacing-20261031", false);
    [MenuItem("NetCafe/Library Lounge/Build collision facing verified")]
    public static void RunCollisionFacingVerified() => Build("LibraryLounge-CollisionFacingVerified-20261031", false);
    [MenuItem("NetCafe/Library Lounge/Build camera fit diagnostic")]
    public static void RunCameraFitDiagnostic() => Build("LibraryLounge-CameraFitDiagnostic-20261031", false);
    [MenuItem("NetCafe/Library Lounge/Build final facing/collision candidate")]
    public static void RunFinalFacingCollisionCandidate() => Build("LibraryLounge-FacingCollisionFinal-20261031", false);
    [MenuItem("NetCafe/Library Lounge/Build final facing/collision QA")]
    public static void RunFinalFacingCollisionQA() => Build("LibraryLounge-FacingCollisionFinal-QA-20261031", true);
    [MenuItem("NetCafe/Library Lounge/Build auto-port demo candidate")]
    public static void RunAutoPortDemoCandidate() => Build("LibraryLounge-AutoPortDemo-20261031", false);

    [MenuItem("NetCafe/Library Lounge/Build auto-port demo QA")]
    public static void RunAutoPortDemoQA() => Build("LibraryLounge-AutoPortDemo-QA-20261031", true);

    static void Build(string folder, bool development)
    {
#if !NETCAFE_B1
        throw new InvalidOperationException("NETCAFE_B1 must be enabled for the integrated multiplayer build");
#else
        Validate();
        string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Builds",folder));
        if (Directory.Exists(path))
            throw new IOException("Refusing overwrite of an existing build: " + path);
        Directory.CreateDirectory(path);
        string exe=Path.Combine(path,"LibraryLounge.exe");
        BuildReport report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes=new[]{ScenePath},
            locationPathName=exe,
            target=BuildTarget.StandaloneWindows64,
            options=development ? BuildOptions.Development : BuildOptions.None
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException("Merged Library Lounge build failed: " + report.summary.result);
        Debug.Log("LIBRARY_LOUNGE_MERGED_BUILD_PASS: " + exe +
            "; development=" + development +
            "; B1=enabled; old_builds_unchanged=true; launcher_pointer_unchanged=true");
#endif
    }
}
