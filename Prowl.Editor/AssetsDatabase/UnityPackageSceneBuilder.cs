// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Editor.Core;
using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.Prefabs;
using Prowl.OrigamiUI;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Editor;

/// <summary>
/// Places the models of an imported .unitypackage in the open scene, under one root named after the
/// package, and gives each rigged model the clips of the package's animation files.
/// Unity's prefabs and animator controllers are not imported, so the scene is rebuilt from the models alone.
/// </summary>
internal static class UnityPackageSceneBuilder
{
    // A big environment pack would otherwise drop hundreds of objects on top of each other.
    private const int MaxSpawned = 64;
    private const float Spacing = 2.5f;

    private sealed class ModelFile
    {
        public required string Path;
        public required Guid Guid;
        public required string Name;
        public bool HasMesh;
        public readonly List<AnimationClip> Clips = [];

        /// <summary>Unity's "Model@Take.fbx" convention: the clips of Model, kept in their own file.</summary>
        public bool IsAnimationFile => Name.Contains('@') || (!HasMesh && Clips.Count > 0);

        /// <summary>The model an animation file belongs to: the part before '@', or empty when unnamed.</summary>
        public string Owner => Name.Contains('@') ? Name[..Name.IndexOf('@')] : "";
    }

    /// <summary>Spawns the imported models. Runs on the main thread after the asset database has imported them.</summary>
    public static void Build(string packageName, IReadOnlyList<string> modelPaths)
    {
        EditorAssetBackend? db = EditorAssetBackend.Instance;
        Scene? scene = Scene.Current;
        if (db == null || scene == null) return;

        if (PrefabEditingMode.IsEditing)
        {
            Toasts.Warning("Unity Package", "Leave prefab editing to place the package's models in a scene.");
            return;
        }

        List<ModelFile> files = Load(db, modelPaths);
        List<ModelFile> models = files.Where(f => !f.IsAnimationFile && f.HasMesh).ToList();
        List<ModelFile> animations = files.Where(f => f.IsAnimationFile).ToList();

        if (models.Count == 0)
        {
            Toasts.Info("Unity Package", "The package has no model to place in the scene; its assets are in the Project panel.");
            return;
        }
        if (models.Count > MaxSpawned)
        {
            Debug.LogWarning($"[UnityPackage] '{packageName}' has {models.Count} models; placing the first {MaxSpawned}. The rest are in the Project panel.");
            models = models.Take(MaxSpawned).ToList();
        }

        var root = new GameObject(packageName);
        scene.Add(root);

        int columns = (int)MathF.Ceiling(MathF.Sqrt(models.Count));
        int animated = 0;
        for (int i = 0; i < models.Count; i++)
        {
            GameObject? instance = PrefabUtility.InstantiatePrefab(models[i].Guid);
            if (instance == null) continue;

            // Centred grid on the ground plane, so the models do not overlap.
            float x = (i % columns - (columns - 1) * 0.5f) * Spacing;
            float z = (i / columns) * Spacing;
            instance.Transform.Position = new Float3(x, 0, z);
            scene.Add(instance);
            instance.SetParent(root);

            if (AttachClips(instance, models[i], animations)) animated++;
        }

        Selection.Select(root);
        Undo.RegisterCreatedObject(root, "Build Unity Package");
        EditorSceneManager.MarkDirty();

        string message = $"Placed {models.Count} model(s) from '{packageName}', {animated} animated.";
        Debug.Log($"[UnityPackage] {message}");
        Toasts.Show("Unity Package", message, ToastType.Success);
    }

    private static List<ModelFile> Load(EditorAssetBackend db, IReadOnlyList<string> modelPaths)
    {
        var files = new List<ModelFile>();
        foreach (string path in modelPaths)
        {
            Guid guid = db.PathToGuid(path);
            AssetEntry? entry = guid == Guid.Empty ? null : db.GetEntry(guid);
            // A model that failed to import has no prefab to spawn.
            if (entry?.MainAssetType == null || !typeof(PrefabAsset).IsAssignableFrom(entry.MainAssetType)) continue;

            var file = new ModelFile { Path = path, Guid = guid, Name = System.IO.Path.GetFileNameWithoutExtension(path) };
            foreach (SubAssetEntry sub in entry.SubAssets)
            {
                Type? type = sub.Type;
                if (type == null) continue;
                if (typeof(Mesh).IsAssignableFrom(type))
                    file.HasMesh = true;
                else if (typeof(AnimationClip).IsAssignableFrom(type) && AssetDatabase.Get(sub.Guid) is AnimationClip clip)
                    file.Clips.Add(clip);
            }
            files.Add(file);
        }
        return files;
    }

    /// <summary>Adds the clips this model can play to its animator. False when it has no animator or no clip fits.</summary>
    private static bool AttachClips(GameObject instance, ModelFile model, List<ModelFile> animations)
    {
        // Only a rigged model gets an animator on import; without a rig there is nothing to animate.
        Animator? animator = instance.GetComponentInChildren<Animator>();
        if (animator == null) return false;

        // Files named for this model ("Hero@Run" for "Hero") when there are any, otherwise every
        // animation file; the bone check below drops clips made for another rig.
        List<ModelFile> sources = animations.Where(a => IsOwner(a.Owner, model.Name)).ToList();
        if (sources.Count == 0) sources = animations;

        var known = new HashSet<Guid>(animator.Clips.Select(c => c.AssetID));
        bool changed = false;
        foreach (AnimationClip clip in sources.SelectMany(s => s.Clips))
        {
            if (!known.Add(clip.AssetID) || !Fits(clip, animator.Transform)) continue;
            animator.Clips.Add(clip);
            changed = true;
        }
        if (!changed) return animator.Clips.Count > 0;

        // The first clip plays on enable. A model's own take is usually just its bind pose, so start idle when it can.
        int idle = animator.Clips.FindIndex(c => c.Name.Contains("idle", StringComparison.OrdinalIgnoreCase));
        if (idle > 0) (animator.Clips[0], animator.Clips[idle]) = (animator.Clips[idle], animator.Clips[0]);

        // The model's prefab is generated and read-only, so the clips are kept as an override on the instance.
        PrefabUtility.RecordComponentOverrides(animator.GameObject, animator);
        animator.Rebind();
        return true;
    }

    /// <summary>"Hero" owns the animations of "Hero", and of a prefixed model such as "SK_Hero", but not of "SuperHero".</summary>
    private static bool IsOwner(string owner, string modelName)
    {
        if (owner.Length == 0 || !modelName.EndsWith(owner, StringComparison.OrdinalIgnoreCase)) return false;
        int start = modelName.Length - owner.Length;
        return start == 0 || modelName[start - 1] is '_' or '-' or ' ';
    }

    /// <summary>A humanoid clip plays on any humanoid rig; a skeletal one needs most of its bones under the animator.</summary>
    private static bool Fits(AnimationClip clip, Transform animatorRoot)
    {
        if (clip.Kind == AnimationClipKind.Humanoid) return true;

        Motion.Skeleton? skeleton = clip.Avatar.IsValid() ? clip.Avatar!.Skeleton : null;
        if (skeleton == null || skeleton.BoneCount == 0) return false;

        var binding = new AnimatorBinding(animatorRoot, skeleton);
        return binding.UnboundBones * 2 <= skeleton.BoneCount;
    }
}
