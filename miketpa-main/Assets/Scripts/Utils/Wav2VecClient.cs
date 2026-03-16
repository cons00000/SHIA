using System;
using System.Net.Sockets;
using System.Text;
namespace Assets.Scripts.Utils
{
    public class Wav2VecClient
    {
        const string HOST = "127.0.0.1";
        const int PORT = 50007;

        public static string SendWav(byte[] wavBytes)
        {
            using (TcpClient client = new TcpClient())
            {
                client.Connect(HOST, PORT);

                using NetworkStream stream = client.GetStream();

                // Send size prefix
                byte[] lengthBytes = BitConverter.GetBytes(wavBytes.Length);
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(lengthBytes);

                stream.Write(lengthBytes, 0, 4);
                stream.Write(wavBytes, 0, wavBytes.Length);

                // Receive response JSON
                byte[] buffer = new byte[8192];
                int bytesRead = stream.Read(buffer, 0, buffer.Length);

                string json = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                return json;
            }
        }
    }
}