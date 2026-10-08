using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// When the project is opened with an empty "Untitled" scene (e.g. the first time it is opened
/// from Unity Hub or after cloning), open the game scene so pressing Play starts the game.
/// Runs once per editor session, after startup has finished loading and compiling.
/// </summary>
[InitializeOnLoad]
static class OpenGameSceneOnLoad
{
    const string GameScene = "Assets/Scenes/VRBasketball.unity";
    const string DoneKey = "OpenGameSceneOnLoad.Done";

    static OpenGameSceneOnLoad()
    {
        if (Application.isBatchMode || SessionState.GetBool(DoneKey, false)) return;
        EditorApplication.update += WaitForEditorReady;
    }

    static void WaitForEditorReady()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        Scene active = SceneManager.GetActiveScene();
        if (!active.IsValid()) return;

        EditorApplication.update -= WaitForEditorReady;
        SessionState.SetBool(DoneKey, true);

        bool untitled = string.IsNullOrEmpty(active.path) && !active.isDirty && SceneManager.sceneCount == 1;
        if (!untitled || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(GameScene) == null) return;
        EditorSceneManager.OpenScene(GameScene);
        Debug.Log("Opened " + GameScene + ". Press Play to start the game.");
    }
}
