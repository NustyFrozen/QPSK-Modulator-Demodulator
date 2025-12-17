using MathNet.Numerics;
using QPSK.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using static QPSK.Models.HelperFunctions;
namespace QPSK;
/// <summary>
/// 
/// </summary>
/// <param name="SampleRate">The sampling rate of your transciever</param>
/// <param name="SymbolRate">The symbol rate which correspond baud rate and cannot be higher than SampleRate/2</param>
/// <param name="RrcAlpha">The Root raised cosine pulse Shaping Alpha</param>
/// <param name="rrcSpan">the span of the RRC Root raise cosine filter common 4-10</param>
/// <param name="differentialEncoding">use differentialEncoding to defeat phase ambiguity</param>
public class QPSKModulator(
    int SampleRate,
    int SymbolRate,
    double RrcAlpha = 0.9,
    int rrcSpan = 6,
    bool differentialEncoding = true,
    string? tsc = null)
{
    private readonly bool _differentialEncoding = differentialEncoding;
    private readonly string? _tsc = string.IsNullOrWhiteSpace(tsc) ? null : tsc;

    private readonly double[] rrcCoeff =
        RRCFilter.generateCoefficents(rrcSpan, RrcAlpha, SampleRate, SymbolRate);

    public double[] getCoeef() => rrcCoeff;

    public long baudRate = 2L * SymbolRate / 8L;

    private const float InvSqrt2 = 0.7071067811865475f; // 1/sqrt(2)

    // Use ComplexFIRFilter with imag=0 taps for IQ pulse shaping
    private readonly ComplexFIRFilter _rrcTx = new ComplexFIRFilter(
        ToInterleavedIQRealTapsStatic(
            RRCFilter.generateCoefficents(rrcSpan, RrcAlpha, SampleRate, SymbolRate)));

    private static float[] ToInterleavedIQRealTapsStatic(double[] realTaps)
    {
        var tapsIQ = new float[realTaps.Length << 1];
        for (int i = 0; i < realTaps.Length; i++)
        {
            int t = i << 1;
            tapsIQ[t] = (float)realTaps[i];
            tapsIQ[t + 1] = 0f;
        }
        return tapsIQ;
    }
    public float[] ModulateBytes(
    ReadOnlySpan<byte> payload,
    ReadOnlySpan<byte> startMarker,
    ReadOnlySpan<byte> endMarker,
    bool pulseShaping = true)
    {
        if (startMarker.Length == 0) throw new ArgumentException("startMarker cannot be empty.", nameof(startMarker));
        if (endMarker.Length == 0) throw new ArgumentException("endMarker cannot be empty.", nameof(endMarker));

        // Frame: START + payload + END
        byte[] framed = new byte[startMarker.Length + payload.Length + endMarker.Length];
        startMarker.CopyTo(framed.AsSpan(0, startMarker.Length));
        payload.CopyTo(framed.AsSpan(startMarker.Length, payload.Length));
        endMarker.CopyTo(framed.AsSpan(startMarker.Length + payload.Length, endMarker.Length));

        // Convert to bitstring and reuse existing Modulate(bits)
        string bits = BitPacker.BytesToBitString(framed);
        return Modulate(bits, pulseShaping);
    }

    public float[] ModulateTextUtf8(
        string text,
        string startMarker = "\u0002", // STX
        string endMarker = "\u0003",   // ETX
        bool pulseShaping = true,
        Encoding? encoding = null)
    {
        if (text == null) throw new ArgumentNullException(nameof(text));
        encoding ??= Encoding.UTF8;

        byte[] payload = encoding.GetBytes(text);
        byte[] start = encoding.GetBytes(startMarker);
        byte[] end = encoding.GetBytes(endMarker);

        return ModulateBytes(payload, start, end, pulseShaping);
    }

    // Dibit -> delta rotation (unit phasor on axes)
    private static void DibitToDelta(int b0, int b1, out float dI, out float dQ)
    {
        // 00 -> +1, 01 -> +j, 11 -> -1, 10 -> -j
        if (b0 == 0 && b1 == 0) { dI = 1f; dQ = 0f; return; }
        if (b0 == 0 && b1 == 1) { dI = 0f; dQ = 1f; return; }
        if (b0 == 1 && b1 == 1) { dI = -1f; dQ = 0f; return; }

        // b0==1 && b1==0
        dI = 0f;
        dQ = -1f;
    }

    public float[] Modulate(string data, bool pulseShaping = true)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));

        // prepend TSC if provided
        if (_tsc != null) data = _tsc + data;

        // must have pairs of bits
        int nDibits = data.Length >> 1;
        if (nDibits == 0) return Array.Empty<float>();

        int sps = SampleRate / SymbolRate;
        if (sps <= 0)
            throw new ArgumentOutOfRangeException(nameof(SampleRate), "SampleRate/SymbolRate must be >= 1.");

        int delay = (rrcCoeff.Length - 1) / 2;     // complex-sample delay
        int baseComplex = delay + (nDibits * sps); // matches old "no pulse shaping" behavior
        int totalComplex = pulseShaping ? (baseComplex + delay) : baseComplex;

        var up = new float[totalComplex << 1]; // interleaved IQ, zero-initialized

        // Differential reference (one per frame)
        float prevI = InvSqrt2, prevQ = InvSqrt2;

        int writeComplex = delay;
        for (int d = 0; d < nDibits; d++)
        {
            int bi = data[(d << 1)] - '0';
            int bq = data[(d << 1) + 1] - '0';

            float symI, symQ;

            if (_differentialEncoding)
            {
                DibitToDelta(bi, bq, out float dI, out float dQ);

                // sym = prev * delta
                // (a+jb)*(c+jd): real = ac - bd, imag = ad + bc
                symI = prevI * dI - prevQ * dQ;
                symQ = prevI * dQ + prevQ * dI;

                prevI = symI;
                prevQ = symQ;
            }
            else
            {
                symI = (bi == 0 ? -InvSqrt2 : InvSqrt2);
                symQ = (bq == 0 ? -InvSqrt2 : InvSqrt2);
            }

            int w = writeComplex << 1;
            up[w] = symI;
            up[w + 1] = symQ;

            // leave (sps-1) zeros in between (already zero-initialized)
            writeComplex += sps;
        }

        if (!pulseShaping)
            return up; // includes leading delay, no trailing delay (matches original behavior)

       
        return _rrcTx.fftFilter(up);
    }
}