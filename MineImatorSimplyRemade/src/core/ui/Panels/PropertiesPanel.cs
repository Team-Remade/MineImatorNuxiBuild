using System.Numerics;
using GlmSharp;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemadeNuxi.core;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

namespace MineImatorSimplyRemade.core.ui.Panels;

public class PropertiesPanel : UiPanel
{
    // ── Project tab state ─────────────────────────────────────────────────────

    public Node Floor;

    public readonly float[] BackgroundColor = [0.5764706f, 0.5764706f, 1f, 1f];

    public bool UseSky;
    public bool UseAdvancedSky;

    public int GetResolutionWidth()  => 0;
    public int GetResolutionHeight() => 0;
    public int GetFramerate()        => 0;

    public int    TextureAnimationFps  = 0;
    public string BackgroundImagePath  = "No image selected";
    public bool   StretchBackground    = true;

    // ── Object tab state ──────────────────────────────────────────────────────

    private SceneObject _currentObject;

    // Scale link toggle
    private bool _linkScale = true;

    // ── Public wiring ─────────────────────────────────────────────────────────

    /// <summary>
    /// Subscribe to SelectionManager events.  Call once from App.Initialize()
    /// after SelectionManager.Initialize() has been called.
    /// </summary>
    public void Initialize()
    {
        if (SelectionManager.Instance != null)
            SelectionManager.Instance.SelectionChanged += OnSelectionChanged;
    }
    
    // ── Selection callback ────────────────────────────────────────────────────

    private void OnSelectionChanged()
    {
        var sel = SelectionManager.Instance?.SelectedObjects;
        _currentObject = (sel != null && sel.Count > 0) ? sel[0] : null;
    }
    
    public override void Render()
    {
        if (ImGui.Begin("Properties"))
        {
            if (ImGui.BeginTabBar("PropertiesTabs"))
            {
                RenderProjectTab();
                RenderObjectTab();
                ImGui.EndTabBar();
            }
        }
        ImGui.End();
    }
    
    private void RenderProjectTab()
    {
        if (!ImGui.BeginTabItem("Project")) return;

        ImGui.Text("Project Properties");
        ImGui.Separator();

        if (ImGui.CollapsingHeader("Project Settings", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Text("Project Name:");
            ImGui.SetNextItemWidth(-1);
            string projectName = "Untitled Project";
            ImGui.InputText("##ProjectName", ref projectName, 256);

            ImGui.Spacing();
            ImGui.Text("Resolution:");
            int resWidth = 1920;
            int resHeight = 1080;
            ImGui.SetNextItemWidth(80);
            ImGui.InputInt("##ResWidth", ref resWidth, 0, 0, ImGuiInputTextFlags.None);
            ImGui.SameLine();
            ImGui.Text(" x ");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(80);
            ImGui.InputInt("##ResHeight", ref resHeight, 0, 0, ImGuiInputTextFlags.None);
            ImGui.Text("Presets:");
            if (ImGui.Button("720p"))  { resWidth = 1280; resHeight = 720; }
            ImGui.SameLine();
            if (ImGui.Button("1080p")) { resWidth = 1920; resHeight = 1080; }
            ImGui.SameLine();
            if (ImGui.Button("1440p")) { resWidth = 2560; resHeight = 1440; }
            ImGui.SameLine();
            if (ImGui.Button("4K"))    { resWidth = 3840; resHeight = 2160; }

            ImGui.Spacing();
            ImGui.Text("Framerate:");
            int framerate = 30;
            ImGui.SetNextItemWidth(80);
            ImGui.InputInt("##Framerate", ref framerate, 0, 0, ImGuiInputTextFlags.None);
            ImGui.SameLine();
            ImGui.Text(" fps");
            ImGui.Text("Presets:");
            if (ImGui.Button("24"))  { framerate = 24; }
            ImGui.SameLine();
            if (ImGui.Button("30"))  { framerate = 30; }
            ImGui.SameLine();
            if (ImGui.Button("60"))  { framerate = 60; }
            ImGui.SameLine();
            if (ImGui.Button("120")) { framerate = 120; }

            ImGui.Spacing();
            ImGui.Text("Texture Animation Speed:");
            int texAnimSpeed = 20;
            ImGui.SetNextItemWidth(80);
            ImGui.InputInt("##TexAnimSpeed", ref texAnimSpeed, 0, 0, ImGuiInputTextFlags.None);
            ImGui.SameLine();
            ImGui.Text(" fps");
            ImGui.Text("Presets:");
            if (ImGui.Button("10##tex")) { texAnimSpeed = 10; }
            ImGui.SameLine();
            if (ImGui.Button("20##tex")) { texAnimSpeed = 20; }
            ImGui.SameLine();
            if (ImGui.Button("30##tex")) { texAnimSpeed = 30; }
            ImGui.SameLine();
            if (ImGui.Button("60##tex")) { texAnimSpeed = 60; }
        }

        if (ImGui.CollapsingHeader("Background Settings"))
        {
            unsafe
            {
                ImGui.Text("Background Color:");
                ImGui.SetNextItemWidth(-1);
                fixed (byte* label = "##BackgroundColor"u8)
                fixed (float* bgColorPtr = BackgroundColor)
                {
                    ImGui.ColorEdit4(label, bgColorPtr, ImGuiColorEditFlags.None);
                }

                ImGui.Spacing();
                ImGui.Text("Presets:");
                var presets = new (string name, float r, float g, float b, float a)[]
                {
                    ("Dawn",    1f,          0.7f,        0.5f,  1f),
                    ("Morning", 0.6f,        0.8f,        1f,    1f),
                    ("Day",     0.5764706f,  0.5764706f,  1f,    1f),
                    ("Sunset",  1f,          0.5f,        0.3f,  1f),
                    ("Dusk",    0.3f,        0.4f,        0.7f,  1f),
                    ("Night",   0.05f,       0.05f,       0.15f, 1f)
                };
                for (int i = 0; i < presets.Length; i++)
                {
                    if (i > 0) ImGui.SameLine();
                    if (ImGui.Button(presets[i].name))
                    {
                        BackgroundColor[0] = presets[i].r;
                        BackgroundColor[1] = presets[i].g;
                        BackgroundColor[2] = presets[i].b;
                        BackgroundColor[3] = presets[i].a;
                    }
                }
                ImGui.Spacing();
                // Background Image (display only; file picking not yet implemented)
                {
                    ImGui.Text("Background Image:");
                    ImGui.SameLine();
                    string backgroundImg = Path.GetFileName(BackgroundImagePath);
                    ImGui.Text(backgroundImg);
                    // TODO: open NativeFileDialog to pick a background texture file
                    if (ImGui.Button("Browse##backgroundBrowse"))
                    {
                        // TODO: implement background image file picker
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Clear##backgroundClear") && BackgroundImagePath != "No image selected")
                    {
                        BackgroundImagePath = "No image selected";
                    }
                }
            }
        }

        ImGui.EndTabItem();
    }
    
    private void RenderObjectTab()
    {
        if (!ImGui.BeginTabItem("Object")) return;

        // ── Object header ─────────────────────────────────────────────────────
        if (_currentObject == null)
        {
            float windowWidth = ImGui.GetWindowWidth();
            string noSelText = "No object selected";
            float textWidth  = ImGui.CalcTextSize(noSelText).X;
            ImGui.SetCursorPosX((windowWidth - textWidth) * 0.5f);
            ImGui.TextDisabled(noSelText);
            ImGui.EndTabItem();
            return;
        }

        // Centered bold name label
        {
            float windowWidth = ImGui.GetWindowWidth();
            string displayName = _currentObject.GetDisplayName();
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 1f, 0.6f, 1f));
            float textWidth = ImGui.CalcTextSize(displayName).X;
            ImGui.SetCursorPosX((windowWidth - textWidth) * 0.5f);
            ImGui.Text(displayName);
            ImGui.PopStyleColor();
        }

        ImGui.Separator();

        // ── Visibility & shadows ──────────────────────────────────────────────
        {
            bool vis = _currentObject.ObjectVisible;
            if (ImGui.Checkbox("Visible", ref vis))
                _currentObject.SetObjectVisible(vis);

            bool inheritVis = _currentObject.InheritVisibility;
            if (ImGui.Checkbox("Inherit Visibility", ref inheritVis))
                _currentObject.InheritVisibility = inheritVis;

            bool castShadow = _currentObject.CastShadow;
            if (ImGui.Checkbox("Cast Shadows", ref castShadow))
                _currentObject.CastShadow = castShadow;
        }

        ImGui.Spacing();

        // ── Position ──────────────────────────────────────────────────────────
        if (ImGui.CollapsingHeader("Position"))
        {
            bool inheritPos = _currentObject.InheritPosition;
            if (ImGui.Checkbox("Inherit Position##pos", ref inheritPos))
                _currentObject.InheritPosition = inheritPos;

            vec3 rawPos = (_currentObject is BoneSceneObject bonePos)
                ? bonePos.TargetPosition
                : _currentObject.LocalPosition;

            float posX = rawPos.x * 16f;
            float posY = rawPos.y * 16f;
            float posZ = rawPos.z * 16f;

            ImGui.PushItemWidth(-ImGui.CalcTextSize("Z").X - ImGui.GetStyle().ItemInnerSpacing.X * 2);
            if (ImGui.DragFloat("X##posX", ref posX, 0.1f))
            {
                rawPos.x = posX / 16f;
                rawPos.y = posY / 16f;
                rawPos.z = posZ / 16f;
                ApplyPosition(rawPos);
            }
            if (ImGui.DragFloat("Y##posY", ref posY, 0.1f))
            {
                rawPos.x = posX / 16f;
                rawPos.y = posY / 16f;
                rawPos.z = posZ / 16f;
                ApplyPosition(rawPos);
            }
            if (ImGui.DragFloat("Z##posZ", ref posZ, 0.1f))
            {
                rawPos.x = posX / 16f;
                rawPos.y = posY / 16f;
                rawPos.z = posZ / 16f;
                ApplyPosition(rawPos);
            }
            ImGui.PopItemWidth();

            if (ImGui.Button("Reset##posReset"))
                ApplyPosition(vec3.Zero);
        }

        // ── Rotation ──────────────────────────────────────────────────────────
        if (ImGui.CollapsingHeader("Rotation (degrees)"))
        {
            bool inheritRot = _currentObject.InheritRotation;
            if (ImGui.Checkbox("Inherit Rotation##rot", ref inheritRot))
                _currentObject.InheritRotation = inheritRot;

            vec3 rawRot = (_currentObject is BoneSceneObject boneRot)
                ? boneRot.TargetRotation
                : _currentObject.LocalRotation;

            float rotX = rawRot.x * (180f / MathF.PI);
            float rotY = rawRot.y * (180f / MathF.PI);
            float rotZ = rawRot.z * (180f / MathF.PI);

            ImGui.PushItemWidth(-ImGui.CalcTextSize("Z").X - ImGui.GetStyle().ItemInnerSpacing.X * 2);
            if (ImGui.DragFloat("X##rotX", ref rotX, 0.5f))
            {
                rawRot = new vec3(rotX * (MathF.PI / 180f), rotY * (MathF.PI / 180f), rotZ * (MathF.PI / 180f));
                ApplyRotation(rawRot);
            }
            if (ImGui.DragFloat("Y##rotY", ref rotY, 0.5f))
            {
                rawRot = new vec3(rotX * (MathF.PI / 180f), rotY * (MathF.PI / 180f), rotZ * (MathF.PI / 180f));
                ApplyRotation(rawRot);
            }
            if (ImGui.DragFloat("Z##rotZ", ref rotZ, 0.5f))
            {
                rawRot = new vec3(rotX * (MathF.PI / 180f), rotY * (MathF.PI / 180f), rotZ * (MathF.PI / 180f));
                ApplyRotation(rawRot);
            }
            ImGui.PopItemWidth();

            if (ImGui.Button("Reset##rotReset"))
                ApplyRotation(vec3.Zero);
        }

        // ── Scale ─────────────────────────────────────────────────────────────
        if (ImGui.CollapsingHeader("Scale"))
        {
            bool inheritScale = _currentObject.InheritScale;
            if (ImGui.Checkbox("Inherit Scale##scale", ref inheritScale))
                _currentObject.InheritScale = inheritScale;

            ImGui.Checkbox("Link Scale", ref _linkScale);

            vec3 curScale = _currentObject.LocalScale;
            float scaleX = curScale.x;
            float scaleY = curScale.y;
            float scaleZ = curScale.z;

            ImGui.PushItemWidth(-ImGui.CalcTextSize("Z").X - ImGui.GetStyle().ItemInnerSpacing.X * 2);
            if (ImGui.DragFloat("X##scaleX", ref scaleX, 0.01f, 0.001f, float.MaxValue))
            {
                scaleX = MathF.Max(scaleX, 0.001f);
                if (_linkScale)
                {
                    float delta = scaleX - curScale.x;
                    scaleY = MathF.Max(curScale.y + delta, 0.001f);
                    scaleZ = MathF.Max(curScale.z + delta, 0.001f);
                }
                _currentObject.SetLocalScale(new vec3(scaleX, scaleY, scaleZ));
            }
            if (ImGui.DragFloat("Y##scaleY", ref scaleY, 0.01f, 0.001f, float.MaxValue))
            {
                scaleY = MathF.Max(scaleY, 0.001f);
                if (_linkScale)
                {
                    float delta = scaleY - curScale.y;
                    scaleX = MathF.Max(curScale.x + delta, 0.001f);
                    scaleZ = MathF.Max(curScale.z + delta, 0.001f);
                }
                _currentObject.SetLocalScale(new vec3(scaleX, scaleY, scaleZ));
            }
            if (ImGui.DragFloat("Z##scaleZ", ref scaleZ, 0.01f, 0.001f, float.MaxValue))
            {
                scaleZ = MathF.Max(scaleZ, 0.001f);
                if (_linkScale)
                {
                    float delta = scaleZ - curScale.z;
                    scaleX = MathF.Max(curScale.x + delta, 0.001f);
                    scaleY = MathF.Max(curScale.y + delta, 0.001f);
                }
                _currentObject.SetLocalScale(new vec3(scaleX, scaleY, scaleZ));
            }
            ImGui.PopItemWidth();

            if (ImGui.Button("Reset##scaleReset"))
                _currentObject.SetLocalScale(vec3.Ones);
        }

        // ── Pivot Offset ──────────────────────────────────────────────────────
        if (ImGui.CollapsingHeader("Pivot Offset"))
        {
            bool inheritPivot = _currentObject.InheritPivotOffset;
            if (ImGui.Checkbox("Inherit Pivot##pivot", ref inheritPivot))
                _currentObject.InheritPivotOffset = inheritPivot;

            vec3 pivot = _currentObject.PivotOffset;
            float pivX = pivot.x * 16f;
            float pivY = pivot.y * 16f;
            float pivZ = pivot.z * 16f;

            ImGui.PushItemWidth(-ImGui.CalcTextSize("Z").X - ImGui.GetStyle().ItemInnerSpacing.X * 2);
            if (ImGui.DragFloat("X##pivX", ref pivX, 0.1f))
                _currentObject.PivotOffset = new vec3(pivX / 16f, pivY / 16f, pivZ / 16f);

            if (ImGui.DragFloat("Y##pivY", ref pivY, 0.1f))
                _currentObject.PivotOffset = new vec3(pivX / 16f, pivY / 16f, pivZ / 16f);

            if (ImGui.DragFloat("Z##pivZ", ref pivZ, 0.1f))
                _currentObject.PivotOffset = new vec3(pivX / 16f, pivY / 16f, pivZ / 16f);
            ImGui.PopItemWidth();

            if (ImGui.Button("Reset##pivReset"))
                _currentObject.PivotOffset = vec3.Zero;
        }

        // ── Material ──────────────────────────────────────────────────────────
        bool isLight = _currentObject is LightSceneObject;
        if (isLight) ImGui.BeginDisabled();
        if (ImGui.CollapsingHeader("Material"))
        {
            // Ensure MaterialSettings exists when we need to write
            EnsureMaterialSettings();
            var mat = _currentObject.MaterialSettings;

            // Alpha – skip for BoneSceneObject
            if (_currentObject is not BoneSceneObject)
            {
                float alpha = mat?.AlbedoColor.a ?? 1f;
                ImGui.SetNextItemWidth(-60f);
                if (ImGui.SliderFloat("Alpha", ref alpha, 0f, 1f))
                {
                    EnsureMaterialSettings();
                    var c = _currentObject.MaterialSettings.AlbedoColor;
                    _currentObject.MaterialSettings.AlbedoColor = new vec4(c.r, c.g, c.b, alpha);
                    _currentObject.SetExplicitMaterialSettings();
                    _currentObject.PropagateMaterialSettingsToChildren();
                }
                ImGui.SameLine();
                ImGui.Text(alpha.ToString("F2"));
            }

            // Albedo color
            {
                vec4 ac = mat?.AlbedoColor ?? new vec4(1f, 1f, 1f, 1f);
                var vec4 = new Vector4(ac.r, ac.g, ac.b, ac.a);
                if (ImGui.ColorEdit4("Albedo", ref vec4))
                {
                    EnsureMaterialSettings();
                    _currentObject.MaterialSettings.AlbedoColor = new vec4(vec4.X, vec4.Y, vec4.Z, vec4.W);
                    _currentObject.SetExplicitMaterialSettings();
                    _currentObject.PropagateMaterialSettingsToChildren();
                }
            }

            // Metallic
            {
                float metallic = mat?.Metallic ?? 0f;
                if (ImGui.SliderFloat("Metallic", ref metallic, 0f, 1f))
                {
                    EnsureMaterialSettings();
                    _currentObject.MaterialSettings.Metallic = metallic;
                    _currentObject.SetExplicitMaterialSettings();
                    _currentObject.PropagateMaterialSettingsToChildren();
                }
            }

            // Roughness
            {
                float roughness = mat?.Roughness ?? 0.5f;
                if (ImGui.SliderFloat("Roughness", ref roughness, 0f, 1f))
                {
                    EnsureMaterialSettings();
                    _currentObject.MaterialSettings.Roughness = roughness;
                    _currentObject.SetExplicitMaterialSettings();
                    _currentObject.PropagateMaterialSettingsToChildren();
                }
            }

            // Emission enabled
            {
                bool emissionEnabled = mat?.EmissionEnabled ?? false;
                if (ImGui.Checkbox("Emission", ref emissionEnabled))
                {
                    EnsureMaterialSettings();
                    _currentObject.MaterialSettings.EmissionEnabled = emissionEnabled;
                    _currentObject.SetExplicitMaterialSettings();
                    _currentObject.PropagateMaterialSettingsToChildren();
                }
            }

            // Emission color (no alpha)
            {
                vec4 ec = mat?.EmissionColor ?? new vec4(0f, 0f, 0f, 1f);
                var vec3 = new Vector3(ec.r, ec.g, ec.b);
                if (ImGui.ColorEdit3("Emission Color", ref vec3))
                {
                    EnsureMaterialSettings();
                    _currentObject.MaterialSettings.EmissionColor = new vec4(vec3.X, vec3.Y, vec3.Z, 1f);
                    _currentObject.SetExplicitMaterialSettings();
                    _currentObject.PropagateMaterialSettingsToChildren();
                }
            }

            // Emission energy
            {
                float emEnergy = mat?.EmissionEnergy ?? 1f;
                if (ImGui.SliderFloat("Emission Energy", ref emEnergy, 0f, 10f))
                {
                    EnsureMaterialSettings();
                    _currentObject.MaterialSettings.EmissionEnergy = emEnergy;
                    _currentObject.SetExplicitMaterialSettings();
                    _currentObject.PropagateMaterialSettingsToChildren();
                }
            }

            // Normal map (display only; file picking not yet implemented)
            {
                string normalName = (mat?.NormalTexture != 0) ? "(texture)" : "None";
                ImGui.Text("Normal: " + normalName);
                // TODO: open NativeFileDialog to pick a normal-map texture file
                if (ImGui.Button("Browse##normalBrowse"))
                {
                    // TODO: implement normal map file picker
                }
                ImGui.SameLine();
                if (ImGui.Button("Clear##normalClear") && mat != null)
                {
                    EnsureMaterialSettings();
                    _currentObject.MaterialSettings.NormalTexture = 0;
                    _currentObject.MaterialSettings.NormalEnabled  = false;
                    _currentObject.SetExplicitMaterialSettings();
                    _currentObject.PropagateMaterialSettingsToChildren();
                }
            }

            // Double Sided
            {
                bool doubleSided = mat?.DoubleSided ?? false;
                if (ImGui.Checkbox("Double Sided", ref doubleSided))
                {
                    EnsureMaterialSettings();
                    _currentObject.MaterialSettings.DoubleSided = doubleSided;
                    _currentObject.SetExplicitMaterialSettings();
                    _currentObject.PropagateMaterialSettingsToChildren();
                }
            }

            ImGui.Spacing();

            // Reset material
            if (ImGui.Button("Reset Material"))
            {
                EnsureMaterialSettings();
                var m = _currentObject.MaterialSettings;
                m.AlbedoColor     = new vec4(1f, 1f, 1f, 1f);
                m.Metallic        = 0f;
                m.Roughness       = 0.5f;
                m.EmissionEnabled = false;
                m.EmissionEnergy  = 1f;
                m.NormalTexture   = 0;
                m.NormalEnabled   = false;
                m.DoubleSided     = false;
                _currentObject.SetExplicitMaterialSettings();
                _currentObject.PropagateMaterialSettingsToChildren();
            }
        }
        if (isLight) ImGui.EndDisabled();

        // ── Light (shown only for LightSceneObject) ───────────────────────────
        if (_currentObject is LightSceneObject light)
        {
            if (ImGui.CollapsingHeader("Light"))
            {
                // Color
                {
                    var lc   = light.LightColor;
                    var vec3 = new Vector3(lc.r, lc.g, lc.b);
                    if (ImGui.ColorEdit3("Color##lightColor", ref vec3))
                        light.LightColor = new vec4(vec3.X, vec3.Y, vec3.Z, 1);
                }

                // Energy
                {
                    float energy = light.LightEnergy;
                    if (ImGui.DragFloat("Energy##lightEnergy", ref energy, 0.05f, 0f, 100f))
                        light.LightEnergy = energy;
                }

                // Range
                {
                    float range = light.LightRange;
                    if (ImGui.DragFloat("Range##lightRange", ref range, 0.1f, 0.01f, 500f))
                        light.LightRange = range;
                }

                // Indirect Energy
                {
                    float indirect = light.LightIndirectEnergy;
                    if (ImGui.DragFloat("Indirect Energy##lightIndirect", ref indirect, 0.05f, 0f, 16f))
                        light.LightIndirectEnergy = indirect;
                }

                // Specular
                {
                    float specular = light.LightSpecular;
                    if (ImGui.SliderFloat("Specular##lightSpecular", ref specular, 0f, 1f))
                        light.LightSpecular = specular;
                }

                // Cast Shadows
                {
                    bool shadow = light.LightShadowEnabled;
                    if (ImGui.Checkbox("Cast Shadows##lightShadow", ref shadow))
                        light.LightShadowEnabled = shadow;
                }

                ImGui.Spacing();

                // Reset light
                if (ImGui.Button("Reset Light"))
                {
                    light.LightEnergy         = 1f;
                    light.LightRange          = 5f;
                    light.LightIndirectEnergy = 1f;
                    light.LightSpecular       = 0.5f;
                    light.LightShadowEnabled  = true;
                    light.LightColor          = new vec4(1f, 1f, 1f, 1f);
                }
            }
        }

        // ── Bend section (not yet implemented) ────────────────────────────────
        // TODO: implement Bend section once BoneSceneObject.BendParameters is available

        ImGui.EndTabItem();
    }
    
    // ── Helpers ───────────────────────────────────────────────────────────────

    private void ApplyPosition(vec3 pos)
    {
        if (_currentObject is BoneSceneObject bone)
            bone.TargetPosition = pos;
        else
            _currentObject.SetLocalPosition(pos);
    }

    private void ApplyRotation(vec3 rot)
    {
        if (_currentObject is BoneSceneObject bone)
            bone.TargetRotation = rot;
        else
            _currentObject.SetLocalRotation(rot);
    }

    /// <summary>
    /// Ensures <see cref="SceneObject.MaterialSettings"/> is non-null before writing.
    /// </summary>
    private void EnsureMaterialSettings()
    {
        if (_currentObject == null) return;
        if (_currentObject.MaterialSettings == null)
            _currentObject.MaterialSettings = new MaterialSettings();
    }
}