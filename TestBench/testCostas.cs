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
    public class testCostas
    {
        public static void RunTest(PublisherSocket pub)
        {
            int sampleRate = 1_000_000, SymbolRate = 1000;
            const int samplesPerFrame = 4096;     // must match "items per message" in GRC
            const int floatsPerSample = 2;        // I and Q
            const int bytesPerFloat = 4;
            QPSKModulator modulator = new QPSKModulator(sampleRate, SymbolRate);
            NCO transmitter_unstable_NCO = new NCO(100e6, sampleRate,1,1);
            NCO receiver_unstable_NCO = new NCO(100e6, sampleRate,1);
            CostasLoopQpsk costas = new CostasLoopQpsk(sampleRate,2*Math.PI*.1, 10);
            var rand = new Random();
            int pos = 0;
            bool generateNewSignal = false;
        generateNewSignal:
            generateNewSignal = false;
            string data = string.Empty;
            for (int i = 0; i < 4096; i++)
                data += rand.Next(0,2).ToString(); //01100011...
            var modulatedSignal = modulator.Modulate(data,false);
            while (true)
            {
                
                float[] SignalPrePLL = new float[samplesPerFrame * floatsPerSample];
                float[] SignalPostPLL = new float[samplesPerFrame * floatsPerSample];

                for (int n = 0; n < samplesPerFrame; n++)
                {
                    if (pos == modulatedSignal.Length)
                    {
                        generateNewSignal = true;
                        pos = 0;
                    }
                    //two lo with drifts
                    var sample = modulatedSignal[pos++] * transmitter_unstable_NCO.NextSample() * receiver_unstable_NCO.NextSample().Conjugate();
                    SignalPrePLL[2 * n] = (float)sample.Real; // I
                    SignalPrePLL[2 * n + 1] = (float)sample.Imaginary; // Q

                    var sample_withPLL = costas.Process(sample);
                    SignalPostPLL[2 * n] = (float)sample_withPLL.Real;
                    SignalPostPLL[2 * n + 1] = (float)sample_withPLL.Imaginary;
                }

                // Convert float[] to byte[] (little-endian)
                byte[] frame = new byte[SignalPrePLL.Length * bytesPerFloat];
                Buffer.BlockCopy(SignalPrePLL, 0, frame, 0, frame.Length);

                byte[] frame_2 = new byte[SignalPostPLL.Length * bytesPerFloat];
                Buffer.BlockCopy(SignalPostPLL, 0, frame_2, 0, frame_2.Length);
                // Send single ZMQ frame without topic
                pub.SendMoreFrame("baseband").SendFrame(frame);
                pub.SendMoreFrame("baseband_PostPLL").SendFrame(frame_2);
                if (generateNewSignal)
                    goto generateNewSignal;

            }
        }
    }
}
