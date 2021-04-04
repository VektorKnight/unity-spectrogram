using System;
using System.Collections.Generic;
using UnityEngine;

namespace Spectrogram {
    /// <summary>
    /// Some helpful texture utilities for curves and gradients.
    /// </summary>
    public static class TextureUtility {
        /// <summary>
        /// Generates a 1D lookup texture for a given gradient.
        /// </summary>
        public static Texture2D GenerateGradientTexture(Gradient g, uint res = 512, bool wrap = false) {
            if (res < 1) {
                throw new ArgumentException("Resolution must be greater than zero!");
            }

            var colors = new Color[res];
            var tex = new Texture2D((int)res, 1, TextureFormat.RGBA32, false) {
                wrapMode = wrap ? TextureWrapMode.Repeat : TextureWrapMode.Clamp
            };

            for (var i = 0; i < res; i++) {
                colors[i] = g.Evaluate((float) i / res);
            }
            
            tex.SetPixels(colors);
            tex.Apply();
            return tex;
        }
        
        /// <summary>
        /// Generates a 1D lookup texture for a given curve.
        /// Samples from [0, 1] and clamps the result to [0, 1].
        /// Curves should fit this range on both axes for best results.
        /// </summary>
        public static Texture2D GenerateCurveTexture(AnimationCurve c, uint res = 512, bool wrap = false) {
            if (res < 1) {
                throw new ArgumentException("Resolution must be greater than zero!");
            }

            var colors = new Color[res];
            var tex = new Texture2D((int)res, 1, TextureFormat.RGBA32, false) {
                wrapMode = wrap ? TextureWrapMode.Repeat : TextureWrapMode.Clamp
            };

            for (var i = 0; i < res; i++) {
                colors[i] = Mathf.Clamp01(c.Evaluate((float) i / res)) * Color.white;
            }
            
            tex.SetPixels(colors);
            tex.Apply();
            return tex;
        }
        
        /// <summary>
        /// Generates a 1D lookup texture for a given set of curves (up to 4).
        /// Each curve is mapped to a different channel (RGBA).
        /// Samples from [0, 1] and clamps the result to [0, 1].
        /// Curves should fit this range on both axes for best results.
        /// </summary>
        public static Texture2D GenerateCurveTexture(List<AnimationCurve> curves, uint res = 512, bool wrap = false) {
            if (res < 1) {
                throw new ArgumentException("Resolution must be greater than zero!");
            }

            var colors = new Color[res];
            var tex = new Texture2D((int)res, 1, TextureFormat.RGBA32, false) {
                wrapMode = wrap ? TextureWrapMode.Repeat : TextureWrapMode.Clamp
            };

            for (var i = 0; i < res; i++) {
                var r = curves[0] != null ? Mathf.Clamp01(curves[0].Evaluate((float) i / res)) : 0f;
                var g = curves[1] != null ? Mathf.Clamp01(curves[1].Evaluate((float) i / res)) : 0f;
                var b = curves[2] != null ? Mathf.Clamp01(curves[2].Evaluate((float) i / res)) : 0f;
                var a = curves[3] != null ? Mathf.Clamp01(curves[3].Evaluate((float) i / res)) : 0f;

                colors[i] = new Color(r, g, b, a);
            }
            
            tex.SetPixels(colors);
            tex.Apply();
            return tex;
        }
    }
}