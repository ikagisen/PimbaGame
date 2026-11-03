using System;
using System.Collections.Generic;
using UnityEngine;

namespace PimbaStage
{
    /// <summary>
    /// Generates an original, royalty-free "arraial" style tune so the game is playable before you add real songs:
    /// bass on the beat, accordion chords on the off-beat ("oom-pah"), drums, and an accordion melody.
    /// The melody notes are returned so the demo chart can follow the tune.
    /// </summary>
    public static class DemoTrack
    {
        public struct MelodyNote
        {
            public double beat;
            public int pitch;
            public double lengthBeats;
        }

        private const int Rate = 44100;
        private const int TableSize = 2048;

        private static float[] reedTable, bassTable;
        private static AudioClip cachedClip;
        private static List<MelodyNote> cachedMelody;
        private static float cachedBpm;
        private static int cachedSeed;

        // Chords as MIDI notes: C, G7, F.  16-bar progression, very typical for the genre.
        private static readonly int[][] Chords = { new[] { 60, 64, 67 }, new[] { 55, 59, 62, 65 }, new[] { 53, 57, 60 } };
        private static readonly int[] Progression = { 0, 0, 1, 1, 1, 1, 0, 0, 2, 2, 0, 0, 1, 1, 0, 0 };
        private static readonly int[] Scale = { 0, 2, 4, 5, 7, 9, 11 };

        public static AudioClip Create(float bpm, int seed, out List<MelodyNote> melody)
        {
            if (cachedClip != null && Mathf.Approximately(cachedBpm, bpm) && cachedSeed == seed)
            {
                melody = cachedMelody;
                return cachedClip;
            }

            BuildTables();
            double spb = 60.0 / Math.Max(60f, bpm);
            const int introBars = 2, mainBars = 48, outroBars = 2;
            int totalBars = introBars + mainBars + outroBars;
            int length = (int)((totalBars * 4 + 1) * spb * Rate);
            var buf = new float[length];
            var rng = new System.Random(seed);
            var noise = new System.Random(seed * 31 + 5);
            melody = new List<MelodyNote>();

            int previousPitch = 72;
            for (int bar = 0; bar < totalBars; bar++)
            {
                bool outro = bar >= introBars + mainBars;
                int[] chord = Chords[outro ? 0 : Progression[Math.Max(0, bar - introBars) % 16]];

                if (outro)
                {
                    if (bar == introBars + mainBars)
                    {
                        double t0 = bar * 4 * spb;
                        AddKick(buf, t0, 0.9f);
                        AddTone(buf, bassTable, t0, Midi(chord[0] - 24), spb * 3, 0.32f, 0.004, 0.25, 1.2);
                        foreach (int n in chord) AddReed(buf, t0, Midi(n), spb * 3, 0.1f);
                        AddReed(buf, t0, Midi(72), spb * 3, 0.2f);
                    }
                    continue;
                }

                for (int beat = 0; beat < 4; beat++)
                {
                    double t = (bar * 4 + beat) * spb;

                    // Drums
                    if (beat % 2 == 0) AddKick(buf, t, 0.85f);
                    else AddSnare(buf, t, 0.3f, noise);
                    AddHat(buf, t, 0.09f, noise);
                    AddHat(buf, t + spb * 0.5, 0.06f, noise);

                    // Bass: root on 1 and 3, fifth on 2 and 4
                    int bassNote = (beat % 2 == 0 ? chord[0] : chord[0] + 7) - 24;
                    AddTone(buf, bassTable, t, Midi(bassNote), spb * 0.85, 0.3f, 0.004, 0.05, 2.5);

                    // Accordion "pah" on the off-beat
                    if (bar > 0)
                        foreach (int n in chord) AddReed(buf, t + spb * 0.5, Midi(n), spb * 0.32, 0.075f);
                }

                if (bar >= introBars)
                    WriteMelodyBar(buf, bar, chord, spb, rng, melody, ref previousPitch, (bar - introBars) % 4 == 3);
            }

            // Normalise
            float peak = 0.001f;
            for (int i = 0; i < buf.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(buf[i]));
            float gain = 0.92f / peak;
            for (int i = 0; i < buf.Length; i++) buf[i] *= gain;

            var clip = AudioClip.Create("Pimba Demo", length, 1, Rate, false);
            clip.SetData(buf, 0);

            cachedClip = clip;
            cachedMelody = melody;
            cachedBpm = bpm;
            cachedSeed = seed;
            return clip;
        }

        private static void WriteMelodyBar(float[] buf, int bar, int[] chord, double spb, System.Random rng,
            List<MelodyNote> melody, ref int previousPitch, bool phraseEnd)
        {
            for (int beat = 0; beat < 4; beat++)
            {
                double b = bar * 4 + beat;

                if (phraseEnd && beat == 2)
                {
                    int end = Nearest(previousPitch, chord, new[] { chord[0] % 12 });
                    AddMelody(buf, melody, b, end, 2.0, spb);
                    previousPitch = end;
                    return;
                }

                double roll = rng.NextDouble();
                if (roll < 0.10) continue; // rest

                if (roll < 0.20 && beat % 2 == 0 && !(phraseEnd && beat == 0))
                {
                    int p = PickPitch(previousPitch, chord, true, rng);
                    AddMelody(buf, melody, b, p, 2.0, spb);
                    previousPitch = p;
                    beat++;
                }
                else if (roll < 0.47)
                {
                    int p1 = PickPitch(previousPitch, chord, true, rng);
                    AddMelody(buf, melody, b, p1, 0.5, spb);
                    int p2 = PickPitch(p1, chord, false, rng);
                    AddMelody(buf, melody, b + 0.5, p2, 0.5, spb);
                    previousPitch = p2;
                }
                else
                {
                    int p = PickPitch(previousPitch, chord, true, rng);
                    AddMelody(buf, melody, b, p, 1.0, spb);
                    previousPitch = p;
                }
            }
        }

        private static int PickPitch(int previous, int[] chord, bool chordTone, System.Random rng)
        {
            int target = previous + rng.Next(-4, 5);
            int[] classes = chordTone ? ChordClasses(chord) : Scale;
            int p = Nearest(target, chord, classes);
            if (p == previous && rng.NextDouble() < 0.5)
                p = Nearest(target + (rng.NextDouble() < 0.5 ? 3 : -3), chord, classes);
            return p;
        }

        private static int[] ChordClasses(int[] chord)
        {
            var c = new int[chord.Length];
            for (int i = 0; i < chord.Length; i++) c[i] = chord[i] % 12;
            return c;
        }

        private static int Nearest(int target, int[] chord, int[] classes)
        {
            int best = 72, bestDistance = int.MaxValue;
            for (int m = 64; m <= 84; m++)
            {
                if (Array.IndexOf(classes, m % 12) < 0) continue;
                int d = Math.Abs(m - target);
                if (d < bestDistance) { bestDistance = d; best = m; }
            }
            return best;
        }

        private static void AddMelody(float[] buf, List<MelodyNote> melody, double beat, int pitch, double lengthBeats, double spb)
        {
            melody.Add(new MelodyNote { beat = beat, pitch = pitch, lengthBeats = lengthBeats });
            double t = beat * spb;
            double dur = lengthBeats * spb * 0.9;
            AddTone(buf, reedTable, t, Midi(pitch), dur, 0.13f, 0.015, 0.06, 0, 6, true);
            AddTone(buf, reedTable, t, Midi(pitch), dur, 0.13f, 0.015, 0.06, 0, -6, true);
        }

        private static void AddReed(float[] buf, double t, double freq, double dur, float amp)
        {
            AddTone(buf, reedTable, t, freq, dur, amp, 0.01, 0.04, 0, 5);
            AddTone(buf, reedTable, t, freq, dur, amp, 0.01, 0.04, 0, -5);
        }

        private static double Midi(int note) => 440.0 * Math.Pow(2.0, (note - 69) / 12.0);

        private static void AddTone(float[] buf, float[] table, double start, double freq, double dur, float amp,
            double attack, double release, double decay, double detuneCents = 0, bool vibrato = false)
        {
            int s0 = (int)(start * Rate);
            int n = (int)((dur + release) * Rate);
            double inc = freq * Math.Pow(2.0, detuneCents / 1200.0) * TableSize / Rate;
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                int idx = s0 + i;
                if (idx >= buf.Length) break;
                double t = (double)i / Rate;
                double env = t < attack ? t / attack : (t < dur ? 1.0 : Math.Max(0.0, 1.0 - (t - dur) / release));
                if (decay > 0) env *= Math.Exp(-t * decay);
                double step = vibrato ? inc * (1.0 + 0.003 * Math.Sin(t * 34.5)) : inc;
                phase += step;
                while (phase >= TableSize) phase -= TableSize;
                if (idx >= 0) buf[idx] += amp * (float)env * table[(int)phase];
            }
        }

        private static void AddKick(float[] buf, double start, float amp)
        {
            int s0 = (int)(start * Rate);
            int n = (int)(0.3 * Rate);
            double phase = 0;
            for (int i = 0; i < n && s0 + i < buf.Length; i++)
            {
                double t = (double)i / Rate;
                double f = 48.0 + 110.0 * Math.Exp(-t * 28.0);
                phase += 2.0 * Math.PI * f / Rate;
                buf[s0 + i] += amp * (float)(Math.Exp(-t * 10.0) * Math.Sin(phase));
            }
        }

        private static void AddSnare(float[] buf, double start, float amp, System.Random noise)
        {
            int s0 = (int)(start * Rate);
            int n = (int)(0.2 * Rate);
            for (int i = 0; i < n && s0 + i < buf.Length; i++)
            {
                double t = (double)i / Rate;
                double v = (noise.NextDouble() * 2.0 - 1.0) * Math.Exp(-t * 20.0)
                         + 0.5 * Math.Sin(2.0 * Math.PI * 185.0 * t) * Math.Exp(-t * 28.0);
                buf[s0 + i] += amp * (float)v;
            }
        }

        private static void AddHat(float[] buf, double start, float amp, System.Random noise)
        {
            int s0 = (int)(start * Rate);
            int n = (int)(0.05 * Rate);
            double previous = 0;
            for (int i = 0; i < n && s0 + i < buf.Length; i++)
            {
                double t = (double)i / Rate;
                double x = noise.NextDouble() * 2.0 - 1.0;
                buf[s0 + i] += amp * (float)((x - previous) * Math.Exp(-t * 90.0));
                previous = x;
            }
        }

        private static void BuildTables()
        {
            if (reedTable != null) return;
            reedTable = new float[TableSize];
            bassTable = new float[TableSize];
            float reedPeak = 0f, bassPeak = 0f;
            for (int i = 0; i < TableSize; i++)
            {
                double x = 2.0 * Math.PI * i / TableSize;
                double reed = 0;
                for (int h = 1; h <= 8; h++) reed += (h % 2 == 0 ? 0.6 : 1.0) / h * Math.Sin(h * x);
                double bass = Math.Sin(x) + 0.3 * Math.Sin(3 * x) + 0.15 * Math.Sin(2 * x);
                reedTable[i] = (float)reed;
                bassTable[i] = (float)bass;
                reedPeak = Mathf.Max(reedPeak, Mathf.Abs(reedTable[i]));
                bassPeak = Mathf.Max(bassPeak, Mathf.Abs(bassTable[i]));
            }
            for (int i = 0; i < TableSize; i++)
            {
                reedTable[i] /= reedPeak;
                bassTable[i] /= bassPeak;
            }
        }
    }
}
