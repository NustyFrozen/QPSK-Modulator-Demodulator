using MathNet.Numerics;
using QPSK.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using static QPSK.Models.HelperFunctions;
namespace QPSK;
public class QPSKDeModulator(
    int SampleRate,
    int SymbolRate,
    float RrcAlpha = 0.9f,
    int rrcSpan = 6,
    double SymbolSyncBandwith = 0.0001,double CostasLoopBandwith = 120,double CFOLoopBandwith = 0.0001f,
    bool differentialEncoding = true,
    string? tsc = null)
{
    private readonly bool _differentialEncoding = differentialEncoding;
    private readonly string? _tsc = string.IsNullOrWhiteSpace(tsc) ? null : tsc;

    // Prebuffered scratch (grows as needed; avoids per-call allocations)
    private float[] _tmpFLL = Array.Empty<float>();
    private float[] _tmpRrc = Array.Empty<float>();
    private float[] _tmpSym = Array.Empty<float>();
    // RRC taps are real; represent them as complex taps with imag=0: [h0,0,h1,0,...]
    private readonly ComplexFIRFilter rrc = new ComplexFIRFilter(
        ToInterleavedIQRealTaps(
            RRCFilter.generateCoefficents(rrcSpan, RrcAlpha, SampleRate, SymbolRate)
        )
    );

    // (unchanged / not refactored here, since your code comments it out and it likely uses Complex[])
    FLLBandEdgeFilter fll = new FLLBandEdgeFilter(SampleRate / SymbolRate, RrcAlpha, 40, (float)CFOLoopBandwith);

    private readonly MuellerMuller symbolSync = setupSymbolSync(SampleRate, SymbolRate,SymbolSyncBandwith);
    private readonly IQ_Balancer iqBalance = new IQ_Balancer();
    private static MuellerMuller setupSymbolSync(double SampleRate,double SymbolRate, double SymbolSyncBandwith)
    {
        double zeta = 1.0 / Math.Sqrt(2.0);

        // Bn is normalized to symbol updates (cycles/symbol): Bn = B_Hz / SymbolRate
        double Bn = SymbolSyncBandwith;

        double wn = ((2.0 * Math.PI * Bn) / (zeta + 0.25) / zeta);

        double denom = 1.0 + 2.0 * zeta * wn + wn * wn;

        double kp = (4.0 * zeta * wn) / denom;
        double ki = (4.0 * wn * wn) / denom;

        return new MuellerMuller(SampleRate / (double)SymbolRate, kp, ki);

    }
    private readonly CostasLoopQpsk costas = new CostasLoopQpsk(SymbolRate, SymbolRate / CostasLoopBandwith);
    // --- Framer / circular buffer state ---
    private readonly byte[] FrameBuffer = new byte[300_000_000]; // 300MB
    private int _rbHead = 0;   // next write position
    private int _rbCount = 0;  // number of valid bytes in buffer (payload only)

    // Start/stop framing state
    private bool _inFrame = false;
    private string _searchCarryBits = "";    // small tail of bits to allow startMarker across calls
    private int _lockedBitOffset = -1;       // 0..7 once locked (alignment of marker)

    // Streaming bit->byte packer state (MSB-first)
    private byte _packByte = 0;
    private int _packBits = 0; // 0..7

    // --- Differential decode must persist across calls ---
    private bool _diffHavePrev = false;
    private float _prevDecI = 0f, _prevDecQ = 0f;
    private int RingCapacity => FrameBuffer.Length;

    private void RingClear()
    {
        _rbHead = 0;
        _rbCount = 0;
    }

    private int RingTailIndex()
    {
        int tail = _rbHead - _rbCount;
        if (tail < 0) tail += RingCapacity;
        return tail;
    }

    private byte RingGetAt(int indexFromStart)
    {
        int idx = RingTailIndex() + indexFromStart;
        idx %= RingCapacity;
        return FrameBuffer[idx];
    }

    private bool RingTryWriteByte(byte b)
    {
        if (_rbCount >= RingCapacity) return false; // refuse overflow (fast fail)
        FrameBuffer[_rbHead] = b;
        _rbHead++;
        if (_rbHead == RingCapacity) _rbHead = 0;
        _rbCount++;
        return true;
    }

    // Packs '0'/'1' chars into bytes MSB-first and writes them to the ring.
    // Returns number of bytes produced; returns -1 on overflow.
    private int AppendBitsToRing(string bits)
    {
        int produced = 0;

        foreach (char c in bits)
        {
            _packByte = (byte)((_packByte << 1) | (c == '1' ? 1 : 0));
            _packBits++;

            if (_packBits == 8)
            {
                if (!RingTryWriteByte(_packByte))
                    return -1;

                produced++;
                _packBits = 0;
                _packByte = 0;
            }
        }

        return produced;
    }

    // Search for pattern in ring buffer, starting from startIndexFromStart (0.._rbCount).
    // Returns index (0-based) or -1.
    private int RingIndexOf(ReadOnlySpan<byte> pattern, int startIndexFromStart)
    {
        if (pattern.Length == 0) return 0;
        if (_rbCount < pattern.Length) return -1;

        int last = _rbCount - pattern.Length;
        for (int i = Math.Max(0, startIndexFromStart); i <= last; i++)
        {
            bool ok = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (RingGetAt(i + j) != pattern[j]) { ok = false; break; }
            }
            if (ok) return i;
        }
        return -1;
    }

    private byte[] RingCopyOut(int length)
    {
        var dst = new byte[length];
        for (int i = 0; i < length; i++)
            dst[i] = RingGetAt(i);
        return dst;
    }

    private void ResetFramer()
    {
        _inFrame = false;
        _lockedBitOffset = -1;
        _searchCarryBits = "";
        RingClear();
        _packByte = 0;
        _packBits = 0;
    }

    public byte[] DeModulateBytes(
     ReadOnlySpan<float> samplesIQ,
     ReadOnlySpan<byte> startMarker,
     ReadOnlySpan<byte> endMarker)
    {
        if (startMarker.Length == 0) throw new ArgumentException("startMarker cannot be empty.", nameof(startMarker));
        if (endMarker.Length == 0) throw new ArgumentException("endMarker cannot be empty.", nameof(endMarker));

        // Demod bits from this chunk (your existing DSP pipeline)
        string rxBits = DeModulate(samplesIQ);
        if (string.IsNullOrEmpty(rxBits))
            return Array.Empty<byte>();

        // 1) Not currently in a frame: hunt for startMarker across all 8 bit offsets.
        if (!_inFrame)
        {
            string candidateBits = _searchCarryBits + rxBits;

            for (int bitOffset = 0; bitOffset < 8; bitOffset++)
            {
                // Pack bytes at this offset and search for startMarker
                byte[] bytes = BitPacker.BitsToBytes(candidateBits, bitOffset);
                if (bytes.Length == 0) continue;

                int s = BitPacker.IndexOf(bytes, startMarker);
                if (s < 0) continue;

                // Found startMarker at byte index s. Compute the bit position in candidateBits
                // where the marker ends (aligned to byte boundary in the packed stream).
                int markerEndBitPos = bitOffset + 8 * (s + startMarker.Length);
                if ((uint)markerEndBitPos > (uint)candidateBits.Length) continue;

                // Enter frame mode: payload starts immediately after the marker.
                _inFrame = true;
                _lockedBitOffset = bitOffset;

                RingClear();
                _packByte = 0;
                _packBits = 0;

                // Append remaining bits after marker into ring as payload bytes
                string afterMarkerBits = candidateBits.Substring(markerEndBitPos);
                int appended = AppendBitsToRing(afterMarkerBits);
                if (appended < 0)
                {
                    // Overflow -> drop and resync
                    ResetFramer();
                    return Array.Empty<byte>();
                }

                // Immediately check if endMarker already arrived
                int endAt = RingIndexOf(endMarker, Math.Max(0, _rbCount - (appended + endMarker.Length)));
                if (endAt >= 0)
                {
                    byte[] payload = RingCopyOut(endAt);
                    ResetFramer();
                    return payload;
                }

                // Frame not done yet
                return Array.Empty<byte>();
            }

            // No start found: keep only a small tail so startMarker can span calls.
            int keepBits = Math.Min(candidateBits.Length, (startMarker.Length * 8) + 7);
            _searchCarryBits = keepBits == 0 ? "" : candidateBits.Substring(candidateBits.Length - keepBits, keepBits);
            return Array.Empty<byte>();
        }

        // 2) Already inside a frame: just keep packing bytes and look for endMarker.
        {
            int appended = AppendBitsToRing(rxBits);
            if (appended < 0)
            {
                // Buffer full before endMarker -> drop frame and resync
                ResetFramer();
                return Array.Empty<byte>();
            }

            int scanFrom = Math.Max(0, _rbCount - (appended + endMarker.Length));
            int endAt = RingIndexOf(endMarker, scanFrom);
            if (endAt >= 0)
            {
                byte[] payload = RingCopyOut(endAt);
                ResetFramer();
                return payload;
            }

            return Array.Empty<byte>();
        }
    }


    public string DeModulateTextUtf8(
        ReadOnlySpan<float> samplesIQ,
        string startMarker = "\u0002", // STX
        string endMarker = "\u0003",   // ETX
        Encoding? encoding = null)
    {
        encoding ??= Encoding.UTF8;

        byte[] start = encoding.GetBytes(startMarker);
        byte[] end = encoding.GetBytes(endMarker);

        byte[] payload = DeModulateBytes(samplesIQ, start, end);
        if (payload.Length == 0) return string.Empty;

        return encoding.GetString(payload);
    }
    private static float[] ToInterleavedIQRealTaps(double[] realTaps)
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

    private static void EnsureCapacity(ref float[] arr, int neededFloats)
    {
        if (arr.Length < neededFloats)
            Array.Resize(ref arr, NextPow2(neededFloats));
    }

    private static int NextPow2(int x)
    {
        if (x <= 0) return 0;
        int p = 1;
        while (p < x) p <<= 1;
        return p;
    }

    private static void AppendDecisionBits(StringBuilder sb, float di, float dq)
    {
        // Mapping:
        // (-1,-1)->00, (-1,+1)->01, (+1,+1)->11, (+1,-1)->10
        if (di < 0f)
        {
            if (dq < 0f) { sb.Append('0'); sb.Append('0'); }
            else { sb.Append('0'); sb.Append('1'); }
        }
        else
        {
            if (dq >= 0f) { sb.Append('1'); sb.Append('1'); }
            else { sb.Append('1'); sb.Append('0'); }
        }
    }

    private static void AppendDeltaBits(StringBuilder sb, float deltaI, float deltaQ)
    {
        float ar = MathF.Abs(deltaI);
        float aq = MathF.Abs(deltaQ);

        if (ar >= aq)
        {
            // +1 -> 00, -1 -> 11
            if (deltaI >= 0f) { sb.Append('0'); sb.Append('0'); }
            else { sb.Append('1'); sb.Append('1'); }
        }
        else
        {
            // +j -> 01, -j -> 10
            if (deltaQ >= 0f) { sb.Append('0'); sb.Append('1'); }
            else { sb.Append('1'); sb.Append('0'); }
        }
    }

    public string DeModulate(float[] SamplesIQ)
    {
        if (SamplesIQ == null) throw new ArgumentNullException(nameof(SamplesIQ));
        return DeModulate(SamplesIQ.AsSpan());
    }

    public string DeModulate(ReadOnlySpan<float> SamplesIQ)
    {
        if ((SamplesIQ.Length & 1) != 0)
            throw new ArgumentException("Samples must be interleaved IQ with even length.", nameof(SamplesIQ));

        if (SamplesIQ.Length == 0)
            return "";

       
        // 1) RRC filter (streaming FIR)
        EnsureCapacity(ref _tmpFLL, SamplesIQ.Length);
        EnsureCapacity(ref _tmpRrc, SamplesIQ.Length);
        EnsureCapacity(ref _tmpSym, SamplesIQ.Length);

       // fll.Process(SamplesIQ, _tmpFLL);
        rrc.Filter(SamplesIQ, _tmpRrc.AsSpan(0, SamplesIQ.Length));

        // 2) Symbol sync (Mueller-Muller): produces <= input complex count symbols
        
        int nSymbols = symbolSync.Process(
            _tmpRrc.AsSpan(0, SamplesIQ.Length),
            _tmpSym.AsSpan(0, SamplesIQ.Length)
        );

        var bits = new StringBuilder(nSymbols * 2);

        // 3) Costas + decision + (optional) differential decode
        for (int k = 0; k < nSymbols; k++)
        {
            int s = k << 1;
            float symI = _tmpSym[s];
            float symQ = _tmpSym[s + 1];

            costas.Process(symI, symQ, out float rotI, out float rotQ);
            CostasLoopQpsk.GetSign(rotI, rotQ, out float decI, out float decQ);

            // distance^2 to decision
            float eI = rotI - decI;
            float eQ = rotQ - decQ;
            float dist2 = eI * eI + eQ * eQ;
            // if (dist2 >= threshold)
            //    continue;

            if (_differentialEncoding)
            {
                if (!_diffHavePrev)
                {
                    _prevDecI = decI; _prevDecQ = decQ;
                    _diffHavePrev = true;
                    continue;
                }

                float deltaI = decI * _prevDecI + decQ * _prevDecQ;
                float deltaQ = decQ * _prevDecI - decI * _prevDecQ;

                _prevDecI = decI; _prevDecQ = decQ;

                AppendDeltaBits(bits, deltaI, deltaQ);
            }
            else
            {
                AppendDecisionBits(bits, decI, decQ);
            }
        }

        var rx = bits.ToString();

        // If TSC provided: align by exact match, return payload only
        if (_tsc != null)
        {
            int idx = rx.IndexOf(_tsc, StringComparison.Ordinal);
            if (idx < 0) return "";

            int start = idx + _tsc.Length;
            if (start > rx.Length) return "";

            return rx.Substring(start);
        }

        return rx;
    }

    public float[] deModulateConstellation(ReadOnlySpan<float> SamplesIQ)
    {
        if (SamplesIQ == null) throw new ArgumentNullException(nameof(SamplesIQ));
        if ((SamplesIQ.Length & 1) != 0) throw new ArgumentException("Samples must be interleaved IQ with even length.", nameof(SamplesIQ));

        // RRC
        EnsureCapacity(ref _tmpRrc, SamplesIQ.Length);
        EnsureCapacity(ref _tmpFLL, SamplesIQ.Length);
        //fll.Process(SamplesIQ, _tmpFLL);
        rrc.Filter(SamplesIQ, _tmpRrc.AsSpan(0, SamplesIQ.Length));

        // Symbol sync
        EnsureCapacity(ref _tmpSym, SamplesIQ.Length);
        int nSymbols = symbolSync.Process(
            _tmpRrc.AsSpan(0, SamplesIQ.Length),
            _tmpSym.AsSpan(0, SamplesIQ.Length)
        );

        // Output rotated constellation points (interleaved IQ)
        var y = new float[nSymbols << 1];
        for (int k = 0; k < nSymbols; k++)
        {
            int s = k << 1;
            costas.Process(_tmpSym[s], _tmpSym[s + 1], out float rotI, out float rotQ);
            y[s] = rotI;
            y[s + 1] = rotQ;
        }
        return y;
    }
}

