using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace TestBench
{
    public static class NoiseGenerator
    {
        private static readonly Random _rng = new Random();

        // Generates complex IQ noise normalized to [-1, 1]
        // noiseFloorDbm: e.g. -60, -80, -100
        // sampleCount: number of COMPLEX samples (IQ pairs)
        public static float[] GenerateIqNoise(float noiseFloorDbm, int sampleCount)
        {
            // Convert dB to linear RMS amplitude (0 dBFS = 1.0)
            float linearRms = (float)Math.Pow(10.0, noiseFloorDbm / 20.0);

            float[] iq = new float[sampleCount * 2]; // I,Q interleaved

            for (int n = 0; n < sampleCount; n++)
            {
                // Box-Muller transform for Gaussian noise
                double u1 = 1.0 - _rng.NextDouble();
                double u2 = 1.0 - _rng.NextDouble();

                double mag = Math.Sqrt(-2.0 * Math.Log(u1)) * linearRms;
                double phase = 2.0 * Math.PI * u2;

                float i = (float)(mag * Math.Cos(phase));
                float q = (float)(mag * Math.Sin(phase));

                // Optional safety clamp (rarely needed)
                i = Math.Clamp(i, -1.0f, 1.0f);
                q = Math.Clamp(q, -1.0f, 1.0f);

                iq[2 * n] = i; // I
                iq[2 * n + 1] = q; // Q
            }

            return iq;
        }
    }
    public static class helperFunctionsTestbench
    {
        public static float[] toFloatInterleaved(this Complex[] input)
        {
            float[] results = new float[input.Length*2];
            for (int i = 0; i < input.Length*2; i+=2)
            {
                results[i] = (float)input[i/2].Real;
                results[i + 1] = (float)input[i/2].Imaginary;
            }
            return results;
        }
    }
}
