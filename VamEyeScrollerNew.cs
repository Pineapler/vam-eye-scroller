using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Battlehub.RTCommon;
using UnityEngine;
using UnityEngine.UI;
using static MeshVR.AssetLoader;
using Request = MeshVR.AssetLoader.AssetBundleFromFileRequest;

namespace Pineapler.EyeScroller {
    public class VamEyeScrollerNew : MVRScript {

        // TODO: Steal material from sclera

        // Current bugs:
        // TODO: Original eyes aren't being hidden when plugin is loaded from a preset
        // TODO: Head bone not being found if eye bundle is loaded while Active



        // JSON Storables
        private JSONStorableBool[] _hideFaceMaterials { get; } = new[] {
            new JSONStorableBool("Lacrimals", true),
            new JSONStorableBool("Pupils", true),
            new JSONStorableBool("Lips", false),
            new JSONStorableBool("Gums", false),
            new JSONStorableBool("Irises", true),
            new JSONStorableBool("Teeth", false),
            new JSONStorableBool("Face", false),
            new JSONStorableBool("Head", false),
            new JSONStorableBool("InnerMouth", false),
            new JSONStorableBool("Tongue", false),
            new JSONStorableBool("EyeReflection", true),
            new JSONStorableBool("Nostrils", false),
            new JSONStorableBool("Cornea", true),
            new JSONStorableBool("Eyelashes", false),
            new JSONStorableBool("Sclera", true),
            new JSONStorableBool("Ears", false),
            new JSONStorableBool("Tear", true),
        };
        private JSONStorableUrl _eyesUrl;
        private JSONStorableBool _activeToggle;
        private JSONStorableBool _delayOneFrame;
        private JSONStorableBool _eyeMirror;
        private JSONStorableFloat _uPerRotation;
        private JSONStorableFloat _vPerRotation;
        private JSONStorableFloat _uIrisOffset;
        private JSONStorableFloat _vIrisOffset;
        private JSONStorableFloat _uniformTexScale;
        private JSONStorableFloat _zBoneOffset;


        // Character-related
        private Atom _person;
        private DAZCharacterSelector _selector;
        private DAZCharacter _character;
        private SkinHandler _skinHandler;

        private bool _dirty = true;
        private int _tryAgainAttempts;
        private bool _failedOnce;

        // Eyes-related
        private bool _eyesBad = true;
        private UIDynamicToggle _validToggleVis;
        private EyesObject _parsedEyes;

        private Vector2 _uvsPerRotation;
        private Vector2 _uvIrisOffset;
        private float _uniformTexScaleF;

        private Transform _lLookReference;
        private Transform _rLookReference;
        private Transform _headTransform;

        private Vector3 _posLastFrame = Vector3.zero;
        private Quaternion _rotLastFrame = Quaternion.identity;
        private Vector3 _lossyScaleLastFrame = Vector3.one;

        private readonly Color BG_DISABLED = new Color(0.7f, 0.7f, 0.7f, 1);
        private readonly Color BG_VALID = new Color(0.4f, 0.7f, 0.4f, 1);
        private readonly Color BG_INVALID = new Color(0.7f, 0.4f, 0.4f, 1);

        public override void Init() {
            try {
                if (containingAtom?.type != "Person") {
                    SuperController.LogError(
                        $"EyeScroller: Please make sure this plugin is applied on a \"Person\" atom (current: {containingAtom.type})");
                    DestroyImmediate(this);
                    return;
                }

                InitCustomUI();
                InitReferences();
            }
            catch (Exception e) {
                SuperController.LogError("EyeScroller: Failed to initialize. " + e);
                DestroyImmediate(this);
            }
        }


        private void Update() {
            try {
                if (_dirty) {
                    RegisterHandlers();
                    return;
                }

                if (_selector.selectedCharacter != _character) {
                    RefreshHandlers();
                    return;
                }
            }
            catch (Exception e) {
                if (_failedOnce) return;
                _failedOnce = true;
                SuperController.LogError("Failed to update HideGeometry: " + e);
            }
        }


        private void LateUpdate() {
            try {
                _validToggleVis.toggle.isOn = !_eyesBad;
                if (!_activeToggle.val) {
                    _validToggleVis.backgroundColor = BG_DISABLED;
                    return;
                }
                if (_eyesBad) {
                    _validToggleVis.backgroundColor = BG_INVALID;
                    return;
                }
                _validToggleVis.backgroundColor = BG_VALID;


                Transform instanceT = _parsedEyes.instance.transform;
                if (_delayOneFrame.val) {
                    instanceT.localPosition = _posLastFrame;
                    instanceT.rotation = _rotLastFrame;
                    instanceT.localScale = _lossyScaleLastFrame;

                    _posLastFrame = _headTransform.TransformPoint(new Vector3(0, 0, _zBoneOffset.val));
                    _rotLastFrame = _headTransform.rotation;
                    _lossyScaleLastFrame = _headTransform.lossyScale;
                }
                else {
                    instanceT.localPosition = _headTransform.TransformPoint(new Vector3(0, 0, _zBoneOffset.val));
                    instanceT.rotation = _headTransform.rotation;
                    instanceT.localScale = _headTransform.lossyScale;
                }


                ScrollUVs();
            }
            catch (Exception e) {
                SuperController.LogError("EyeScroller: " + e);
                SuperController.LogMessage($"Objects exist? {_validToggleVis != null} {_parsedEyes != null} {_headTransform != null} {_zBoneOffset != null}");
            }
        }


         private readonly Vector2 POINT_FIVE = new Vector2(0.5f, 0.5f);

         private void ScrollUVs() {
             ScrollUV(_parsedEyes.lMesh, _lLookReference, _parsedEyes.lOriginalUVs, _parsedEyes.lCurrentUVs, false);
             ScrollUV(_parsedEyes.rMesh, _rLookReference, _parsedEyes.rOriginalUVs, _parsedEyes.rCurrentUVs, _eyeMirror.val);
         }


         private void ScrollUV(Mesh mesh, Transform referenceRot, Vector2[] originalUVs, Vector2[] currentUVs, bool mirror) {

             float mirrorF = mirror ? -1f : 1f;
             Vector3 refObjRotation = referenceRot.localRotation.eulerAngles;

             // Get angles in range [-180, 180]
             float horizontalRot = Mathf.Repeat(refObjRotation.y + 180f, 360f) - 180f;
             float verticalRot = Mathf.Repeat(refObjRotation.x + 180f, 360f) - 180f;

             // Scale rotation-to-UV offset
             Vector2 uvOffset = new Vector2(horizontalRot * mirrorF, verticalRot) / 180f * _uvsPerRotation;

             for (int i = 0; i < currentUVs.Length; i++) {
                 Vector2 uv = originalUVs[i];
                 uv += _uvIrisOffset;
                 uv += uvOffset;
                 uv -= POINT_FIVE;
                 uv /= _uniformTexScaleF;
                 uv += POINT_FIVE;
                 currentUVs[i] = uv;
             }

             mesh.uv = currentUVs;
         }


        private void OnEnable() {
            RegisterHandlers();
        }

        private void OnDisable() {
            ClearHandlers();
        }

        private void OnDestroy() {
            if (_parsedEyes != null && _parsedEyes.instance != null) {
                _parsedEyes.Destroy();
                _parsedEyes = null;
            }
        }

        // ###########################################################
        // # JSON, Reference and UI Setup ############################
        // ###########################################################

        #region JSON, Reference and UI Setup

        public void InitCustomUI() {
            _activeToggle = new JSONStorableBool("Active", false, (bool _) => RefreshHandlers());
            _eyeMirror = new JSONStorableBool("Mirror one eye", true);
            _uPerRotation = new JSONStorableFloat("U values per revolution", 1f,
                val => _uvsPerRotation = new Vector2(val, _vPerRotation.val), -10f, 10f, false);
            _vPerRotation = new JSONStorableFloat("V values per revolution", 1f,
                val => _uvsPerRotation = new Vector2(_uPerRotation.val, val), -10f, 10f, false);
            _uIrisOffset = new JSONStorableFloat("U Iris Offset", 0,
                val => _uvIrisOffset = new Vector2(val, _vIrisOffset.val), -0.5f, 0.5f, false);
            _vIrisOffset = new JSONStorableFloat("V Iris Offset", 0,
                val => _uvIrisOffset = new Vector2(_uIrisOffset.val, val), -0.5f, 0.5f, false);
            _uniformTexScale = new JSONStorableFloat("Texture scale", 1f, val => _uniformTexScaleF = val, 0.0001f, 3f, false);
            _zBoneOffset = new JSONStorableFloat("Z Bone Offset", 0f, -0.1f, 0.1f, false);
            _delayOneFrame = new JSONStorableBool("Delay Position One Frame", true);

            RegisterBool(_activeToggle);
            RegisterBool(_eyeMirror);
            RegisterBool(_delayOneFrame);
            RegisterFloat(_uPerRotation);
            RegisterFloat(_vPerRotation);
            RegisterFloat(_uIrisOffset);
            RegisterFloat(_vIrisOffset);
            RegisterFloat(_uniformTexScale);

            // These get read very frequently, unwrap them from JSON to avoid passing around objects
            _uvsPerRotation = new Vector2(_uPerRotation.val, _vPerRotation.val);
            _uvIrisOffset = new Vector2(_uIrisOffset.val, _vIrisOffset.val);
            _uniformTexScaleF = _uniformTexScale.val;

            CreateToggle(_activeToggle);
            _validToggleVis = CreateToggle(new JSONStorableBool("Valid setup", false), true);
            _validToggleVis.toggle.interactable = false;

            CreateSpacer();

            _eyesUrl = Utils.SetupAssetBundleChooser(this, "Eyes URL", String.Empty, false, "assetbundle");
            _eyesUrl.setCallbackFunction += url => {
                if (_parsedEyes != null && _parsedEyes.instance != null) {
                    _parsedEyes.Destroy();
                    _parsedEyes = null;
                }
                Request request = new AssetBundleFromFileRequest {
                    path = url,
                    callback = OnAssetBundleLoaded
                };
                QueueLoadAssetBundleFromFile(request);
            };

            foreach (var hideFaceMaterial in _hideFaceMaterials) {
                hideFaceMaterial.setCallbackFunction = _ => RefreshHandlers();
                // RegisterBool(hideFaceMaterial);
                // CreateToggle(hideFaceMaterial);
            }


            CreateSpacer();
            CreateToggle(_eyeMirror);
            CreateSlider(_uPerRotation);
            CreateSlider(_vPerRotation);
            CreateSpacer();
            CreateSlider(_uIrisOffset);
            CreateSlider(_vIrisOffset);
            CreateSlider(_zBoneOffset, true).valueFormat = "F4";
            CreateSlider(_uniformTexScale, true);


        }

        public void InitReferences() {
            _person = containingAtom;
            _selector = _person.GetComponentInChildren<DAZCharacterSelector>();
        }

        #endregion


        // ###########################################################
        // # Eye Model Loading #######################################
        // ###########################################################

        #region Eye Model Loading

        public void OnAssetBundleLoaded(Request request) {
            try {
                RefreshHandlers();
                _eyesBad = true;
                string[] assetPaths = request.assetBundle.GetAllAssetNames();

                string firstPrefabPath = assetPaths.FirstOrDefault(s => s.EndsWith(".prefab"));

                if (firstPrefabPath == null) {
                    Utils.PrintErrorUsage("No prefab was found in the specified AssetBundle.");
                    return;
                }

                _parsedEyes = new EyesObject(request, firstPrefabPath);
                if (!_parsedEyes.isValidRig) {
                    return;
                }
                _eyesBad = false;
            }
            catch (Exception e) {
                SuperController.LogError(e.Message);

                _eyesBad = true;
            }
        }
        #endregion


        // ###########################################################
        // # Character Loading #######################################
        // ###########################################################

        #region Character Loading

        public void RegisterHandlers() {
            _dirty = false;

            // ReSharper disable once Unity.NoNullPropagation

            _character = _selector.selectedCharacter;
            if (_character == null) {
                MakeDirty("character", "is not yet loaded.");
                return;
            }

            if (_character.skin == null) {
                MakeDirty("skin", "is not yet loaded.");
                return;
            }


            if (_activeToggle.val && !_eyesBad) {
                if (!RegisterHandler(new SkinHandler(_character.skin,_hideFaceMaterials.Where(x => x.val).Select(x => x.name), _hideFaceMaterials.Length))) {
                    MakeDirty("Handler", "is not configured");
                    return;
                }

                foreach (DAZBone bone in _character.skin.root.dazBones) {
                    switch (bone.name) {
                         case "lEye":
                             _lLookReference = bone.transform;
                             break;
                         case "rEye":
                             _rLookReference = bone.transform;
                             break;
                         case "head":
                             _headTransform = bone.transform;
                             break;
                    }
                }

                _skinHandler.MaterialsOff();
            }


            if (!_dirty) {
                _tryAgainAttempts = 0;
            }
        }

        private bool RegisterHandler(SkinHandler handler) {
            _skinHandler = handler;
            bool configured = handler.Prepare();
            if (!configured) {
                ClearHandlers();
                return false;
            }

            return true;
        }


        public void RefreshHandlers() {
            if (!enabled || !_activeToggle.val) return;
            ClearHandlers();
            RegisterHandlers();
        }


        public void ClearHandlers() {
            _skinHandler?.Dispose();
            _skinHandler = null;
            _character = null;
            _dirty = false;
        }


        private void MakeDirty(string what, string reason)
        {
            _dirty = true;
            _tryAgainAttempts++;
            if (_tryAgainAttempts > 90 * 20) // Approximately 20 to 40 seconds
            {
                SuperController.LogError($"Failed to apply HideGeometry. Reason: {what} {reason}. Try reloading the plugin, or report the issue to @Pineapler.");
                enabled = false;
            }
        }

        #endregion
    }

    // ###########################################################
    // # Eyes class ##############################################
    // ###########################################################
    #region Eyes class
    public class EyesObject {
        public bool isValidRig = true;
        public GameObject instance;
        public Transform root;
        public Mesh lMesh;
        public Mesh rMesh;
        public Vector2[] lOriginalUVs;
        public Vector2[] rOriginalUVs;
        public Vector2[] lCurrentUVs;
        public Vector2[] rCurrentUVs;


        public EyesObject(Request request, string path) {
            root = request.assetBundle.LoadAsset<GameObject>(path).transform;
            if (!PopulateData(root)) {
                Utils.PrintErrorUsage($"Error deconstructing the eyes prefab before instantiating.\n" +
                                      $"[eye.l found: {lMesh != null}]\n[eye.r found: {rMesh != null}]\n" +
                                      $"[eye.l readable: {lMesh?.isReadable}]\n[eye.r readable: {rMesh?.isReadable}]\n" +
                                      $"Found object hierarchy: \n\n{Utils.ObjectHierarchyToString(root)}");
                DoneWithAssetBundleFromFile(request.path);
                return;
            }

            instance = GameObject.Instantiate(root.gameObject);
            if (!PopulateData(instance.transform)) {
                Utils.PrintErrorUsage($"Error deconstructing the eyes prefab after instantiating.\n" +
                                      $"[eye.l found: {lMesh != null}]\n[eye.r found: {rMesh != null}]\n" +
                                      $"[eye.l readable: {lMesh?.isReadable}]\n[eye.r readable: {rMesh?.isReadable}]\n" +
                                      $"Found object hierarchy: \n\n{Utils.ObjectHierarchyToString(root)}");
                GameObject.DestroyImmediate(instance);
                DoneWithAssetBundleFromFile(request.path);
                return;
            }
            // DoneWithAssetBundleFromFile(request.path);
            lOriginalUVs = (Vector2[])lMesh.uv.Clone();
            rOriginalUVs = (Vector2[])rMesh.uv.Clone();
            lCurrentUVs = lMesh.uv;
            rCurrentUVs = rMesh.uv;
        }


        private bool PopulateData(Transform root) {
            for (int i = root.childCount - 1; i >= 0;  i--) { // bottom up, this way we get references to the first .l and .r
                Transform t = root.GetChild(i);
                if (t.name.EndsWith(".l")) {
                    lMesh = t.GetComponent<MeshFilter>().mesh;
                } else if (t.name.EndsWith(".r")) {
                    rMesh = t.GetComponent<MeshFilter>().mesh;
                }
            }
            return lMesh != null && rMesh != null && lMesh.isReadable && rMesh.isReadable;
        }

        public void Destroy() {
            if (instance != null) {
                GameObject.Destroy(instance);
            }
        }

        ~EyesObject(){
            Destroy();
        }
    }
    #endregion


    // ###########################################################
    // # Character Material Replacement ##########################
    // ###########################################################
    #region Character Material Replacement
    public class SkinHandler : IDisposable {

        private readonly DAZSkinV2 _skin;
        private readonly IEnumerable<string> _materialsToHide;
        private readonly int _materialsToHideMax;
        private List<SkinShaderMaterialSnapshot> _materialRefs;


        public SkinHandler(DAZSkinV2 skin, IEnumerable<string> materialsToHide, int materialsToHideMax)
        {
            _skin = skin;
            _materialsToHide = materialsToHide;
            _materialsToHideMax = materialsToHideMax;
        }


        public bool Prepare()
        {
            _materialRefs = new List<SkinShaderMaterialSnapshot>();

            foreach (var material in GetMaterialsToHide())
            {
                var materialInfo = SkinShaderMaterialSnapshot.FromMaterial(material);

                Shader shader;
                if (!ReplacementShaders.ShadersMap.TryGetValue(material.shader.name, out shader))
                    SuperController.LogError("Missing replacement shader: '" + material.shader.name + $"' ({material.name} will not be hidden)");

                if (shader != null)
                {
                    material.shader = shader;
                    materialInfo.alphaAdjustSupport = material.HasProperty("_AlphaAdjust");
                    materialInfo.alphaCutoffSupport = material.HasProperty("_Cutoff");
                    materialInfo.specColorSupport = material.HasProperty("_SpecColor");
                }
                else
                {
                    materialInfo.alphaAdjustSupport = materialInfo.originalAlphaAdjustSupport;
                    materialInfo.alphaCutoffSupport = materialInfo.originalAlphaCutoffSupport;
                    materialInfo.specColorSupport = materialInfo.originalSpecColorSupport;
                }

                _materialRefs.Add(materialInfo);
            }

            // This is a hack to force a refresh of the shaders cache
            _skin.BroadcastMessage("OnApplicationFocus", true);
            return true;
        }


        private IEnumerable<Material> GetMaterialsToHide()
        {
            var materials = new List<Material>(_materialsToHideMax);

            foreach (var material in _skin.GPUmaterials)
            {
                if (material == null)
                    continue;
                if (!_materialsToHide.Any(materialToHide => material.name.StartsWith(materialToHide)))
                    continue;

                materials.Add(material);
            }

            return materials;
        }


        public void MaterialsOff()
        {
            for (var i = 0; i < _materialRefs.Count; i++)
            {
                var materialRef = _materialRefs[i];
                var material = materialRef.material;
                if (materialRef.alphaCutoffSupport)
                    material.SetFloat("_Cutoff", 0.3f);
                if (materialRef.alphaAdjustSupport)
                    material.SetFloat("_AlphaAdjust", -1f);
                var color = material.GetColor("_Color");
                material.SetColor("_Color", new Color(color.r, color.g, color.b, 0f));
                if (materialRef.specColorSupport)
                    material.SetColor("_SpecColor", new Color(0f, 0f, 0f, 0f));
            }
        }

        public void MaterialsOn()
        {
            for (var i = 0; i < _materialRefs.Count; i++)
            {
                var materialRef = _materialRefs[i];
                var material = materialRef.material;
                if (materialRef.alphaCutoffSupport)
                    material.SetFloat("_Cutoff", materialRef.originalAlphaCutoff);
                if (materialRef.alphaAdjustSupport)
                    material.SetFloat("_AlphaAdjust", materialRef.originalAlphaAdjust);
                var color = material.GetColor("_Color");
                material.SetColor("_Color", new Color(color.r, color.g, color.b, materialRef.originalColorAlpha));
                if (materialRef.specColorSupport)
                    material.SetColor("_SpecColor", materialRef.originalSpecColor);
            }
        }


        public void Dispose()
        {
            foreach (var material in _materialRefs)
                material.material.shader = material.originalShader;

            // This is a hack to force a refresh of the shaders cache
            _skin.BroadcastMessage("OnApplicationFocus", true);

            _materialRefs.Clear();
        }
    }


    public class SkinShaderMaterialSnapshot
    {
        public Material material;
        public bool alphaAdjustSupport;
        public bool alphaCutoffSupport;
        public bool specColorSupport;

        public Shader originalShader;
        public float originalAlphaAdjust;
        public float originalAlphaCutoff;
        public float originalColorAlpha;
        public Color originalSpecColor;
        public bool originalAlphaAdjustSupport;
        public bool originalAlphaCutoffSupport;
        public bool originalSpecColorSupport;

        public static SkinShaderMaterialSnapshot FromMaterial(Material material)
        {
            var originalAlphaAdjustSupport = material.HasProperty("_AlphaAdjust");
            var originalAlphaCutoffSupport = material.HasProperty("_Cutoff");
            var originalSpecColorSupport = material.HasProperty("_SpecColor");
            return new SkinShaderMaterialSnapshot
            {
                material = material,
                originalShader = material.shader,
                originalAlphaAdjustSupport = originalAlphaAdjustSupport,
                originalAlphaAdjust = originalAlphaAdjustSupport ? material.GetFloat("_AlphaAdjust") : 0,
                originalAlphaCutoffSupport = originalAlphaCutoffSupport,
                originalAlphaCutoff = originalAlphaCutoffSupport ? material.GetFloat("_Cutoff") : 0,
                originalColorAlpha = material.GetColor("_Color").a,
                originalSpecColorSupport = originalSpecColorSupport,
                originalSpecColor = originalSpecColorSupport ? material.GetColor("_SpecColor") : Color.black
            };
        }
    }


    public static class ReplacementShaders
    {
        public static readonly Dictionary<string, Shader> ShadersMap = new Dictionary<string, Shader>
        {
            // Opaque materials
            { "Custom/Subsurface/GlossCullComputeBuff", Shader.Find("Custom/Subsurface/TransparentGlossSeparateAlphaComputeBuff") },
            { "Custom/Subsurface/GlossNMCullComputeBuff", Shader.Find("Custom/Subsurface/TransparentGlossNMSeparateAlphaComputeBuff") },
            { "Custom/Subsurface/GlossNMDetailCullComputeBuff", Shader.Find("Custom/Subsurface/TransparentGlossNMDetailNoCullSeparateAlphaComputeBuff") },
            { "Custom/Subsurface/CullComputeBuff", Shader.Find("Custom/Subsurface/TransparentSeparateAlphaComputeBuff") },

            // Transparent materials
            { "Custom/Subsurface/TransparentGlossNMNoCullSeparateAlphaComputeBuff", null },
            { "Custom/Subsurface/TransparentGlossNoCullSeparateAlphaComputeBuff", null },
            { "Custom/Subsurface/TransparentGlossComputeBuff", null },
            { "Custom/Subsurface/TransparentComputeBuff", null },
            { "Custom/Subsurface/AlphaMaskComputeBuff", null },
            { "Marmoset/Transparent/Simple Glass/Specular IBLComputeBuff", null },

            // Hunting-Succubus' tesselation material
            { "Custom/Subsurface/GlossNMTessMappedFixedComputeBuff", Shader.Find("Custom/Subsurface/TransparentGlossNMDetailNoCullSeparateAlphaComputeBuff") },

            // If we currently work with incorrectly restored materials, let's just keep them
            { "Custom/Subsurface/TransparentGlossSeparateAlphaComputeBuff", null },
            { "Custom/Subsurface/TransparentGlossNMSeparateAlphaComputeBuff", null },
            { "Custom/Subsurface/TransparentGlossNMDetailNoCullSeparateAlphaComputeBuff", null },
            { "Custom/Subsurface/TransparentSeparateAlphaComputeBuff", null },
            { "Custom/Discard", null},
        };

    }
    #endregion

    // ################################################################
    // ### Utils ######################################################
    // ################################################################
    #region utils
    public static class Utils {

        public static string ObjectHierarchyToString(Transform root) {
            StringBuilder builder = new StringBuilder();
            ObjectHierarchyToString(root, builder);
            return builder.ToString();
        }


        private static void ObjectHierarchyToString(Transform root, StringBuilder builder, int currentDepth = 0) {
            for (int i = 0; i < currentDepth; i++) {
                builder.Append("|\t");
            }

            builder.Append(root.name + "\n");

            foreach (Transform child in root) {
                ObjectHierarchyToString(child, builder, currentDepth+1);
            }
        }


        public static void PrintErrorUsage(string prefixLine = "") {
            string errorString = "EyeScroller: " + prefixLine + "\n" +
                                 @"Please make sure your AssetBundle contains a prefab with following elements:
<eyes>.prefab
|   <eye>.l
|   <eye>.r";

            SuperController.LogError(errorString);
        }


        /// <summary>
        /// Gets the transform of a child specified with a relative address.
        /// </summary>
        /// <param name="root">The parent transform to search from.</param>
        /// <param name="address">Forward slash-separated path to traverse children.</param>
        /// <returns></returns>
        public static Transform GetChildByHierarchy(Transform root, string address) {
            return GetChildByHierarchy(root, address.Split('/'));
        }

        /// <summary>
        /// Gets the transform of a child specified with a relative address.
        /// </summary>
        /// <param name="root">The parent transform to search from.</param>
        /// <param name="address">Array of child names to traverse in order.</param>
        /// <returns></returns>
        public static Transform GetChildByHierarchy(Transform root, string[] address) {
            string originalRootName = root.name;
            foreach (string nextName in address) {
                Transform prevRoot = root;
                for (int i = 0; i < root.childCount; i++) {
                    Transform child = root.GetChild(i);
                    if (!child.name.Equals(nextName)) continue;
                    root = child;
                    break;
                }

                if (prevRoot == root) {
                    throw new Exception($"Address {string.Join("/", address)} does not match the structure of GameObject {originalRootName}");
                }
            }
            return root;
        }


        public static String TransformParentsToString(Transform child) {
            string outStr = "/" + child.name;
            while (child.parent != null) {
                child = child.parent;
                outStr = "/" + child.name + outStr;
            }

            return outStr;
        }


        // ################################################################
        // # The following is from Utils by MacGruber #####################
        // # https://www.patreon.com/MacGruber_Laboratory #################
        // # Licensed under CC BY-SA ######################################
        // ################################################################

        // Overrides for the 3 methods below.
        public static string PluginPathOverride = null;
        public static string PackagePathOverride = null;
        public static bool? IsInPackageOverride = null;

        // Get directory path where the plugin is located. Based on Alazi's & VAMDeluxe's method.
        public static string GetPluginPath(MVRScript self)
        {
            if (PluginPathOverride != null)
                return PluginPathOverride;

            string id = self.name.Substring(0, self.name.IndexOf('_'));
            string filename = self.manager.GetJSON()["plugins"][id].Value;
            return filename.Substring(0, filename.LastIndexOfAny(new char[] { '/', '\\' }));
        }

        // Get path prefix of the package that contains our plugin.
        public static string GetPackagePath(MVRScript self)
        {
            if (PackagePathOverride != null)
                return PackagePathOverride;

            string id = self.name.Substring(0, self.name.IndexOf('_'));
            string filename = self.manager.GetJSON()["plugins"][id].Value;
            int idx = filename.IndexOf(":/");
            if (idx >= 0)
                return filename.Substring(0, idx+2);
            else
                return string.Empty;
        }

        // Check if our plugin is running from inside a package
        public static bool IsInPackage(MVRScript self)
        {
            if (IsInPackageOverride != null)
                return (bool)IsInPackageOverride;

            string id = self.name.Substring(0, self.name.IndexOf('_'));
            string filename = self.manager.GetJSON()["plugins"][id].Value;
            return filename.IndexOf(":/") >= 0;
        }

        // Create VaM-UI AssetBundleChooser.
        public static JSONStorableUrl SetupAssetBundleChooser(MVRScript self, string label, string defaultValue, bool rightSide, string fileExtensions)
        {
            JSONStorableUrl storable = new JSONStorableUrl(label, defaultValue, fileExtensions);
            self.RegisterUrl(storable);
            UIDynamicButton button = self.CreateButton("Select " + label, false);
            UIDynamicTextField textfield = self.CreateTextField(storable, false);
            textfield.UItext.alignment = TextAnchor.MiddleRight;
            textfield.UItext.horizontalOverflow = HorizontalWrapMode.Overflow;
            textfield.UItext.verticalOverflow = VerticalWrapMode.Truncate;
            LayoutElement layout = textfield.GetComponent<LayoutElement>();
            layout.preferredHeight = layout.minHeight = 35;
            textfield.height = 35;
            if (!string.IsNullOrEmpty(defaultValue))
                storable.SetFilePath(defaultValue);
            storable.RegisterFileBrowseButton(button.button);
            return storable;
        }

    }
    #endregion
}
