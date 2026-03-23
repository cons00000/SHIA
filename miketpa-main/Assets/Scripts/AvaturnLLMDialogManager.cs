using ACTA;
using Assets.Scripts;
using Assets.Scripts.Utils;
using System;
using System.Collections;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Windows.Speech;
using Whisper;
using Whisper.Utils;
using Application = UnityEngine.Application;
using Button = UnityEngine.UI.Button;
using Debug = UnityEngine.Debug;
using Text = UnityEngine.UI.Text;

public enum EndPoint
{
    OpenWebUI,
    Ollama
};

public class AvaturnLLMDialogManager : MonoBehaviour
{
    public AudioSource audioSource;
    public float volume = 0.5f;

    public Transform informationPanel;
    public Transform textPanel;
    public Transform buttonPanel;
    public GameObject ButtonPrefab;
    private GameObject button;
    public FacialExpressionAvaturn faceExpression;
    private Animator anim;

    // Dictation
    private DictationRecognizer dictationRecognizer;

    // Whisper
    public bool useWhisper = true;
    public WhisperManager whisper;
    public MicrophoneRecord microphoneRecord;
    public bool streamSegments = true;
    public bool printLanguage = false;
    private string _buffer;

    // Conversation memory
    public int numberOfTurn = 10;
    private JsonParser jsonParser = new JsonParser();
    private JsonValue conversationList = new JsonValue(JsonType.Array);

    // LLM
    public string urlOllama;
    public EndPoint endPoint = EndPoint.OpenWebUI;
    public string modelName;
    public string APIkey;
    [TextArea(15, 20)]
    public string preprompt;
    private string _response;

    // Piper
    public bool usePiper = true;
    public int piperPort = 5000;
    public float speakerID = 1;
    public bool usePhonemeGenerator = false;

    // ---------------------------------------------------------------
    //  MODELE COMPUTATIONNEL CPM
    // ---------------------------------------------------------------
    [Header("Condition experimentale")]
    [Tooltip("Promotion = mettre en avant les gains / Prevention = mettre en avant les risques")]
    public ComputationalModel.MotivationalProfile motivationalProfile
        = ComputationalModel.MotivationalProfile.Promotion;

    private ComputationalModel computationalModel;
    // ---------------------------------------------------------------

    void Start()
    {
        anim = this.gameObject.GetComponent<Animator>();

        computationalModel = new ComputationalModel
        {
            ExperimenterProfile = motivationalProfile
        };

        InformationDisplay("");
        Text textp = textPanel.transform.GetComponentInChildren<Text>().GetComponent<Text>();
        textp.text = "";
        button = (GameObject)Instantiate(ButtonPrefab);
        button.GetComponentInChildren<Text>().text = "Dictation";
        button.GetComponent<Button>().onClick.AddListener(delegate { OnButtonPressed(); });
        button.GetComponent<RectTransform>().position = new Vector3(0 * 170.0f + 90.0f, 39.0f, 0.0f);
        button.transform.SetParent(buttonPanel);

        dictationRecognizer = new DictationRecognizer();
        dictationRecognizer.AutoSilenceTimeoutSeconds = 10;
        dictationRecognizer.InitialSilenceTimeoutSeconds = 10;
        dictationRecognizer.DictationResult += DictationRecognizer_DictationResult;
        dictationRecognizer.DictationError += DictationRecognizer_DictationError;
        dictationRecognizer.DictationComplete += DictationRecognizer_DictationComplete;

        whisper.OnNewSegment += OnNewSegment;
        microphoneRecord.OnRecordStop += OnRecordStop;
    }

    private void DictationRecognizer_DictationComplete(DictationCompletionCause cause)
        => button.GetComponentInChildren<Text>().text = "Dictation";

    private void DictationRecognizer_DictationError(string error, int hresult)
    {
        useWhisper = true;
        button.GetComponentInChildren<Text>().text = "Record";
    }

    private void DictationRecognizer_DictationResult(string text, ConfidenceLevel confidence)
    {
        text = string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
        if (string.IsNullOrEmpty(text)) return;
        textPanel.GetComponentInChildren<Text>().text = text;
        computationalModel.RecordUserTurn(text);

        JsonValue userTurn = new JsonValue(JsonType.Object);
        JsonValue userRole = new JsonValue(JsonType.String);
        userRole.StringValue = "user";
        JsonValue userContent = new JsonValue(JsonType.String);
        userContent.StringValue = text;
        userTurn.ObjectValues.Add("role", userRole);
        userTurn.ObjectValues.Add("content", userContent);
        conversationList.ArrayValues.Add(userTurn);
        if (conversationList.ArrayValues.Count > numberOfTurn)
            conversationList.ArrayValues.RemoveAt(0);

        SendToChat(conversationList);
    }

    private void OnButtonPressed()
    {
        if (useWhisper) // on utilise Whisper -> ne s'occupe que de l'affichage du bouton selon si la condition de lancer l'enregistrement est vraie ou pas
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

    private async void OnRecordStop(AudioChunk audioChunk) // retranscription de la conversation ?
    {
        _buffer = "";
        var res = await whisper.GetTextAsync(audioChunk.Data, audioChunk.Frequency, audioChunk.Channels);
        if (res == null) return;

        var text = string.IsNullOrWhiteSpace(res.Result) ? string.Empty : res.Result.Trim();
        if (string.IsNullOrEmpty(text)) return;
        computationalModel.RecordUserTurn(text);
        UserAnalysis(text);

        string displayText = text;
        if (printLanguage) displayText += $"\n\nLanguage: {res.Language}";
        textPanel.GetComponentInChildren<Text>().text = displayText;

        JsonValue userTurn = new JsonValue(JsonType.Object);
        JsonValue userRole = new JsonValue(JsonType.String);
        userRole.StringValue = "user";
        JsonValue userContent = new JsonValue(JsonType.String);
        userContent.StringValue = text;
        userTurn.ObjectValues.Add("role", userRole);
        userTurn.ObjectValues.Add("content", userContent);
        conversationList.ArrayValues.Add(userTurn);
        if (conversationList.ArrayValues.Count > numberOfTurn)
            conversationList.ArrayValues.RemoveAt(0);

        SendToChat(conversationList);
    }

    private void OnNewSegment(WhisperSegment segment)
    {
        if (!streamSegments) return;
        _buffer += segment.Text;
        textPanel.GetComponentInChildren<Text>().text = _buffer + "...";
    }

    void Update() { }

    // ---------------------------------------------------------------
    //  LLM
    // ---------------------------------------------------------------

    IEnumerator ChatRequest(string url, string json)
    {
        var uwr = new UnityWebRequest(url, "POST");
        byte[] jsonToSend = new System.Text.UTF8Encoding().GetBytes(json);
        uwr.uploadHandler   = (UploadHandler)new UploadHandlerRaw(jsonToSend);
        uwr.downloadHandler = (DownloadHandler)new DownloadHandlerBuffer();
        uwr.SetRequestHeader("Content-Type", "application/json");
        uwr.SetRequestHeader("Authorization", "Bearer " + APIkey);

        yield return uwr.SendWebRequest();

        if (uwr.result != UnityWebRequest.Result.Success)
        {
            Debug.Log("Error While Sending: " + uwr.error);
            yield break;
        }

        Debug.Log("Received: " + uwr.downloadHandler.text);
        _response = uwr.downloadHandler.text;

        JsonValue response = jsonParser.Parse(_response);
        string responseString = "";
        if (endPoint == EndPoint.OpenWebUI)
            responseString = response.ObjectValues["choices"].ArrayValues[0]
                .ObjectValues["message"].ObjectValues["content"].StringValue;
        else if (endPoint == EndPoint.Ollama)
            responseString = response.ObjectValues["message"].ObjectValues["content"].StringValue;

        InformationDisplay(responseString);
        _response = ProcessAffectiveContent(responseString);

        // Mise a jour CPM + posture non-verbale
        computationalModel.RecordAgentTurn(_response);
        ApplySchererPosture(computationalModel.CurrentPosture);
        Debug.Log("[ENGAGEMENT] " + computationalModel.GetEngagementSummary());

        LLMAnalysis(_response);

        JsonValue assistantTurn = new JsonValue(JsonType.Object);
        JsonValue assistantRole = new JsonValue(JsonType.String);
        assistantRole.StringValue = "assistant";
        JsonValue assistantContent = new JsonValue(JsonType.String);
        assistantContent.StringValue = _response;
        assistantTurn.ObjectValues.Add("role", assistantRole);
        assistantTurn.ObjectValues.Add("content", assistantContent);
        conversationList.ArrayValues.Add(assistantTurn);
        if (conversationList.ArrayValues.Count > numberOfTurn)
            conversationList.ArrayValues.RemoveAt(0);

        PlayAudio(_response);
    }

    IEnumerator UserRequest(string url, string json)
    {
        var uwr = new UnityWebRequest(url, "POST");
        byte[] jsonToSend = new System.Text.UTF8Encoding().GetBytes(json);
        uwr.uploadHandler   = (UploadHandler)new UploadHandlerRaw(jsonToSend);
        uwr.downloadHandler = (DownloadHandler)new DownloadHandlerBuffer();
        uwr.SetRequestHeader("Content-Type", "application/json");
        uwr.SetRequestHeader("Authorization", "Bearer " + APIkey);

        yield return uwr.SendWebRequest();

        if (uwr.result != UnityWebRequest.Result.Success)
        {
            Debug.Log("Error While Sending: " + uwr.error);
            yield break;
        }

        _response = uwr.downloadHandler.text;
        Debug.Log("[USER_ANALYSIS] " + _response);
    }

    IEnumerator LLMRequest(string url, string json)
    {
        var uwr = new UnityWebRequest(url, "POST");
        byte[] jsonToSend = new System.Text.UTF8Encoding().GetBytes(json);
        uwr.uploadHandler   = (UploadHandler)new UploadHandlerRaw(jsonToSend);
        uwr.downloadHandler = (DownloadHandler)new DownloadHandlerBuffer();
        uwr.SetRequestHeader("Content-Type", "application/json");
        uwr.SetRequestHeader("Authorization", "Bearer " + APIkey);

        yield return uwr.SendWebRequest();

        if (uwr.result != UnityWebRequest.Result.Success)
        {
            Debug.Log("Error While Sending: " + uwr.error);
            yield break;
        }

        _response = uwr.downloadHandler.text;
        Debug.Log("[LLM_ANALYSIS] " + _response);
    }

    // ---------------------------------------------------------------
    //  BALISES EMOTIONNELLES
    // ---------------------------------------------------------------

    private string ProcessAffectiveContent(string response) // récupérer et afficher les expressions faciales
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
    //  POSTURE NON-VERBALE CPM DE SCHERER
    // ---------------------------------------------------------------

    private void ApplySchererPosture(ComputationalModel.PostureType posture)
    {
        switch (posture)
        {
            case ComputationalModel.PostureType.Enthusiastic:
                if (computationalModel.ActiveProfile == ComputationalModel.MotivationalProfile.Promotion)
                    DisplayAUs(new int[] { 6, 12, 2 }, new int[] { 80, 80, 40 }, 2.5f);
                else
                    DisplayAUs(new int[] { 6, 12 }, new int[] { 50, 50 }, 2.0f);
                anim.SetTrigger("JOY");
                break;

            case ComputationalModel.PostureType.Pedagogical:
                if (computationalModel.ActiveProfile == ComputationalModel.MotivationalProfile.Prevention)
                    DisplayAUs(new int[] { 1, 4, 17 }, new int[] { 50, 40, 30 }, 2.5f);
                else
                    DisplayAUs(new int[] { 1, 2 }, new int[] { 40, 30 }, 2.0f);
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
    //  ENVOI AU LLM
    // ---------------------------------------------------------------

    private void SendToChat(JsonValue conversationList)
    {
        if (conversationList.ArrayValues.Count == 0) return;

        JsonValue fullConv = new JsonValue(JsonType.Array);
        JsonValue systemTurn = new JsonValue(JsonType.Object);
        JsonValue systemRole = new JsonValue(JsonType.String);
        systemRole.StringValue = "system";
        JsonValue systemContent = new JsonValue(JsonType.String);

        // Preprompt de base + instructions CPM dynamiques (VI1 + VI2)
        string fullSystem = preprompt + computationalModel.BuildDynamicSystemInstructions();
        systemContent.StringValue = Regex.Replace(Regex.Replace(fullSystem, "[\"\']", ""), "\\s", " ");

        systemTurn.ObjectValues.Add("role", systemRole);
        systemTurn.ObjectValues.Add("content", systemContent);
        fullConv.ArrayValues.Add(systemTurn);
        fullConv.ArrayValues.AddRange(conversationList.ArrayValues);

        JsonValue data = new JsonValue(JsonType.Object);
        JsonValue modelNameValue = new JsonValue(JsonType.String);
        modelNameValue.StringValue = modelName;
        data.ObjectValues.Add("model", modelNameValue);
        data.ObjectValues.Add("messages", fullConv);
        JsonValue streamValue = new JsonValue(JsonType.Boolean);
        streamValue.BoolValue = false;
        data.ObjectValues.Add("stream", streamValue);

        string endPointS = endPoint == EndPoint.OpenWebUI ? "api/chat/completions" : "api/chat";
        StartCoroutine(ChatRequest(urlOllama + endPointS, data.ToJsonString()));
    }

    // ---------------------------------------------------------------
    //  ANALYSE EMOTIONNELLE
    // ---------------------------------------------------------------

    private void UserAnalysis(string content)
    {
        string endPointS = endPoint == EndPoint.OpenWebUI ? "api/chat/completions" : "api/chat";
        StartCoroutine(UserRequest(urlOllama + endPointS, BuildAnalysisJson(content)));
    }

    private void LLMAnalysis(string content)
    {
        string endPointS = endPoint == EndPoint.OpenWebUI ? "api/chat/completions" : "api/chat";
        StartCoroutine(LLMRequest(urlOllama + endPointS, BuildAnalysisJson(content)));
    }

    private string BuildAnalysisJson(string content)
    {
        JsonValue fullConv = new JsonValue(JsonType.Array);

        JsonValue systemTurn = new JsonValue(JsonType.Object);
        JsonValue systemRole = new JsonValue(JsonType.String);
        systemRole.StringValue = "system";
        JsonValue systemContent = new JsonValue(JsonType.String);
        systemContent.StringValue = "Tu es un systeme d'analyse des emotions. Reponds uniquement par une valeur entiere entre 0 et 100 representant l'intensite emotionnelle detectee. Aucun autre mot.";
        systemTurn.ObjectValues.Add("role", systemRole);
        systemTurn.ObjectValues.Add("content", systemContent);
        fullConv.ArrayValues.Add(systemTurn);

        JsonValue userTurn = new JsonValue(JsonType.Object);
        JsonValue userRole = new JsonValue(JsonType.String);
        userRole.StringValue = "user";
        JsonValue userContent = new JsonValue(JsonType.String);
        userContent.StringValue = content;
        userTurn.ObjectValues.Add("role", userRole);
        userTurn.ObjectValues.Add("content", userContent);
        fullConv.ArrayValues.Add(userTurn);

        JsonValue data = new JsonValue(JsonType.Object);
        JsonValue modelNameValue = new JsonValue(JsonType.String);
        modelNameValue.StringValue = modelName;
        data.ObjectValues.Add("model", modelNameValue);
        data.ObjectValues.Add("messages", fullConv);
        JsonValue streamValue = new JsonValue(JsonType.Boolean);
        streamValue.BoolValue = false;
        data.ObjectValues.Add("stream", streamValue);

        return data.ToJsonString();
    }

    // ---------------------------------------------------------------
    //  AUDIO — Piper TTS
    // ---------------------------------------------------------------

    public void PlayAudio(int a)
    {
        try
        {
            AudioClip music = (AudioClip)Resources.Load("Sounds/" + a);
            audioSource.PlayOneShot(music, volume);
        }
        catch (Exception e) { UnityEngine.Debug.LogException(e); }
    }

    public void PlayAudio(string text)
    {
        if (!usePiper)
        {
#if UNITY_STANDALONE_WIN
            Narrator.speak(text);
#else
            Debug.Log("Narrator not available");
#endif
        }
        else
        {
            StartCoroutine(postTTSRequest(text));
        }
    }

    IEnumerator postTTSRequest(string text)
    {
        text = Regex.Replace(Regex.Replace(text, "[\"\']", ""), "\\s", " ");
        var uwr = new UnityWebRequest("http://localhost:" + piperPort.ToString(), "POST");
        byte[] jsonToSend = new System.Text.UTF8Encoding().GetBytes(
            "{ \"text\": \"" + text + "\" , \"speaker_id\": " + speakerID.ToString() + "}");
        uwr.uploadHandler   = (UploadHandler)new UploadHandlerRaw(jsonToSend);
        uwr.downloadHandler = (DownloadHandler)new DownloadHandlerBuffer();
        uwr.SetRequestHeader("Content-Type", "application/json");

        yield return uwr.SendWebRequest();

        byte[] wavData = uwr.downloadHandler.data;
        if (usePhonemeGenerator)
        {
            string json = Wav2VecClient.SendWav(wavData);
            Debug.Log("Python returned: " + json);
        }

        AudioClip clip = WavUtility.ToAudioClip(wavData, "DownloadedClip");
        audioSource.clip = clip;
        audioSource.Play();
    }

    // ---------------------------------------------------------------
    //  UI
    // ---------------------------------------------------------------

    public void InformationDisplay(string s)
        => informationPanel.GetComponentInChildren<Text>().text = s;

    public void DisplayQuestion(string s)
        => textPanel.GetComponentInChildren<Text>().text = s;

    public void EndDialog()
    {
        anim.SetTrigger("Greet");
        Debug.Log("[BILAN INTERACTION] " + computationalModel.GetEngagementSummary());
    }

    public void DisplayAUs(int[] aus, int[] intensities, float duration)
        => faceExpression.setFacialAUs(aus, intensities, duration);

    public void Doubt(float intensity_factor, float duration)
    {
        DisplayAUs(
            new int[] { 6, 4, 14 },
            new int[] { (int)(intensity_factor * 100), (int)(intensity_factor * 80), (int)(intensity_factor * 80) },
            duration);
    }
}
