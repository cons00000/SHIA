using ACTA;
using Assets.Scripts;
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
 * AvaturnLLMDialogManager — version CPM + Profil motivationnel (avatar Avaturn)
 * Aligné sur la nouvelle API de ComputationalModel (namespace Assets.Scripts).
 *
 * Différences avec LMStudioDialogManager :
 *  - Utilise FacialExpressionAvaturn au lieu de FacialExpression
 *  - Appelle Ollama (api/generate) au lieu de LM Studio (/v1/chat/completions)
 *  - Expose AvaturnLLMDialogManager.Doubt() pour KeyListenerAvaturn
 */
public class AvaturnLLMDialogManager : MonoBehaviour
{
    public AudioSource audioSource;
    public float volume = 0.5f;

    public Transform informationPanel;
    public Transform textPanel;
    public Transform buttonPanel;
    public GameObject ButtonPrefab;
    private GameObject button;

    // Avatar Avaturn → FacialExpressionAvaturn
    public FacialExpressionAvaturn faceExpression;
    private Animator anim;

    // Dictation
    private DictationRecognizer dictationRecognizer;

    // Whisper
    public bool useWhisper = true;
    public WhisperManager whisper;
    public MicrophoneRecord microphoneRecord;
    public bool streamSegments = true;
    public bool printLanguage  = false;
    private string _buffer;

    // Mémoire conversationnelle
    private Queue<string> conversationList;

    // LLM (Ollama)
    public string urlOllama;
    public string modelName;
    [TextArea(15, 20)]
    public string preprompt;
    private string _response;

    // MaryTTS
    public bool   useMaryTTS  = false;
    public int    maryTTSPort = 59125;
    public string marylanguage = "fr";
    public string mary_voice   = "upmc-pierre-hsmm";

    // ---------------------------------------------------------------
    //  CONDITION EXPÉRIMENTALE — visible dans l'Inspector
    // ---------------------------------------------------------------
    [Header("Condition expérimentale")]
    [Tooltip("true = condition adaptative | false = condition contrôle (cadrage inversé)")]
    public bool isAdaptiveCondition = true;

    // Modèle computationnel socio-affectif
    private ComputationalModel model;

    // ---------------------------------------------------------------

    void Start()
    {
        anim = GetComponent<Animator>();

        // Initialisation du modèle avec la condition choisie
        model = new ComputationalModel { IsAdaptiveCondition = isAdaptiveCondition };

        InformationDisplay("");
        textPanel.GetComponentInChildren<Text>().text = "";

        button = Instantiate(ButtonPrefab);
        button.GetComponentInChildren<Text>().text = "Record";
        button.GetComponent<Button>().onClick.AddListener(OnButtonPressed);
        button.GetComponent<RectTransform>().position = new Vector3(90f, 39f, 0f);
        button.transform.SetParent(buttonPanel);

        conversationList = new Queue<string>();

        // Dictation
        dictationRecognizer = new DictationRecognizer
        {
            AutoSilenceTimeoutSeconds    = 10,
            InitialSilenceTimeoutSeconds = 10
        };
        dictationRecognizer.DictationResult   += DictationRecognizer_DictationResult;
        dictationRecognizer.DictationError    += DictationRecognizer_DictationError;
        dictationRecognizer.DictationComplete += DictationRecognizer_DictationComplete;

        // Whisper
        whisper.OnNewSegment      += OnNewSegment;
        microphoneRecord.OnRecordStop += OnRecordStop;
    }

    // ---------------------------------------------------------------
    //  DICTATION
    // ---------------------------------------------------------------

    private void DictationRecognizer_DictationComplete(DictationCompletionCause cause)
        => button.GetComponentInChildren<Text>().text = "Dictation";

    private void DictationRecognizer_DictationError(string error, int hresult)
    {
        useWhisper = true;
        button.GetComponentInChildren<Text>().text = "Record";
    }

    private void DictationRecognizer_DictationResult(string text, ConfidenceLevel confidence)
    {
        textPanel.GetComponentInChildren<Text>().text = text;
        HandleUserInput(text);
    }

    // ---------------------------------------------------------------
    //  WHISPER
    // ---------------------------------------------------------------

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
            else
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
        if (res == null) return;

        var text = res.Result;
        if (printLanguage) text += $"\n\nLanguage: {res.Language}";

        textPanel.GetComponentInChildren<Text>().text = text;
        HandleUserInput(text);
    }

    private void OnNewSegment(WhisperSegment segment)
    {
        if (!streamSegments) return;
        _buffer += segment.Text;
        textPanel.GetComponentInChildren<Text>().text = _buffer + "...";
    }

    // ---------------------------------------------------------------
    //  PIPELINE PRINCIPAL
    // ---------------------------------------------------------------

    /// <summary>
    /// Point d'entrée unique pour tout message utilisateur.
    /// Met à jour le modèle CPM puis envoie au LLM.
    /// </summary>
    private void HandleUserInput(string userText)
    {
        if (string.IsNullOrEmpty(userText)) return;

        // Métriques VD1 + estimation niveau VI2
        model.RecordUserTurn(userText);

        conversationList.Enqueue(userText);
        if (conversationList.Count > 10)
            conversationList.Dequeue();

        string fullconv = string.Join(" ", conversationList);
        SendToChat(fullconv);
    }

    // ---------------------------------------------------------------
    //  LLM — Ollama
    // ---------------------------------------------------------------

    IEnumerator postRequest(string url, string json)
    {
        var uwr = new UnityWebRequest(url, "POST");
        byte[] jsonToSend = System.Text.Encoding.UTF8.GetBytes(json);
        uwr.uploadHandler   = new UploadHandlerRaw(jsonToSend);
        uwr.downloadHandler = new DownloadHandlerBuffer();
        uwr.SetRequestHeader("Content-Type", "application/json");

        yield return uwr.SendWebRequest();

        if (uwr.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("LLM Error: " + uwr.error);
            yield break;
        }

        Debug.Log("LLM Received: " + uwr.downloadHandler.text);
        _response = uwr.downloadHandler.text;

        // Extraction depuis le JSON Ollama (clé "response")
        int pos    = _response.IndexOf("response\":");
        int endpos = _response.Substring(pos + 11).IndexOf("\"");
        _response  = _response.Substring(pos + 11, endpos);

        InformationDisplay(_response);

        // Balises émotionnelles → AUs + animation
        _response = ProcessAffectiveContent(_response);

        // Mise à jour CPM depuis la réponse agent → posture non-verbale
        model.RecordAgentTurn(_response);
        ApplySchererPosture(model.CurrentPosture);

        // Log engagement VD1
        Debug.Log("[ENGAGEMENT] " + model.GetEngagementSummary());

        conversationList.Enqueue(_response);
        if (conversationList.Count > 10)
            conversationList.Dequeue();

        PlayAudio(_response);
    }

    /// <summary>
    /// Construit le JSON Ollama avec preprompt de base + instructions CPM dynamiques.
    /// </summary>
    private void SendToChat(string prompt)
    {
        if (string.IsNullOrEmpty(prompt)) return;

        string fullSystem = preprompt + model.BuildDynamicSystemInstructions();
        fullSystem = fullSystem.Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
        prompt     = prompt.Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");

        string json = "{\"model\": \"" + modelName + "\"," +
                      "\"system\": \"" + fullSystem + "\"," +
                      "\"prompt\": \"" + prompt + "\"," +
                      "\"stream\": false}";

        StartCoroutine(postRequest(urlOllama + "api/generate", json));
    }

    // ---------------------------------------------------------------
    //  BALISES ÉMOTIONNELLES
    // ---------------------------------------------------------------

    private string ProcessAffectiveContent(string response)
    {
        if (response.Contains("{JOY}"))
        {
            DisplayAUs(new int[] { 6, 12 }, new int[] { 80, 80 }, 5f);
            anim.SetTrigger("JOY");
            response = response.Replace("{JOY}", "");
        }
        if (response.Contains("{SAD}"))
        {
            DisplayAUs(new int[] { 1, 4, 15 }, new int[] { 60, 60, 30 }, 5f);
            anim.SetTrigger("SAD");
            response = response.Replace("{SAD}", "");
        }
        if (response.Contains("{EMPATHY}"))
        {
            DisplayAUs(new int[] { 1, 15 }, new int[] { 50, 40 }, 3f);
            anim.SetTrigger("Listen");
            response = response.Replace("{EMPATHY}", "");
        }
        if (response.Contains("{DOUBT}"))
        {
            DisplayAUs(new int[] { 1, 4, 17 }, new int[] { 50, 40, 40 }, 3f);
            response = response.Replace("{DOUBT}", "");
        }
        return response.Trim();
    }

    // ---------------------------------------------------------------
    //  POSTURE NON-VERBALE CPM
    // ---------------------------------------------------------------

    private void ApplySchererPosture(ComputationalModel.PostureType posture)
    {
        switch (posture)
        {
            case ComputationalModel.PostureType.Enthusiastic:
                if (model.ActiveProfile == ComputationalModel.MotivationalProfile.Promotion)
                    DisplayAUs(new int[] { 6, 12, 2 }, new int[] { 80, 80, 40 }, 2.5f);
                else
                    DisplayAUs(new int[] { 6, 12 },    new int[] { 50, 50 },     2.0f);
                anim.SetTrigger("JOY");
                break;

            case ComputationalModel.PostureType.Pedagogical:
                if (model.ActiveProfile == ComputationalModel.MotivationalProfile.Prevention)
                    DisplayAUs(new int[] { 1, 4, 17 }, new int[] { 50, 40, 30 }, 2.5f);
                else
                    DisplayAUs(new int[] { 1, 2 },     new int[] { 40, 30 },     2.0f);
                anim.SetTrigger("Explain");
                break;

            case ComputationalModel.PostureType.Empathetic:
                DisplayAUs(new int[] { 1, 15 }, new int[] { 50, 40 }, 2.0f);
                anim.SetTrigger("Listen");
                break;

            case ComputationalModel.PostureType.Neutral:
            default:
                anim.SetTrigger("Neutral");
                break;
        }
    }

    // ---------------------------------------------------------------
    //  MÉTHODES PUBLIQUES — appelées par KeyListenerAvaturn
    // ---------------------------------------------------------------

    /// <summary>
    /// Joue une phrase directement via TTS (ex. phrase d'amorce).
    /// Appelé par KeyListenerAvaturn (touche "s").
    /// </summary>
    public void PlayAudio(string text)
    {
        if (!useMaryTTS)
        {
#if UNITY_STANDALONE_WIN
            Narrator.speak(text);
#else
            Debug.Log("Narrator not available on this platform.");
#endif
        }
        else
        {
            string req = "http://localhost:" + maryTTSPort + "/process?INPUT_TEXT=" +
                         text.Replace(" ", "+") +
                         "&INPUT_TYPE=TEXT&OUTPUT_TYPE=AUDIO&AUDIO=WAVE_FILE&LOCALE=" +
                         marylanguage + "&VOICE=" + mary_voice;
            Debug.Log("MaryTTS request: " + req);
            StartCoroutine(SetAudioClipFromFile(req));
        }
    }

    /// <summary>
    /// Déclenche une expression de doute sur l'avatar pendant <duration> secondes.
    /// Appelé par KeyListenerAvaturn (touche "f").
    /// </summary>
    public void Doubt(float intensity, float duration)
    {
        int i = Mathf.RoundToInt(intensity * 50f);
        DisplayAUs(new int[] { 1, 4, 17 }, new int[] { i, i, i / 2 }, duration);
    }

    public void PlayAudio(int a)
    {
        try
        {
            AudioClip music = Resources.Load<AudioClip>("Sounds/" + a);
            audioSource.PlayOneShot(music, volume);
        }
        catch (Exception e) { Debug.LogException(e); }
    }

    public void DisplayAUs(int[] aus, int[] intensities, float duration)
        => faceExpression.setFacialAUs(aus, intensities, duration);

    public void InformationDisplay(string s)
        => informationPanel.GetComponentInChildren<Text>().text = s;

    public void DisplayQuestion(string s)
        => textPanel.GetComponentInChildren<Text>().text = s;

    public void EndDialog()
    {
        anim.SetTrigger("Greet");
        Debug.Log("[BILAN INTERACTION] " + model.GetEngagementSummary());
    }

    // ---------------------------------------------------------------
    //  AUDIO — MaryTTS
    // ---------------------------------------------------------------

    IEnumerator SetAudioClipFromFile(string path)
    {
        using (var www = UnityWebRequestMultimedia.GetAudioClip(path, AudioType.WAV))
        {
            yield return www.SendWebRequest();
            if (www.result == UnityWebRequest.Result.ConnectionError)
            {
                Debug.LogWarning("MaryTTS unreachable: " + www.error);
                TryRestartMaryTTS();
            }
            else
            {
                audioSource.PlayOneShot(DownloadHandlerAudioClip.GetContent(www), volume);
            }
        }
    }

    private void TryRestartMaryTTS()
    {
        string maryPath = Application.streamingAssetsPath + "/marytts-5.2/bin/marytts-server";
        if (!System.IO.File.Exists(maryPath)) return;
        try
        {
            System.Diagnostics.Process.Start(new ProcessStartInfo
            {
                UseShellExecute = true,
                WindowStyle     = ProcessWindowStyle.Minimized,
                FileName        = maryPath
            });
        }
        catch (Exception e) { Debug.LogException(e); }
    }

    void Update() { }
}