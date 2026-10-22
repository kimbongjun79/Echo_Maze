using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Tools/Echo Maze/Build Item Display (or -executeMethod EchoItemDisplaySetup.Build).
// Creates Resources/EchoItemDisplay.prefab and textured cabinet/frame/stool echo materials.
// Layout contract: floor/wall-surface pivot, local +Z into the corridor; both furniture backs
// touch local Z=0. Frame/Item origin is its mesh center at Y=1.65m, independent of stool height.
// EchoItem generates the gem at runtime and retains the frame/stool after collection.
// Build updates only assets (including the cabinet prefab), never loads or saves a scene.
public static class EchoItemDisplaySetup
{
    const string Models = "Assets/VARCO3DImports/models/";
    const string TextureFolder = "Assets/PF/EchoItemTextures";
    const string PrefabPath = "Assets/Resources/EchoItemDisplay.prefab";
    const string CabinetPath = "Assets/PF/Gray Metal Locker.prefab";
    const string CabinetTextures = "Assets/VARCO3DImports/Gray Metal Locker/model-material_0";

    [MenuItem("Tools/Echo Maze/Build Item Display")]
    public static void Build()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before building item displays.");
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(TextureFolder)) AssetDatabase.CreateFolder("Assets/PF", "EchoItemTextures");
        var stoolMaterial = CreateMaterial("pentimento_stool_v01", "EchoStool");
        var frameMaterial = CreateMaterial("pentimento_frame_v01", "EchoFrame");
        var cabinetMaterial = CreateMaterial(null, CabinetTextures + "-base_color.png",
            CabinetTextures + "-normal.png", "EchoCabinet");
        ConfigureCabinet(cabinetMaterial);
        var root = new GameObject("EchoItemDisplay");
        try
        {
            var stool = Child("Stool", root.transform);
            var stoolModel = Model("pentimento_stool_v01", stool, stoolMaterial);
            Normalize(stoolModel, new Vector3(.8f, .65f, .8f), true);
            // Bounds, not exporter pivots, define the rear face touching the wall.
            stoolModel.localPosition += Vector3.forward * -BoundsOf(stoolModel).min.z;
            var stoolBounds = BoundsOf(stoolModel);
            var stoolCollider = stool.gameObject.AddComponent<BoxCollider>();
            stoolCollider.center = stoolBounds.center;
            stoolCollider.size = stoolBounds.size;

            var item = Child("Item", root.transform);
            var frame = Child("Frame", item);
            var frameModel = Model("pentimento_frame_v01", frame, frameMaterial);
            // Exported models may lie in XY, XZ, or YZ: put the thin axis on Z before scaling.
            var size = BoundsOf(frameModel).size;
            if (size.x < size.y && size.x < size.z)
                frameModel.localRotation = Quaternion.Euler(0, 90, 0) * frameModel.localRotation;
            else if (size.y < size.x && size.y < size.z)
                frameModel.localRotation = Quaternion.Euler(90, 0, 0) * frameModel.localRotation;
            Normalize(frameModel, new Vector3(.9f, .85f, .25f), false);
            var frameBounds = BoundsOf(frameModel);
            item.localPosition = new Vector3(0, 1.65f, frameBounds.extents.z);
            var target = frame.gameObject.AddComponent<BoxCollider>();
            target.isTrigger = true;
            target.center = Vector3.zero;
            target.size = new Vector3(frameBounds.size.x, frameBounds.size.y, Mathf.Max(.08f, frameBounds.size.z));
            item.gameObject.AddComponent<EchoItem>().ConfigureDisplay(root.transform, frame);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        Debug.Log("Echo furniture built: wall-flush stool/frame, frame center 1.65m, textured echo cabinet. No scene saved.");
    }

    static void ConfigureCabinet(Material material)
    {
        var root = PrefabUtility.LoadPrefabContents(CabinetPath);
        try
        {
            // Keep the existing hierarchy, dimensions, pivot and colliders used by hiding/moving.
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++) materials[i] = material;
                renderer.sharedMaterials = materials;
            }
            PrefabUtility.SaveAsPrefabAsset(root, CabinetPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static Transform Child(string name, Transform parent)
    {
        var child = new GameObject(name).transform;
        child.SetParent(parent, false);
        return child;
    }

    static Transform Model(string name, Transform parent, Material material)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(Models + name + ".fbx");
        if (!source) throw new InvalidOperationException("Missing display model: " + name);
        var model = UnityEngine.Object.Instantiate(source, parent, false);
        model.name = name;
        foreach (var collider in model.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(collider);
        foreach (var animator in model.GetComponentsInChildren<Animator>()) UnityEngine.Object.DestroyImmediate(animator);
        foreach (var renderer in model.GetComponentsInChildren<Renderer>())
        {
            var materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++) materials[i] = material;
            renderer.sharedMaterials = materials;
        }
        return model.transform;
    }

    static Bounds BoundsOf(Transform model)
    {
        var renderers = model.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) throw new InvalidOperationException(model.name + " has no renderers.");
        var bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    static void Normalize(Transform model, Vector3 maximumSize, bool floorPivot)
    {
        var size = BoundsOf(model).size;
        if (size.x <= 0 || size.y <= 0 || size.z <= 0)
            throw new InvalidOperationException(model.name + " has invalid bounds.");
        float scale = Mathf.Min(maximumSize.x / size.x, Mathf.Min(maximumSize.y / size.y, maximumSize.z / size.z));
        model.localScale *= scale;
        var bounds = BoundsOf(model);
        model.localPosition -= new Vector3(bounds.center.x, floorPivot ? bounds.min.y : bounds.center.y, bounds.center.z);
    }

    static Material CreateMaterial(string modelName, string assetName)
    {
        string modelPath = Models + modelName + ".fbx";
        string colorPath = TextureFolder + "/" + modelName + "_base_color.png";
        string normalPath = TextureFolder + "/" + modelName + "_normal.png";
        if (!AssetDatabase.LoadAssetAtPath<Texture2D>(colorPath) || !AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath))
        {
            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (!importer || !importer.ExtractTextures(TextureFolder))
                throw new InvalidOperationException("Cannot extract display textures: " + modelPath);
            AssetDatabase.Refresh();
        }
        var source = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Material>().Single();
        return CreateMaterial(source, colorPath, normalPath, assetName);
    }

    static Material CreateMaterial(Material source, string colorPath, string normalPath, string assetName)
    {
        var color = AssetDatabase.LoadAssetAtPath<Texture2D>(colorPath);
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
        if (!color || !normal) throw new InvalidOperationException("Missing base-color or normal texture for " + assetName);
        var normalImporter = (TextureImporter)AssetImporter.GetAtPath(normalPath);
        if (normalImporter.textureType != TextureImporterType.NormalMap)
        {
            normalImporter.textureType = TextureImporterType.NormalMap;
            normalImporter.SaveAndReimport();
        }
        var shader = Shader.Find("Echo/Monster");
        if (!shader) throw new InvalidOperationException("Echo/Monster textured echo shader is missing.");
        string materialPath = "Assets/PF/" + assetName + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (!material)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, materialPath);
        }
        if (source) material.CopyPropertiesFromMaterial(source);
        material.shader = shader;
        material.SetTexture("_BaseMap", color);
        material.SetTexture("_BumpMap", normal);
        material.SetColor("_EchoTint", Color.white);
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_ColorSaturation", .65f);
        material.SetFloat("_Base", 1);
        material.SetFloat("_Cull", 0);
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssetIfDirty(material);
        return material;
    }
}
