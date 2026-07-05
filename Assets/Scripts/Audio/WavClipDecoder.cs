using System;
using UnityEngine;

namespace Rpg.Audio
{
    public static class WavClipDecoder
    {
        public static bool TryDecodePcm16(byte[] wavBytes, out AudioClip clip)
        {
            clip = null;
            if (wavBytes == null || wavBytes.Length < 44)
                return false;
            if (!HasFourCc(wavBytes, 0, "RIFF") || !HasFourCc(wavBytes, 8, "WAVE"))
                return false;

            var offset = 12;
            var channels = 0;
            var sampleRate = 0;
            var bitsPerSample = 0;
            var pcmOffset = -1;
            var pcmSize = 0;

            while (offset + 8 <= wavBytes.Length)
            {
                var chunkSize = ReadInt32(wavBytes, offset + 4);
                var chunkData = offset + 8;
                if (chunkSize < 0 || chunkData + chunkSize > wavBytes.Length)
                    return false;

                if (HasFourCc(wavBytes, offset, "fmt "))
                {
                    if (chunkSize < 16)
                        return false;
                    var audioFormat = ReadInt16(wavBytes, chunkData);
                    channels = ReadInt16(wavBytes, chunkData + 2);
                    sampleRate = ReadInt32(wavBytes, chunkData + 4);
                    bitsPerSample = ReadInt16(wavBytes, chunkData + 14);
                    if (audioFormat != 1)
                        return false;
                }
                else if (HasFourCc(wavBytes, offset, "data"))
                {
                    pcmOffset = chunkData;
                    pcmSize = chunkSize;
                }

                offset = chunkData + chunkSize + (chunkSize % 2);
            }

            if (pcmOffset < 0 || pcmSize <= 0 || channels <= 0 || sampleRate <= 0 || bitsPerSample != 16)
                return false;
            var frameCount = pcmSize / (channels * 2);
            if (frameCount <= 0)
                return false;

            var samples = new float[frameCount * channels];
            var src = pcmOffset;
            for (var i = 0; i < frameCount * channels; i++)
            {
                var sample = (short)(wavBytes[src] | (wavBytes[src + 1] << 8));
                samples[i] = sample / 32768f;
                src += 2;
            }

            clip = AudioClip.Create("dialogue_tts", frameCount, channels, sampleRate, false);
            clip.SetData(samples, 0);
            return true;
        }

        static bool HasFourCc(byte[] data, int offset, string fourCc)
        {
            return offset + 4 <= data.Length
                   && data[offset] == fourCc[0]
                   && data[offset + 1] == fourCc[1]
                   && data[offset + 2] == fourCc[2]
                   && data[offset + 3] == fourCc[3];
        }

        static short ReadInt16(byte[] data, int offset)
        {
            return (short)(data[offset] | (data[offset + 1] << 8));
        }

        static int ReadInt32(byte[] data, int offset)
        {
            return data[offset]
                   | (data[offset + 1] << 8)
                   | (data[offset + 2] << 16)
                   | (data[offset + 3] << 24);
        }
    }
}
