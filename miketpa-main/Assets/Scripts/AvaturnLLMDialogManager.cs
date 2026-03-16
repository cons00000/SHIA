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


/*
* La classe LLMDialogManager permet de centraliser les fonctionnalités liés à l'aspect conversationnel de l'agent en Full Audio en utilisant un LLM hébergé sur un serveur distant. 
* ATTENTION : pour faire fonctionner le plugin Whisper de Macoron, il faut ajouter les modèles dans le répertoire 
* StreamingAssets. Allez voir les pages dédiées de ces modules pour plus d'explications. Ils ne sont pas fournis par défaut car ils prennent
* trop de place.
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
    public FacialExpressionAvaturn faceExpression;
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
    public int numberOfTurn = 10;
    private JsonParser jsonParser = new JsonParser();
    private JsonValue conversationList = new JsonValue(JsonType.Array);

    //LLM

    public string urlOllama;
    public EndPoint endPoint = EndPoint.OpenWebUI; // api/chat/completions
    public string modelName;
    public string APIkey;
    [TextArea(15, 20)]
    public string preprompt;
    private string _response;

    //piper
    public bool usePiper = true;
    public int piperPort = 5000;
    public float speakerID = 1;

    public bool usePhonemeGenerator = false;

    //ComputationalModel
    private ComputationalModel computationalModel = new ComputationalModel();

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
        UserAnalysis(text);
        if (printLanguage)
            text += $"\n\nLanguage: {res.Language}";
        Text textp = textPanel.transform.GetComponentInChildren<Text>().GetComponent<Text>();
        textp.text = text;
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


    IEnumerator ChatRequest(string url, string json)
    {
        var uwr = new UnityWebRequest(url, "POST");
        byte[] jsonToSend = new System.Text.UTF8Encoding().GetBytes(json);
        uwr.uploadHandler = (UploadHandler)new UploadHandlerRaw(jsonToSend);
        uwr.downloadHandler = (DownloadHandler)new DownloadHandlerBuffer();
        uwr.SetRequestHeader("Content-Type", "application/json");
        uwr.SetRequestHeader("Authorization", "Bearer " + APIkey);

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
            JsonValue response = jsonParser.Parse(_response);
            String responseString = "";
            if (endPoint == EndPoint.OpenWebUI)
            {
                responseString = response.ObjectValues["choices"].ArrayValues[0].ObjectValues["message"].ObjectValues["content"].StringValue;
            }
            else if (endPoint == EndPoint.Ollama)
            {
                responseString = response.ObjectValues["message"].ObjectValues["content"].StringValue;
            }
            InformationDisplay(responseString);
            //_response = ProcessAffectiveContent(responseString);
            _response = responseString;
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
    }

    IEnumerator UserRequest(string url, string json)
    {
        var uwr = new UnityWebRequest(url, "POST");
        byte[] jsonToSend = new System.Text.UTF8Encoding().GetBytes(json);
        uwr.uploadHandler = (UploadHandler)new UploadHandlerRaw(jsonToSend);
        uwr.downloadHandler = (DownloadHandler)new DownloadHandlerBuffer();
        uwr.SetRequestHeader("Content-Type", "application/json");
        uwr.SetRequestHeader("Authorization", "Bearer " + APIkey);

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
            JsonValue response = jsonParser.Parse(_response);
            computationalModel.UserValues(response.StringValue);
        }
    }

    IEnumerator LLMRequest(string url, string json)
    {
        var uwr = new UnityWebRequest(url, "POST");
        byte[] jsonToSend = new System.Text.UTF8Encoding().GetBytes(json);
        uwr.uploadHandler = (UploadHandler)new UploadHandlerRaw(jsonToSend);
        uwr.downloadHandler = (DownloadHandler)new DownloadHandlerBuffer();
        uwr.SetRequestHeader("Content-Type", "application/json");
        uwr.SetRequestHeader("Authorization", "Bearer " + APIkey);

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
            JsonValue response = jsonParser.Parse(_response);
            computationalModel.LLMValues(response.StringValue);
        }
    }



    /*private string ProcessAffectiveContent(string response)
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

    */

    private void SendToChat(JsonValue conversationList)
    {
        if (conversationList.ArrayValues.Count == 0)
            return;
        JsonValue fullConv = new JsonValue(JsonType.Array);
        JsonValue systemTurn = new JsonValue(JsonType.Object);
        JsonValue systemRole = new JsonValue(JsonType.String);
        systemRole.StringValue = "system";
        JsonValue systemContent = new JsonValue(JsonType.String);
        systemContent.StringValue = Regex.Replace(Regex.Replace(preprompt, "[\"\']", ""), "\\s", " ");
        //systemContent.StringValue = "Tu t'appelles John et tu réponds avec un niveau de patience qui va de 1, très patient, à 5, très impatient. Le niveau de patience actuelle est égale à :" +computationalModel.getEmotion();
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
        string endPointS = "";
        if (endPoint == EndPoint.OpenWebUI)
        {
            endPointS = "api/chat/completions";
        }
        if (endPoint == EndPoint.Ollama)
        {
            endPointS = "api/chat";
        }
        StartCoroutine(ChatRequest(urlOllama + endPointS, data.ToJsonString()));
    }

    private void UserAnalysis(String content)
    {

        JsonValue fullConv = new JsonValue(JsonType.Array);
        JsonValue systemTurn = new JsonValue(JsonType.Object);
        JsonValue systemRole = new JsonValue(JsonType.String);
        systemRole.StringValue = "system";
        JsonValue systemContent = new JsonValue(JsonType.String);
        systemContent.StringValue = "Tu es un système d'analyse des émotions. Quand je te parle tu réponds une valeur entière entre 0 et 100 d'intensité émotionnelle que tu détectes dans ma phrase. Tu ne dis rien d'autre que la valeur. Tu ne dis pas un mot, juste la valeur numérique, comme une machine.";
        systemTurn.ObjectValues.Add("role", systemRole);
        systemTurn.ObjectValues.Add("content", systemContent);
        fullConv.ArrayValues.Add(systemTurn);
        JsonValue userTurn = new JsonValue(JsonType.Object);
        JsonValue userRole = new JsonValue(JsonType.String);
        userRole.StringValue = "user";
        JsonValue userContent = new JsonValue(JsonType.String);
        userContent.StringValue = content;
        userTurn.ObjectValues.Add("role",userRole);
        userTurn.ObjectValues.Add("content",userContent);
        fullConv.ArrayValues.Add(userTurn);
        JsonValue data = new JsonValue(JsonType.Object);
        JsonValue modelNameValue = new JsonValue(JsonType.String);
        modelNameValue.StringValue = modelName;
        data.ObjectValues.Add("model", modelNameValue);
        data.ObjectValues.Add("messages", fullConv);
        JsonValue streamValue = new JsonValue(JsonType.Boolean);
        streamValue.BoolValue = false;
        data.ObjectValues.Add("stream", streamValue);
        string endPointS = "";
        if (endPoint == EndPoint.OpenWebUI)
        {
            endPointS = "api/chat/completions";
        }
        if (endPoint == EndPoint.Ollama)
        {
            endPointS = "api/chat";
        }
        StartCoroutine(UserRequest(urlOllama + endPointS, data.ToJsonString()));
    }

    private void LLMAnalysis(String content)
    {
        JsonValue fullConv = new JsonValue(JsonType.Array);
        JsonValue systemTurn = new JsonValue(JsonType.Object);
        JsonValue systemRole = new JsonValue(JsonType.String);
        systemRole.StringValue = "system";
        JsonValue systemContent = new JsonValue(JsonType.String);
        systemContent.StringValue = "Tu es un système d'analyse des émotions. Quand je te parle tu réponds une valeur entière entre 0 et 100 d'intensité émotionnelle que tu détectes dans ma phrase. Tu ne dis rien d'autre que la valeur. Tu ne dis pas un mot, juste la valeur numérique, comme une machine.";
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
        string endPointS = "";
        if (endPoint == EndPoint.OpenWebUI)
        {
            endPointS = "api/chat/completions";
        }
        if (endPoint == EndPoint.Ollama)
        {
            endPointS = "api/chat";
        }
        StartCoroutine(LLMRequest(urlOllama + endPointS, data.ToJsonString()));
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


    IEnumerator postTTSRequest(string text)
    {
        text = Regex.Replace(Regex.Replace(text, "[\"\']", ""), "\\s"," ");
        var uwr = new UnityWebRequest("http://localhost:"+ piperPort.ToString(), "POST");
        byte[] jsonToSend = new System.Text.UTF8Encoding().GetBytes("{ \"text\": \"" + text + "\" , \"speaker_id\": " + speakerID.ToString()+"}");
        uwr.uploadHandler = (UploadHandler)new UploadHandlerRaw(jsonToSend);
        uwr.downloadHandler = (DownloadHandler)new DownloadHandlerBuffer();
        uwr.SetRequestHeader("Content-Type", "application/json");

        //Send the request then wait here until it returns
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


    /*
     * Cette méthode permet de demander à piperTTS de générer un audio, puis de le jouer, à partir du texte
     * piperTTS server doit donc être lancé sur la machine.
     */
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

    /*
    * Exemple de fonction déclenchant une expression émotionnelle
    * intensity_factor devrait être entre 0 et 1
    */
    public void Doubt(float intensity_factor, float duration)
    {
        DisplayAUs(new int[] { 6, 4, 14 }, new int[] { (int)(intensity_factor * 100), (int)(intensity_factor * 80), (int)(intensity_factor * 80) }, duration);
    }


}
