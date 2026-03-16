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
* La classe LMStudioDialogManager permet de centraliser les fonctionnalits lis  l'aspect conversationnel de l'agent en Full Audio pour une dmo avec LMStudio. 
* ATTENTION : pour faire fonctionner le plugin Whisper de Macoron, il faut ajouter les modles dans le rpertoire 
* StreamingAssets. Allez voir les pages ddies de ces modules pour plus d'explications. Ils ne sont pas fournis par dfaut car ils prennent
* trop de place.
* Pour le fonctionnement de MaryTTS, le serveur MaryTTS doit s'excuter depuis le rpertoire StreamingAssets et n'est pas fourni galement
*/
public class LMStudioDialogManager : MonoBehaviour
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
    public bool printLanguage = true;
    private string _buffer;

    //conversation memory
    private Queue<String> conversationList;

    //LLM
    public string urlLMStudio = "localhost";
    public int portLMStudio = 1234;
    [TextArea(15, 20)]
    public string preprompt;
    private string _response;
    public string temperature = "0.7";

    //openMary
    public string marylanguage = "fr";
    public string mary_voice = "upmc-pierre-hsmm";

    // Start is called before the first frame update
    void Start()
    {
        anim = this.gameObject.GetComponent<Animator>();
        InformationDisplay("");
        Text textp = textPanel.transform.GetComponentInChildren<Text>().GetComponent<Text>();
        textp.text = "";
        button = (GameObject)Instantiate(ButtonPrefab);
        button.GetComponentInChildren<Text>().text = "Record";

        button.GetComponent<Button>().onClick.AddListener(delegate { OnButtonPressed(); });
        button.GetComponent<RectTransform>().position = new Vector3(0 * 170.0f + 90.0f, 39.0f, 0.0f);
        button.transform.SetParent(buttonPanel);

        conversationList = new Queue<String>();

        //dictation
        dictationRecognizer = new DictationRecognizer();
        dictationRecognizer.AutoSilenceTimeoutSeconds = 20;
        dictationRecognizer.InitialSilenceTimeoutSeconds = 10;
        dictationRecognizer.DictationResult += DictationRecognizer_DictationResult;
        dictationRecognizer.DictationError += DictationRecognizer_DictationError;
        dictationRecognizer.DictationComplete += DictationRecognizer_DictationComplete;

        //whisper
        whisper.OnNewSegment += OnNewSegment;
        microphoneRecord.OnRecordStop += OnRecordStop;
        //LLM
        //manager.OnResponseUpdated += OnResponseHandler;

    }

    //whisper


    private void DictationRecognizer_DictationComplete(DictationCompletionCause cause)
    {
        button.GetComponentInChildren<Text>().text = "Record";
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
            }
        }
    }

    private async void OnRecordStop(AudioChunk audioChunk)
    {
        button.GetComponentInChildren<Text>().text = "Record";
        _buffer = "";

        var res = await whisper.GetTextAsync(audioChunk.Data, audioChunk.Frequency, audioChunk.Channels);
        if (res == null)
            return;

        /*
        var time = sw.ElapsedMilliseconds;
        var rate = length / (time * 0.001f);
        timeText.text = $"Time: {time} ms\nRate: {rate:F1}x";
        */
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
            int pos = _response.IndexOf("content\": ");
            Debug.Log(pos);
            int endpos = _response.Substring(pos + 11).IndexOf("\"");
            Debug.Log(endpos);
            _response = _response.Substring(pos + 11, endpos);
            _response = _response.Split("###")[0];
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
            DisplayAUs(new int[] { 6, 12 }, new int[] { 80, 80 }, 2.0f);
            anim.SetTrigger("JOY");
            return response.Remove(response.IndexOf("{JOY}"), 4);
        }
        return response;
    }

    private void SendToChat(string prompt)
    {
        if (string.IsNullOrEmpty(prompt))
            return;
        StartCoroutine(postRequest("http://" + urlLMStudio + ":" + portLMStudio + "/v1/chat/completions", "{ \r\n  \"messages\": [ \r\n    { \"role\": \"system\", \"content\": \"" + preprompt + "\" },\r\n    { \"role\": \"user\", \"content\": \"" + prompt + "\" }\r\n  ], \r\n  \"temperature\": " + temperature + ", \r\n  \"max_tokens\": -1,\r\n  \"stream\": false\r\n}"));
    }


    /*
     * Cette mthode permet de jouer un fichier audio depuis le rpertoire Resources/Sounds dont le nom est de la forme <entier>.mp3 
     */
    public void PlayAudio(int a)
    {
        try
        {
            //Charge un fichier audio depuis le rpertoire Resources
            AudioClip music = (AudioClip)Resources.Load("Sounds/" + a);
            audioSource.PlayOneShot(music, volume);
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogException(e);
        }
    }

    /*
     * Cette mthode permet de demander  MaryTTS de gnrer un audio, puis de le jouer,  partir du texte
     * MaryTTS server doit donc tre lanc sur la machine.
     * Pour l'instant, il est attendu que le rpertoire marytts-5.2 soit copi dans le rpertoire StreamingAssets du projet 
     * et que MaryTTS-Server soit excut  partir de /marytts-5.2/bin/ 
     */
    public void PlayAudio(string text)
    {
        // need to change player setting to allow non-https connections
        string maryTTS_request = "http://localhost:59125/process?INPUT_TEXT=" + text.Replace(" ", "+") + "&INPUT_TYPE=TEXT&OUTPUT_TYPE=AUDIO&AUDIO=WAVE_FILE&LOCALE=fr&VOICE=" + mary_voice;
        Debug.Log("request: " + maryTTS_request);

        StartCoroutine(SetAudioClipFromFile(maryTTS_request));
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
     * Cette mthode affiche du texte dans le panneau d'affichage  gauche de l'UI
     */
    public void InformationDisplay(string s)
    {

        Text text = informationPanel.transform.GetComponentInChildren<Text>().GetComponent<Text>();
        text.text = s;

    }
    /*
     * Cette mthode affiche le texte de la question dans la partie basse de l'UI
     */
    public void DisplayQuestion(string s)
    {
        Text text = textPanel.transform.GetComponentInChildren<Text>().GetComponent<Text>();
        text.text = s;
    }

    /* 
     * Cette mthode affiche les rponses sous forme de boutons dans l'UI.
     */
    public void DisplayAnswers(List<string> proposals)
    {




    }

    public void EndDialog()
    {

        anim.SetTrigger("Greet");
    }


    /*
     * Cette mthode permet de faire jouer des AUs  l'agent
     */
    public void DisplayAUs(int[] aus, int[] intensities, float duration)
    {
        faceExpression.setFacialAUs(aus, intensities, duration);
    }

}
