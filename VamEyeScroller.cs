

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Battlehub.RTCommon;
using Request = MeshVR.AssetLoader.AssetBundleFromFileRequest;
using MeshVR;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Pineapler.EyeScroller {
/// <summary>
/// VamEyeScroller Version 0.1.0
/// By Pineapler
/// Stylized eyes that don't give you nightmares
/// Source: https://github.com/pineaplers/vam-eye-scroller
/// </summary>
    public class VamEyeScroller : MVRScript {

        // Format asset bundles as:
        // ## IMPORTANT
        //  eyes.prefab
        //  |   eye.l
        //  |   eye.r

        private static readonly string _fileExt = "assetbundle";
        private string _lastBrowseDir = @"Custom\Assets\";
        private bool _isValidSetup = true;

        private JSONStorableBool _activeToggle;

        private JSONStorableBool _eyeMirror;
        private JSONStorableFloat _uPerRotation;
        private JSONStorableFloat _vPerRotation;
        private JSONStorableFloat _uniformTexScale;

        private Vector2 _uvsPerRotation;
        private float _uniformTexScaleF;

        private JSONStorableUrl _eyePresetUrl;
        private EyesObject _parsedEyes;

        private GameObject _headObj;
        private Transform _lLookReference;
        private Transform _rLookReference;
        private DAZCharacter _character;
        private DAZCharacterSelector _selector;
        private SkinHandler _skinHandler;

        private UIDynamicToggle _validToggleVis;
        private UIDynamicTextField _eyeBundleInfo;

        private Color _bgDisabled = new Color(0.7f, 0.7f, 0.7f, 1);
        private Color _bgValid = new Color(0.4f, 0.7f, 0.4f, 1);
        private Color _bgInvalid = new Color(0.7f, 0.4f, 0.4f, 1);

// ----------------------------------------------------------------------------------
        public override void Init() {
            try {
                if (containingAtom?.type != "Person") {
                    SuperController.LogError($"EyeScroller: Please make sure this plugin is applied on a \"Person\" atom (current: {containingAtom.type})");
                    DestroyImmediate(this);
                    return;
                }

                InitCustomUI();

                ValidateSetup();

            }
            catch (Exception e) {
                SuperController.LogError("EyeScroller: Failed to initialize. " + e);
                _isValidSetup = false;
            }
        }

// ----------------------------------------------------------------------------------
        private void InitCustomUI() {
                _activeToggle = new JSONStorableBool("Active", false, OnSetActive);
                _eyeMirror = new JSONStorableBool("Mirror one eye", true);
                _uPerRotation = new JSONStorableFloat("U values per revolution", 1f, val => _uvsPerRotation = new Vector2(val, _vPerRotation.val), -2f, 2f, false);
                _vPerRotation = new JSONStorableFloat("V values per revolution", 1f, val => _uvsPerRotation = new Vector2(_uPerRotation.val, val),-2f, 2f, false);
                _uniformTexScale = new JSONStorableFloat("Texture scale", 1f, val => _uniformTexScaleF = val, 0.0001f, 10f);
                _eyePresetUrl = new JSONStorableUrl("Eye Atom Preset", string.Empty);

                RegisterBool(_activeToggle);
                RegisterBool(_eyeMirror);
                RegisterFloat(_uPerRotation);
                RegisterFloat(_vPerRotation);
                RegisterFloat(_uniformTexScale);
                RegisterUrl(_eyePresetUrl);

                // These get read very frequently, unwrap them from JSON to avoid passing around objects
                _uvsPerRotation = new Vector2(_uPerRotation.val, _vPerRotation.val);
                _uniformTexScaleF = _uniformTexScale.val;

                // You can use Create* methods to add a control in the plugin's custom UI
                CreateToggle(_activeToggle);
                _validToggleVis = CreateToggle(new JSONStorableBool("Valid setup", false), true);
                _validToggleVis.toggle.interactable = false;

                CreateSpacer();
                UIDynamicButton eyeLoader = CreateButton("Select Eye AssetBundle");
                eyeLoader.button.onClick.AddListener(() => {
                    SuperController.singleton.NormalizeMediaPath(_lastBrowseDir);
                    SuperController.singleton.GetMediaPathDialog(GetEyeAssetPath, _fileExt);
                });
                _eyeBundleInfo = CreateTextField(new JSONStorableString("Selected AssetBundle", ""));
                _eyeBundleInfo.height = 10f;

                CreateSpacer();
                CreateToggle(_eyeMirror);
                CreateSlider(_uPerRotation);
                CreateSlider(_vPerRotation);
                CreateSlider(_uniformTexScale);

        }

// ----------------------------------------------------------------------------------
        private void ValidateSetup() {
            try {
                _isValidSetup = true;

                if (_parsedEyes?.instance != null) {
                    GameObject.Destroy(_parsedEyes.instance);
                }

                if (string.IsNullOrEmpty(_eyePresetUrl.val)) {
                    _isValidSetup = false;
                    return;
                }

                // Display the currently selected file
                _eyeBundleInfo.text = _eyePresetUrl.val.Substring(_eyePresetUrl.val.LastIndexOfAny(new char[] { '/', '\\' }) + 1);
                LoadEyeAsset();
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
            _eyePresetUrl.val = path;
            ValidateSetup();
        }

// ----------------------------------------------------------------------------------
        private void LoadEyeAsset() {
            try {
                Request request = new AssetLoader.AssetBundleFromFileRequest
                    { path = _eyePresetUrl.val, callback = OnEyeBundleLoaded };
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

                OnSetActive(_activeToggle);
            }
            catch (Exception e) {
                SuperController.LogError(e.Message);
                _isValidSetup = false;
            }
        }

// ----------------------------------------------------------------------------------
        private void OnSetActive(JSONStorableBool state) {
            _parsedEyes?.instance?.SetActive(state.val);
            // TODO: Disable real eye materials from here
        }

// ----------------------------------------------------------------------------------
        private void LateUpdate() {
            try {
                _validToggleVis.toggle.isOn = _isValidSetup;
                if (!_activeToggle.val) {
                    _validToggleVis.backgroundColor = _bgDisabled;
                    return;
                }
                if (!_isValidSetup) {
                    _validToggleVis.backgroundColor = _bgInvalid;
                    return;
                }
                _validToggleVis.backgroundColor = _bgValid;

                ScrollUVs();
            }
            catch (Exception e){
                SuperController.LogError("EyeScroller: " + e);
                _isValidSetup = false;
            }
        }



// ----------------------------------------------------------------------------------

        private readonly Vector2 POINT_FIVE = new Vector2(0.5f, 0.5f);

        // TODO: Get head bone
        // TODO: Get eye bones
        // TODO: IMPORTANT: Replace null transforms in ScrollUV
        private void ScrollUVs() {
            ScrollUV(_parsedEyes.lMesh, null, _parsedEyes.lOriginalUVs, _parsedEyes.lCurrentUVs, false);
            ScrollUV(_parsedEyes.rMesh, null, _parsedEyes.rOriginalUVs, _parsedEyes.rCurrentUVs, _eyeMirror.val);
        }

        private void ScrollUV(Mesh mesh, Transform referenceRot, Vector2[] originalUVs, Vector2[] currentUVs, bool mirror) {

            // Vector3 refObjRotation = referenceRot.localRotation.eulerAngles;
            Vector3 refObjRotation = Vector3.zero;
            // Get angles in range [-180, 180]
            float horizontalRot = Mathf.Repeat(refObjRotation.y + 180f, 360f) - 180f;
            float verticalRot = Mathf.Repeat(refObjRotation.x + 180f, 360f) - 180f;

            // Scale rotation-to-UV offset
            Vector2 uvOffset = new Vector2(horizontalRot, verticalRot) / 180f * _uvsPerRotation;

            for (int i = 0; i < currentUVs.Length; i++) {
                Vector2 uv = originalUVs[i];
                uv += uvOffset;
                uv -= POINT_FIVE;
                uv /= _uniformTexScaleF;
                uv += POINT_FIVE;
                currentUVs[i] = uv;
            }

            mesh.uv = currentUVs;
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

    }
    #endregion
}
