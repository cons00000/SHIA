using ACTA;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Windows;
using UnityEngine.Windows.Speech;
using Whisper;
using Whisper.Utils;
using Application = UnityEngine.Application;
using Button = UnityEngine.UI.Button;
using Debug = UnityEngine.Debug;
using Text = UnityEngine.UI.Text;
/*
* La classe LLMDialogManager permet de centraliser les fonctionnalités liés à l'aspect conversationnel de l'agent en Full Audio en utilisant un LLM hébergé sur un serveur distant. 
* ATTENTION : pour faire fonctionner le plugin Whisper de Macoron, il faut ajouter les modèles dans le répertoire 
* StreamingAssets. Allez voir les pages dédiées de ces modules pour plus d'explications. Ils ne sont pas fournis par défaut car ils prennent
* trop de place.
* Pour le fonctionnement de MaryTTS, le serveur MaryTTS doit s'exécuter depuis le répertoire StreamingAssets et n'est pas fourni également
*/
public class LLMDialogManager : MonoBehaviour
{

    public AudioSource audioSource;

    public float volume = 0.5f;

    public Transform informationPanel;
    public Transform textPanel;
    public Transform buttonPanel;
    public GameObject ButtonPrefab;
    private GameObject button;
    public FacialExpression faceExpression;
    private Animator anim;

    //dictation
    private DictationRecognizer dictationRecognizer;

    //whisper
    public bool useWhisper = true;

    public WhisperManager whisper;
    public MicrophoneRecord microphoneRecord;
    public bool streamSegments = true;
    public bool printLanguage = false;
    private string _buffer;

    //conversation memory
    private Queue<String> conversationList;

    //LLM
    public string urlOllama;
    public string modelName;
    [TextArea(15, 20)]
    public string preprompt;
    private string _response;

    //openMary
    public bool useMaryTTS = false;
    public int maryTTSPort = 59125;
    public string marylanguage = "en";
    public string mary_voice = "cmu-rms";

    // Start is called before the first frame update
    void Start()
    {
        anim = this.gameObject.GetComponent<Animator>();
        InformationDisplay("");
        Text textp = textPanel.transform.GetComponentInChildren<Text>().GetComponent<Text>();
        textp.text = "";
        button = (GameObject)Instantiate(ButtonPrefab);
        button.GetComponentInChildren<Text>().text = "Dictation";

        button.GetComponent<Button>().onClick.AddListener(delegate { OnButtonPressed(); });

        button.GetComponent<RectTransform>().position = new Vector3(0 * 170.0f + 90.0f, 39.0f, 0.0f);
        button.transform.SetParent(buttonPanel);

        conversationList = new Queue<String>();

        //dictation
        dictationRecognizer = new DictationRecognizer();
        dictationRecognizer.AutoSilenceTimeoutSeconds = 10;
        dictationRecognizer.InitialSilenceTimeoutSeconds = 10;
        dictationRecognizer.DictationResult += DictationRecognizer_DictationResult;
        dictationRecognizer.DictationError += DictationRecognizer_DictationError;
        dictationRecognizer.DictationComplete += DictationRecognizer_DictationComplete;


        //whisper
        whisper.OnNewSegment += OnNewSegment;
        microphoneRecord.OnRecordStop += OnRecordStop;

    }

    private void DictationRecognizer_DictationComplete(DictationCompletionCause cause)
    {
        button.GetComponentInChildren<Text>().text = "Dictation";
    }

    private void DictationRecognizer_DictationError(string error, int hresult)
    {
        useWhisper = true;
        button.GetComponentInChildren<Text>().text = "Record";

    }

    private void DictationRecognizer_DictationResult(string text, ConfidenceLevel confidence)
    {
        Text textp = textPanel.transform.GetComponentInChildren<Text>().GetComponent<Text>();
        textp.text = text;
        conversationList.Enqueue(text);
        if (conversationList.Count > 10)
            conversationList.Dequeue();
        string fullconv = "";
        foreach (String s in conversationList)
        {
            fullconv += " " + s;
        }
        SendToChat(fullconv);
    }

    //whisper


    private void OnButtonPressed()
    {
        if (useWhisper)
        {
            if (!microphoneRecord.IsRecording)
            {
                microphoneRecord.StartRecord();
                button.GetComponentInChildren<Text>().text = "Stop";
            }
            else
            {
                microphoneRecord.StopRecord();
                button.GetComponentInChildren<Text>().text = "Record";
            }
        }
        else
        {
            if (dictationRecognizer.Status != SpeechSystemStatus.Running)
            {
                dictationRecognizer.Start();
                button.GetComponentInChildren<Text>().text = "Stop";
            }
            if (dictationRecognizer.Status == SpeechSystemStatus.Running)
            {
                dictationRecognizer.Stop();
                button.GetComponentInChildren<Text>().text = "Dictation";
            }
        }
    }

    private async void OnRecordStop(AudioChunk audioChunk)
    {
        _buffer = "";

        var res = await whisper.GetTextAsync(audioChunk.Data, audioChunk.Frequency, audioChunk.Channels);
        if (res == null)
            return;

        var text = res.Result;
        if (printLanguage)
            text += $"\n\nLanguage: {res.Language}";
        Text textp = textPanel.transform.GetComponentInChildren<Text>().GetComponent<Text>();
        textp.text = text;
        conversationList.Enqueue(text);
        if (conversationList.Count > 10)
            conversationList.Dequeue();
        string fullconv = "";
        foreach (String s in conversationList)
        {
            fullconv += " " + s;
        }
        SendToChat(fullconv);
    }





    private void OnNewSegment(WhisperSegment segment)
    {
        if (!streamSegments)
            return;

        _buffer += segment.Text;
        Text textp = textPanel.transform.GetComponentInChildren<Text>().GetComponent<Text>();
        textp.text = _buffer + "...";
    }

    // Update is called once per frame
    void Update()
    {

    }


    /*
     * LLM
     */

    IEnumerator postRequest(string url, string json)
    {
        var uwr = new UnityWebRequest(url, "POST");
        byte[] jsonToSend = new System.Text.UTF8Encoding().GetBytes(json);
        uwr.uploadHandler = (UploadHandler)new UploadHandlerRaw(jsonToSend);
        uwr.downloadHandler = (DownloadHandler)new DownloadHandlerBuffer();
        uwr.SetRequestHeader("Content-Type", "application/json");

        //Send the request then wait here until it returns
        yield return uwr.SendWebRequest();

        if (uwr.result != UnityWebRequest.Result.Success)
        {
            Debug.Log("Error While Sending: " + uwr.error);
        }
        else
        {
            Debug.Log("Received: " + uwr.downloadHandler.text);
            _response = uwr.downloadHandler.text;
            //retrieve response from the JSON
            int pos = _response.IndexOf("response\":");
            Debug.Log(pos);
            int endpos = _response.Substring(pos + 11).IndexOf("\"");
            Debug.Log(endpos);
            _response = _response.Substring(pos + 11, endpos);
            InformationDisplay(_response);
            _response = ProcessAffectiveContent(_response);
            conversationList.Enqueue(_response);
            if (conversationList.Count > 10)
                conversationList.Dequeue();
            PlayAudio(_response);
        }
    }

    private string ProcessAffectiveContent(string response)
    {
        if (response.Contains("{JOY}"))
        {
            DisplayAUs(new int[] { 6, 12 }, new int[] { 80, 80 }, 5f);
            anim.SetTrigger("JOY");
            return response.Remove(response.IndexOf("{JOY}"), 4);
        }
        if (response.Contains("{SAD}"))
        {
            DisplayAUs(new int[] { 1, 4, 15 }, new int[] { 60, 60, 30 }, 5f);
            anim.SetTrigger("SAD");
            return response.Remove(response.IndexOf("{SAD}"), 4);
        }
        return response;
    }

    private void SendToChat(string prompt)
    {
        if (string.IsNullOrEmpty(prompt))
            return;
        //StartCoroutine(postRequest(urlOllama+ "api/chat", "{\"model\": \""+ modelName + "\",\"messages\": [{\"role\": \"system\",\"content\": \"" + preprompt+"\"},{\"role\": \"user\",\"content\": \"" + prompt+"\"}],\"stream\": false}"));        
        StartCoroutine(postRequest(urlOllama + "api/generate", "{\"model\": \"" + modelName + "\",\"system\": \"" + preprompt + "\",\"prompt\": \"" + prompt + "\",\"stream\": false}"));
    }


    /*
     * Cette méthode permet de jouer un fichier audio depuis le répertoire Resources/Sounds dont le nom est de la forme <entier>.mp3 
     */
    public void PlayAudio(int a)
    {
        try
        {
            //Charge un fichier audio depuis le répertoire Resources
            AudioClip music = (AudioClip)Resources.Load("Sounds/" + a);
            audioSource.PlayOneShot(music, volume);
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogException(e);
        }
    }

    /*
     * Cette méthode permet de demander à MaryTTS de générer un audio, puis de le jouer, à partir du texte
     * MaryTTS server doit donc être lancé sur la machine.
     * Pour l'instant, il est attendu que le répertoire marytts-5.2 soit copié dans le répertoire StreamingAssets du projet 
     * et que MaryTTS-Server soit exécuté à partir de /marytts-5.2/bin/ 
     */
    public void PlayAudio(string text)
    {
        if (!useMaryTTS)
        {
#if UNITY_STANDALONE_WIN
            Narrator.speak(text);
#else
            Debug.Log("Narrator not available");
#endif
        }
        else
        {
            // need to change player setting to allow non-https connections
            string maryTTS_request = "http://localhost:" + maryTTSPort + "/process?INPUT_TEXT=" + text.Replace(" ", "+") + "&INPUT_TYPE=TEXT&OUTPUT_TYPE=AUDIO&AUDIO=WAVE_FILE&LOCALE=" + marylanguage + "&VOICE=" + mary_voice;
            Debug.Log("request: " + maryTTS_request);

            StartCoroutine(SetAudioClipFromFile(maryTTS_request));
        }
    }

    IEnumerator SetAudioClipFromFile(string path)
    {
        using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(path, AudioType.WAV))
        {
            yield return www.SendWebRequest();
            if (www.result == UnityWebRequest.Result.ConnectionError)
            {
                Debug.Log(www.error);
                Debug.Log("Unable to use MaryTTS voice synthesiser.");
                string MaryTTSLocation = Application.streamingAssetsPath + "/marytts-5.2/bin/marytts-server";
                if (File.Exists(MaryTTSLocation))
                {
                    Debug.Log("Trying to restart MaryTTS server.");
                    Process proc = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            UseShellExecute = true,
                            WindowStyle = System.Diagnostics.ProcessWindowStyle.Minimized, // cannot close it if set to hidden
                            CreateNoWindow = true,
                            FileName = MaryTTSLocation
                        }
                    };
                    try
                    {
                        proc.Start();
                        System.Threading.Thread.Sleep(1000);
                    }
                    catch (Exception e)
                    {
                        Debug.Log("Failed to start MaryTTS server: ");
                        Debug.LogException(e);
                    }

                    if (proc.StartTime <= DateTime.Now && !proc.HasExited)
                    {
                        Debug.Log("Restarted MaryTTS server.");
                    }
                    else
                    {
                        var errorMsg = string.Format("Failed to started MaryTTS (server not running). Disabling MaryTTS.");

                        if (proc.HasExited)
                        {
                            errorMsg = string.Format("Failed to started MaryTTS (server was closed). Disabling MaryTTS.");
                        }

                        Debug.Log(errorMsg);
                    }
                }
                else
                {
                    Debug.Log("Failed to restart MaryTTS server. Disabling MaryTTS.");
                }
            }
            else
            {
                AudioClip music = DownloadHandlerAudioClip.GetContent(www);
                audioSource.PlayOneShot(music, volume);
            }
        }
    }



    /*
     * Cette méthode affiche du texte dans le panneau d'affichage à gauche de l'UI
     */
    public void InformationDisplay(string s)
    {

        Text text = informationPanel.transform.GetComponentInChildren<Text>().GetComponent<Text>();
        text.text = s;

    }
    /*
     * Cette méthode affiche le texte de la question dans la partie basse de l'UI
     */
    public void DisplayQuestion(string s)
    {
        Text text = textPanel.transform.GetComponentInChildren<Text>().GetComponent<Text>();
        text.text = s;
    }

    public void EndDialog()
    {

        anim.SetTrigger("Greet");
    }


    /*
     * Cette méthode permet de faire jouer des AUs à l'agent
     */
    public void DisplayAUs(int[] aus, int[] intensities, float duration)
    {
        faceExpression.setFacialAUs(aus, intensities, duration);
    }


}
