// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Runtime.CompilerServices;

namespace Lumora.Audio;

// One section running on a stereo buffer. A new design is reached by a straight line through G, K and the
// mix, one increment per sample, and the recursion coefficients are worked out from G and K afresh on every sample
// of the ramp. Every point on that line is itself a valid section with positive G and K, which is what keeps
// a ramp from ever passing through something unstable. At rest the coefficients are fixed and a section set to
// the identity costs nothing. -xlinka
internal struct SvfStage
{
    private SvfDesign _design;
    private SvfDesign _target;
    private SvfDesign _step;
    private int _ramp;
    private float _a1;
    private float _a2;
    private float _a3;
    private float _left1;
    private float _left2;
    private float _right1;
    private float _right2;

    public readonly bool IsIdle => _ramp == 0 && _design.IsIdentity;

    public readonly bool IsRamping => _ramp > 0;

    public void Snap(in SvfDesign design)
    {
        _design = design;
        _target = design;
        _ramp = 0;
        Coefficients(design, out _a1, out _a2, out _a3);
        if (design.IsIdentity)
            Clear();
    }

    // Starts from wherever the last sample left the section.
    public void RampTo(in SvfDesign target, int samples)
    {
        _target = target;
        if (samples <= 0 || target.Equals(_design))
        {
            if (_ramp > 0 || !target.Equals(_design))
                Snap(target);
            return;
        }
        float inverse = 1f / samples;
        _step = new SvfDesign
        {
            G = (target.G - _design.G) * inverse,
            K = (target.K - _design.K) * inverse,
            M0 = (target.M0 - _design.M0) * inverse,
            M1 = (target.M1 - _design.M1) * inverse,
            M2 = (target.M2 - _design.M2) * inverse,
        };
        _ramp = samples;
    }

    public void Clear()
    {
        _left1 = 0f;
        _left2 = 0f;
        _right1 = 0f;
        _right2 = 0f;
    }

    public void Run(Span<StereoSample> buffer)
    {
        int count = buffer.Length;
        int i = 0;
        float left1 = _left1;
        float left2 = _left2;
        float right1 = _right1;
        float right2 = _right2;

        if (_ramp > 0)
        {
            int ramped = Math.Min(_ramp, count);
            SvfDesign design = _design;
            SvfDesign step = _step;
            for (; i < ramped; i++)
            {
                if (--_ramp == 0)
                {
                    design = _target;
                }
                else
                {
                    design.G += step.G;
                    design.K += step.K;
                    design.M0 += step.M0;
                    design.M1 += step.M1;
                    design.M2 += step.M2;
                }
                float a1 = 1f / (1f + design.G * (design.G + design.K));
                float a2 = design.G * a1;
                float a3 = design.G * a2;
                StereoSample x = buffer[i];
                buffer[i] = new StereoSample(
                    Tick(x.left, ref left1, ref left2, a1, a2, a3, design.M0, design.M1, design.M2),
                    Tick(x.right, ref right1, ref right2, a1, a2, a3, design.M0, design.M1, design.M2));
            }
            _design = design;
            if (_ramp == 0)
            {
                Coefficients(design, out _a1, out _a2, out _a3);
                if (design.IsIdentity)
                {
                    left1 = 0f;
                    left2 = 0f;
                    right1 = 0f;
                    right2 = 0f;
                }
            }
        }

        if (i < count && !_design.IsIdentity)
        {
            float a1 = _a1;
            float a2 = _a2;
            float a3 = _a3;
            float m0 = _design.M0;
            float m1 = _design.M1;
            float m2 = _design.M2;
            for (; i < count; i++)
            {
                StereoSample x = buffer[i];
                buffer[i] = new StereoSample(
                    Tick(x.left, ref left1, ref left2, a1, a2, a3, m0, m1, m2),
                    Tick(x.right, ref right1, ref right2, a1, a2, a3, m0, m1, m2));
            }
        }

        _left1 = left1;
        _left2 = left2;
        _right1 = right1;
        _right2 = right2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Tick(float x, ref float s1, ref float s2, float a1, float a2, float a3, float m0, float m1, float m2)
    {
        float v3 = x - s2;
        float v1 = a1 * s1 + a2 * v3;
        float v2 = s2 + a2 * s1 + a3 * v3;
        s1 = DspMath.Flush(2f * v1 - s1);
        s2 = DspMath.Flush(2f * v2 - s2);
        return m0 * x + m1 * v1 + m2 * v2;
    }

    private static void Coefficients(in SvfDesign design, out float a1, out float a2, out float a3)
    {
        a1 = 1f / (1f + design.G * (design.G + design.K));
        a2 = design.G * a1;
        a3 = design.G * a2;
    }
}
