using System;
using System.Collections;
using System.IO;
using UnityEngine;

// Saves the game render without game HUD/menu overlays. Never captures or uploads the host desktop separately.
public sealed class CafeScreenshot : MonoBehaviour
{
    const string FolderName = "线上网吧";
    string outputDirectory;
    bool capturing;

    public event Action Changed;
    public bool Capturing => capturing;
    public bool LastCaptureSucceeded { get; private set; }
    public string LastSavedPath { get; private set; } = "";
    public string Status { get; private set; } = "";
    public string OutputDirectory => string.IsNullOrEmpty(outputDirectory) ? DefaultOutputDirectory : outputDirectory;

    public static string DefaultOutputDirectory
    {
        get
        {
            string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            return string.IsNullOrEmpty(pictures)
                ? Path.Combine(Application.persistentDataPath, "Screenshots")
                : Path.Combine(pictures, FolderName);
        }
    }

    void Awake() { ShowOutputLocation(); }

    public void SetOutputDirectory(string path)
    {
        outputDirectory = string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
        ShowOutputLocation();
    }

    public void Capture()
    {
        if (!capturing) StartCoroutine(CaptureRoutine());
    }

    IEnumerator CaptureRoutine()
    {
        capturing = true;
        SetStatus("正在生成无菜单的游戏截图…");
        // Hide only game chrome, not the desktopRoot RawImage required by
        // immersive full-screen; retain focus/selected button via CanvasGroup.
        var hud = GetComponent<CafeHud>();
        var entry = GetComponent<CafeEntryUi>();
        var hidden = new System.Collections.Generic.List<(CanvasGroup group, float alpha, bool interactable, bool raycasts)>();
        if (hud == null)
        {
            LastCaptureSucceeded = false;
            LastSavedPath = "";
            SetStatus("截图保存失败：游戏界面未就绪，无法保证无菜单画面。");
            capturing = false;
            yield break;
        }
        try
        {
            var lounge = GetComponent<CafeLoungeHud>();
            HideChrome(lounge != null && lounge.Dock != null ? lounge.Dock.gameObject : null, hidden);
            HideChrome(lounge != null ? lounge.PanelsRoot : null, hidden);
            HideChrome(lounge != null ? lounge.ChromeRoot : null, hidden);
            HideChrome(lounge != null ? lounge.MorePanelRoot : null, hidden);
            HideChrome(hud.menuRoot, hidden);
            HideChrome(hud.roomHud, hidden);
            HideChrome(hud.toolbarPanel != null ? hud.toolbarPanel.gameObject : null, hidden);
            HideChrome(hud.pointerImage != null ? hud.pointerImage.gameObject : null, hidden);
            HideChrome(entry != null ? entry.ScreenshotOverlayRoot : null, hidden);
            // Do not snapshot the frame where the menu was still rendered.
            yield return new WaitForEndOfFrame();
            SaveCleanFrame();
        }
        finally
        {
            foreach (var previous in hidden)
            {
                if (!previous.group) continue;
                previous.group.alpha = previous.alpha;
                previous.group.interactable = previous.interactable;
                previous.group.blocksRaycasts = previous.raycasts;
            }
            capturing = false;
            Changed?.Invoke();
        }
    }

    static void HideChrome(GameObject target, System.Collections.Generic.List<(CanvasGroup, float, bool, bool)> previous)
    {
        if (target == null || !target.activeInHierarchy) return;
        var group = target.GetComponent<CanvasGroup>();
        if (group == null) group = target.AddComponent<CanvasGroup>();
        previous.Add((group, group.alpha, group.interactable, group.blocksRaycasts));
        // Keep Selectable focus intact; toggling interactable would drop the
        // currently selected screenshot button and break gamepad/keyboard UI.
        group.alpha = 0;
    }

    void SaveCleanFrame()
    {
        Texture2D texture = null;
        try
        {
            texture = ScreenCapture.CaptureScreenshotAsTexture();
            if (texture == null) throw new InvalidOperationException("Unity 未返回截图纹理。");
            byte[] png = texture.EncodeToPNG();
            if (png == null || png.Length == 0) throw new InvalidOperationException("PNG 编码结果为空。");

            string path;
            bool fallback = false;
            try
            {
                path = SavePng(OutputDirectory, png);
            }
            catch (Exception saveError) when (string.IsNullOrEmpty(outputDirectory) &&
                (saveError is UnauthorizedAccessException || saveError is IOException))
            {
                // On managed Windows desktops, Pictures can be protected by
                // Controlled Folder Access or redirected to an unwritable path.
                // An explicit test/user-selected directory must never silently fall back.
                string localFolder = Path.Combine(Application.persistentDataPath, "Screenshots");
                path = SavePng(localFolder, png);
                fallback = true;
                Debug.LogWarning("[NetCafe/Screenshot] Pictures directory unavailable; used local fallback (" +
                    saveError.GetType().Name + ").");
            }
            LastSavedPath = path;
            LastCaptureSucceeded = true;
            SetStatus((fallback ? "图片目录不可写，已改存：" : "已保存：") + path);
        }
        catch (Exception error)
        {
            LastSavedPath = "";
            LastCaptureSucceeded = false;
            SetStatus("截图保存失败：" + error.Message);
            Debug.LogWarning("[NetCafe/Screenshot] " + error);
        }
        finally
        {
            if (texture != null) Destroy(texture);
        }
    }

    static string SavePng(string folder, byte[] png)
    {
        Directory.CreateDirectory(folder);
        string prefix = Path.Combine(folder, "NetCafe_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
        for (int suffix = 1; suffix <= 100; suffix++)
        {
            string path = prefix + (suffix == 1 ? "" : "_" + suffix) + ".png";
            try
            {
                // CreateNew is atomic: multiple capture requests must never overwrite
                // an existing screenshot, even if they select a name concurrently.
                using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    file.Write(png, 0, png.Length);
                return path;
            }
            catch (IOException) when (File.Exists(path))
            {
                // Same-millisecond collision: try the next filename.
            }
        }
        throw new IOException("截图文件名冲突次数过多，请稍后重试。");
    }

    void ShowOutputLocation() => SetStatus("截图保存到：" + OutputDirectory);

    void SetStatus(string value)
    {
        Status = value;
        Changed?.Invoke();
    }
}
