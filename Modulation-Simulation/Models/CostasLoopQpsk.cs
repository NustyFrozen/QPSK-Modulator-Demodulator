using MathNet.Numerics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace QPSK.Models;

/// <summary>
/// 
/// </summary>
/// <param name="sampleRate">sample rate</param>
/// <param name="loopBandwith">cutoff LPF of the error typ 40-150</param>
/// <param name="FIRTaps">how strong you want the smoothing Small smoothing 11-31, strong 51-201  must be odd number</param>

public class CostasLoopQpsk
{
    readonly double sampleRate;
    readonly double loopBandwidthHz;
    readonly double damping;

    double alpha, beta;   // loop gains from BW + damping
    double theta;         // NCO phase [rad]
    double freq;          // NCO frequency state [rad/sample]

    public CostasLoopQpsk(
        double sampleRate,
        double loopBandwidthHz,
        double damping = 0.707)
    {
        this.sampleRate = sampleRate;
        this.loopBandwidthHz = loopBandwidthHz;
        this.damping = damping;

        // normalize loop BW to rad/sample
        double bw = 2.0 * Math.PI * loopBandwidthHz / sampleRate;

        // alpha, beta (Tom Rondeau / GNU Radio)
        double d = 1.0 + 2.0 * damping * bw + bw * bw;
        alpha = (4.0 * damping * bw) / d;
        beta = (4.0 * bw * bw) / d;

        theta = 0.0;
        freq = 0.0;
    }

    // Hard limiter for QPSK, float IQ
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void GetSign(float i, float q, out float di, out float dq)
    {
        di = (i >= 0f) ? 1f : -1f;
        dq = (q >= 0f) ? 1f : -1f;
    }

    /// <summary>
    /// Process one interleaved IQ sample.
    /// Input: (inI,inQ). Output: mixed (outI,outQ).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Process(float inI, float inQ, out float outI, out float outQ)
    {
        // Compute e^{-j theta} = cos(theta) - j sin(theta)
        // mixed = (inI + j inQ) * (c - j s)
        // => outI = inI*c + inQ*s
        // => outQ = inQ*c - inI*s
        double c = Math.Cos(theta);
        double s = Math.Sin(theta);

        double mi = (double)inI * c + (double)inQ * s;
        double mq = (double)inQ * c - (double)inI * s;

        outI = (float)mi;
        outQ = (float)mq;

        // QPSK decision on mixed
        GetSign(outI, outQ, out float estI, out float estQ);

        // Costas phase detector: e = estI*mq - estQ*mi
        double phaseError = (double)estI * mq - (double)estQ * mi;

        // 2nd-order loop update
        freq += beta * phaseError;
        theta += freq + alpha * phaseError;

        // keep theta bounded
        const double TwoPi = 2.0 * Math.PI;
        if (theta > Math.PI) theta -= TwoPi;
        else if (theta < -Math.PI) theta += TwoPi;
    }

    /// <summary>
    /// Span-based block processing. iqIn/iqOut are interleaved IQ: [I0,Q0,I1,Q1,...]
    /// Returns number of complex samples processed.
    /// </summary>
    public int Process(ReadOnlySpan<float> iqIn, Span<float> iqOut)
    {
        if ((iqIn.Length & 1) != 0)
            throw new ArgumentException("Input must be interleaved IQ with even length.", nameof(iqIn));
        if (iqOut.Length < iqIn.Length)
            throw new ArgumentException("Output span is too small.", nameof(iqOut));

        int n = iqIn.Length >> 1;
        for (int k = 0; k < n; k++)
        {
            int s = k << 1;
            Process(iqIn[s], iqIn[s + 1], out float yI, out float yQ);
            iqOut[s] = yI;
            iqOut[s + 1] = yQ;
        }
        return n;
    }

    /// <summary>
    /// Convenience allocating overload.
    /// </summary>
    public float[] Process(float[] iqIn)
    {
        if (iqIn == null) throw new ArgumentNullException(nameof(iqIn));
        var y = new float[iqIn.Length];
        Process(iqIn.AsSpan(), y.AsSpan());
        return y;
    }

    /// <summary>
    /// Optional: expose current NCO phase/frequency (radians, rad/sample).
    /// </summary>
    public (double theta, double freq) GetState() => (theta, freq);
}