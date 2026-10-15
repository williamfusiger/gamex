using System;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Gamex.EditorTools
{
    /// <summary>
    /// Renders the greybox scene to image files from the command line, so the level can be
    /// checked without opening the editor:
    /// <c>Unity -batchmode -projectPath . -executeMethod Gamex.EditorTools.SceneCapture.Capture -captureOut DIR -quit</c>
    /// </summary>
    public static class SceneCapture
    {
        const int Width = 1600;
        const int Height = 900;

        // HDRP builds up sky lighting and fog over several frames; the first ones come out wrong.
        const int WarmupFrames = 16;

        public static void Capture()
        {
            string outputFolder = ArgumentValue("-captureOut") ?? "Temp/Captures";
            Directory.CreateDirectory(outputFolder);

            EditorSceneManager.OpenScene(GreyboxSceneBuilder.ScenePath);

            Camera playerCamera = Camera.main;
            Save(Render(playerCamera), Path.Combine(outputFolder, "player-view.png"));

            // Looks straight down from below the tree canopies, which would otherwise hide the ground.
            var overview = new GameObject("Overview Camera") { hideFlags = HideFlags.HideAndDontSave }.AddComponent<Camera>();
            overview.orthographic = true;
            overview.orthographicSize = 62f;
            overview.aspect = 1f;
            overview.nearClipPlane = 0.1f;
            overview.farClipPlane = 20f;
            overview.transform.SetPositionAndRotation(new Vector3(0f, 6f, 0f), Quaternion.Euler(90f, 0f, 0f));
            Save(Render(overview, Height, Height), Path.Combine(outputFolder, "overview.png"));
            UnityEngine.Object.DestroyImmediate(overview.gameObject);

            Debug.Log($"Scene captures written to {Path.GetFullPath(outputFolder)}");
        }

        static Texture2D Render(Camera camera, int width = Width, int height = Height)
        {
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previousTarget = camera.targetTexture;
            camera.targetTexture = target;
            for (int i = 0; i < WarmupFrames; i++)
                camera.Render();

            RenderTexture.active = target;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();

            RenderTexture.active = null;
            camera.targetTexture = previousTarget;
            target.Release();
            return image;
        }

        static void Save(Texture2D image, string path) => File.WriteAllBytes(path, image.EncodeToPNG());

        static string ArgumentValue(string name)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(arguments, name);
            return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
        }
    }
}
