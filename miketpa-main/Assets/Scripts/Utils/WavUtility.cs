using UnityEngine;
using System;
using System.IO;
namespace Assets.Scripts.Utils
{
    public static class WavUtility
    {
        public static AudioClip ToAudioClip(byte[] fileBytes, string name = "wav")
        {
            int channels = BitConverter.ToInt16(fileBytes, 22);
            int sampleRate = BitConverter.ToInt32(fileBytes, 24);
            int byteRate = BitConverter.ToInt32(fileBytes, 28);
            int bitsPerSample = BitConverter.ToInt16(fileBytes, 34);

            int dataIndex = FindDataChunkPos(fileBytes);
            int dataSize = BitConverter.ToInt32(fileBytes, dataIndex + 4);
            int samples = dataSize / (bitsPerSample / 8);

            float[] floatData = new float[samples];

            int offset = dataIndex + 8;

            if (bitsPerSample == 16)
            {
                for (int i = 0; i < samples; i++)
                {
                    short value = BitConverter.ToInt16(fileBytes, offset + i * 2);
                    floatData[i] = value / 32768f;
                }
            }
            else if (bitsPerSample == 8)
            {
                for (int i = 0; i < samples; i++)
                {
                    floatData[i] = (fileBytes[offset + i] - 128f) / 128f;
                }
            }
            else
            {
                Debug.LogError("Unsupported WAV bit depth: " + bitsPerSample);
                return null;
            }

            AudioClip clip = AudioClip.Create(name, samples / channels, channels, sampleRate, false);
            clip.SetData(floatData, 0);
            return clip;
        }

        private static int FindDataChunkPos(byte[] bytes)
        {
            int pos = 12; // après "RIFF....WAVE"

            while (pos < bytes.Length - 8)
            {
                // nom du chunk (4 caractères)
                string chunkName = System.Text.Encoding.ASCII.GetString(bytes, pos, 4);

                // taille du chunk (4 octets suivants)
                int chunkSize = BitConverter.ToInt32(bytes, pos + 4);

                if (chunkName == "data")
                {
                    return pos;
                }

                // passer au chunk suivant (4 + 4 + chunkSize)
                pos += 8 + chunkSize;
            }

            throw new Exception("WAV data chunk not found");
        }
        public static void PrintWavInfo(byte[] wavBytes)
        {
            using (var reader = new BinaryReader(new MemoryStream(wavBytes)))
            {
                try
                {
                    string chunkID = new string(reader.ReadChars(4));
                    if (chunkID != "RIFF") throw new Exception("Ce n'est pas un WAV valide");

                    reader.ReadInt32(); // taille fichier
                    string format = new string(reader.ReadChars(4));
                    if (format != "WAVE") throw new Exception("Format non WAVE");

                    // fmt chunk
                    string subChunk1ID = new string(reader.ReadChars(4));
                    int subChunk1Size = reader.ReadInt32();
                    short audioFormat = reader.ReadInt16();
                    short numChannels = reader.ReadInt16();
                    int sampleRate = reader.ReadInt32();
                    int byteRate = reader.ReadInt32();
                    short blockAlign = reader.ReadInt16();
                    short bitsPerSample = reader.ReadInt16();

                    // chercher chunk data
                    string subChunk2ID = new string(reader.ReadChars(4));
                    while (subChunk2ID != "data")
                    {
                        int skip = reader.ReadInt32();
                        reader.BaseStream.Seek(skip, SeekOrigin.Current);
                        subChunk2ID = new string(reader.ReadChars(4));
                    }
                    int subChunk2Size = reader.ReadInt32();

                    int numSamples = subChunk2Size / (numChannels * bitsPerSample / 8);
                    float durationSec = (float)numSamples / sampleRate;

                    Debug.Log($"[WAV INFO] Channels={numChannels}, SampleRate={sampleRate} Hz, Bits={bitsPerSample}, Frames={numSamples}, Duration={durationSec:F2} sec");
                }
                catch (Exception e)
                {
                    Debug.LogError("Erreur analyse WAV : " + e.Message);
                }
            }
        }
    }


}