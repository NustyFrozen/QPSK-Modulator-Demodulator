using MathNet.Numerics;
using Modulation_Simulation.Models;
using NetMQ;
using NetMQ.Sockets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace TestBench
{
    public static class testFullDemodChain
    {
        public static void RunTest(PublisherSocket pub)
        {
            int sampleRate = 10_000_000, SymbolRate = sampleRate/20;
            const int samplesPerFrame = 4096;     // must match "items per message" in GRC
            const int floatsPerSample = 2;        // I and Q
            const int bytesPerFloat = 4;
            ComplexFIRFilter rrc = new ComplexFIRFilter(RRCFilter.generateCoefficents(6, .9, sampleRate, SymbolRate).Select(x => new Complex(x, 0)).ToArray());
            MuellerMuller symbolSync = new MuellerMuller(
    sampleRate / SymbolRate,  // = 8
    0.0097,                    // Kp (was 0.013)
   .000006                    // Ki (was 1.7e-4)
);

            QPSKModulator modulator = new QPSKModulator(sampleRate, SymbolRate,.9);
            NCO transmitter_unstable_NCO = new NCO(100e6, sampleRate, 1);
            NCO receiver_unstable_NCO = new NCO(100e6, sampleRate, 1);

            CostasLoopQpsk costas = new CostasLoopQpsk(SymbolRate, SymbolRate / 40);
            var rand = new Random();
            int pos = 0;
            bool generateNewSignal = false;
            List<Complex> mfBlock = new List<Complex>();
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
                    var sample = modulatedSignal[pos++]* transmitter_unstable_NCO.NextSample() * receiver_unstable_NCO.NextSample().Conjugate() + new Complex(noise[2 * n], noise[2 * n + 1]);
                    SignalPreSync[2 * n] = (float)sample.Real; // I
                    SignalPreSync[2 * n + 1] = (float)sample.Imaginary ; // Q

                    // matched-filtered sample
                    var mfSample = rrc.Filter(sample);
                    mfBlock.Add(mfSample);
                }
                 
                var decided = symbolSync.Process(mfBlock.ToArray());
                mfBlock.Clear(); // M&M keeps its own internal buffer if needed
                // Convert float[] to byte[] (little-endian)
                byte[] frame = new byte[SignalPreSync.Length * bytesPerFloat];
                Buffer.BlockCopy(SignalPreSync, 0, frame, 0, frame.Length);


                // Send single ZMQ frame without topic
                pub.SendMoreFrame("baseband").SendFrame(frame);

                if (decided.Count > 0)
                {
                    var results = decided.ToArray().toFloatInterleaved();
                    byte[] frame_2 = new byte[results.Length * bytesPerFloat];
                    
                    Buffer.BlockCopy(results, 0, frame_2, 0, frame_2.Length);
                    pub.SendMoreFrame("baseband_PostSymbolSync").SendFrame(frame_2);


                    var results2 = decided.Select(x => costas.Process(x)).ToArray().toFloatInterleaved();
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
