

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Battlehub.RTSaveLoad;
using uFileBrowser;
using UnityEngine;

namespace Pineapler.EyeScroller {
    public class VamEyeScroller : MVRScript {

        // Format asset bundles as:
        // ## IMPORTANT
        //  eyes.prefab
        //  |   eye.l
        //  |   eye.r
        //
        // ## Probably not important for now
        //  eyeMaterial
        //  eyeTexture
        //  eyeModel(s)

        private Material _replacementMat;
        private JSONStorableFloat _uPerRotation;
        private JSONStorableFloat _vPerRotation;
        private JSONStorableFloat _uniformTexScale;
        // private JSONStorableActionPresetFilePath _eyePresetUrl;
        private JSONStorableUrl _eyePresetUrl;
        // private CustomUnityAssetLoader _cuaLoader;
        private GameObject _headObj;
        private GameObject _personMeshObj;
        private SkinnedMeshRenderer _skinnedMesh;
        private Dictionary<int, Material> _originalEyeMats = new Dictionary<int, Material>();
        // private HashSet<string> _eyeMatMask = new HashSet<string> {
        //     "Cornea (Instance)",
        //     "EyeReflection (Instance)",
        //     "Irises (Instance)",
        //     "Lacrimals (Instance)",
        //     "Pupils (Instance)",
        //     "Sclera (Instance)",
        //     "Tear (Instance)"
        // };



        private Dictionary<string, int[]> _indexPaths = new Dictionary<string, int[]>{
            {"head", new [] { 0, 1, 0, 1, 0, 1, 0, 0, 0, 2, 0 }},
            {"Genesis2Female.Shape", new []{ 0, 1, 0, 1, 0, 0 }}
        };

        public override void Init() {
            try {
                if (containingAtom?.type != "Person") {
                    SuperController.LogError($"EyeScroller: Please make sure this plugin is applied on a \"Person\" atom (current: {containingAtom.type})");
                    DestroyImmediate(this);
                    return;
                }

                // Example storable; you can also create string, float and action JSON storables
                _uPerRotation = new JSONStorableFloat("U values per rotation", 1f, -2f, 2f, false);
                _vPerRotation = new JSONStorableFloat("V values per rotation", 1f, -2f, 2f, false);
                _uniformTexScale = new JSONStorableFloat("Texture scale", 1f, 0.0001f, 10f);
                _eyePresetUrl = new JSONStorableUrl("Eye Atom Preset", string.Empty, LoadEyeAsset);

                // You can use Register* methods to make your storable triggerable, and save and restore the value with the scene
                RegisterFloat(_uPerRotation);
                RegisterFloat(_vPerRotation);
                RegisterFloat(_uniformTexScale);
                RegisterUrl(_eyePresetUrl);

                // You can use Create* methods to add a control in the plugin's custom UI
                CreateButton("NOT IMPL. Select Eye ");
                CreateSlider(_uPerRotation);
                CreateSlider(_vPerRotation);
                CreateSlider(_uniformTexScale);


                _replacementMat = GenerateReplacementMat();


                // --- Get GameObject references ---
                _headObj = FindGameObjectInChildren("head");
                _personMeshObj = FindGameObjectInChildren("Genesis2Female.Shape");
                _skinnedMesh = _personMeshObj.GetComponent<SkinnedMeshRenderer>();
                CollectEyeMats();
                DisableEyeMats();


                // --- Debugging buttons ---
                CreateSpacer(true);

                UIDynamicButton printHeadPath = CreateButton("Print head transform path", true);
                printHeadPath.button.onClick.AddListener(() => {
                    int[] idxPath = GetParentToChildPath(_headObj.transform);
                    SuperController.LogMessage(
                        $"Head bone: {{{string.Join(", ", idxPath.Select(x => x.ToString()).ToArray())}}}");
                });

                UIDynamicButton printShapePath = CreateButton("Print G2FShape path", true);
                printShapePath.button.onClick.AddListener(() => {
                    int[] idxPath = GetParentToChildPath(_personMeshObj.transform);
                    SuperController.LogMessage(
                        $"Person Mesh: {{{string.Join(", ", idxPath.Select(x => x.ToString()).ToArray())}}}");
                });

                UIDynamicButton printSkinMats = CreateButton("Print Skin Materials", true);
                printSkinMats.button.onClick.AddListener(() => {
                    foreach (Material m in _skinnedMesh.materials) {
                        SuperController.LogMessage(m.name);
                    }
                });

            }
            catch (Exception e) {
                SuperController.LogError("EyeScroller: Failed to initialize. " + e);
            }
        }

        /// <summary>
        /// Search for a named GameObject in the children of the containingAtom.
        /// </summary>
        /// <param name="targetName"></param>
        /// <returns>a reference to the target GameObject</returns>
        /// <exception cref="Exception">No object with the specified name is found</exception>
        private GameObject FindGameObjectInChildren(string targetName) {
            GameObject target = DirectSearch(_indexPaths[targetName]);
            if (target == null || !target.name.Equals(targetName)) {
                target = DfsFallback(targetName, containingAtom.transform, true);
            }

            if (target == null) {
                // Script is useless if it's not on a player, throw exception and stop the script
                throw new Exception($"EyeScroller: Could not find \"{targetName}\". Please make sure this script is applied to a Person atom.");
            }

            return target;
        }


        /// <summary>
        /// Navigate the transform tree from the attached transform.
        /// </summary>
        /// <param name="treePath"></param>
        /// <returns></returns>
        private GameObject DirectSearch(int[] treePath) {
            try {
                Transform current = containingAtom.gameObject.transform;
                foreach (int idx in treePath) {
                    current = current.GetChild(idx);
                }

                return current.gameObject;
            }
            catch (Exception e){
                SuperController.LogError(e.Message);
                return null;
            }
        }


        /// <summary>
        /// Fallback Depth-first search of the character's GameObject, just in case the object structure isn't constant
        /// </summary>
        /// <param name="targetName">Name of the gameObject to find</param>
        /// <param name="root">Root node of the tree we are to search</param>
        /// <param name="currentDepth">Depth of the tree at the point of the function call</param>
        /// <param name="printStructure">Write the tree structure to the message log</param>
        /// <returns></returns>
        private GameObject DfsFallback(string targetName, Transform root, bool printStructure = false, int currentDepth = 0) {
            string tabStr = "";
            if (printStructure) {
                for (int i = 0; i < currentDepth; i++) {
                    tabStr += "|\t";
                }
                SuperController.LogMessage(tabStr + root.name);
            }

            if (root.name.Equals(targetName)) {
                return root.gameObject;
            }


            foreach (Transform child in root) {
                GameObject foundObject = DfsFallback(targetName, child, printStructure, currentDepth+1);
                if (foundObject != null) return foundObject;
            }

            return null;
        }


        /// <summary>
        /// Reconstruct the transform tree path to get to a child transform
        /// </summary>
        /// <param name="child"></param>
        /// <returns></returns>
        private int[] GetParentToChildPath(Transform child) {
            List<int> reverseList = new List<int>(16);
            while (child != containingAtom.transform) {
                reverseList.Add(child.GetSiblingIndex());
                child = child.parent;
            }

            reverseList.Reverse();
            return reverseList.ToArray();
        }


        private Material GenerateReplacementMat() {
            Material replacementMat = new Material(Shader.Find("Unlit/Transparent Cutout"));
            return replacementMat;
        }


        private void CollectEyeMats() {
            _originalEyeMats.Clear();
            for(int i = 0; i  < _skinnedMesh.materials.Length; i++){
                Material m = _skinnedMesh.materials[i];
                if (_eyeMatMask.Contains(m.name)) {
                    _originalEyeMats.Add(i, m);
                }
            }
        }


        public void LoadEyeAsset(string path) { }


        public void UpdateEyeMats() {
            EnableEyeMats();
            // TODO: Update material mask from json
            // UpdateMatMask();
            DisableEyeMats();
        }


        private void EnableEyeMats() {
            Material[] tempMats = _skinnedMesh.materials; // Can't set mats by index

            foreach (KeyValuePair<int, Material> kv in _originalEyeMats) {
                tempMats[kv.Key] = kv.Value;
            }

            _skinnedMesh.materials = tempMats;

        }


        private void DisableEyeMats() {
            CollectEyeMats();

            Material[] tempMats = _skinnedMesh.materials; // Can't set mats by index
            foreach (KeyValuePair<int, Material> kv in _originalEyeMats) {
                tempMats[kv.Key] = _replacementMat;
            }

            _skinnedMesh.materials = tempMats;
        }

        private void LateUpdate() {
            try {

            }
            catch (Exception e){
                SuperController.LogError("EyeScroller: " + e);
            }
        }

        private void OnDestroy() {
            EnableEyeMats();
        }
    }


    // ##############################################################################
    // ###  The following code has been repurposed from acidbubbles' ImprovedPoV  ###
    // ##############################################################################

    public static class HandlerConfigurationResult
    {
        public const int Success = 0;
        public const int CannotApply = 1;
        public const int TryAgainLater = 2;
    }

    public interface IHandler
    {
        void Restore();
        void BeforeRender();
        void AfterRender();
    }

    public class SkinHandler : IHandler
        {
            public class SkinShaderMaterialReference
            {
                public Material material;
                public Shader originalShader;
                public float originalAlphaAdjust;
                public float originalColorAlpha;
                public Color originalSpecColor;

                public static SkinShaderMaterialReference FromMaterial(Material material)
                {
                    return new SkinShaderMaterialReference
                    {
                        material = material,
                        originalShader = material.shader,
                        originalAlphaAdjust = material.GetFloat("_AlphaAdjust"),
                        originalColorAlpha = material.GetColor("_Color").a,
                        originalSpecColor = material.GetColor("_SpecColor")
                    };
                }
            }

            public static readonly string[] MaterialsToHide = new[]
            {
                "Lacrimals",
                "Pupils",
                // "Lips",
                // "Gums",
                "Irises",
                // "Teeth",
                // "Face",
                // "Head",
                // "InnerMouth",
                // "Tongue",
                "EyeReflection",
                // "Nostrils",
                "Cornea",
                // "Eyelashes",
                "Sclera",
                // "Ears",
                "Tear"
            };

            public static IList<Material> GetMaterialsToHide(DAZSkinV2 skin)
            {
    #if (POV_DIAGNOSTICS)
                if (skin == null) throw new NullReferenceException("skin is null");
                if (skin.GPUmaterials == null) throw new NullReferenceException("skin materials are null");
    #endif

                var materials = new List<Material>(MaterialsToHide.Length);

                foreach (var material in skin.GPUmaterials)
                {
                    if (!MaterialsToHide.Any(materialToHide => material.name.StartsWith(materialToHide)))
                        continue;

                    materials.Add(material);
                }

    #if (POV_DIAGNOSTICS)
                // NOTE: Tear is not on all models
                if (materials.Count < MaterialsToHide.Length - 1)
                    throw new Exception("Not enough materials found to hide. List: " + string.Join(", ", skin.GPUmaterials.Select(m => m.name).ToArray()));
    #endif

                return materials;
            }

            private static readonly Dictionary<string, Shader> ReplacementShaders = new Dictionary<string, Shader>
                {
                    // Opaque materials
                    { "Custom/Subsurface/GlossCullComputeBuff", Shader.Find("Custom/Subsurface/TransparentGlossSeparateAlphaComputeBuff") },
                    { "Custom/Subsurface/GlossNMCullComputeBuff", Shader.Find("Custom/Subsurface/TransparentGlossNMSeparateAlphaComputeBuff") },
                    { "Custom/Subsurface/GlossNMDetailCullComputeBuff", Shader.Find("Custom/Subsurface/TransparentGlossNMDetailNoCullSeparateAlphaComputeBuff") },
                    { "Custom/Subsurface/CullComputeBuff", Shader.Find("Custom/Subsurface/TransparentSeparateAlphaComputeBuff") },

                    // Transparent materials
                    { "Custom/Subsurface/TransparentGlossNoCullSeparateAlphaComputeBuff", null },
                    { "Custom/Subsurface/TransparentGlossComputeBuff", null },
                    { "Custom/Subsurface/TransparentComputeBuff", null },
                    { "Custom/Subsurface/AlphaMaskComputeBuff", null },
                    { "Marmoset/Transparent/Simple Glass/Specular IBLComputeBuff", null },
                };

            private DAZSkinV2 _skin;
            private List<SkinShaderMaterialReference> _materialRefs;

            public int Configure(DAZSkinV2 skin)
            {
                _skin = skin;
                _materialRefs = new List<SkinShaderMaterialReference>();

                foreach (var material in GetMaterialsToHide(skin))
                {
    #if (IMPROVED_POV)
                    if(material == null)
                        throw new InvalidOperationException("Attempts to apply the shader strategy on a destroyed material.");

                    if (material.GetInt(SkinShaderMaterialReference.ImprovedPovEnabledShaderKey) == 1)
                        throw new InvalidOperationException("Attempts to apply the shader strategy on a skin that already has the plugin enabled (shader key).");
    #endif

                    var materialInfo = SkinShaderMaterialReference.FromMaterial(material);

                    Shader shader;
                    if (!ReplacementShaders.TryGetValue(material.shader.name, out shader))
                        SuperController.LogError("Missing replacement shader: '" + material.shader.name + "'");

                    if (shader != null) material.shader = shader;

                    _materialRefs.Add(materialInfo);
                }

                // This is a hack to force a refresh of the shaders cache
                skin.BroadcastMessage("OnApplicationFocus", true);
                return HandlerConfigurationResult.Success;
            }

            public void Restore()
            {
                foreach (var material in _materialRefs)
                    material.material.shader = material.originalShader;

                _materialRefs = null;

                // This is a hack to force a refresh of the shaders cache
                _skin.BroadcastMessage("OnApplicationFocus", true);
            }

            public void BeforeRender()
            {
                foreach (var materialRef in _materialRefs)
                {
                    var material = materialRef.material;
                    material.SetFloat("_AlphaAdjust", -1f);
                    var color = material.GetColor("_Color");
                    material.SetColor("_Color", new Color(color.r, color.g, color.b, 0f));
                    material.SetColor("_SpecColor", new Color(0f, 0f, 0f, 0f));
                }
            }

            public void AfterRender()
            {
                foreach (var materialRef in _materialRefs)
                {
                    var material = materialRef.material;
                    material.SetFloat("_AlphaAdjust", materialRef.originalAlphaAdjust);
                    var color = material.GetColor("_Color");
                    material.SetColor("_Color", new Color(color.r, color.g, color.b, materialRef.originalColorAlpha));
                    material.SetColor("_SpecColor", materialRef.originalSpecColor);
                }
            }
        }
}
