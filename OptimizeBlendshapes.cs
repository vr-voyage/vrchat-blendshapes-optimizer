#if UNITY_EDITOR

using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using Myy;
using UnityEditor.Animations;

public static class VRCAvatarHelpers
{
    public static AnimatorController[] GetAnimatorControllers(this VRCAvatarDescriptor descriptor)
    {
        List<AnimatorController> controllers = new List<AnimatorController>(8);
        foreach (var layer in descriptor.baseAnimationLayers)
        {
            var controller = layer.animatorController;
            if (controller != null)
            {
                controllers.Add((AnimatorController) controller);
            }
        }
        foreach (var layer in descriptor.specialAnimationLayers)
        {
            var controller = layer.animatorController;
            if (controller != null)
            {
                controllers.Add((AnimatorController)controller);
            }
        }
        return controllers.ToArray();
    }
}

public static class MyyMeshHelpers
{
    public static string[] GetBlendshapesNames(
        this Mesh mesh,
        int[] blendshapesIndices)
    {
        int nBlendshapes = blendshapesIndices.Length;
        string[] blendshapeNames = new string[nBlendshapes];

        int blendshapeCount = mesh.blendShapeCount;

        for (int i = 0; i < nBlendshapes; i++)
        {
            int blendshapeIndex = blendshapesIndices[i];
            bool blendshapeInRange = (blendshapeIndex >= 0) & (blendshapeIndex < blendshapeCount);
            blendshapeNames[i] = blendshapeInRange ? mesh.GetBlendShapeName(blendshapeIndex) : null;
        }

        return blendshapeNames;
    }

    public static int[] GetBlendshapesIndices(
        this Mesh mesh,
        string[] blendshapesNames)
    {
        int nBlendshapes = blendshapesNames.Length;
        int[] blendshapesIndices = new int[nBlendshapes];

        for (int i = 0; i < nBlendshapes; i++)
        {
            string currentName = blendshapesNames[i];
            
            blendshapesIndices[i] = (currentName != null ? mesh.GetBlendShapeIndex(currentName) : -1);
        }
        return blendshapesIndices;
    }

    public static string[] GetBlendshapesNames(
        this SkinnedMeshRenderer skin,
        int[] blendshapesIndices)
    {
        return skin.sharedMesh.GetBlendshapesNames(blendshapesIndices);
    }

    public static int[] GetBlendshapesIndices(
        this SkinnedMeshRenderer skin,
        string[] blendshapeNames)
    {
        return skin.sharedMesh.GetBlendshapesIndices(blendshapeNames);
    }
}

public class OptimizeBlendshapes : EditorWindow
{
    public VRCAvatarDescriptor avatar;

    SimpleEditorUI ui;

    class BlendShapeFrame
    {
        public Vector3[] deltaVertices;
        public Vector3[] deltaNormals;
        public Vector3[] deltaTangents;

        public BlendShapeFrame(int vertexCount)
        {
            deltaVertices = new Vector3[vertexCount];
            deltaNormals = new Vector3[vertexCount];
            deltaTangents = new Vector3[vertexCount];
        }
    }

    class BlendShapeData
    {
        public string name;
        public int frames;
        public BlendShapeFrame[] frame;

        public BlendShapeData(string name, int frames, int vertexCount)
        {
            this.name = name;
            this.frames = frames;
            frame = new BlendShapeFrame[frames];
            /* Initialize everything nicely */
            for (int i = 0; i < frames; i++)
            {
                frame[i] = new BlendShapeFrame(vertexCount);
            }
        }

        public void AddTo(Mesh mesh)
        {
            float fMax = frames;
            for (int f = 0; f < frames; f++)
            {
                var frameData = frame[f];
                mesh.AddBlendShapeFrame(
                    name,
                    (f + 1) * 100 / fMax, /* Weight from 0 to 100f */
                    frameData.deltaVertices,
                    frameData.deltaNormals,
                    frameData.deltaTangents);
            }
        }
    }

    public class UsedBlendshapes : Dictionary<SkinnedMeshRenderer, HashSet<string>>
    {
        void EnsureKeyExist(SkinnedMeshRenderer renderer)
        {
            if (!ContainsKey(renderer))
            {
                this[renderer] = new HashSet<string>();
            }
        }

        public void Add(SkinnedMeshRenderer renderer, string blendshapeName)
        {
            EnsureKeyExist(renderer);

            this[renderer].Add(blendshapeName);
        }

        public void Add(SkinnedMeshRenderer renderer, int[] blendshapesIndices)
        {
            EnsureKeyExist(renderer);
            
            foreach (int blendshapeIndex in blendshapesIndices)
            {
                this[renderer].Add(renderer.sharedMesh.GetBlendShapeName(blendshapeIndex));
            }
            
        }

        public void Add(SkinnedMeshRenderer renderer, string[] blendshapeNames)
        {
            EnsureKeyExist(renderer);
            this[renderer].UnionWith(blendshapeNames);
        }

        public void Dump()
        {
            foreach (var rendererInfo in this)
            {
                var renderer = rendererInfo.Key;
                Debug.Log(renderer.name);
                foreach (var blendshape in rendererInfo.Value)
                {
                    Debug.Log($"\t{blendshape}");
                }
            }
        }

        BlendShapeData[] GetBlendshapesData(Mesh mesh, string[] blendshapesNames)
        {

            List<BlendShapeData> blendShapesData = new List<BlendShapeData>(blendshapesNames.Length);
            foreach (string blendShapeName in blendshapesNames)
            {
                int blendShapeIndex = mesh.GetBlendShapeIndex(blendShapeName);
                if (blendShapeIndex < 0)
                {
                    Debug.LogError($"{mesh.name} has no blendshape named {blendShapeName}");
                    continue;
                }

                int vertexCount = mesh.vertexCount;
                int blendShapeFrames = mesh.GetBlendShapeFrameCount(blendShapeIndex);
                BlendShapeData blendShapeData = new BlendShapeData(blendShapeName, blendShapeFrames, vertexCount);

                for (int blendShapeFrame = 0; blendShapeFrame < blendShapeFrames; blendShapeFrame++)
                {
                    BlendShapeFrame frame = blendShapeData.frame[blendShapeFrame];
                    mesh.GetBlendShapeFrameVertices(
                        blendShapeIndex, blendShapeFrame,
                        frame.deltaVertices, frame.deltaNormals, frame.deltaTangents);
                }

                blendShapesData.Add(blendShapeData);
            }

            return blendShapesData.ToArray();
        }

        public void OptimizeRenderers(MyyAssetsManager assetsManager)
        {


            foreach (var rendererInfo in this)
            {                
                var renderer = rendererInfo.Key;
                var mesh = Instantiate(renderer.sharedMesh);
                string[] blendShapesNames = new string[rendererInfo.Value.Count];
                rendererInfo.Value.CopyTo(blendShapesNames);

                var blendShapesData = GetBlendshapesData(mesh, blendShapesNames);

                mesh.ClearBlendShapes();

                foreach (var blendShapeData in blendShapesData)
                {
                    blendShapeData.AddTo(mesh);
                }

                assetsManager.GenerateAsset(mesh, $"{mesh.name}-{mesh.GetInstanceID()}-Optimized.mesh");

                renderer.sharedMesh = mesh;
            }
        }

    }

    public UsedBlendshapes CollectBlendshapesInfo(
        AnimatorController controller,
        UsedBlendshapes usedBlendshapes)
    {
        
        foreach (var animationClip in controller.animationClips)
        {
            var curvesBindings = AnimationUtility.GetCurveBindings(animationClip);
            
            foreach (var curveBinding in curvesBindings)
            {
                var curve = AnimationUtility.GetEditorCurve(animationClip, curveBinding);

                string propertyName = curveBinding.propertyName;
                if (!propertyName.StartsWith("blendShape."))
                {
                    continue;
                }

                bool valueSet = false;
                foreach (var keyFrame in curve.keys)
                {
                    valueSet = (keyFrame.value > 0);
                    if (valueSet) break;
                }
                if (!valueSet)
                {
                    //Debug.Log("Ignoring blendshape not set");
                    continue;
                }

                Transform t = avatar.gameObject.transform.Find(curveBinding.path);
                if (t == null)
                {
                    Debug.Log($"Could not find object at {curveBinding.path}. Ignoring");
                    continue;
                }

                if (t.gameObject.TryGetComponent<SkinnedMeshRenderer>(out var renderer))
                {
                    usedBlendshapes.Add(renderer, propertyName.Replace("blendShape.", ""));
                }
                else
                {
                    Debug.Log($"{curveBinding.path} doesn't seem to have a SkinnedMeshRenderer. Ignoring");
                    continue;
                }
            }
        }
        return usedBlendshapes;
    }

    public bool CheckProvidedAvatar(SerializedProperty prop)
    {
        return avatar != null;
    }

    public void OnEnable()
    {
        ui = new SimpleEditorUI(this,
            ("Avatar to configure", "avatar", CheckProvidedAvatar));
    }

    [MenuItem("Voyage / Optimize Blendshapes")]

    public static void ShowWindow()
    {
        GetWindow(typeof(OptimizeBlendshapes));
        
    }

    private void OnGUI()
    {
        

        if (ui.DrawFields() && GUILayout.Button("Optimize Blendshapes"))
        {

            MyyAssetsManager myyAssetsManager = new MyyAssetsManager();
            string saveDir = myyAssetsManager.MkDir($"Optimized-${avatar.name}");
            myyAssetsManager.SetPath(saveDir);
            AnimatorController[] controllers = avatar.GetAnimatorControllers();
            UsedBlendshapes usedBlendshapes = new UsedBlendshapes();
            string[] eyeBlendshapeNames = new string[0];
            foreach (var controller in controllers)
            {
                CollectBlendshapesInfo(controller, usedBlendshapes);
            }


            bool blendshapesVisemes =
                (avatar.lipSync == VRC.SDKBase.VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape)
                & (avatar.VisemeSkinnedMesh != null);
            if (blendshapesVisemes)
            {
                usedBlendshapes.Add(avatar.VisemeSkinnedMesh, avatar.VisemeBlendShapes);
            }

            bool eyelookVisemes =
                (avatar.enableEyeLook)
                & (avatar.customEyeLookSettings.eyelidsSkinnedMesh != null)
                & (avatar.customEyeLookSettings.eyelidType == VRCAvatarDescriptor.EyelidType.Blendshapes);
            if (eyelookVisemes)
            {
                var eyeLookSettings = avatar.customEyeLookSettings;
                var skinEyeLids = eyeLookSettings.eyelidsSkinnedMesh;
                eyeBlendshapeNames = skinEyeLids.GetBlendshapesNames(eyeLookSettings.eyelidsBlendshapes);
                usedBlendshapes.Add(skinEyeLids, eyeBlendshapeNames);
            }

            usedBlendshapes.Dump();
            usedBlendshapes.OptimizeRenderers(myyAssetsManager);

            if (eyeBlendshapeNames.Length > 0)
            {
                var eyeLookSettings = avatar.customEyeLookSettings;
                int[] newBlendshapesIndices = 
                    eyeLookSettings.eyelidsSkinnedMesh.GetBlendshapesIndices(eyeBlendshapeNames);
                eyeLookSettings.eyelidsBlendshapes = newBlendshapesIndices;
            }

            

        }
    }
}

#endif