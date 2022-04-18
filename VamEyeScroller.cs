using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Request = MeshVR.AssetLoader.AssetBundleFromFileRequest;
using MeshVR;
using UnityEngine;

namespace Pineapler.EyeScroller {
/// <summary>
/// VamEyeScroller Version 0.0.0
/// By Pineapler
/// Stylized eyes that don't give you nightmares
/// Source: https://github.com/pineaplers/vam-eye-scroller
/// </summary>
    public class VamEyeScroller : MVRScript {

        private static readonly string _fileExt = "assetbundle";
        private string _lastBrowseDir = @"Custom\Assets\";
        private bool _isValidSetup = true;

        private JSONStorableBool _activeToggle;

        private JSONStorableBool _eyeMirror;
        private JSONStorableBool _delayOneFrame;
        private JSONStorableFloat _uPerRotation;
        private JSONStorableFloat _vPerRotation;
        private JSONStorableFloat _uIrisOffset;
        private JSONStorableFloat _vIrisOffset;
        private JSONStorableFloat _uniformTexScale;
        private JSONStorableFloat _zBoneOffset;

        private Vector2 _uvsPerRotation;
        private Vector2 _uvIrisOffset;
        private float _uniformTexScaleF;

        private JSONStorableUrl _eyeBundleUrl;
        private EyesObject _parsedEyes;

        private DAZCharacter _character;
        private DAZCharacterSelector _selector;
        private SkinHandler _skinHandler;
        private Transform _headTransform;
        private Transform _lLookReference;
        private Transform _rLookReference;


        private bool _dirty = true;
        private int _tryAgainAttempts;
        private int _tryAgainLimit = 90 * 20;

        private UIDynamicToggle _validToggleVis;
        private UIDynamicTextField _eyeBundleInfo;

        private Vector3 _posLastFrame = Vector3.zero;
        private Quaternion _rotLastFrame = Quaternion.identity;
        private Vector3 _lossyScaleLastFrame = Vector3.one;

        private readonly string HEAD_ADDR = "rescale2/PhysicsModel/Genesis2Female/hip/abdomen/abdomen2/chest/neck/head";
        private readonly Color BG_DISABLED = new Color(0.7f, 0.7f, 0.7f, 1);
        private readonly Color BG_VALID = new Color(0.4f, 0.7f, 0.4f, 1);
        private readonly Color BG_INVALID = new Color(0.7f, 0.4f, 0.4f, 1);

// ----------------------------------------------------------------------------------
        public override void Init() {
            try {
                if (containingAtom?.type != "Person") {
                    SuperController.LogError($"EyeScroller: Please make sure this plugin is applied on a \"Person\" atom (current: {containingAtom.type})");
                    DestroyImmediate(this);
                    return;
                }

                GetPersonObjects();
                InitCustomUI();
            }
            catch (Exception e) {
                SuperController.LogError("EyeScroller: Failed to initialize. " + e);
                _isValidSetup = false;
            }
        }

// ----------------------------------------------------------------------------------
    private void InitCustomUI() {
        _activeToggle = new JSONStorableBool("Active", false, OnSetActiveDel);
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
        _eyeBundleUrl = new JSONStorableUrl("Eye AssetBundle", string.Empty, GetEyeAssetPath);

        RegisterBool(_activeToggle);
        RegisterBool(_eyeMirror);
        RegisterBool(_delayOneFrame);
        RegisterFloat(_uPerRotation);
        RegisterFloat(_vPerRotation);
        RegisterFloat(_uIrisOffset);
        RegisterFloat(_vIrisOffset);
        RegisterFloat(_uniformTexScale);
        RegisterUrl(_eyeBundleUrl);

        // These get read very frequently, unwrap them from JSON to avoid passing around objects
        _uvsPerRotation = new Vector2(_uPerRotation.val, _vPerRotation.val);
        _uvIrisOffset = new Vector2(_uIrisOffset.val, _vIrisOffset.val);
        _uniformTexScaleF = _uniformTexScale.val;

        CreateToggle(_activeToggle);

        _validToggleVis = CreateToggle(new JSONStorableBool("Valid setup", false), true);
        _validToggleVis.toggle.interactable = false;

        CreateSpacer(true);

        UIDynamicButton eyeLoader = CreateButton("Select Eye AssetBundle");
        eyeLoader.button.onClick.AddListener(() => {
            // _eyeBundleUrl.FileBrowse();
            // TODO: replace this with package-navigable version
            SuperController.singleton.NormalizeMediaPath(_lastBrowseDir);
            SuperController.singleton.GetMediaPathDialog(GetEyeAssetPath, _fileExt);
        });
        _eyeBundleInfo = CreateTextField(new JSONStorableString("Selected AssetBundle", ""));
        _eyeBundleInfo.height = 10f;

        CreateSpacer();
        CreateToggle(_eyeMirror);
        CreateSlider(_uPerRotation);
        CreateSlider(_vPerRotation);
        CreateSpacer();
        CreateSlider(_uIrisOffset);
        CreateSlider(_vIrisOffset);
        CreateSlider(_zBoneOffset, true).valueFormat = "F4";
        CreateSlider(_uniformTexScale, true);

        // Debug buttons
        CreateSpacer(true);
        UIDynamicButton printBundle = CreateButton("Print Bundle URL", true);
        printBundle.button.onClick.AddListener(() => {
            SuperController.LogMessage(_eyeBundleUrl.val);
        });

        UIDynamicButton printBoneStructure = CreateButton("Print delay", true);
        printBoneStructure.button.onClick.AddListener(() => {
            SuperController.LogMessage(_character.skin.delayDisplayOneFrame.ToString());
        });
}

// ----------------------------------------------------------------------------------
        private void GetPersonObjects() {
            _selector = containingAtom.GetComponentInChildren<DAZCharacterSelector>();
            _character = _selector.selectedCharacter;
            _skinHandler = new SkinHandler();
            _skinHandler.Configure(_character.skin);
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

            // _headTransform = Utils.GetChildByHierarchy(containingAtom.transform, HEAD_ADDR);
            // SuperController.LogMessage(Utils.TransformParentsToString(_headTransform));
        }

// ----------------------------------------------------------------------------------
        private void ValidateSetup() {
            try {
                _isValidSetup = true;

                if (_parsedEyes?.instance != null) {
                    GameObject.Destroy(_parsedEyes.instance);
                }

                if (string.IsNullOrEmpty(_eyeBundleUrl.val)) {
                    _isValidSetup = false;
                    return;
                }

                // Display the currently selected file
                LoadEyeAsset();
                _eyeBundleInfo.text = _eyeBundleUrl.val.Substring(_eyeBundleUrl.val.LastIndexOfAny(new char[] { '/', '\\' }) + 1);
            }
            catch (Exception e) {
                SuperController.LogError(e.Message);
                _isValidSetup = false;
            }
        }

// ----------------------------------------------------------------------------------
        public void GetEyeAssetPath(string path) {
            if (string.IsNullOrEmpty(path)) {
                return;
            }
            _lastBrowseDir = path.Substring(0, path.LastIndexOfAny(new char[] { '/', '\\' })) + @"\";
            _eyeBundleUrl.val = path;
            ValidateSetup();
        }

// ----------------------------------------------------------------------------------
        private void LoadEyeAsset() {
            try {
                string fullPath = _eyeBundleUrl.val;
                SuperController.LogMessage(fullPath);
                Request request = new AssetLoader.AssetBundleFromFileRequest
                    { path = fullPath, callback = OnEyeBundleLoaded };
                AssetLoader.QueueLoadAssetBundleFromFile(request);
            }
            catch (Exception e) {
                SuperController.LogError(e.Message);
                _isValidSetup = false;
            }
        }
// ----------------------------------------------------------------------------------
        private void OnEyeBundleLoaded(Request request) {
            try {
                string[] assetPaths = request.assetBundle.GetAllAssetNames();

                string firstPrefabPath = assetPaths.FirstOrDefault(s => s.EndsWith(".prefab"));

                if (firstPrefabPath == null) {
                    Utils.PrintErrorUsage("No prefab was found in the specified AssetBundle.");
                    _isValidSetup = false;
                    return;
                }

                _parsedEyes = new EyesObject(request, firstPrefabPath);
                if (!_parsedEyes.isValidRig) {
                    _isValidSetup = false;
                    return;
                }

                OnSetActive(_activeToggle.val);
            }
            catch (Exception e) {
                SuperController.LogError(e.Message);
                _isValidSetup = false;
            }
        }

// ----------------------------------------------------------------------------------
        private void OnDisable() {
            OnSetActive(false);
        }

        private void OnEnable() {
            OnSetActive(_activeToggle.val);
        }

// ----------------------------------------------------------------------------------
        private void OnSetActiveDel(JSONStorableBool state) {
            OnSetActive(state.val);
        }

        private void OnSetActive(bool state) {
            state = state && _isValidSetup;

            // Enable/disable custom eyes
            _parsedEyes?.instance?.SetActive(state);

            // Disable/enable original eyes
            if (state) {
                _skinHandler.BeforeRender();
            }
            else {
                _skinHandler.AfterRender();
            }
        }

// ----------------------------------------------------------------------------------
        private void LateUpdate() {
            try {
                _validToggleVis.toggle.isOn = _isValidSetup;
                if (!_activeToggle.val) {
                    _validToggleVis.backgroundColor = BG_DISABLED;
                    return;
                }
                if (!_isValidSetup) {
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
            catch (Exception e){
                SuperController.LogError("EyeScroller: " + e);
                SuperController.LogMessage($"Objects exist? {_validToggleVis != null} {_parsedEyes != null} {_headTransform != null} {_zBoneOffset != null}");
                _isValidSetup = false;
            }
        }



// ----------------------------------------------------------------------------------

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

// ----------------------------------------------------------------------------------
        private void MakeDirty(string reason)
        {
            _dirty = true;
            _tryAgainAttempts++;
            if (_tryAgainAttempts > _tryAgainLimit) // Approximately 20 to 40 seconds
            {
                SuperController.LogError("Failed to apply ImprovedPoV. Reason: " + reason + ". Try reloading the plugin, or report the issue to @Acidbubbles.");
                enabled = false;
            }
        }

// ----------------------------------------------------------------------------------
        private void OnDestroy() {
            if (_parsedEyes != null) {
                DestroyImmediate(_parsedEyes.instance);
            }
        }
    }


    // ################################################################
    // ### Eyes Manager ###############################################
    // ################################################################

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

// ----------------------------------------------------------------------------------
        public EyesObject(Request request, string path) {
            root = request.assetBundle.LoadAsset<GameObject>(path).transform;
            if (!PopulateData(root)) {
                Utils.PrintErrorUsage($"Error deconstructing the eyes prefab before instantiating.\n" +
                                               $"[eye.l found: {lMesh != null}]\n[eye.r found: {rMesh != null}]\n" +
                                               $"[both meshes readable: {lMesh?.isReadable} {rMesh?.isReadable}]\n" +
                                               $"Found object hierarchy: \n\n{Utils.ObjectHierarchyToString(root)}");
                return;
            }
            // TODO: set parent bone here
            instance = GameObject.Instantiate(root.gameObject);
            if (!PopulateData(instance.transform)) {
                Utils.PrintErrorUsage($"Error deconstructing the eyes prefab after instantiating.\n" +
                                               $"[eye.l found: {lMesh != null}]\n[eye.r found: {rMesh != null}]\n" +
                                               $"[both meshes readable: {lMesh?.isReadable} {rMesh?.isReadable}]\n" +
                                               $"Found object hierarchy: \n\n{Utils.ObjectHierarchyToString(root)}");
                GameObject.DestroyImmediate(instance);
                return;
            }

            lOriginalUVs = (Vector2[]) lMesh.uv.Clone();
            rOriginalUVs = (Vector2[]) rMesh.uv.Clone();
            lCurrentUVs = lMesh.uv;
            rCurrentUVs = rMesh.uv;

        }

// ----------------------------------------------------------------------------------
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

        ~EyesObject(){
            if (instance != null) {
                GameObject.DestroyImmediate(instance);
            }
        }
    }


    // ##############################################################################
    // ###  The following code has been repurposed from Acidbubbles' ImprovedPoV  ###
    // ###  This is used for hiding the original eye materials ######################
    // ##############################################################################
#region Acidbubbles
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

                    // These are throwing errors
                    { "Custom/Discard", null },
                    { "Custom/Subsurface/TransparentSeparateAlphaComputeBuff", null },
                    { "Custom/Subsurface/TransparentGlossNMSeparateAlphaComputeBuff", null },
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
    #endregion

    // ################################################################
    // ### Utils ######################################################
    // ################################################################
    #region utils
    public class Utils {

// ----------------------------------------------------------------------------------
        public static string ObjectHierarchyToString(Transform root) {
            StringBuilder builder = new StringBuilder();
            ObjectHierarchyToString(root, builder);
            return builder.ToString();
        }

// ----------------------------------------------------------------------------------
        private static void ObjectHierarchyToString(Transform root, StringBuilder builder, int currentDepth = 0) {

            for (int i = 0; i < currentDepth; i++) {
                builder.Append("|\t");
            }

            builder.Append(root.name + "\n");

            foreach (Transform child in root) {
                ObjectHierarchyToString(child, builder, currentDepth+1);
            }
        }


// ----------------------------------------------------------------------------------
        public static void PrintErrorUsage(string prefixLine = "") {
            string errorString = "EyeScroller: " + prefixLine + "\n" +
                                 @"Please make sure your AssetBundle contains a prefab with following elements:
<eyes>.prefab
|   <eye>.l
|   <eye>.r";

            SuperController.LogError(errorString);
        }

// ----------------------------------------------------------------------------------

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

// ----------------------------------------------------------------------------------
        public static String TransformParentsToString(Transform child) {
            string outStr = "/" + child.name;
            while (child.parent != null) {
                child = child.parent;
                outStr = "/" + child.name + outStr;
            }

            return outStr;
        }
    }
    #endregion
}
