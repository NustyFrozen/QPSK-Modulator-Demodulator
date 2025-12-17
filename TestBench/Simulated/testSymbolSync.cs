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
    public static class testSymbolSync
    {
        public static void RunTest(PublisherSocket pub)
        {
            int sampleRate = 10_000_000, SymbolRate = 4_000_000;//1MB
            const int samplesPerFrame = 4096;     // must match "items per message" in GRC
            const int floatsPerSample = 2;        // I and Q
            const int bytesPerFloat = 4;
            ComplexFIRFilter rrc = new ComplexFIRFilter(RRCFilter.generateCoefficents(6, .7, sampleRate, SymbolRate).Select(x=>(float)(x)).ToArray());
            MuellerMuller symbolSync = new MuellerMuller(sampleRate / SymbolRate, 1e-3, 1e-5);
            QPSKModulator modulator = new QPSKModulator(sampleRate, SymbolRate);
            NCO transmitter_unstable_NCO = new NCO(100e6, sampleRate, 1, 21);
            NCO receiver_unstable_NCO = new NCO(100e6, sampleRate, 1);
            var rand = new Random();
            int pos = 0;
            bool generateNewSignal = false;
            List<float> mfBlock = new List<float>();
        generateNewSignal:
            generateNewSignal = false;
            string data = string.Empty;
            for (int i = 0; i < 4096; i++)
                data += rand.Next(0, 2).ToString(); //01100011...
            var modulatedSignal = modulator.Modulate(data).toComplex();
            
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
                    var sample = modulatedSignal[pos++]* transmitter_unstable_NCO.NextSample() * receiver_unstable_NCO.NextSample().Conjugate();
                    SignalPreSync[2 * n] = (float)sample.Real; // I
                    SignalPreSync[2 * n + 1] = (float)sample.Imaginary; // Q

                    // matched-filtered sample
                    var mfSample = new float[2];
                    rrc.Filter(new float[]{(float)sample .Real, (float)sample.Imaginary},mfSample);
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
                }

                if (generateNewSignal)
                    goto generateNewSignal;

            }
        }
    }
}
