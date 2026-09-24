using System;
using TMPro;
using UnityEngine;

namespace Runic.Shared;

internal static class CaptionMeshCurve
{
    // Only call on freshly generated TMP geometry; includes glyphs and emoji submeshes.
    internal static bool Apply(TMP_TextInfo info, float vertical, float depth, out Vector3 min, out Vector3 max,
        float wrapRadius = 0, float wrapDepthRadius = 0)
    {
        min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        float left = float.PositiveInfinity, right = float.NegativeInfinity;
        for (int i = 0; i < info.characterCount; i++) {
            var ch = info.characterInfo[i]; if (!ch.isVisible) continue;
            var vertices = info.meshInfo[ch.materialReferenceIndex].vertices;
            for (int j = 0; j < 4; j++) {
                float x = vertices[ch.vertexIndex + j].x;
                left = Math.Min(left, x); right = Math.Max(right, x);
            }
        }
        if (!(right > left)) return false;
        float center = (left + right) * .5f;
        bool wrap = wrapRadius > .0001f && wrapDepthRadius > .0001f;
        float strength = Math.Max(0, 1 + depth);
        // Avoid folding letters around the back of a narrow container. Scale both ellipse
        // radii together so its aspect ratio is retained for unusually wide captions.
        if (wrap && strength > 0) {
            float minimumRadius = (right - left) * .5f * strength / 1.45f;
            if (wrapRadius < minimumRadius) {
                wrapDepthRadius *= minimumRadius / wrapRadius;
                wrapRadius = minimumRadius;
            }
        }
        for (int i = 0; i < info.characterCount; i++) {
            var ch = info.characterInfo[i]; if (!ch.isVisible) continue;
            var vertices = info.meshInfo[ch.materialReferenceIndex].vertices;
            for (int j = 0; j < 4; j++) {
                int index = ch.vertexIndex + j; var v = vertices[index];
                float bend = CaptionCurve.Arc(v.x, left, right);
                v.y += bend * vertical;
                if (wrap) {
                    // The caption center stays on the face. The ends turn inward around
                    // the container instead of leaving the ends in a flat tangent plane.
                    if (strength > .0001f) {
                        double angle = (v.x - center) * strength / wrapRadius;
                        v.x = center + (float)(wrapRadius / strength * Math.Sin(angle));
                        v.z += (float)(wrapDepthRadius / strength * (1 - Math.Cos(angle)));
                    }
                }
                else v.z -= bend * depth; // Negative local Z faces the reader.
                vertices[index] = v;
                min.x = Math.Min(min.x, v.x); max.x = Math.Max(max.x, v.x);
                min.y = Math.Min(min.y, v.y); max.y = Math.Max(max.y, v.y);
                min.z = Math.Min(min.z, v.z); max.z = Math.Max(max.z, v.z);
            }
        }
        return true;
    }
}
