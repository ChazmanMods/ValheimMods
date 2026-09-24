using System;
using System.Reflection;
using Runic.Shared;
using RunicSigns.Core;
using RunicSigns.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

internal static class CaptionCurveTests
{
    private static void Check(bool value) { if (!value) throw new Exception("Caption curve regression."); }
    private static void Near(float a, float b) => Check(Math.Abs(a - b) < .000001f);
    internal static void FineControlsPreserveSavedShapes()
    {
        Near(CaptionCurve.FromDisplay(1), .01f);
        Near(CaptionCurve.FromDisplay(-1), -.01f);
        Near(CaptionCurve.FromDisplay(.1f), .001f);
        Near(CaptionCurve.Step(0, 1), .01f);
        Near(CaptionCurve.Step(0, -1), -.01f);
        float bend = 0;
        for (int i = 0; i < 100; i++) bend = CaptionCurve.Step(bend, 1);
        Near(bend, 1); Near(CaptionCurve.Step(bend, 1), 1);
        for (int i = 0; i < 100; i++) bend = CaptionCurve.Step(bend, -1);
        Near(bend, 0); Near(CaptionCurve.Step(-1, -1), -1);
        var legacy = new SignSettings { CurveVertical = .01f, CurveDepth = -.25f };
        string saved = legacy.Encode();
        Check(SignSettings.TryDecode(saved, out var read));
        Near(CaptionCurve.ToDisplay(read.CurveVertical), 1);
        Near(CaptionCurve.ToDisplay(read.CurveDepth), -25);
        Check(read.Encode() == saved); // Merely opening the editor never rewrites old curves.
    }

    private static TMP_TextInfo Geometry() => new() {
        characterCount = 3,
        characterInfo = new[] {
            new TMP_CharacterInfo { isVisible = true, materialReferenceIndex = 0, vertexIndex = 0 },
            new TMP_CharacterInfo { isVisible = true, materialReferenceIndex = 1, vertexIndex = 0 },
            new TMP_CharacterInfo { isVisible = false, materialReferenceIndex = 0, vertexIndex = 4 } },
        meshInfo = new[] {
            new TMP_MeshInfo { vertices = new[] {new Vector3(-10,0,0), new Vector3(-10,2,0), new Vector3(0,2,0), new Vector3(0,0,0)} },
            new TMP_MeshInfo { vertices = new[] {new Vector3(0,0,0), new Vector3(0,2,0), new Vector3(10,2,0), new Vector3(10,0,0)} } }
    };

    internal static void ContainerWrapFollowsSurfaceAndKeepsCenter()
    {
        var text = new GameObject().AddComponent<TextMeshProUGUI>();
        var backing = new GameObject().AddComponent<Image>();
        var bend = CaptionBend.Attach(text, backing);
        foreach (float depthRadius in new[] { 42.5f, 32.5f }) {
            bend.SetSurfaceWrap(42.5f, depthRadius);
            bend.Set(.01f, 0);
            for (int frame = 0; frame < 3; frame++) {
                var geometry = Geometry();
                foreach (var mesh in geometry.meshInfo)
                    for (int i = 0; i < mesh.vertices.Length; i++) mesh.vertices[i].x *= 3;
                text.Generate(geometry);
                var left = geometry.meshInfo[0].vertices[0];
                var center = geometry.meshInfo[0].vertices[3];
                var right = geometry.meshInfo[1].vertices[3];
                Near(center.x, 0); Near(center.z, 0); Near(center.y, .3f);
                Check(left.x > -30 && right.x < 30 && left.z > 5 && right.z > 5);
                Near(left.x, -right.x); Near(left.z, right.z);
                // Circular and elliptical barrel cross-sections, including the 2.5cm gap.
                foreach (var mesh in geometry.meshInfo) foreach (var vertex in mesh.vertices) {
                    double x = vertex.x / 42.5, z = (depthRadius - vertex.z) / depthRadius;
                    Check(Math.Abs(x * x + z * z - 1) < .00001);
                }
                Check(backing.rectTransform.localPosition.z > right.z);
            }
        }
        bend.SetSurfaceWrap(42.5f, 42.5f);
        float previous = -1;
        foreach (float amount in new[] { -.01f, 0, .01f }) {
            bend.Set(0, amount); var geometry = Geometry(); text.Generate(geometry);
            float depth = geometry.meshInfo[0].vertices[0].z;
            Check(depth > previous); previous = depth;
        }
        // A very wide caption cannot fold behind the container.
        bend.SetSurfaceWrap(1, 1); bend.Set(0, 1);
        var wide = Geometry(); text.Generate(wide);
        var end = wide.meshInfo[1].vertices[3];
        double radius = 10 / 1.45;
        Check(end.x > 0 && end.z < radius);
        // The full flatten adjustment and the reset/toggle path both restore flat text.
        bend.Set(0, -1); var flat = Geometry(); text.Generate(flat);
        Near(flat.meshInfo[0].vertices[0].x, -10); Near(flat.meshInfo[0].vertices[0].z, 0);
        bend.SetSurfaceWrap(0, 0); bend.Set(0, 0); flat = Geometry(); text.Generate(flat);
        Near(flat.meshInfo[0].vertices[0].x, -10); Near(flat.meshInfo[0].vertices[0].z, 0);
        Near(backing.rectTransform.localPosition.z, 0);
    }

    internal static void StorageAndSignsBendGlyphsAndEmojisEqually()
    {
        var text = new GameObject().AddComponent<TextMeshProUGUI>();
        var backing = new GameObject().AddComponent<Image>();
        var bend = CaptionBend.Attach(text, backing);
        var signText = new GameObject().AddComponent<TextMeshProUGUI>();
        using var sign = new SignAppearance(new GameObject().transform, signText);
        foreach (float direction in new[] { 1f, -1f }) {
            float amount = CaptionCurve.FromDisplay(direction);
            bend.Set(amount, -amount);
            sign.Apply(new SignSettings { CurveVertical = amount, CurveDepth = -amount });
            for (int frame = 0; frame < 3; frame++) {
                var chestGeometry = Geometry(); var signGeometry = Geometry();
                text.Generate(chestGeometry); signText.Generate(signGeometry);
                Near(chestGeometry.meshInfo[0].vertices[0].y, 0);
                Near(chestGeometry.meshInfo[0].vertices[3].y, .1f * direction);
                Near(chestGeometry.meshInfo[1].vertices[0].z, .1f * direction);
                for (int mesh = 0; mesh < 2; mesh++) for (int vertex = 0; vertex < 4; vertex++) {
                    Near(chestGeometry.meshInfo[mesh].vertices[vertex].y, signGeometry.meshInfo[mesh].vertices[vertex].y);
                    Near(chestGeometry.meshInfo[mesh].vertices[vertex].z, signGeometry.meshInfo[mesh].vertices[vertex].z);
                }
                if (direction > 0) Check(backing.rectTransform.localPosition.z > .1f);
            }
        }
        bend.Set(0, 0); var flat = Geometry(); text.Generate(flat);
        Near(flat.meshInfo[0].vertices[3].y, 0); Near(flat.meshInfo[0].vertices[3].z, 0);
        Near(backing.rectTransform.localPosition.z, 0);
        typeof(CaptionBend).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(bend, null);
        Check(text.GeometryHandlers == 0);
    }
}
