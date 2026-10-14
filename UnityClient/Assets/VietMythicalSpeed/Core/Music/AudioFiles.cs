using System;
using System.IO;
using System.Text;

namespace VMS.Core.Music
{
    public static class WavWriter
    {
        /// <summary>Writes 16-bit PCM. <paramref name="samples"/> are interleaved when channels &gt; 1.</summary>
        public static void Write(Stream stream, float[] samples, int sampleRate, int channels = 1)
        {
            int dataBytes = samples.Length * 2;
            using (var w = new BinaryWriter(stream, Encoding.ASCII, true))
            {
                w.Write(Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + dataBytes);
                w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt "));
                w.Write(16);
                w.Write((short)1);
                w.Write((short)channels);
                w.Write(sampleRate);
                w.Write(sampleRate * channels * 2);
                w.Write((short)(channels * 2));
                w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data"));
                w.Write(dataBytes);
                var block = new byte[8192];
                int pos = 0;
                for (int i = 0; i < samples.Length; i++)
                {
                    float s = samples[i];
                    if (s > 1f) s = 1f;
                    else if (s < -1f) s = -1f;
                    short v = (short)Math.Round(s * 32767f);
                    block[pos++] = (byte)(v & 0xFF);
                    block[pos++] = (byte)((v >> 8) & 0xFF);
                    if (pos == block.Length)
                    {
                        w.Write(block, 0, pos);
                        pos = 0;
                    }
                }
                if (pos > 0) w.Write(block, 0, pos);
            }
        }
    }

    public struct AudioTags
    {
        public string title;
        public string artist;
    }

    /// <summary>Minimal ID3v2.3/2.4 (TIT2/TPE1) and ID3v1 reader for nicer song names after import.</summary>
    public static class Id3Reader
    {
        public static AudioTags Read(string path)
        {
            try
            {
                using (var fs = File.OpenRead(path))
                    return Read(fs);
            }
            catch (IOException)
            {
                return default(AudioTags);
            }
            catch (UnauthorizedAccessException)
            {
                return default(AudioTags);
            }
        }

        public static AudioTags Read(Stream stream)
        {
            var tags = new AudioTags();
            if (stream == null || !stream.CanRead) return tags;
            var header = new byte[10];
            if (stream.CanSeek) stream.Position = 0;
            if (ReadFully(stream, header, 10) == 10 && header[0] == 'I' && header[1] == 'D' && header[2] == '3')
            {
                int major = header[3];
                int size = Syncsafe(header, 6);
                if (size > 0 && size < 4 * 1024 * 1024 && (major == 3 || major == 4))
                {
                    var body = new byte[size];
                    int read = ReadFully(stream, body, size);
                    ParseV2(body, read, major, ref tags);
                }
            }
            if ((string.IsNullOrEmpty(tags.title) || string.IsNullOrEmpty(tags.artist)) && stream.CanSeek && stream.Length >= 128)
            {
                stream.Position = stream.Length - 128;
                var v1 = new byte[128];
                if (ReadFully(stream, v1, 128) == 128 && v1[0] == 'T' && v1[1] == 'A' && v1[2] == 'G')
                {
                    if (string.IsNullOrEmpty(tags.title)) tags.title = Latin1(v1, 3, 30);
                    if (string.IsNullOrEmpty(tags.artist)) tags.artist = Latin1(v1, 33, 30);
                }
            }
            return tags;
        }

        static void ParseV2(byte[] body, int length, int major, ref AudioTags tags)
        {
            int pos = 0;
            while (pos + 10 <= length)
            {
                if (body[pos] == 0) break;
                string id = Encoding.ASCII.GetString(body, pos, 4);
                int frameSize = major == 4 ? Syncsafe(body, pos + 4)
                    : (body[pos + 4] << 24) | (body[pos + 5] << 16) | (body[pos + 6] << 8) | body[pos + 7];
                pos += 10;
                if (frameSize <= 0 || pos + frameSize > length) break;
                if (id == "TIT2" && string.IsNullOrEmpty(tags.title)) tags.title = DecodeText(body, pos, frameSize);
                else if (id == "TPE1" && string.IsNullOrEmpty(tags.artist)) tags.artist = DecodeText(body, pos, frameSize);
                pos += frameSize;
            }
        }

        static string DecodeText(byte[] data, int offset, int size)
        {
            if (size < 2) return null;
            byte encoding = data[offset];
            Encoding enc;
            switch (encoding)
            {
                case 1: enc = Encoding.Unicode; break;
                case 2: enc = Encoding.BigEndianUnicode; break;
                case 3: enc = Encoding.UTF8; break;
                default: enc = null; break;
            }
            int start = offset + 1, count = size - 1;
            if (encoding == 1 && count >= 2)
            {
                if (data[start] == 0xFE && data[start + 1] == 0xFF) enc = Encoding.BigEndianUnicode;
                if ((data[start] == 0xFF && data[start + 1] == 0xFE) || (data[start] == 0xFE && data[start + 1] == 0xFF))
                {
                    start += 2;
                    count -= 2;
                }
            }
            string s = enc == null ? Latin1Raw(data, start, Math.Max(0, count)) : enc.GetString(data, start, Math.Max(0, count));
            int nul = s.IndexOf('\0');
            if (nul >= 0) s = s.Substring(0, nul);
            s = s.Trim();
            return s.Length == 0 ? null : s;
        }

        // ISO-8859-1 maps bytes 1:1 to code points; decoded by hand because IL2CPP may strip code-page encodings.
        static string Latin1Raw(byte[] data, int offset, int count)
        {
            var chars = new char[count];
            for (int i = 0; i < count; i++) chars[i] = (char)data[offset + i];
            return new string(chars);
        }

        static string Latin1(byte[] data, int offset, int count)
        {
            string s = Latin1Raw(data, offset, count);
            int nul = s.IndexOf('\0');
            if (nul >= 0) s = s.Substring(0, nul);
            s = s.Trim();
            return s.Length == 0 ? null : s;
        }

        static int Syncsafe(byte[] b, int o) => (b[o] & 0x7F) << 21 | (b[o + 1] & 0x7F) << 14 | (b[o + 2] & 0x7F) << 7 | (b[o + 3] & 0x7F);

        static int ReadFully(Stream s, byte[] buffer, int count)
        {
            int total = 0;
            while (total < count)
            {
                int n = s.Read(buffer, total, count - total);
                if (n <= 0) break;
                total += n;
            }
            return total;
        }
    }
}
