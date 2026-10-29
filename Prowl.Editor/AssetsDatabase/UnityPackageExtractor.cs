// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

using Prowl.Editor.Core;
using Prowl.Editor.Projects;
using Prowl.OrigamiUI;

namespace Prowl.Editor;

/// <summary>
/// Imports the raw assets (models, textures, audio, scripts) of a .unitypackage into the project's
/// Assets folder, keeping their folder layout and dropping Unity-only files (materials, prefabs, scenes...).
/// The copy runs off the main thread; the asset database is refreshed once it finishes.
/// </summary>
public static class UnityPackageExtractor
{
    // Everything else (.mat, .prefab, .unity, .controller, .anim, .asset, .cginc, ...) is skipped.
    private static readonly HashSet<string> s_allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        ".fbx", ".obj", ".dae", ".blend",
        ".png", ".jpg", ".jpeg", ".tga", ".bmp",
        ".wav", ".mp3", ".ogg",
        ".cs",
        ".txt", ".json", ".xml", ".csv",
    };

    // The allowed extensions that import as a model, which is what a scene can be built from.
    private static readonly HashSet<string> s_modelExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".fbx", ".obj", ".dae", ".blend",
    };

    private static volatile bool s_running;

    [MenuItem("Tools/Import UnityPackage", priority: 0)]
    static void Open()
    {
        EditorApplication.OpenFileDialog(FileDialogMode.Open, path =>
        {
            if (path == null) return;
            if (!File.Exists(path))
            {
                Runtime.Debug.LogError($"[UnityPackage] File not found: '{path}'.");
                return;
            }
            StartImport(path);
        },
        // FileDialog matches filters with a plain EndsWith, so no "*" wildcard.
        filters: [".unitypackage"],
        filterLabels: ["Unity Package"]);
    }

    /// <summary>
    /// Imports a package in the background. Used by the menu item and by dropping a package on the editor.
    /// With <paramref name="buildScene"/>, its models are then placed in the scene with their animations.
    /// </summary>
    internal static void StartImport(string packagePath, bool buildScene = false)
    {
        if (s_running)
        {
            Toasts.Warning("Unity Package", "An import is already running.");
            return;
        }

        Project? project = Project.Current;
        if (project == null) return;

        string assetsPath = project.AssetsPath;
        s_running = true;
        Toasts.Info("Unity Package", $"Importing {Path.GetFileName(packagePath)}...");

        // Extracting a large package takes long enough to freeze the editor, so it runs off the main thread.
        _ = Task.Run(() =>
        {
            Result result;
            try { result = Extract(packagePath, assetsPath); }
            catch (Exception ex) { result = new Result { Fatal = ex.Message }; }
            finally { s_running = false; }

            EditorApplication.RunOnMainThread(() => Report(packagePath, result, buildScene));
        });
    }

    internal sealed class Result
    {
        public int Imported;
        public int Skipped;
        public readonly List<string> Errors = [];
        public string? Fatal;
        /// <summary>Assets-relative paths ('/' separated) of the package's models now in the project, copied or already there.</summary>
        public readonly List<string> ModelPaths = [];
    }

    /// <summary>Extracts the package into a temp folder and copies its allowed assets under assetsPath. Never touches the asset database.</summary>
    internal static Result Extract(string packagePath, string assetsPath)
    {
        var result = new Result();
        string tempDir = Path.Combine(Path.GetTempPath(), "Temp_UnityExtract", Guid.NewGuid().ToString("N"));
        string assetsRoot = Path.GetFullPath(assetsPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        try
        {
            Directory.CreateDirectory(tempDir);

            // A .unitypackage is a gzipped tar. ExtractToDirectory refuses entries that escape tempDir.
            using (FileStream file = File.OpenRead(packagePath))
            using (var gzip = new GZipStream(file, CompressionMode.Decompress))
                TarFile.ExtractToDirectory(gzip, tempDir, overwriteFiles: true);

            // One folder per asset, named by its Unity GUID, holding 'asset' (the raw file),
            // 'pathname' (its original project path) and 'asset.meta'.
            foreach (string guidDir in Directory.EnumerateDirectories(tempDir))
            {
                string assetFile = Path.Combine(guidDir, "asset");
                string pathnameFile = Path.Combine(guidDir, "pathname");
                // Folders have a pathname but no asset.
                if (!File.Exists(assetFile) || !File.Exists(pathnameFile)) continue;

                string rel = "";
                try
                {
                    rel = ReadPathname(pathnameFile);
                    if (!s_allowed.Contains(Path.GetExtension(rel)))
                    {
                        result.Skipped++;
                        continue;
                    }

                    string dest = Path.GetFullPath(Path.Combine(assetsRoot, rel));
                    if (!dest.StartsWith(assetsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Errors.Add($"'{rel}': path leaves the Assets folder.");
                        continue;
                    }
                    // A model already in the project still counts, so dropping the same package again rebuilds the scene.
                    bool isModel = s_modelExts.Contains(Path.GetExtension(rel));

                    // Never overwrite an asset the project already has.
                    if (File.Exists(dest))
                    {
                        result.Skipped++;
                        if (isModel) result.ModelPaths.Add(rel.Replace('\\', '/'));
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    File.Copy(assetFile, dest);
                    result.Imported++;
                    if (isModel) result.ModelPaths.Add(rel.Replace('\\', '/'));
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"'{(rel.Length > 0 ? rel : Path.GetFileName(guidDir))}': {ex.Message}");
                }
            }
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); }
            catch (Exception ex) { result.Errors.Add($"Could not delete temp folder '{tempDir}': {ex.Message}"); }
        }

        return result;
    }

    /// <summary>Reads a pathname file as a path relative to Assets, with the platform's separators.</summary>
    internal static string ReadPathname(string pathnameFile)
    {
        // Older packages put a second line (e.g. "00") after the path.
        string text = File.ReadAllText(pathnameFile).Trim();
        int newline = text.IndexOfAny(['\r', '\n']);
        string path = (newline >= 0 ? text[..newline] : text).Trim().Replace('\\', '/');

        if (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            path = path["Assets/".Length..];

        return Path.Combine(path.Split('/', StringSplitOptions.RemoveEmptyEntries));
    }

    private static void Report(string packagePath, Result result, bool buildScene)
    {
        string name = Path.GetFileName(packagePath);
        // Scan now rather than waiting for the watcher, so the files show up in the Project panel at once.
        // The scan imports synchronously, so the models are ready for the scene below.
        if (result.Imported > 0)
            EditorAssetBackend.Instance?.Refresh();

        foreach (string error in result.Errors)
            Runtime.Debug.LogWarning($"[UnityPackage] {error}");

        if (result.Fatal != null)
        {
            Runtime.Debug.LogError($"[UnityPackage] Failed to import '{name}': {result.Fatal}");
            Toasts.Show("Unity Package", $"Failed to import {name}.", ToastType.Error);
            return;
        }

        string message = $"Imported {result.Imported} file(s) from '{name}', skipped {result.Skipped}, failed {result.Errors.Count}.";
        Runtime.Debug.Log($"[UnityPackage] {message}");
        Toasts.Show("Unity Package", message, result.Errors.Count > 0 ? ToastType.Warning : ToastType.Success);

        if (buildScene)
            UnityPackageSceneBuilder.Build(Path.GetFileNameWithoutExtension(packagePath), result.ModelPaths);
    }
}
