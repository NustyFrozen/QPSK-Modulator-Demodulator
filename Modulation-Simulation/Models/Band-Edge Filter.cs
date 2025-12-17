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
/// FLL band-edge filter (port of GNU Radio fll_band_edge_cc_impl).
/// </summary>
public class FLLBandEdgeFilter
{
    const float TWO_PI = 2.0f * MathF.PI;

    // Design parameters
    public float sps;         // samples per symbol
    public float rolloff;     // rolloff [0,1]
    public int filterSize;    // number of taps
    public float bandwidth;   // loop bandwidth

    // Loop state
    public float phase;
    public float freq;

    // Loop gains and limits
    float alpha;   // kept for completeness, but 0 for FLL
    float beta;
    float maxFreq;
    float minFreq;

    // Filters (taps are complex -> interleaved IQ float arrays)
    float[] _tapsLowerIQ = Array.Empty<float>();
    float[] _tapsUpperIQ = Array.Empty<float>();
    ComplexFIRFilter filterLower = null!;
    ComplexFIRFilter filterUpper = null!;

    public FLLBandEdgeFilter(float sps, float rolloff, int filterSize, float bandwidth)
    {
        if (sps <= 0.0f) throw new ArgumentOutOfRangeException(nameof(sps), "sps must be > 0.");
        if (rolloff < 0 || rolloff > 1.0f) throw new ArgumentOutOfRangeException(nameof(rolloff), "rolloff must be in [0,1].");
        if (filterSize <= 0) throw new ArgumentOutOfRangeException(nameof(filterSize), "filterSize must be > 0.");
        if (bandwidth <= 0.0f) throw new ArgumentOutOfRangeException(nameof(bandwidth), "bandwidth must be > 0.");

        this.sps = sps;
        this.rolloff = rolloff;
        this.filterSize = filterSize;
        this.bandwidth = bandwidth;

        phase = 0.0f;
        freq = 0.0f;

        alpha = 0.0f;               // FLL: no direct phase update
        beta = 4.0f * bandwidth / sps;

        maxFreq = TWO_PI * (2.0f / sps);
        minFreq = -maxFreq;

        DesignFilter();
    }

    public int Process(ReadOnlySpan<float> inputIQ, Span<float> outputIQ)
    {
        if ((inputIQ.Length & 1) != 0)
            throw new ArgumentException("Input must be interleaved IQ with even length.", nameof(inputIQ));
        if (outputIQ.Length < inputIQ.Length)
            throw new ArgumentException("Output span is too small.", nameof(outputIQ));

        int n = inputIQ.Length >> 1;

        for (int i = 0; i < n; i++)
        {
            int s = i << 1;

            float inI = inputIQ[s];
            float inQ = inputIQ[s + 1];

            Process(inI, inQ, out float outI, out float outQ);

            outputIQ[s] = outI;
            outputIQ[s + 1] = outQ;
        }

        return n;
    }

   
    public float[] Process(float[] inputIQ)
    {
        if (inputIQ == null) throw new ArgumentNullException(nameof(inputIQ));
        var y = new float[inputIQ.Length];
        Process(inputIQ.AsSpan(), y.AsSpan());
        return y;
    }

    /// <summary>
    /// Process one IQ sample.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Process(float inI, float inQ, out float outI, out float outQ)
    {
        // NCO: multiply by e^{j phase} = cos + j sin
        // (inI + j inQ) * (c + j s)
        // => outI = inI*c - inQ*s
        // => outQ = inI*s + inQ*c
        float c = MathF.Cos(phase);
        float s = MathF.Sin(phase);

        outI = inI * c - inQ * s;
        outQ = inI * s + inQ * c;

        // Band-edge filters (operate on mixed output)
        filterUpper.Filter(outI, outQ, out float upI, out float upQ);
        filterLower.Filter(outI, outQ, out float loI, out float loQ);

        float powUpper = upI * upI + upQ * upQ;
        float powLower = loI * loI + loQ * loQ;

        float error = powLower - powUpper;

        // advance the loop
        freq += beta * error;
        phase += freq + alpha * error;

        WrapPhase();
        LimitFrequency();
    }

   
    void DesignFilter()
    {
        int numTaps = filterSize;
        int mid = (numTaps - 1) / 2;

        var bbTaps = new float[numTaps];
        float sum = 0.0f;

        // taken from gnuradio blocks
        for (int i = 0; i < numTaps; i++)
        {
            float k = (i - mid) / (2.0f * sps);
            float pos = rolloff * k;

            float tap = Sinc(pos - 0.5f) + Sinc(pos + 0.5f);
            sum += tap;
            bbTaps[i] = tap;
        }

        
        for (int i = 0; i < numTaps; i++)
            bbTaps[i] /= sum;

        
        _tapsLowerIQ = new float[numTaps << 1];
        _tapsUpperIQ = new float[numTaps << 1];

        // ---- SHIFT TO ±(1+rolloff)/2 SYMBOL RATE ----
        for (int i = 0; i < numTaps; i++)
        {
            float k = (i - mid) / (2.0f * sps);
            float angle = -TWO_PI * (1.0f + rolloff) * k;

            float wc = MathF.Cos(angle);
            float ws = MathF.Sin(angle);

            // tapsLower = bbTap * e^{j angle}
            float li = bbTaps[i] * wc;
            float lq = bbTaps[i] * ws;

            int t = i << 1;
            _tapsLowerIQ[t] = li;
            _tapsLowerIQ[t + 1] = lq;

            // tapsUpper = conj(tapsLower)
            _tapsUpperIQ[t] = li;
            _tapsUpperIQ[t + 1] = -lq;
        }

        filterLower = new ComplexFIRFilter(_tapsLowerIQ);
        filterUpper = new ComplexFIRFilter(_tapsUpperIQ);
    }

    void WrapPhase()
    {
        if (phase > TWO_PI || phase < -TWO_PI)
            phase = MathF.IEEERemainder(phase, TWO_PI);
    }

    void LimitFrequency()
    {
        if (freq > maxFreq) freq = maxFreq;
        else if (freq < minFreq) freq = minFreq;
    }

    static float Sinc(float x)
    {
        if (x == 0.0f) return 1.0f;
        float arg = MathF.PI * x;
        return MathF.Sin(arg) / arg;
    }
}
