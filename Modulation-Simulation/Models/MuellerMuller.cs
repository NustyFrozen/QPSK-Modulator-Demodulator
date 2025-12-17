using MathNet.Numerics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace QPSK.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.X86;

public class MuellerMuller
{
    private readonly double samplesPerSymbol;   // e.g. 8.0
    private readonly double kp;
    private readonly double ki;

   
    private int baseIndex;          // integer sample index into *logical* buffer (complex-sample index)
    private double mu;              // fractional position between baseIndex and baseIndex+1
    private double ncoIntegral;     // integrator in timing loop

    private float prevSampleI, prevSampleQ;
    private float prevDecisionI, prevDecisionQ;
    private bool hasPrev;

    // Interleaved IQ buffer store: [I0,Q0,I1,Q1,...]
    // We keep it as a linear queue with start+count (in complex samples) to avoid RemoveRange copies.
    private float[] _bufIQ = new float[4096]; // grows as needed (floats)
    private int _bufStart; // in complex samples
    private int _bufCount; // in complex samples

    public MuellerMuller(double samplesPerSymbol, double kp, double ki)
    {
        this.samplesPerSymbol = samplesPerSymbol;
        this.kp = kp;
        this.ki = ki;

        baseIndex = 1;
        mu = 0.0;
        ncoIntegral = 0.0;
        hasPrev = false;

        _bufStart = 0;
        _bufCount = 0;
    }
    public int Process(ReadOnlySpan<float> incomingMfSamplesIQ, Span<float> outputSymbolsIQ)
    {
        if ((incomingMfSamplesIQ.Length & 1) != 0)
            throw new ArgumentException("Input must be interleaved IQ with even length.", nameof(incomingMfSamplesIQ));

        // Append new matched-filter outputs to internal buffer
        Append(incomingMfSamplesIQ);

        int outSymbols = 0;

        while (baseIndex + 2 < _bufCount) // and baseIndex will be kept >= 1
        {
            // interpolate at current timing phase
            CubicLagrange4(baseIndex, mu, out float currI, out float currQ);


            // QPSK hard decision (sign on I/Q)
            GetSignQpsk(currI, currQ, out float decI, out float decQ);

            double advance;

            if (hasPrev)
            {
                // Mueller–Muller TED:
                // e = Re{ conj(d_{k-1})*x_k - conj(d_k)*x_{k-1} }
                // For conj(a)*b, real = aI*bI + aQ*bQ
                double term1Real = (double)prevDecisionI * currI + (double)prevDecisionQ * currQ;
                double term2Real = (double)decI * prevSampleI + (double)decQ * prevSampleQ;
                double e = term1Real - term2Real;

                // PI loop filter
                ncoIntegral += ki * e;
                double correction = kp * e + ncoIntegral;

                //// clamp correction to avoid symbol slips
                const double maxStep = 0.1; // max +/- step in *samples*
                if (correction > maxStep) correction = maxStep;
                if (correction < -maxStep) correction = -maxStep;

                advance = samplesPerSymbol + correction;
            }
            else
            {
                hasPrev = true;
                advance = samplesPerSymbol;
            }

            // output *one* symbol per timing update
            int o = outSymbols << 1;
            if (o + 1 >= outputSymbolsIQ.Length)
                break; // caller didn't give enough space; keep state, produce partial

            outputSymbolsIQ[o] = currI;
            outputSymbolsIQ[o + 1] = currQ;
            outSymbols++;

            // update previous symbol/decision
            prevSampleI = currI; prevSampleQ = currQ;
            prevDecisionI = decI; prevDecisionQ = decQ;

            // advance timing: time = baseIndex + mu + advance
            double newTime = baseIndex + mu + advance;
            baseIndex = (int)Math.Floor(newTime);
            mu = newTime - baseIndex;

            // If next baseIndex is already too close to the end, stop and wait for more input next call.
            if (baseIndex + 1 >= _bufCount)
                break;
        }

        // Drop consumed samples from the buffer, keep last two for next interpolation.
        int consumed = Math.Min(Math.Max(0, baseIndex - 1), Math.Max(0, _bufCount - 3));
        if (consumed > 0)
        {
            _bufStart += consumed;
            _bufCount -= consumed;
            baseIndex -= consumed; // renormalize index into shortened logical buffer

            // occasional compaction (amortized O(1))
            if (_bufStart > 2048 && _bufStart > (_bufIQ.Length >> 3))
                Compact();
        }

        return outSymbols;
    }

    /// <summary>
    /// Convenience allocating overload: returns interleaved IQ symbols.
    /// </summary>
    public float[] Process(float[] incomingMfSamplesIQ)
    {
        if (incomingMfSamplesIQ == null) throw new ArgumentNullException(nameof(incomingMfSamplesIQ));
        if ((incomingMfSamplesIQ.Length & 1) != 0) throw new ArgumentException("Input must be interleaved IQ with even length.", nameof(incomingMfSamplesIQ));

        // A safe-ish upper bound: you can’t output more symbols than you have input samples.
        // (Usually far less: about inputSamples / samplesPerSymbol.)
        int maxSymbols = incomingMfSamplesIQ.Length >> 1;
        var tmp = new float[maxSymbols << 1];

        int n = Process(incomingMfSamplesIQ.AsSpan(), tmp.AsSpan());
        if (n == maxSymbols) return tmp;

        var y = new float[n << 1];
        Array.Copy(tmp, y, y.Length);
        return y;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CubicLagrange4(int n, double mu, out float outI, out float outQ)
    {
        // Uses samples at n-1, n, n+1, n+2
        int idxm1 = ((_bufStart + (n - 1)) << 1);
        int idx0 = ((_bufStart + n) << 1);
        int idx1 = idx0 + 2;
        int idx2 = idx0 + 4;

        float xm1I = _bufIQ[idxm1];
        float xm1Q = _bufIQ[idxm1 + 1];
        float x0I = _bufIQ[idx0];
        float x0Q = _bufIQ[idx0 + 1];
        float x1I = _bufIQ[idx1];
        float x1Q = _bufIQ[idx1 + 1];
        float x2I = _bufIQ[idx2];
        float x2Q = _bufIQ[idx2 + 1];

        float t = (float)mu;
        float tm1 = t - 1f;
        float tm2 = t - 2f;
        float tp1 = t + 1f;

        // Lagrange basis for points at {-1,0,1,2}
        float c_m1 = -(t * tm1 * tm2) * (1f / 6f);  // for x[n-1]
        float c_0 = (tp1 * tm1 * tm2) * (1f / 2f);  // for x[n]
        float c_1 = -(tp1 * t * tm2) * (1f / 2f);  // for x[n+1]
        float c_2 = (tp1 * t * tm1) * (1f / 6f);  // for x[n+2]

        outI = c_m1 * xm1I + c_0 * x0I + c_1 * x1I + c_2 * x2I;
        outQ = c_m1 * xm1Q + c_0 * x0Q + c_1 * x1Q + c_2 * x2Q;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void GetSignQpsk(float i, float q, out float di, out float dq)
    {
        di = (i >= 0f) ? 1f : -1f;
        dq = (q >= 0f) ? 1f : -1f;
    }

    private void Append(ReadOnlySpan<float> incomingIQ)
    {
        int incomingComplex = incomingIQ.Length >> 1; // complex samples
        if (incomingComplex == 0) return;

        EnsureCapacityForAppend(incomingComplex);

        int writeComplexIndex = _bufStart + _bufCount;
        int writeFloatIndex = writeComplexIndex << 1;

        incomingIQ.CopyTo(_bufIQ.AsSpan(writeFloatIndex));
        _bufCount += incomingComplex;
    }

    private void EnsureCapacityForAppend(int incomingComplex)
    {
        int neededComplex = _bufStart + _bufCount + incomingComplex;
        int neededFloats = neededComplex << 1;

        if (neededFloats <= _bufIQ.Length)
            return;

        // First try to compact in-place to free headroom
        if (_bufStart > 0)
        {
            Compact();
            neededComplex = _bufStart + _bufCount + incomingComplex;
            neededFloats = neededComplex << 1;
            if (neededFloats <= _bufIQ.Length)
                return;
        }

        // Grow
        int newLen = _bufIQ.Length;
        while (newLen < neededFloats) newLen <<= 1;

        var next = new float[newLen];
        int countFloats = _bufCount << 1;
        Array.Copy(_bufIQ, _bufStart << 1, next, 0, countFloats);
        _bufIQ = next;
        _bufStart = 0;
    }

    private void Compact()
    {
        if (_bufStart == 0) return;
        int countFloats = _bufCount << 1;
        Array.Copy(_bufIQ, _bufStart << 1, _bufIQ, 0, countFloats);
        _bufStart = 0;
    }
}