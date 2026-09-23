using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace CinderPass.EditorTools
{
    /// <summary>
    /// The runtime keeps plain C# state (drivetrain, input routing, game flow) that a hot reload would wipe,
    /// leaving the car unresponsive and systems throwing. With the editor's default "Recompile And Continue
    /// Playing", saving a script mid-session does exactly that, so this guard leaves Play Mode as soon as a
    /// recompile starts. It only affects this project; the editor preference itself is left untouched.
    /// </summary>
    [InitializeOnLoad]
    static class PlayModeReloadGuard
    {
        static PlayModeReloadGuard()
        {
            CompilationPipeline.compilationStarted += _ =>
            {
                if (!EditorApplication.isPlaying) return;
                Debug.LogWarning("[CinderPass] Scripts are recompiling - leaving Play Mode (the game does not support hot reload).");
                EditorApplication.isPlaying = false;
            };
        }
    }
}
