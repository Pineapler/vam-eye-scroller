using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Pineapler.EyeScroller {
    public class VamEyeScrollerNew : MVRScript {

        // TODO: Eye model loading
        // TODO: Store and load eye model file path

        // JSON Storables
        public JSONStorableBool[] hideFaceMaterials { get; } = new[] {
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

        public JSONStorableBool activeToggle;


        private Atom _person;
        private DAZCharacterSelector _selector;
        private DAZCharacter _character;
        private SkinHandler _skinHandler;

        private bool _eyesBad;
        private bool _dirty = true;
        private int _tryAgainAttempts;
        private bool _failedOnce;


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

        private void OnEnable() {
            RegisterHandlers();
        }

        private void OnDisable() {
            ClearHandlers();
        }

        // private void OnDestroy() {
        //     // TODO: Destroy eye mesh
        // }

        // ###########################################################
        // # JSON, Reference and UI Setup ############################
        // ###########################################################

        #region JSON, Reference and UI Setup

        public void InitCustomUI() {
            activeToggle = new JSONStorableBool("Active", false, (bool _) => RefreshHandlers());
            RegisterBool(activeToggle);
            CreateToggle(activeToggle);

            CreateSpacer();

            foreach (var hideFaceMaterial in hideFaceMaterials) {
                hideFaceMaterial.setCallbackFunction = _ => RefreshHandlers();
                // RegisterBool(hideFaceMaterial);
                // CreateToggle(hideFaceMaterial);
            }



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


            if (activeToggle.val && !_eyesBad) {
                if (!RegisterHandler(new SkinHandler(_character.skin,hideFaceMaterials.Where(x => x.val).Select(x => x.name), hideFaceMaterials.Length))) {
                    MakeDirty("Handler", "is not configured");
                    return;
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
            if (!enabled) return;
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
}
