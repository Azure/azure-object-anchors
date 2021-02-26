// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
using Microsoft.MixedReality.Toolkit;
using System.Threading.Tasks;
using UnityEngine;

#if UNITY_WSA && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Media.SpeechSynthesis;
using Windows.Storage.Streams;
#endif

namespace Microsoft.Azure.ObjectAnchors.Unity.Sample
{
    public class TextToSpeech : MonoBehaviour
    {
        private AudioSource genericSource;
        private AudioSource specifiedSource;
        private WAV wav;
        private bool samplesReady = false;

#if UNITY_WSA && !UNITY_EDITOR
    private SpeechSynthesizer synthesizer;
    private SpeechSynthesisStream synthesisStream;
    private string defaultVoiceLanguage = "en-US";
    private VoiceGender defaultVoiceGender = VoiceGender.Female;
    private VoiceInformation defaultVoice = null;

    /// <summary>
    /// Get the default voice to play
    /// </summary>
    private VoiceInformation DefaultVoice
    {
        get
        {
            if (defaultVoice != null)
            {
                return defaultVoice;
            }

            if (SpeechSynthesizer.AllVoices.Count == 0)
            {
                return null;
            }

            VoiceInformation languageMatch = null;
            VoiceInformation genderMatch = null;
            foreach (var voice in SpeechSynthesizer.AllVoices)
            {
                if (voice.Language == defaultVoiceLanguage && voice.Gender == defaultVoiceGender)
                {
                    languageMatch = genderMatch = voice;
                    break;
                }
                else if (voice.Language == defaultVoiceLanguage && languageMatch == null)
                {
                    languageMatch = voice;
                }
                else if (voice.Gender == defaultVoiceGender && genderMatch == null)
                {
                    genderMatch = voice;
                }
            }

            defaultVoice = languageMatch ?? genderMatch ?? SpeechSynthesizer.AllVoices[0];
            return defaultVoice;
        }
    }

    private VoiceInformation GetVoiceFromIndex(int voiceIndex)
    {
        if (voiceIndex < 0 || voiceIndex >= SpeechSynthesizer.AllVoices.Count)
        {
            return null;
        }
        return SpeechSynthesizer.AllVoices[voiceIndex];
    }
#endif

        void Awake()
        {
            genericSource = gameObject.EnsureComponent<AudioSource>();
#if UNITY_WSA && !UNITY_EDITOR
        synthesizer = new SpeechSynthesizer();
#endif
        }


#if UNITY_WSA && !UNITY_EDITOR
    public async Task SpeakAndWait(string text, AudioSource audioSource = null, int voiceIndex = -1)
    {
        specifiedSource = audioSource;
        synthesizer.Voice = GetVoiceFromIndex(voiceIndex) ?? DefaultVoice;
        synthesisStream = await synthesizer.SynthesizeTextToStreamAsync(text);
        byte[] bytes = new byte[synthesisStream.Size];
        await synthesisStream.ReadAsync(bytes.AsBuffer(), (uint)synthesisStream.Size, InputStreamOptions.None);
        wav = new WAV(bytes);
        samplesReady = true;
    }
#else
        public Task SpeakAndWait(string text, AudioSource audioSource = null, int voiceIndex = -1)
        {
            samplesReady = true;
            return Task.CompletedTask;
        }
#endif

        public async void Speak(string text, AudioSource audioSource = null, int voiceIndex = -1)
        {
            await SpeakAndWait(text, audioSource, voiceIndex);
        }

        void Update()
        {
            if (samplesReady)
            {
                samplesReady = false;

#if UNITY_WSA && !UNITY_EDITOR
            AudioClip audioClip = AudioClip.Create("ttsSound", wav.SampleCount, 1, wav.Frequency, false);
            audioClip.SetData(wav.LeftChannel, 0);
            if (specifiedSource != null)
            {
                specifiedSource.clip = audioClip;
                specifiedSource.Play();
            }
            else
            {
                genericSource.enabled = true;
                genericSource.clip = audioClip;
                genericSource.Play();
            }
#endif
            }

            if (!genericSource.isPlaying && genericSource.enabled)
            {
                genericSource.enabled = false;
            }
        }
        public class WAV
        {

            // convert two bytes to one float in the range -1 to 1
            static float bytesToFloat(byte firstByte, byte secondByte)
            {
                // convert two bytes to one short (little endian)
                short s = (short)(secondByte << 8 | firstByte);
                // convert to range from -1 to (just below) 1
                return s / 32768.0F;
            }

            static int bytesToInt(byte[] bytes, int offset = 0)
            {
                int value = 0;
                for (int i = 0; i < 4; i++)
                {
                    value |= bytes[offset + i] << i * 8;
                }
                return value;
            }

            // properties
            public float[] LeftChannel { get; internal set; }
            public float[] RightChannel { get; internal set; }
            public int ChannelCount { get; internal set; }
            public int SampleCount { get; internal set; }
            public int Frequency { get; internal set; }

            public WAV(byte[] wav)
            {

                // Determine if mono or stereo
                ChannelCount = wav[22];     // Forget byte 23 as 99.999% of WAVs are 1 or 2 channels

                // Get the frequency
                Frequency = bytesToInt(wav, 24);

                // Get past all the other sub chunks to get to the data subchunk:
                int pos = 12;   // First Subchunk ID from 12 to 16

                // Keep iterating until we find the data chunk (i.e. 64 61 74 61 ...... (i.e. 100 97 116 97 in decimal))
                while (!(wav[pos] == 100 && wav[pos + 1] == 97 && wav[pos + 2] == 116 && wav[pos + 3] == 97))
                {
                    pos += 4;
                    int chunkSize = wav[pos] + wav[pos + 1] * 256 + wav[pos + 2] * 65536 + wav[pos + 3] * 16777216;
                    pos += 4 + chunkSize;
                }
                pos += 8;

                // Pos is now positioned to start of actual sound data.
                SampleCount = (wav.Length - pos) / 2;     // 2 bytes per sample (16 bit sound mono)
                if (ChannelCount == 2) SampleCount /= 2;        // 4 bytes per sample (16 bit stereo)

                // Allocate memory (right will be null if only mono sound)
                LeftChannel = new float[SampleCount];
                if (ChannelCount == 2) RightChannel = new float[SampleCount];
                else RightChannel = null;

                // Write to double array/s:
                int i = 0;
                while (pos < wav.Length)
                {
                    LeftChannel[i] = bytesToFloat(wav[pos], wav[pos + 1]);
                    pos += 2;
                    if (ChannelCount == 2)
                    {
                        RightChannel[i] = bytesToFloat(wav[pos], wav[pos + 1]);
                        pos += 2;
                    }
                    i++;
                }
            }

            public override string ToString()
            {
                return string.Format("[WAV: LeftChannel={0}, RightChannel={1}, ChannelCount={2}, SampleCount={3}, Frequency={4}]", LeftChannel, RightChannel, ChannelCount, SampleCount, Frequency);
            }
        }
    }
}