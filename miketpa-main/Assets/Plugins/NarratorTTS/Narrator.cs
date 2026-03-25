using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace ACTA
{
    public class Narrator : MonoBehaviour
    {
        public enum CONTEXT { TUTORIAL, TRAINING_ANN, TRAINING_TUT, ASSESSMENT };

#if UNITY_STANDALONE_WIN
        [DllImport("WindowsTTS")]
        public static extern void initSpeech();

        [DllImport("WindowsTTS")]
        public static extern void destroySpeech();

        [DllImport("WindowsTTS")]
        public static extern void addToSpeechQueue(byte[] s);

        [DllImport("WindowsTTS")]
        public static extern void clearSpeechQueue();

        [DllImport("WindowsTTS")]
        public static extern void statusMessage(StringBuilder str, int length);

        [DllImport("WindowsTTS")]
        public static extern void changeVoice(int vIdx);

        [DllImport("WindowsTTS")]
        public static extern bool isSpeaking();
#endif

        public CONTEXT mode;
        public static Narrator theVoice = null;
        public int voiceIdx = 0;

        static List<string> keyValue = new List<string>();
        static List<string> keyValueOnTask = new List<string>();
        static int currIdx = 0;

        void OnEnable()
        {
#if UNITY_STANDALONE_WIN
            if (theVoice == null)
            {
                theVoice = this;
                Debug.Log("Initializing speech");
                initSpeech();
            }
#endif
        }

        void OnDestroy()
        {
#if UNITY_STANDALONE_WIN
            if (theVoice == this)
            {
                Debug.Log("Destroying speech");
                destroySpeech();
                theVoice = null;
            }
#endif
        }

        public static void speak(string msg, bool interruptable = false)
        {
#if UNITY_STANDALONE_WIN
            Encoding encoding = System.Text.Encoding.GetEncoding(Encoding.UTF8.CodePage);
            var data = encoding.GetBytes(msg);
            if (interruptable)
                clearSpeechQueue();
            addToSpeechQueue(data);
#else
            // Optionnel : macOS ou Linux TTS minimal
            Debug.Log("[TTS] " + msg);
#endif
        }

        private void Awake()
        {
#if UNITY_STANDALONE_WIN
            changeVoice(voiceIdx);
#endif
        }

        public void TestSpeech()
        {
            Narrator.speak("Do you hear me?", false);
        }

        private void OnApplicationQuit()
        {
#if UNITY_STANDALONE_WIN
            Narrator.destroySpeech();
#endif
        }
    }
}