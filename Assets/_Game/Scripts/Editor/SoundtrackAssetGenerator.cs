using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ZombieSurvival.Editor
{
    // 프로젝트에 포함되는 세 곡을 결정론적으로 합성합니다. 같은 코드에서는 매번 같은 WAV가 나오므로
    // 원본 파일을 잃어도 Tools 메뉴나 배치 명령으로 다시 만들 수 있고 외부 음원 라이선스에도 의존하지 않습니다.
    public static class SoundtrackAssetGenerator
    {
        private const int SampleRate = 48000;
        private const string OutputFolder = "Assets/_Game/Resources/Audio/Music";

        [MenuItem("Tools/2.5D Zombie Survival/Generate Basic Soundtrack")]
        public static void GenerateAll()
        {
            EnsureOutputFolder();

            WriteWave(OutputFolder + "/BGM_Title_LastLight.wav", ComposeTitle());
            WriteWave(OutputFolder + "/BGM_Hideout_EmbersRemain.wav", ComposeHideout());
            WriteWave(OutputFolder + "/BGM_School_SilentBell.wav", ComposeSchool());

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureImporter(OutputFolder + "/BGM_Title_LastLight.wav");
            ConfigureImporter(OutputFolder + "/BGM_Hideout_EmbersRemain.wav");
            ConfigureImporter(OutputFolder + "/BGM_School_SilentBell.wav");
            AssetDatabase.SaveAssets();
            Debug.Log("기본 배경음악 생성 완료: 타이틀, 거점, 폐쇄된 학교");
        }

        private static StereoBuffer ComposeTitle()
        {
            const float bpm = 62f;
            const int bars = 16;
            float beat = 60f / bpm;
            float bar = beat * 4f;
            var music = new StereoBuffer(bar * bars, SampleRate);

            music.AddNoiseBed(0.012f, 0.17f, 4101, 0.4f);
            music.AddDrone(Midi(38), 0f, music.Duration, 0.055f, -0.18f, 0.07f);
            music.AddDrone(Midi(45), 0f, music.Duration, 0.026f, 0.2f, 0.04f);

            int[][] chords =
            {
                new[] { 50, 53, 57, 64 }, // Dm(add9)
                new[] { 46, 50, 53, 57 }, // Bbmaj7
                new[] { 43, 50, 53, 57 }, // Gm6/9
                new[] { 45, 52, 55, 58 }  // A7(b9, sus)
            };

            for (int barIndex = 0; barIndex < bars; barIndex++)
            {
                float start = barIndex * bar;
                int[] chord = chords[barIndex % chords.Length];
                music.AddPadChord(chord, start, bar * 1.08f, 0.034f, barIndex % 2 == 0 ? -0.16f : 0.16f);
                music.AddCello(Midi(chord[0] - 12), start, bar * 0.96f, 0.052f, barIndex % 2 == 0 ? -0.2f : 0.2f);

                if (barIndex % 4 == 0)
                {
                    float motifStart = start + beat * 0.8f;
                    int[] motif = { 62, 63, 57, 55 };
                    float[] offsets = { 0f, 0.72f, 1.82f, 2.72f };
                    for (int i = 0; i < motif.Length; i++)
                        music.AddFeltPiano(Midi(motif[i]), motifStart + offsets[i] * beat, beat * 1.25f, 0.105f, i % 2 == 0 ? -0.28f : 0.28f);
                }

                if (barIndex % 4 == 2)
                {
                    music.AddFeltPiano(Midi(chord[1] + 12), start + beat * 1.5f, beat * 1.7f, 0.052f, -0.35f);
                    music.AddFeltPiano(Midi(chord[3] + 12), start + beat * 2.65f, beat * 1.2f, 0.045f, 0.35f);
                }
            }

            music.AddMetalBell(Midi(50), beat * 0.45f, 5.8f, 0.12f, -0.15f);
            music.AddMetalBell(Midi(38), bar * 8f + beat * 0.35f, 7.5f, 0.13f, 0.18f);
            music.AddNoiseSwell(bar * 7.25f, bar * 0.72f, 0.032f, 8127, 0.15f);
            music.AddNoiseSwell(bar * 15.05f, bar * 0.62f, 0.026f, 9151, -0.18f);
            music.ApplyEcho(0.38f, 0.19f, 0.15f);
            music.ApplyLowPass(9200f);
            music.FinalizeMix(0.78f, 1.1f, 0.72f);
            return music;
        }

        private static StereoBuffer ComposeHideout()
        {
            const float bpm = 54f;
            const int bars = 24;
            float beat = 60f / bpm;
            float bar = beat * 3f;
            var music = new StereoBuffer(bar * bars, SampleRate);

            music.AddNoiseBed(0.0065f, 0.1f, 2207, 0.15f);
            music.AddDrone(Midi(38), 0f, music.Duration, 0.035f, -0.12f, 0.035f);
            music.AddDrone(Midi(45), 0f, music.Duration, 0.018f, 0.18f, 0.025f);

            int[][] chords =
            {
                new[] { 50, 53, 57, 64 }, // Dm(add9)
                new[] { 46, 50, 53, 57 }, // Bbmaj7
                new[] { 45, 48, 53, 57 }, // Fmaj7/A
                new[] { 48, 50, 55, 62 }, // Csus2
                new[] { 43, 50, 53, 57 }, // Gm6/9
                new[] { 41, 50, 53, 57 }, // Dm/F
                new[] { 46, 50, 53, 60 }, // Bb(add9)
                new[] { 45, 50, 52, 57 }  // Asus4
            };

            for (int barIndex = 0; barIndex < bars; barIndex++)
            {
                float start = barIndex * bar;
                int[] chord = chords[(barIndex / 2) % chords.Length];
                music.AddWarmPad(chord, start, bar * 1.12f, 0.029f, barIndex % 2 == 0 ? -0.13f : 0.13f);

                if (barIndex % 3 == 0)
                {
                    int[][] phrases =
                    {
                        new[] { 62, 65, 69, 67 },
                        new[] { 57, 62, 65, 64 },
                        new[] { 58, 62, 65, 60 }
                    };
                    int[] phrase = phrases[(barIndex / 3) % phrases.Length];
                    float phraseStart = start + beat * 0.32f;
                    for (int i = 0; i < phrase.Length; i++)
                    {
                        float noteStart = phraseStart + i * beat * 0.62f;
                        music.AddFeltPiano(Midi(phrase[i]), noteStart, beat * 1.18f, 0.067f, i % 2 == 0 ? -0.3f : 0.3f);
                    }
                }

                if (barIndex % 4 == 1)
                {
                    music.AddGuitarHarmonic(Midi(chord[2] + 12), start + beat * 1.2f, beat * 2f, 0.035f, 0.42f);
                    music.AddGuitarHarmonic(Midi(chord[3] + 12), start + beat * 2.05f, beat * 1.2f, 0.026f, -0.36f);
                }
            }

            music.AddCello(Midi(38), bar * 5f, bar * 2.5f, 0.026f, -0.24f);
            music.AddCello(Midi(34), bar * 13f, bar * 2.5f, 0.024f, 0.2f);
            music.AddCello(Midi(38), bar * 21f, bar * 2.1f, 0.022f, -0.18f);
            music.AddCrackle(0.009f, 6337);
            music.ApplyEcho(0.46f, 0.16f, 0.13f);
            music.ApplyLowPass(7600f);
            music.FinalizeMix(0.95f, 1.3f, 0.68f);
            return music;
        }

        private static StereoBuffer ComposeSchool()
        {
            const float bpm = 70f;
            const int bars = 24;
            float beat = 60f / bpm;
            float bar = beat * 4f;
            var music = new StereoBuffer(bar * bars, SampleRate);

            music.AddNoiseBed(0.017f, 0.24f, 7723, 0.58f);
            music.AddDrone(Midi(26), 0f, music.Duration, 0.049f, -0.16f, 0.045f);
            music.AddDrone(Midi(39), 0f, music.Duration, 0.028f, 0.16f, 0.055f);
            music.AddDrone(Midi(45), 0f, music.Duration, 0.014f, -0.04f, 0.085f);

            int[][] clusters =
            {
                new[] { 50, 51, 57 },
                new[] { 43, 50, 55 },
                new[] { 46, 50, 57 },
                new[] { 45, 51, 55 }
            };
            for (int barIndex = 0; barIndex < bars; barIndex += 2)
            {
                int[] cluster = clusters[(barIndex / 2) % clusters.Length];
                music.AddDarkPad(cluster, barIndex * bar, bar * 2.2f, 0.024f, barIndex % 4 == 0 ? -0.2f : 0.2f);
            }

            int[] fragmentBars = { 1, 3, 6, 9, 11, 14, 17, 19, 22 };
            int[][] fragments =
            {
                new[] { 62, 63 },
                new[] { 57, 55 },
                new[] { 63, 57, 62 },
                new[] { 55, 51 },
                new[] { 62, 57 },
                new[] { 63, 62, 55 },
                new[] { 57, 63 },
                new[] { 50, 51, 57 },
                new[] { 62, 63, 57, 55 }
            };
            for (int phraseIndex = 0; phraseIndex < fragmentBars.Length; phraseIndex++)
            {
                float start = fragmentBars[phraseIndex] * bar + beat * (phraseIndex % 3) * 0.45f;
                int[] fragment = fragments[phraseIndex];
                for (int noteIndex = 0; noteIndex < fragment.Length; noteIndex++)
                {
                    music.AddPreparedPiano(
                        Midi(fragment[noteIndex]),
                        start + noteIndex * beat * 0.78f,
                        beat * 1.35f,
                        0.07f,
                        noteIndex % 2 == 0 ? -0.42f : 0.42f);
                }
            }

            // 후반에는 아주 낮은 맥박만 추가해 전투 효과음을 가리지 않고도 위험이 가까워졌음을 느끼게 합니다.
            for (int barIndex = 8; barIndex < bars; barIndex++)
            {
                float strength = 0.018f + 0.012f * Mathf.InverseLerp(8f, bars - 1f, barIndex);
                music.AddLowPulse(Midi(barIndex % 4 == 3 ? 27 : 26), barIndex * bar + beat * 0.25f, beat * 0.72f, strength, -0.1f);
                music.AddLowPulse(Midi(33), barIndex * bar + beat * 2.25f, beat * 0.66f, strength * 0.72f, 0.14f);
            }

            music.AddMetalBell(Midi(62), bar * 0.7f, 8.5f, 0.075f, -0.32f);
            music.AddMetalBell(Midi(50), bar * 8.15f, 9.5f, 0.085f, 0.28f);
            music.AddMetalBell(Midi(51), bar * 16.3f, 10f, 0.09f, -0.18f);
            music.AddNoiseSwell(bar * 7.15f, bar * 0.72f, 0.044f, 771, -0.25f);
            music.AddNoiseSwell(bar * 15.1f, bar * 0.82f, 0.05f, 883, 0.25f);
            music.AddNoiseSwell(bar * 22.5f, bar * 0.65f, 0.038f, 997, -0.12f);
            music.AddMetalTicks(0.012f, 4439);
            music.ApplyEcho(0.63f, 0.21f, 0.18f);
            music.ApplyLowPass(6900f);
            music.FinalizeMix(0.82f, 1.25f, 0.7f);
            return music;
        }

        private static float Midi(int note)
        {
            return 440f * Mathf.Pow(2f, (note - 69) / 12f);
        }

        private static void EnsureOutputFolder()
        {
            string absolutePath = Path.GetFullPath(OutputFolder);
            Directory.CreateDirectory(absolutePath);
        }

        private static void WriteWave(string assetPath, StereoBuffer music)
        {
            string absolutePath = Path.GetFullPath(assetPath);
            using (var stream = new FileStream(absolutePath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                int frameCount = music.FrameCount;
                int dataSize = frameCount * 2 * sizeof(short);
                writer.Write(new[] { 'R', 'I', 'F', 'F' });
                writer.Write(36 + dataSize);
                writer.Write(new[] { 'W', 'A', 'V', 'E' });
                writer.Write(new[] { 'f', 'm', 't', ' ' });
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)2);
                writer.Write(SampleRate);
                writer.Write(SampleRate * 2 * sizeof(short));
                writer.Write((short)(2 * sizeof(short)));
                writer.Write((short)16);
                writer.Write(new[] { 'd', 'a', 't', 'a' });
                writer.Write(dataSize);

                for (int i = 0; i < frameCount; i++)
                {
                    writer.Write(FloatToPcm16(music.Left[i]));
                    writer.Write(FloatToPcm16(music.Right[i]));
                }
            }
        }

        private static short FloatToPcm16(float sample)
        {
            return (short)Mathf.RoundToInt(Mathf.Clamp(sample, -1f, 1f) * short.MaxValue);
        }

        private static void ConfigureImporter(string assetPath)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as AudioImporter;
            if (importer == null) return;

            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.Streaming;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.72f;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            settings.preloadAudioData = false;
            importer.defaultSampleSettings = settings;
            importer.forceToMono = false;
            importer.loadInBackground = true;
            importer.SaveAndReimport();
        }

        private sealed class StereoBuffer
        {
            private readonly int sampleRate;

            public StereoBuffer(float duration, int rate)
            {
                sampleRate = rate;
                FrameCount = Mathf.CeilToInt(duration * rate);
                Duration = FrameCount / (float)rate;
                Left = new float[FrameCount];
                Right = new float[FrameCount];
            }

            public int FrameCount { get; }
            public float Duration { get; }
            public float[] Left { get; }
            public float[] Right { get; }

            public void AddFeltPiano(float frequency, float start, float duration, float amplitude, float pan)
            {
                AddTone(start, duration, amplitude, pan, (time, life) =>
                {
                    float body = Mathf.Sin(Tau * frequency * time) * Mathf.Exp(-2.8f * life);
                    float second = Mathf.Sin(Tau * frequency * 2.01f * time + 0.4f) * 0.34f * Mathf.Exp(-4.8f * life);
                    float third = Mathf.Sin(Tau * frequency * 3.98f * time + 1.1f) * 0.12f * Mathf.Exp(-7.2f * life);
                    float hammer = Mathf.Sin(Tau * frequency * 7.3f * time) * 0.055f * Mathf.Exp(-22f * life);
                    return body + second + third + hammer;
                }, 0.018f, Mathf.Min(0.9f, duration * 0.55f));
            }

            public void AddPreparedPiano(float frequency, float start, float duration, float amplitude, float pan)
            {
                AddTone(start, duration, amplitude, pan, (time, life) =>
                {
                    float fundamental = Mathf.Sin(Tau * frequency * time) * Mathf.Exp(-3.7f * life);
                    float detuned = Mathf.Sin(Tau * frequency * 1.012f * time + 0.7f) * 0.38f * Mathf.Exp(-4.5f * life);
                    float metal = Mathf.Sin(Tau * frequency * 5.71f * time) * 0.18f * Mathf.Exp(-10f * life);
                    return fundamental + detuned + metal;
                }, 0.008f, Mathf.Min(1.1f, duration * 0.65f));
            }

            public void AddGuitarHarmonic(float frequency, float start, float duration, float amplitude, float pan)
            {
                AddTone(start, duration, amplitude, pan, (time, life) =>
                {
                    float first = Mathf.Sin(Tau * frequency * time) * Mathf.Exp(-3.2f * life);
                    float octave = Mathf.Sin(Tau * frequency * 2.002f * time + 0.3f) * 0.5f * Mathf.Exp(-4.2f * life);
                    return first + octave;
                }, 0.012f, duration * 0.72f);
            }

            public void AddCello(float frequency, float start, float duration, float amplitude, float pan)
            {
                AddTone(start, duration, amplitude, pan, (time, life) =>
                {
                    float vibrato = 1f + Mathf.Sin(Tau * 4.7f * time) * 0.0028f;
                    float phase = Tau * frequency * vibrato * time;
                    return Mathf.Sin(phase) * 0.62f + Triangle(phase) * 0.31f + Mathf.Sin(phase * 2f) * 0.12f;
                }, Mathf.Min(0.8f, duration * 0.18f), Mathf.Min(1.1f, duration * 0.25f));
            }

            public void AddPadChord(int[] midiNotes, float start, float duration, float amplitude, float pan)
            {
                for (int i = 0; i < midiNotes.Length; i++)
                    AddPadVoice(Midi(midiNotes[i]), start + i * 0.035f, duration, amplitude / Mathf.Sqrt(midiNotes.Length), pan + (i - 1.5f) * 0.11f, false);
            }

            public void AddWarmPad(int[] midiNotes, float start, float duration, float amplitude, float pan)
            {
                for (int i = 0; i < midiNotes.Length; i++)
                    AddPadVoice(Midi(midiNotes[i]), start + i * 0.055f, duration, amplitude / Mathf.Sqrt(midiNotes.Length), pan + (i - 1.5f) * 0.13f, true);
            }

            public void AddDarkPad(int[] midiNotes, float start, float duration, float amplitude, float pan)
            {
                for (int i = 0; i < midiNotes.Length; i++)
                {
                    float frequency = Midi(midiNotes[i]);
                    AddTone(start + i * 0.09f, duration, amplitude / Mathf.Sqrt(midiNotes.Length), pan + (i - 1f) * 0.16f, (time, life) =>
                    {
                        float beating = Mathf.Sin(Tau * frequency * time) + Mathf.Sin(Tau * frequency * 1.006f * time + 0.8f);
                        return beating * 0.44f + Mathf.Sin(Tau * frequency * 0.5f * time) * 0.18f;
                    }, 1.7f, 2.2f);
                }
            }

            private void AddPadVoice(float frequency, float start, float duration, float amplitude, float pan, bool warm)
            {
                AddTone(start, duration, amplitude, pan, (time, life) =>
                {
                    float slow = Mathf.Sin(Tau * 0.08f * time + frequency * 0.01f) * 0.0025f;
                    float phase = Tau * frequency * (1f + slow) * time;
                    float body = Mathf.Sin(phase) * (warm ? 0.76f : 0.62f);
                    float upper = Mathf.Sin(phase * 2f + 0.35f) * (warm ? 0.18f : 0.27f);
                    float air = Mathf.Sin(phase * 3.01f + 1.2f) * 0.08f;
                    return body + upper + air;
                }, 1.15f, 1.65f);
            }

            public void AddDrone(float frequency, float start, float duration, float amplitude, float pan, float drift)
            {
                AddTone(start, duration, amplitude, pan, (time, life) =>
                {
                    float movement = 1f + Mathf.Sin(Tau * drift * time) * 0.003f;
                    float phase = Tau * frequency * movement * time;
                    return Mathf.Sin(phase) * 0.72f + Mathf.Sin(phase * 2.001f + 0.5f) * 0.18f + Mathf.Sin(phase * 0.5f) * 0.1f;
                }, 2.2f, 2.4f);
            }

            public void AddMetalBell(float frequency, float start, float duration, float amplitude, float pan)
            {
                AddTone(start, duration, amplitude, pan, (time, life) =>
                {
                    float decay = Mathf.Exp(-4.2f * life);
                    float a = Mathf.Sin(Tau * frequency * time) * 0.38f;
                    float b = Mathf.Sin(Tau * frequency * 2.71f * time + 0.4f) * 0.31f;
                    float c = Mathf.Sin(Tau * frequency * 4.13f * time + 1.2f) * 0.19f;
                    float d = Mathf.Sin(Tau * frequency * 6.82f * time + 0.8f) * 0.1f;
                    return (a + b + c + d) * decay;
                }, 0.006f, duration * 0.82f);
            }

            public void AddLowPulse(float frequency, float start, float duration, float amplitude, float pan)
            {
                AddTone(start, duration, amplitude, pan, (time, life) =>
                {
                    float phase = Tau * frequency * time;
                    return (Mathf.Sin(phase) + Mathf.Sin(phase * 0.5f) * 0.3f) * Mathf.Exp(-5.5f * life);
                }, 0.025f, duration * 0.65f);
            }

            public void AddNoiseBed(float amplitude, float smoothing, int seed, float stereoWidth)
            {
                var random = new System.Random(seed);
                float leftState = 0f;
                float rightState = 0f;
                float slowLeft = 0f;
                float slowRight = 0f;
                float coefficient = Mathf.Clamp01(smoothing);
                for (int i = 0; i < FrameCount; i++)
                {
                    float common = (float)(random.NextDouble() * 2.0 - 1.0);
                    float side = (float)(random.NextDouble() * 2.0 - 1.0) * stereoWidth;
                    leftState = Mathf.Lerp(leftState, common + side, coefficient);
                    rightState = Mathf.Lerp(rightState, common - side, coefficient);
                    slowLeft = Mathf.Lerp(slowLeft, leftState, 0.008f);
                    slowRight = Mathf.Lerp(slowRight, rightState, 0.008f);
                    float movement = 0.58f + 0.42f * Mathf.Sin(Tau * i / sampleRate * 0.071f + 0.8f);
                    Left[i] += (leftState * 0.35f + slowLeft * 0.65f) * amplitude * movement;
                    Right[i] += (rightState * 0.35f + slowRight * 0.65f) * amplitude * movement;
                }
            }

            public void AddNoiseSwell(float start, float duration, float amplitude, int seed, float pan)
            {
                var random = new System.Random(seed);
                float state = 0f;
                AddGenerated(start, duration, pan, (time, life) =>
                {
                    state = Mathf.Lerp(state, (float)(random.NextDouble() * 2.0 - 1.0), 0.035f);
                    float envelope = Mathf.Sin(Mathf.PI * Mathf.Clamp01(life));
                    return state * envelope * amplitude;
                });
            }

            public void AddCrackle(float amplitude, int seed)
            {
                var random = new System.Random(seed);
                float stateLeft = 0f;
                float stateRight = 0f;
                for (int i = 0; i < FrameCount; i++)
                {
                    stateLeft *= 0.965f;
                    stateRight *= 0.96f;
                    if (random.NextDouble() < 0.00042)
                    {
                        float hit = (float)(0.45 + random.NextDouble() * 0.55) * amplitude;
                        if (random.NextDouble() < 0.5) stateLeft += hit;
                        else stateRight += hit;
                    }
                    Left[i] += stateLeft;
                    Right[i] += stateRight;
                }
            }

            public void AddMetalTicks(float amplitude, int seed)
            {
                var random = new System.Random(seed);
                int tickCount = Mathf.CeilToInt(Duration / 5.5f);
                for (int tick = 0; tick < tickCount; tick++)
                {
                    float start = 2.5f + tick * 5.1f + (float)random.NextDouble() * 3.4f;
                    float frequency = 1100f + (float)random.NextDouble() * 1800f;
                    float pan = (float)random.NextDouble() * 1.4f - 0.7f;
                    AddTone(start, 0.16f, amplitude, pan, (time, life) =>
                        Mathf.Sin(Tau * frequency * time) * Mathf.Exp(-15f * life), 0.001f, 0.13f);
                }
            }

            public void ApplyEcho(float delaySeconds, float feedback, float crossFeed)
            {
                int delay = Mathf.Max(1, Mathf.RoundToInt(delaySeconds * sampleRate));
                for (int i = delay; i < FrameCount; i++)
                {
                    float previousLeft = Left[i - delay];
                    float previousRight = Right[i - delay];
                    Left[i] += previousLeft * feedback + previousRight * crossFeed * 0.35f;
                    Right[i] += previousRight * feedback + previousLeft * crossFeed * 0.35f;
                }
            }

            public void ApplyLowPass(float cutoff)
            {
                float coefficient = 1f - Mathf.Exp(-Tau * Mathf.Clamp(cutoff, 30f, sampleRate * 0.45f) / sampleRate);
                float leftState = 0f;
                float rightState = 0f;
                for (int i = 0; i < FrameCount; i++)
                {
                    leftState += coefficient * (Left[i] - leftState);
                    rightState += coefficient * (Right[i] - rightState);
                    Left[i] = leftState;
                    Right[i] = rightState;
                }
            }

            public void FinalizeMix(float fadeInSeconds, float fadeOutSeconds, float targetPeak)
            {
                int fadeInFrames = Mathf.Max(1, Mathf.RoundToInt(fadeInSeconds * sampleRate));
                int fadeOutFrames = Mathf.Max(1, Mathf.RoundToInt(fadeOutSeconds * sampleRate));
                float peak = 0f;
                for (int i = 0; i < FrameCount; i++)
                {
                    float edge = 1f;
                    if (i < fadeInFrames) edge *= Smooth(i / (float)fadeInFrames);
                    int framesFromEnd = FrameCount - 1 - i;
                    if (framesFromEnd < fadeOutFrames) edge *= Smooth(framesFromEnd / (float)fadeOutFrames);

                    Left[i] = SoftLimit(Left[i] * edge);
                    Right[i] = SoftLimit(Right[i] * edge);
                    peak = Mathf.Max(peak, Mathf.Abs(Left[i]), Mathf.Abs(Right[i]));
                }

                if (peak <= 0.00001f) return;
                // 합성 단계에서 헤드룸을 크게 둔 조용한 곡도 목표 피크까지 올려 AudioSource 음량이
                // 실제 게임 믹스의 기준이 되게 합니다. 비정상적으로 작은 신호만 과증폭되지 않도록 상한을 둡니다.
                float gain = Mathf.Min(8f, targetPeak / peak);
                for (int i = 0; i < FrameCount; i++)
                {
                    Left[i] *= gain;
                    Right[i] *= gain;
                }
            }

            private void AddTone(
                float start,
                float duration,
                float amplitude,
                float pan,
                Func<float, float, float> oscillator,
                float attack,
                float release)
            {
                AddGenerated(start, duration, pan, (time, life) =>
                {
                    float envelope = Envelope(time, duration, attack, release);
                    return oscillator(time, life) * amplitude * envelope;
                });
            }

            private void AddGenerated(float start, float duration, float pan, Func<float, float, float> generator)
            {
                int startFrame = Mathf.Max(0, Mathf.RoundToInt(start * sampleRate));
                int endFrame = Mathf.Min(FrameCount, Mathf.CeilToInt((start + duration) * sampleRate));
                float leftGain = Mathf.Sqrt(0.5f * (1f - Mathf.Clamp(pan, -1f, 1f)));
                float rightGain = Mathf.Sqrt(0.5f * (1f + Mathf.Clamp(pan, -1f, 1f)));
                for (int frame = startFrame; frame < endFrame; frame++)
                {
                    float time = (frame - startFrame) / (float)sampleRate;
                    float life = duration <= 0f ? 1f : time / duration;
                    float sample = generator(time, life);
                    Left[frame] += sample * leftGain;
                    Right[frame] += sample * rightGain;
                }
            }

            private static float Envelope(float time, float duration, float attack, float release)
            {
                float attackGain = attack <= 0f ? 1f : Mathf.Clamp01(time / attack);
                float remaining = duration - time;
                float releaseGain = release <= 0f ? 1f : Mathf.Clamp01(remaining / release);
                return Smooth(attackGain) * Smooth(releaseGain);
            }

            private static float Triangle(float phase)
            {
                return 2f / Mathf.PI * Mathf.Asin(Mathf.Sin(phase));
            }

            private static float Smooth(float value)
            {
                value = Mathf.Clamp01(value);
                return value * value * (3f - 2f * value);
            }

            private static float SoftLimit(float value)
            {
                return (float)Math.Tanh(value * 1.18f) / (float)Math.Tanh(1.18f);
            }

            private const float Tau = Mathf.PI * 2f;
        }
    }
}
