using UnityEditor;

namespace Gamex.EditorTools
{
    /// <summary>
    /// The whole pipeline in one call: import what has been downloaded, build the kit, build the scene.
    /// <c>Unity -batchmode -projectPath . -executeMethod Gamex.EditorTools.ForestBuild.BuildAll -quit</c>.
    /// Every stage skips work whose inputs have not changed, except the scene, which takes seconds.
    /// </summary>
    public static class ForestBuild
    {
        [MenuItem("Gamex/Forest/Build All")]
        public static void BuildAll()     // ForestImport.Run, ForestPrefabs.BuildAll, ForestSceneBuilder.Build
        {
            // Asked first: from the menu the import can block the Editor for minutes, and the question
            // about replacing the open scene should not be waiting at the end of that.
            if (!ForestSceneBuilder.ConfirmRebuild()) return;

            ForestImport.Run();
            ForestPrefabs.BuildAll(ForestArgs.Flag("-forestForce"));
            ForestSceneBuilder.BuildConfirmed();
        }
    }
}
