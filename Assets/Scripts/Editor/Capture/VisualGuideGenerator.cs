#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;
using JunaidIRF.SyncedInteractions;
using JunaidIRF.SyncedInteractions.Editor;

namespace JunaidIRF.SyncedInteractions.EditorCapture
{
    /// <summary>
    /// Orchestrates visual capture passes and builds the complete VisualGuide.md documentation.
    /// </summary>
    public static class VisualGuideGenerator
    {
        private const string OutputDir = "Packages/com.junaidirf.syncedinteractions/Documentation~/images";
        private const string GuidePath = "Packages/com.junaidirf.syncedinteractions/Documentation~/VisualGuide.md";
        private const string DefaultAssetPath = "Assets/Settings/Executions/Bite_Interaction.asset";

        [MenuItem("Tools/Synced Interactions/Capture Studio Inspector")]
        public static string CaptureInspector()
        {
            Directory.CreateDirectory(OutputDir);
            var asset = AssetDatabase.LoadAssetAtPath<InteractionAsset>(DefaultAssetPath);
            if (asset == null) return "Asset not found: " + DefaultAssetPath;

            string outPath = Path.Combine(OutputDir, "inspector_interaction_asset.png");
            EditorUICapture.CaptureAssetInspectorStudio(asset, 480, 760, outPath);
            return "Saved " + outPath;
        }

        [MenuItem("Tools/Synced Interactions/Capture Dual Rig Dopesheet")]
        public static string CaptureDualRig()
        {
            Directory.CreateDirectory(OutputDir);
            var asset = AssetDatabase.LoadAssetAtPath<InteractionAsset>(DefaultAssetPath);
            if (asset == null) return "Asset not found: " + DefaultAssetPath;

            string outPath = Path.Combine(OutputDir, "dual_rig_animation_window.png");
            try
            {
                InteractionDualRig.Open(asset);
                EditorUICapture.PumpMessages(300);

                var anim = Object.FindAnyObjectByType<Animator>();
                if (anim != null) Selection.activeGameObject = anim.gameObject;

                var aw = EditorWindow.GetWindow<AnimationWindow>();
                if (aw != null)
                {
                    aw.Focus();
                    aw.Repaint();
                }
                EditorUICapture.PumpMessages(400);

                EditorUICapture.CaptureWindow("AnimationWindow", outPath, 24, 0, 0, 0);
            }
            finally
            {
                InteractionDualRig.Close();
                EditorUICapture.PumpMessages(200);
            }

            return "Saved " + outPath;
        }

        [MenuItem("Tools/Synced Interactions/Generate All Visual Guide Assets")]
        public static string GenerateAll()
        {
            CaptureInspector();
            CaptureDualRig();
            BuildGuideMarkdown();
            AssetDatabase.Refresh();
            return "Visual Guide screenshots and markdown generated successfully!";
        }

        public static void BuildGuideMarkdown()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# 📸 Synced Interactions — Visual Guide & Inspector Reference");
            sb.AppendLine();
            sb.AppendLine("A comprehensive visual walkthrough of authoring, staging, and configuring synced interactions in Unity.");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("## 1. Interaction Asset Inspector");
            sb.AppendLine();
            sb.AppendLine("The **InteractionAsset** is the central ScriptableObject defining participant roles, slot alignments, and timed execution events.");
            sb.AppendLine();
            sb.AppendLine("<div align=\"center\">");
            sb.AppendLine("  <img src=\"images/inspector_interaction_asset.png\" width=\"480\" alt=\"Interaction Asset Inspector\" />");
            sb.AppendLine("  <p><em>Studio Inspector view of <code>Bite_Interaction.asset</code> showing Roles, Anchor mode, Alignment, and Events.</em></p>");
            sb.AppendLine("</div>");
            sb.AppendLine();
            sb.AppendLine("### Key Inspector Sections");
            sb.AppendLine();
            sb.AppendLine("| Section | Purpose | Best Practices |");
            sb.AppendLine("| :--- | :--- | :--- |");
            sb.AppendLine("| **Roles** | Defines participating actors (`Initiator`, `Victim`, `Partner`). | Always assign real preview prefabs to see live alignments in Stage mode. |");
            sb.AppendLine("| **Slot Offset & Yaw** | Relative transform where the partner snaps before playing. | Use **Open on Stage** or Scene handles to place partners visually without typing numbers. |");
            sb.AppendLine("| **Alignment** | How actors navigate to their slots (`Glide`, `Warp`, `None`). | Use `Glide` (0.2s–0.4s) for smooth transitions; enable ground raycasting to snap onto terrain. |");
            sb.AppendLine("| **Events** | Timed markers firing modular `InteractionAction` callbacks. | Click **Use Selected** to bind target bone paths instantly from the Scene view. |");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("## 2. Authoring Workflows");
            sb.AppendLine();
            sb.AppendLine("```mermaid");
            sb.AppendLine("flowchart LR");
            sb.AppendLine("    A[\"InteractionAsset\"] --> B[\"Mode 1: Animate Both<br/>(Animation Window)\"]");
            sb.AppendLine("    A --> C[\"Mode 2: Open on Stage<br/>(Timeline Window)\"]");
            sb.AppendLine("    B --> D[\"Unified Dopesheet<br/>Keyframe both characters<br/>0 missing bones\"]");
            sb.AppendLine("    C --> E[\"Scrub Timeline<br/>Drag slot positions in Scene<br/>Retime event markers\"]");
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("### Mode 1: Animate Both (Unified Dopesheet)");
            sb.AppendLine("- Click **Animate Both** in the asset Inspector.");
            sb.AppendLine("- Both characters are mounted under a composite `[Interaction Rig]` with automatic hierarchy resolution.");
            sb.AppendLine("- Keyframe and scrub both characters simultaneously in Unity's standard **Animation Window**.");
            sb.AppendLine("- Modifying dopesheet event markers automatically synchronizes with `InteractionAsset.events`.");
            sb.AppendLine();
            sb.AppendLine("<div align=\"center\">");
            sb.AppendLine("  <img src=\"images/dual_rig_animation_window.png\" width=\"860\" alt=\"Dual Rig Animation Window Dopesheet\" />");
            sb.AppendLine("  <p><em>Unity Animation Window showing both characters combined on a unified dopesheet with zero missing curves.</em></p>");
            sb.AppendLine("</div>");
            sb.AppendLine();
            sb.AppendLine("### Mode 2: Open on Stage (Timeline Staging)");
            sb.AppendLine("- Click **Open on Stage** to spawn preview actors in an isolated scene.");
            sb.AppendLine("- Drag the Timeline scrubber to verify timing, dismemberment/sound events, and alignment arcs.");
            sb.AppendLine("- Move or rotate preview characters directly in the Scene view; slot offsets update in real time.");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("## 3. Fast Bone Picking Workflow");
            sb.AppendLine();
            sb.AppendLine("Instead of typing deep hierarchy paths like `mixamorig:Hips/mixamorig:Spine/mixamorig:Neck`:");
            sb.AppendLine();
            sb.AppendLine("1. Open your interaction on **Stage** or in the **Dual Rig**.");
            sb.AppendLine("2. Click any bone in the **Scene View** or **Hierarchy**.");
            sb.AppendLine("3. In the Inspector event drawer, click **`Use Selected`**.");
            sb.AppendLine("4. The exact relative transform path is resolved and populated automatically.");

            File.WriteAllText(GuidePath, sb.ToString());
        }
    }
}
#endif
