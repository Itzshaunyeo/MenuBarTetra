using UnityEngine;

/// <summary>Ensures the game opens directly from the executable and configures the compact Windows window.</summary>
public static class RuntimeBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void StartGame()
    {
        Screen.SetResolution(430, 820, FullScreenMode.Windowed);
        Application.runInBackground = true;
        var game = new GameObject("MenuBar Tetra");
        Object.DontDestroyOnLoad(game);
        game.AddComponent<TetraGame>();
        #if UNITY_STANDALONE_WIN
        game.AddComponent<WindowsPlacement>();
        #endif
    }
}

#if UNITY_STANDALONE_WIN
sealed class WindowsPlacement : MonoBehaviour
{
    System.Collections.IEnumerator Start()
    {
        // Unity creates the HWND after the first rendered frame.
        yield return new WaitForEndOfFrame();
        WindowsWindow.MakeCompactTopMost();
    }
}
#endif
