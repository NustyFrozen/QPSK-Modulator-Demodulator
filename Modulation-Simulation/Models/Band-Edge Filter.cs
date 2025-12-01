using MathNet.Numerics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Modulation_Simulation.Models;
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
        FIRFilter filterLower;
        FIRFilter filterUpper;

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
            beta    = TWO_PI * 4.0f * bandwidth / sps;        // matches ctor in C++ code
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

                // Band-edge filters
                Complex outUpper = filterLower.Filter(output_asspan[i]); // note: lower filter -> upper edge
                Complex outLower = filterUpper.Filter(output_asspan[i]); // and vice versa (matches C++)
                
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

       

        // ----------------- internals -----------------

        void DesignFilter()
        {
            int M = (int)MathF.Round(filterSize / sps);
            float power = 0.0f;

            var bbTaps = new float[filterSize];
            float halfSpsInv = 2.0f / sps;

            // Baseband taps: sum of two sincs
            for (int i = 0; i < filterSize; i++)
            {
                float k = -M + i * halfSpsInv;
                float pos = rolloff * k;
                float tap = Sinc(pos - 0.5f) + Sinc(pos + 0.5f);
                power += tap * tap;
                bbTaps[i] = tap;
            }

            tapsLower = new Complex[filterSize];
            tapsUpper = new Complex[filterSize];

            int N = (bbTaps.Length - 1) / 2;
            float invPower = 1.0f / power;
            float invTwiceSps = 0.5f / sps;

            for (int i = 0; i < filterSize; i++)
            {
                float tap = bbTaps[i] * invPower;
                float k = (i - N) * invTwiceSps;
                int index = filterSize - i - 1;

                float angle = -TWO_PI * (1.0f + rolloff) * k;
                Complex w = Expj(angle);

                tapsLower[index] = tap * w;
                tapsUpper[index] = Complex.Conjugate(tapsLower[index]);
            }

            filterUpper = new FIRFilter(tapsUpper);
            filterLower = new FIRFilter(tapsLower);
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
