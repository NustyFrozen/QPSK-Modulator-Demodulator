using MathNet.Numerics;
using QPSK.Models;
using NetMQ;
using NetMQ.Sockets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using QPSK;
namespace TestBench.Simulated
{
    public static class testFullDemodChain
    {
        public static void RunTest(PublisherSocket pub)
        {
            int sampleRate = 10_000_000, SymbolRate = sampleRate/30;
            const int samplesPerFrame = 4096;     // must match "items per message" in GRC
            const int floatsPerSample = 2;        // I and Q
            const int bytesPerFloat = 4;
            
            ComplexFIRFilter rrc = new ComplexFIRFilter(RRCFilter.generateCoefficents(11, .9, sampleRate, SymbolRate).Select(x => (float)x).ToArray());
            FLLBandEdgeFilter fll = new FLLBandEdgeFilter(sampleRate / SymbolRate, .9f, 10, 0.1f);
            double R = SymbolRate;            // symbols per second, e.g. 2e6
            double Bn_frac = 0.000000002;           // 1% of symbol rate loop bandwidth
            double zeta = 1.0 / Math.Sqrt(2.0);
            double Kd = 1.0;            // assume M&M error is normalized

            double omega_frac = 2.0 * Math.PI * Bn_frac;   // rad/sample (per symbol)

            double Kp = 2.0 * zeta * omega_frac / Kd;
            double Ki = omega_frac * omega_frac / Kd;

            MuellerMuller symbolSync = new MuellerMuller(
                sampleRate / SymbolRate,  // samples per symbol
                Kp,
                Ki
            );

            QPSKModulator modulator = new QPSKModulator(sampleRate, SymbolRate,.9,10);
            NCO transmitter_unstable_NCO = new NCO(935e6, sampleRate, 20,120);
            NCO receiver_unstable_NCO = new NCO(935e6, sampleRate, 10,30);

            CostasLoopQpsk costas = new CostasLoopQpsk(SymbolRate, SymbolRate / 10);
            var rand = new Random();
            int pos = 0;
            bool generateNewSignal = false;
            List<float> mfBlock = new List<float>();
        generateNewSignal:
            generateNewSignal = false;
            string data = string.Empty;
            for (int i = 0; i < 4096; i++)
            {
               var sym = rand.Next(0, 2).ToString(); //01100011...
                data += sym;
            }
            var modulatedSignal = modulator.Modulate(data);
            var noise = NoiseGenerator.GenerateIqNoise(-90, samplesPerFrame * floatsPerSample);
            while (true)
            {
                float[] SignalPreSync = new float[samplesPerFrame * floatsPerSample];


                for (int n = 0; n < samplesPerFrame; n++)
                {
                    if (pos == modulatedSignal.Length)
                    {
                        generateNewSignal = true;
                        pos = 0;
                    }
                    //two lo with drifts
                    var sample_withError = (modulatedSignal[pos++] +new Complex(noise[2 * n], noise[2 * n + 1])) * transmitter_unstable_NCO.NextSample() * receiver_unstable_NCO.NextSample().Conjugate();
                    float[] sample = new float[2];
                    fll.Process(new float[]{(float)sample_withError.Real,(float)sample_withError.Imaginary}, sample);
                    SignalPreSync[2 * n] = sample[0]; // I
                    SignalPreSync[2 * n + 1] = sample[1]; // Q

                    // matched-filtered sample
                    var mfSample = new float[2];
                    rrc.Filter(sample,mfSample);
                    mfBlock.AddRange(mfSample);
                }
                 
                var decided = symbolSync.Process(mfBlock.ToArray());
                mfBlock.Clear(); // M&M keeps its own internal buffer if needed
                // Convert float[] to byte[] (little-endian)
                byte[] frame = new byte[SignalPreSync.Length * bytesPerFloat];
                Buffer.BlockCopy(SignalPreSync, 0, frame, 0, frame.Length);


                // Send single ZMQ frame without topic
                pub.SendMoreFrame("baseband").SendFrame(frame);

                if (decided.Length > 0)
                {
                    var results = decided;
                    byte[] frame_2 = new byte[results.Length * bytesPerFloat];
                    
                    Buffer.BlockCopy(results, 0, frame_2, 0, frame_2.Length);
                    pub.SendMoreFrame("baseband_PostSymbolSync").SendFrame(frame_2);

                    
                    var results2 = costas.Process(decided);
                    byte[] frame_3 = new byte[results2.Length * bytesPerFloat];

                    Buffer.BlockCopy(results2, 0, frame_3, 0, frame_3.Length);
                    pub.SendMoreFrame("baseband_PostSymbolSyncPostCostas").SendFrame(frame_3);
                }

                if (generateNewSignal)
                    goto generateNewSignal;

            }
        }
    }
}
