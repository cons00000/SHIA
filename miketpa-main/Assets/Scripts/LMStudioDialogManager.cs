using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Assets.Scripts;
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
 * LMStudioDialogManager — version CPM+Profil motivationnel
 *
 * Nouveautés :
 *  - Intègre ComputationalModel pour le cadrage VI1 (Promotion/Prévention)
 *    et la complexité adaptative VI2 (Novice/Intermédiaire/Expert)
 *  - Injecte dynamiquement les instructions dans le system prompt du LLM
 *  - Applique la posture non-verbale CPM de Scherer à l'avatar
 *  - Enregistre les métriques d'engagement (VD1) à chaque tour
 *  - Loggue un résumé d'engagement à chaque réponse de l'agent
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

    // Dictation
    private DictationRecognizer dictationRecognizer;

    // Whisper
    public bool useWhisper = true;
    public WhisperManager whisper;
    public MicrophoneRecord microphoneRecord;
    public bool streamSegments  = true;
    public bool printLanguage   = true;
    private string _buffer;

    // Mémoire de conversation (fenêtre glissante)
    private Queue<String> conversationList;

    // LLM
    public string urlLMStudio  = "localhost";
    public int    portLMStudio = 1234;
    [TextArea(15, 20)]
    public string preprompt;
    private string _response;
    public string temperature = "0.7";

    // MaryTTS
    public string marylanguage = "fr";
    public string mary_voice   = "upmc-pierre-hsmm";

    // ---------------------------------------------------------------
    //  MODÈLE COMPUTATIONNEL (CPM + Profil)
    // ---------------------------------------------------------------
    private ComputationalModel model;
    private InteractionLogger logger;

    // ---------------------------------------------------------------
    //  CONDITION EXPÉRIMENTALE
    //  Accessible depuis l'Inspector Unity pour basculer en contrôle
    // ---------------------------------------------------------------
    [Header("Condition expérimentale")]
    [Tooltip("Promotion = mettre en avant les gains / Prevention = mettre en avant les risques")]
    public ComputationalModel.MotivationalProfile motivationalProfile = ComputationalModel.MotivationalProfile.Promotion;
    // ---------------------------------------------------------------

    void Start()
    {
        anim = GetComponent<Animator>();

        // Initialisation du modèle avec la condition choisie dans l'Inspector
        model = new ComputationalModel { ExperimenterProfile = motivationalProfile };

        // Récupération du logger (doit être sur le même GameObject)
        logger = GetComponent<InteractionLogger>();
        if (logger == null)
            Debug.LogError("[LMStudioDialogManager] InteractionLogger introuvable sur ce GameObject !");

        InformationDisplay("");
        textPanel.GetComponentInChildren<Text>().text = "";

        button = Instantiate(ButtonPrefab);
        button.GetComponentInChildren<Text>().text = "Record";
        button.GetComponent<Button>().onClick.AddListener(OnButtonPressed);
        button.GetComponent<RectTransform>().position = new Vector3(90f, 39f, 0f);
        button.transform.SetParent(buttonPanel);

        conversationList = new Queue<String>();

        // Dictation
        dictationRecognizer = new DictationRecognizer
        {
            AutoSilenceTimeoutSeconds    = 20,
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
        => button.GetComponentInChildren<Text>().text = "Record";

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
            }
        }
    }

    private async void OnRecordStop(AudioChunk audioChunk)
    {
        button.GetComponentInChildren<Text>().text = "Record";
        _buffer = "";

        var res = await whisper.GetTextAsync(audioChunk.Data, audioChunk.Frequency, audioChunk.Channels);
        if (res == null) return;

        var text = string.IsNullOrWhiteSpace(res.Result) ? string.Empty : res.Result.Trim();
        if (string.IsNullOrEmpty(text)) return;
        string displayText = text;
        if (printLanguage)
            displayText += $"\n\nLanguage: {res.Language}";

        textPanel.GetComponentInChildren<Text>().text = displayText;
        HandleUserInput(text);
    }

    private void OnNewSegment(WhisperSegment segment)
    {
        if (!streamSegments) return;
        _buffer += segment.Text;
        textPanel.GetComponentInChildren<Text>().text = _buffer + "...";
    }

    // ---------------------------------------------------------------
    //  PIPELINE PRINCIPAL : saisie utilisateur → LLM → avatar
    // ---------------------------------------------------------------

    /// <summary>
    /// Point d'entrée unique pour tout message de l'utilisateur.
    /// Enregistre les métriques VD1, met à jour le modèle, puis envoie au LLM.
    /// </summary>
    private void HandleUserInput(string userText)
    {
        if (string.IsNullOrEmpty(userText)) return;
        userText = userText.Trim();

        // 1. Métriques d'engagement + estimation niveau (VI2)
        model.RecordUserTurn(userText);
        logger?.LogTurn("User", userText, model);

        // 2. Fenêtre conversationnelle
        conversationList.Enqueue("Utilisateur: " + userText);
        if (conversationList.Count > 10)
            conversationList.Dequeue();

        string fullconv = string.Join("\n", conversationList);

        // 3. Envoi au LLM avec system prompt dynamique
        SendToChat(fullconv);
    }

    // ---------------------------------------------------------------
    //  LLM
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

        // Extraction du contenu depuis le JSON OpenAI-compatible
        int pos    = _response.IndexOf("content\": ");
        int endpos = _response.Substring(pos + 11).IndexOf("\"");
        _response  = _response.Substring(pos + 11, endpos);
        _response  = _response.Split("###")[0];

        // Effets non-verbaux encodés dans la réponse (balises émotionnelles)
        _response = ProcessAffectiveContent(_response);

        // Mise à jour du modèle CPM depuis la réponse agent + posture non-verbale
        model.RecordAgentTurn(_response);
        logger?.LogTurn("Agent", _response, model);
        ApplySchererPosture(model.CurrentPosture);

        // Log d'engagement (VD1)
        Debug.Log("[ENGAGEMENT] " + model.GetEngagementSummary());

        // Fenêtre mémoire agent
        conversationList.Enqueue("Agent: " + _response);
        if (conversationList.Count > 10)
            conversationList.Dequeue();

        PlayAudio(_response);
    }

    /// <summary>
    /// Construit le JSON d'appel LLM en injectant les instructions CPM dynamiques.
    /// </summary>
    private void SendToChat(string prompt)
    {
        if (string.IsNullOrEmpty(prompt)) return;

        // Le system prompt de base + instructions CPM dynamiques (VI1 + VI2)
        string fullSystem = preprompt + model.BuildDynamicSystemInstructions();

        // Échappement minimal pour JSON (guillemets et retours à la ligne)
        fullSystem = fullSystem.Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
        prompt     = prompt.Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");

        string json = "{ \"messages\": [ " +
                      "{ \"role\": \"system\", \"content\": \"" + fullSystem + "\" }, " +
                      "{ \"role\": \"user\",   \"content\": \"" + prompt     + "\" } " +
                      "], \"temperature\": " + temperature + ", \"max_tokens\": -1, \"stream\": false }";

        StartCoroutine(postRequest("http://" + urlLMStudio + ":" + portLMStudio + "/v1/chat/completions", json));
    }

    // ---------------------------------------------------------------
    //  EXPRESSIONS NON-VERBALES — BALISES ÉMOTIONNELLES
    // ---------------------------------------------------------------

    /// <summary>
    /// Détecte les balises émotionnelles dans la réponse LLM et applique
    /// les AUs et animations correspondantes.
    /// </summary>
    private string ProcessAffectiveContent(string response)
    {
        if (response.Contains("{JOY}"))
        {
            DisplayAUs(new int[] { 6, 12 }, new int[] { 80, 80 }, 2.0f);
            anim.SetTrigger("JOY");
            response = response.Replace("{JOY}", "");
        }
        if (response.Contains("{DOUBT}"))
        {
            DisplayAUs(new int[] { 1, 4, 17 }, new int[] { 50, 40, 40 }, 2.0f);
            response = response.Replace("{DOUBT}", "");
        }
        if (response.Contains("{EMPATHY}"))
        {
            DisplayAUs(new int[] { 1, 15 }, new int[] { 50, 40 }, 2.0f);
            anim.SetTrigger("Listen");
            response = response.Replace("{EMPATHY}", "");
        }
        return response.Trim();
    }

    // ---------------------------------------------------------------
    //  POSTURE NON-VERBALE CPM DE SCHERER
    // ---------------------------------------------------------------

    /// <summary>
    /// Applique la posture interactionnelle (AUs + animation) correspondant
    /// à la posture déterminée par le CPM, modulée par le profil motivationnel.
    /// </summary>
    private void ApplySchererPosture(ComputationalModel.PostureType posture)
    {
        switch (posture)
        {
            case ComputationalModel.PostureType.Enthusiastic:
                // Promotion : sourire marqué, ouverture, enthousiasme
                if (model.ActiveProfile == ComputationalModel.MotivationalProfile.Promotion)
                    DisplayAUs(new int[] { 6, 12, 2 }, new int[] { 80, 80, 40 }, 2.5f);
                else  // Prévention : même posture mais plus sobre
                    DisplayAUs(new int[] { 6, 12 },    new int[] { 50, 50 },     2.0f);
                anim.SetTrigger("JOY");
                break;

            case ComputationalModel.PostureType.Pedagogical:
                // Prévention : sérieux, légère préoccupation
                if (model.ActiveProfile == ComputationalModel.MotivationalProfile.Prevention)
                    DisplayAUs(new int[] { 1, 4, 17 }, new int[] { 50, 40, 30 }, 2.5f);
                else  // Promotion : pédagogie douce, encourageante
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
    //  AUDIO
    // ---------------------------------------------------------------

    public void PlayAudio(int a)
    {
        try
        {
            AudioClip music = Resources.Load<AudioClip>("Sounds/" + a);
            audioSource.PlayOneShot(music, volume);
        }
        catch (Exception e) { Debug.LogException(e); }
    }

    public void PlayAudio(string text)
    {
        string req = "http://localhost:59125/process?INPUT_TEXT=" +
                     text.Replace(" ", "+") +
                     "&INPUT_TYPE=TEXT&OUTPUT_TYPE=AUDIO&AUDIO=WAVE_FILE&LOCALE=fr&VOICE=" + mary_voice;
        Debug.Log("MaryTTS request: " + req);
        StartCoroutine(SetAudioClipFromFile(req, text));
    }

    IEnumerator SetAudioClipFromFile(string path, string displayText)
    {
        using (var www = UnityWebRequestMultimedia.GetAudioClip(path, AudioType.WAV))
        {
            yield return www.SendWebRequest();
            if (www.result == UnityWebRequest.Result.ConnectionError)
            {
                InformationDisplay(displayText);
                Debug.LogWarning("MaryTTS unreachable: " + www.error);
                TryRestartMaryTTS();
            }
            else
            {
                InformationDisplay(displayText);
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
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                UseShellExecute = true,
                WindowStyle     = System.Diagnostics.ProcessWindowStyle.Minimized,
                FileName        = maryPath
            });
        }
        catch (Exception e) { Debug.LogException(e); }
    }

    // ---------------------------------------------------------------
    //  UI
    // ---------------------------------------------------------------

    public void InformationDisplay(string s)
        => informationPanel.GetComponentInChildren<Text>().text = s;

    public void DisplayQuestion(string s)
        => textPanel.GetComponentInChildren<Text>().text = s;

    public void DisplayAnswers(List<string> proposals) { /* à implémenter */ }

    public void EndDialog()
    {
        anim.SetTrigger("Greet");
        Debug.Log("[BILAN INTERACTION] " + model.GetEngagementSummary());
    }

    public void DisplayAUs(int[] aus, int[] intensities, float duration)
        => faceExpression.setFacialAUs(aus, intensities, duration);

    void Update() { }
}
