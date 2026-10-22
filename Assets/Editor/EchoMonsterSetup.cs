using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tools/Echo Maze/Build Monster: 선택한 FBX를 Humanoid로 리타기팅하고 생성 에셋을 재구성합니다.
// 대기/걷기/달리기 = 속도 블렌드, flexing = 위협, swiping = 접촉 공격.
// 모델의 Armature/Root와 모션의 Root 경로가 달라 Generic 클립을 직접 연결하면 안 됩니다.
// Apply Monster Material은 씬/애니메이션을 저장·재생성하지 않고 프리팹 머테리얼만 갱신합니다.
// EchoMonster.mat은 FBX 내장 색/노멀 텍스처와 원본 표면 값을 유지하며 EchoField의 맵 음파에 반응합니다.
static class EchoMonsterSetup
{
    const string Source = "Assets/Creature NPC Pack/";
    const string Model = Source + "pentimento_monster_v03 (1).fbx";
    const string ControllerPath = "Assets/PF/EchoMonster.controller";
    const string PrefabPath = "Assets/PF/EchoMonster.prefab";
    const string TextureFolder = "Assets/PF/EchoMonsterTextures";
    const string MaterialPath = "Assets/PF/EchoMonster.mat";

    [MenuItem("Tools/Echo Maze/Build Monster")]
    public static void Build()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before rebuilding the monster.");
        Import(Model, false, null);
        var idle = Import(Source + "mutant breathing idle.fbx", true, "Idle");
        var walk = Import(Source + "mutant walking.fbx", true, "Walk");
        var run = Import(Source + "mutant run.fbx", true, "Run");
        var alert = Import(Source + "mutant flexing muscles.fbx", false, "Alert");
        var attack = Import(Source + "mutant swiping.fbx", false, "Attack");
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
        var avatar = AssetDatabase.LoadAllAssetsAtPath(Model).OfType<Avatar>().Single();
        if (!avatar.isValid || !avatar.isHuman) throw new InvalidOperationException("Monster Humanoid avatar is invalid.");

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (!controller) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        else
        {
            // 재생성 시 GUID를 유지하여 기존 프리팹 참조가 끊어지지 않습니다.
            controller.layers = Array.Empty<AnimatorControllerLayer>();
            controller.parameters = Array.Empty<AnimatorControllerParameter>();
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(ControllerPath))
                if (asset != controller) UnityEngine.Object.DestroyImmediate(asset, true);
            controller.AddLayer("Base Layer");
        }
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        var sm = controller.layers[0].stateMachine;
        var locomotion = sm.AddState("Locomotion", new Vector3(250, 80));
        var tree = new BlendTree { name = "Idle Walk Run", blendType = BlendTreeType.Simple1D, blendParameter = "Speed", useAutomaticThresholds = false };
        AssetDatabase.AddObjectToAsset(tree, controller);
        tree.AddChild(idle, 0);
        tree.AddChild(walk, 1);
        tree.AddChild(run, 3.2f);
        locomotion.motion = tree;
        locomotion.iKOnFeet = true;
        sm.defaultState = locomotion;
        var alertState = sm.AddState("Alert", new Vector3(500, 10));
        alertState.motion = alert;
        alertState.speed = 1.5f;
        alertState.iKOnFeet = true;
        ReturnToMovement(alertState, locomotion);
        var attackState = sm.AddState("Attack", new Vector3(500, 160));
        attackState.motion = attack;
        attackState.iKOnFeet = true;
        ReturnToMovement(attackState, locomotion);
        EditorUtility.SetDirty(controller);

        var material = CreateMaterial();
        var root = new GameObject("EchoMonster");
        try
        {
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
            visual.name = "pentimento_monster_v03";
            visual.transform.SetParent(root.transform, false);
            var animator = visual.GetComponent<Animator>();
            if (!animator) animator = visual.AddComponent<Animator>();
            animator.avatar = avatar;
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var renderers = visual.GetComponentsInChildren<SkinnedMeshRenderer>();
            if (renderers.Length == 0) throw new InvalidOperationException("Monster has no skinned mesh.");
            foreach (var renderer in renderers) renderer.sharedMaterial = material;
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            float scale = 2.2f / bounds.size.y;
            visual.transform.localScale *= scale;
            visual.transform.localPosition = new Vector3(-bounds.center.x * scale, -bounds.min.y * scale, -bounds.center.z * scale);
            var monster = root.AddComponent<EchoMonster>();
            monster.alertDuration = alert.length / alertState.speed;
            monster.roarRadius = 30;
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        AssetDatabase.SaveAssets();

        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/main.unity")
            throw new InvalidOperationException("Open main.unity, then run Build Monster to assign the prefab.");
        var maze = UnityEngine.Object.FindFirstObjectByType<EchoMaze>();
        Undo.RecordObject(maze, "Assign EchoMonster");
        maze.monsterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        EditorUtility.SetDirty(maze);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("EchoMonster built: pentimento_monster_v03, Humanoid retargeting, Idle/Walk/Run + Alert/Attack.");
    }

    [MenuItem("Tools/Echo Maze/Apply Monster Material")]
    public static void ApplyMaterial()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before updating the monster material.");
        var material = CreateMaterial();
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>())
                renderer.sharedMaterial = material;
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
    }

    // FBX 내장 텍스처는 외부 Linux 경로를 참조하여 자동 연결되지 않으므로 명시적으로 추출합니다.
    // 맵의 음파 계산은 공유하되 원본의 색/노멀/금속성/매끄러움은 유지합니다.
    static Material CreateMaterial()
    {
        if (!AssetDatabase.IsValidFolder(TextureFolder)) AssetDatabase.CreateFolder("Assets/PF", "EchoMonsterTextures");
        const string colorPath = TextureFolder + "/pentimento_monster_v03_base_color.png";
        const string normalPath = TextureFolder + "/pentimento_monster_v03_normal.png";
        if (!AssetDatabase.LoadAssetAtPath<Texture2D>(colorPath) || !AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath))
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(Model);
            if (!importer.ExtractTextures(TextureFolder)) throw new InvalidOperationException("Cannot extract embedded monster textures.");
            AssetDatabase.Refresh();
        }
        var color = AssetDatabase.LoadAssetAtPath<Texture2D>(colorPath);
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
        if (!color || !normal) throw new InvalidOperationException("Monster base-color or normal texture is missing.");
        var normalImporter = (TextureImporter)AssetImporter.GetAtPath(normalPath);
        if (normalImporter.textureType != TextureImporterType.NormalMap)
        {
            normalImporter.textureType = TextureImporterType.NormalMap;
            normalImporter.SaveAndReimport();
        }
        var source = AssetDatabase.LoadAllAssetsAtPath(Model).OfType<Material>().Single();
        var shader = Shader.Find("Echo/Monster");
        if (!shader) throw new InvalidOperationException("Echo/Monster shader is missing.");
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (!material)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        material.shader = shader;
        material.SetTexture("_BaseMap", color);
        material.SetTextureScale("_BaseMap", source.GetTextureScale("_BaseMap"));
        material.SetTextureOffset("_BaseMap", source.GetTextureOffset("_BaseMap"));
        material.SetTexture("_BumpMap", normal);
        material.SetTextureScale("_BumpMap", source.GetTextureScale("_BumpMap"));
        material.SetTextureOffset("_BumpMap", source.GetTextureOffset("_BumpMap"));
        material.SetColor("_BaseColor", source.GetColor("_BaseColor"));
        material.SetFloat("_BumpScale", source.GetFloat("_BumpScale"));
        material.SetFloat("_Metallic", source.GetFloat("_Metallic"));
        material.SetFloat("_Smoothness", source.GetFloat("_Smoothness"));
        material.SetFloat("_Cull", source.GetFloat("_Cull"));
        material.SetFloat("_Base", 1);
        EditorUtility.SetDirty(material);
        return material;
    }

    static AnimationClip Import(string path, bool loop, string name)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = name != null;
        if (name != null)
        {
            var clip = importer.defaultClipAnimations[0];
            clip.name = name;
            clip.loopTime = loop;
            clip.loopPose = loop;
            clip.lockRootRotation = true;
            clip.lockRootHeightY = true;
            // XZ 이동은 포즈에 굽지 않고 루트 모션으로 추출합니다.
            // applyRootMotion=false와 함께 써야 메시가 AI 위치에서 밀려나지 않습니다.
            clip.lockRootPositionXZ = false;
            clip.keepOriginalOrientation = true;
            clip.keepOriginalPositionXZ = false;
            clip.heightFromFeet = true;
            importer.clipAnimations = new[] { clip };
        }
        importer.SaveAndReimport();
        if (name == null) return null;
        var result = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Single(c => c.name == name);
        if (!result.humanMotion) throw new InvalidOperationException(path + " failed Humanoid retargeting.");
        return result;
    }

    static void ReturnToMovement(AnimatorState from, AnimatorState to)
    {
        var transition = from.AddTransition(to);
        transition.hasExitTime = true;
        transition.exitTime = 1;
        transition.hasFixedDuration = true;
        transition.duration = .2f;
        transition.interruptionSource = TransitionInterruptionSource.None;
    }
}
