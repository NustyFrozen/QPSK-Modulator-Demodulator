using MathNet.Numerics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace QPSK.Models;
    /// <summary>
    /// FLL band-edge filter (port of GNU Radio fll_band_edge_cc_impl).
    /// </summary>
    public class FLLBandEdgeFilter
    {
        const float TWO_PI = 2.0f * (float)Math.PI;

        // Design parameters
        public float sps;         // samples per symbol
        public float rolloff;     // rolloff [0,1]
        public int   filterSize;  // number of taps
        public float bandwidth;   // loop bandwidth

        // Loop state
        public float phase;
        public float freq;

        // Loop gains and limits
        float alpha;   // kept for completeness, but 0 for FLL
        float beta;
        float maxFreq;
        float minFreq;

        // Filters
        Complex[] tapsLower;
        Complex[] tapsUpper;
        ComplexFIRFilter filterLower;
        ComplexFIRFilter filterUpper;

        public FLLBandEdgeFilter(float sps, float rolloff, int filterSize, float bandwidth)
        {
            if (sps <= 0.0f)       throw new ArgumentOutOfRangeException(nameof(sps), "sps must be > 0.");
            if (rolloff < 0 || rolloff > 1.0f)
                                  throw new ArgumentOutOfRangeException(nameof(rolloff), "rolloff must be in [0,1].");
            if (filterSize <= 0)   throw new ArgumentOutOfRangeException(nameof(filterSize), "filterSize must be > 0.");
            if (bandwidth <= 0.0f) throw new ArgumentOutOfRangeException(nameof(bandwidth), "bandwidth must be > 0.");

            this.sps        = sps;
            this.rolloff    = rolloff;
            this.filterSize = filterSize;
            this.bandwidth  = bandwidth;

            phase = 0.0f;
            freq  = 0.0f;

            alpha   = 0.0f;                                   // FLL: no direct phase update
        beta = 4.0f * bandwidth / sps;

        maxFreq = TWO_PI * (2.0f / sps);
            minFreq = -maxFreq;

            DesignFilter();
        }

        /// <summary>
        /// process the FLL
        /// </summary>
        public Complex[] Process(Complex[] input)
        {
            if (input == null)  throw new ArgumentNullException(nameof(input));
            Complex[] output = new Complex[input.Length];
            var output_asspan = output.AsSpan();
            for (int i = 0; i < input.Length; i++)
            {
                // NCO
                Complex nco = Expj(phase);
                output_asspan[i] = input[i] * nco;

            Complex outUpper = filterUpper.Filter(output_asspan[i]);
            Complex outLower = filterLower.Filter(output_asspan[i]);


            float powUpper = (float)(outUpper.Real * outUpper.Real + outUpper.Imaginary * outUpper.Imaginary);
                float powLower = (float)(outLower.Real * outLower.Real + outLower.Imaginary * outLower.Imaginary);

                float error = powLower - powUpper;

                //advanced the loop
                freq  += beta * error;
                phase += freq + this.alpha * error;
                
                //+- 360 degrees
                WrapPhase();
                
                LimitFrequency();
            }

            return output;
        }
    public Complex Process(Complex input)
    {
        if (input == null) throw new ArgumentNullException(nameof(input));
           
            // NCO
            Complex nco = Expj(phase);
        Complex output = input * nco;
        Complex outUpper = filterUpper.Filter(output);
            Complex outLower = filterLower.Filter(output);


            float powUpper = (float)(outUpper.Real * outUpper.Real + outUpper.Imaginary * outUpper.Imaginary);
            float powLower = (float)(outLower.Real * outLower.Real + outLower.Imaginary * outLower.Imaginary);

            float error = powLower - powUpper;

            //advanced the loop
            freq += beta * error;
            phase += freq + this.alpha * error;

            //+- 360 degrees
            WrapPhase();

            LimitFrequency();
        

        return output;
    }


    // ----------------- internals -----------------

    void DesignFilter()
    {
        int numTaps = filterSize;
        int mid = (numTaps - 1) / 2;

        var bbTaps = new float[numTaps];
        float sum = 0.0f;

        // ---- BASEBAND BAND-EDGE TAPS (GNU RADIO EXACT FORM) ----
        for (int i = 0; i < numTaps; i++)
        {
            float k = (i - mid) / (2.0f * sps);
            float pos = rolloff * k;

            float tap = Sinc(pos - 0.5f) + Sinc(pos + 0.5f);
            sum += tap;
            bbTaps[i] = tap;
        }

        // ---- NORMALIZE BY SUM (NOT POWER!) ----
        for (int i = 0; i < numTaps; i++)
            bbTaps[i] /= sum;

        tapsLower = new Complex[numTaps];
        tapsUpper = new Complex[numTaps];

        // ---- SHIFT TO ±(1+rolloff)/2 SYMBOL RATE ----
        for (int i = 0; i < numTaps; i++)
        {
            float k = (i - mid) / (2.0f * sps);
            float angle = -TWO_PI * (1.0f + rolloff) * k;

            Complex w = Expj(angle);

            tapsLower[i] = bbTaps[i] * w;
            tapsUpper[i] = Complex.Conjugate(tapsLower[i]);
        }

        filterLower = new ComplexFIRFilter(tapsLower);
        filterUpper = new ComplexFIRFilter(tapsUpper);
    }


    void WrapPhase()
        {
            if (phase > TWO_PI || phase < -TWO_PI)
                phase = (float)Math.IEEERemainder(phase, TWO_PI);
        }

        void LimitFrequency()
        {
            if (freq > maxFreq)      freq = maxFreq;
            else if (freq < minFreq) freq = minFreq;
        }

        static float Sinc(float x)
        {
            if (x == 0.0f) return 1.0f;
            float arg = (float)Math.PI * x;
            return MathF.Sin(arg) / arg;
        }

        static Complex Expj(float angle)
        {
            double c = Math.Cos(angle);
            double s = Math.Sin(angle);
            return new Complex(c, s);
        }

        
    }
