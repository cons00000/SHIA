using System;
using System.IO;
using Assets.Scripts;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Enregistre les métriques d'engagement (VD1) dans un fichier Markdown horodaté.
/// Attachez ce script au même GameObject que LMStudioDialogManager.
/// Appelez LogTurn() à chaque tour.
/// </summary>
public class InteractionLogger : MonoBehaviour
{
    [Header("Export")]
    [Tooltip("Identifiant participant (à renseigner avant chaque passation)")]
    public string ParticipantID = "P00";

    [Tooltip("Dossier de sortie relatif au dossier Assets (ex: 'Logs/Sessions')")]
    public string OutputFolder = "Logs/Sessions";

    [Tooltip("Libelle exporte dans la colonne Cond. du log Markdown")]
    public string ConditionLabel = "Adaptive";

    private struct TurnRecord
    {
        public int    TurnIndex;
        public string Role;         // "User" ou "Agent"
        public int    MessageLength;
        public bool   ContainsQuestion;
        public string KnowledgeLevel;
        public string Posture;
        public string MotivationalProfile;
        public string Condition;    // Adaptive / Control
        public float  Novelty;
        public float  Complexity;
        public float  CopingPotential;
        public float  GoalRelevance;
        public float  AverageUserLength;
        public float  AverageAgentLength;
        public int    LastUserWords;
        public int    LastAgentWords;
        public float  EstimatedLastUserSpeechSec;
        public float  EstimatedLastAgentSpeechSec;
        public float  MaxRecommendedAgentSpeechSec;
        public float  MaxAgentToUserSpeechRatio;
        public float  DialogueBalance;
        public float  AgentToUserRatio;
        public float  TimestampSec;
        public int    EmotionalIntensity;  // score 0-100 retourné par LLMAnalysis / UserAnalysis
    }

    private System.Collections.Generic.List<TurnRecord> _records
        = new System.Collections.Generic.List<TurnRecord>();

    private int _turnIndex = 0;

    void Awake()
    {
        #if UNITY_EDITOR
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        #endif
    }

    void OnDestroy()
    {
        SaveToMarkdown();

        #if UNITY_EDITOR
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        #endif
    }

    /// <summary>
    /// Enregistre un tour de parole
    /// </summary>
    public void LogTurn(string role, string message, ComputationalModel model, int emotionalIntensity = -1)
    {
        _records.Add(new TurnRecord
        {
            TurnIndex = _turnIndex++,
            Role = role,
            MessageLength = message?.Length ?? 0,
            ContainsQuestion = message != null && message.Contains("?"),
            KnowledgeLevel = model.UserKnowledge.ToString(),
            Posture = model.CurrentPosture.ToString(),
            MotivationalProfile = model.ActiveProfile.ToString(),
            Condition = string.IsNullOrWhiteSpace(ConditionLabel) ? "Adaptive" : ConditionLabel,
            Novelty = model.Novelty,
            Complexity = model.Complexity,
            CopingPotential = model.CopingPotential,
            GoalRelevance = model.GoalRelevance,
            AverageUserLength = model.AverageMessageLength,
            AverageAgentLength = model.AverageAgentMessageLength,
            LastUserWords = model.LastUserWordCount,
            LastAgentWords = model.LastAgentWordCount,
            EstimatedLastUserSpeechSec = model.EstimatedLastUserSpeechSec,
            EstimatedLastAgentSpeechSec = model.EstimatedLastAgentSpeechSec,
            MaxRecommendedAgentSpeechSec = model.MaxRecommendedAgentSpeechSec,
            MaxAgentToUserSpeechRatio = model.MaxAgentToUserSpeechRatio,
            DialogueBalance = model.DialogueBalance,
            AgentToUserRatio = model.AgentToUserLengthRatio,
            TimestampSec = Time.time,
            EmotionalIntensity = emotionalIntensity
        });
    }

    /// <summary>
    /// Met à jour le score d'intensité émotionnelle du dernier tour enregistré pour un rôle donné.
    /// À appeler depuis LLMRequest / UserRequest dès que le score est disponible.
    /// </summary>
    public void PatchLastEmotionalIntensity(string role, int score)
    {
        // On cherche le dernier enregistrement correspondant au rôle (en partant de la fin)
        for (int i = _records.Count - 1; i >= 0; i--)
        {
            var r = _records[i];
            if (r.Role == role)
            {
                r.EmotionalIntensity = score;
                _records[i] = r;
                return;
            }
        }
        Debug.LogWarning($"[InteractionLogger] PatchLastEmotionalIntensity : aucun tour '{role}' trouvé.");
    }

    public void SaveToMarkdown()
    {
        // 1. Vérification si vide
        if (_records.Count == 0)
        {
            Debug.LogWarning("[InteractionLogger] Aucun tour n'a été enregistré ! Vérifiez que LogTurn() est bien appelé dans votre DialogManager.");
            return;
        }

        // 2. Création du dossier relatif à Application.dataPath (le dossier "Assets" de Unity)
        string folderPath = Path.Combine(Application.dataPath, OutputFolder);
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string fileName = $"interaction_{ParticipantID}_{timestamp}.md";
        string path = Path.Combine(folderPath, fileName);

        // 3. Écriture du fichier Markdown
        using (var sw = new StreamWriter(path, false, System.Text.Encoding.UTF8))
        {
            // Titre et métadonnées
            sw.WriteLine($"# Log de Session - Participant {ParticipantID}");
            sw.WriteLine($"**Date de création :** {System.DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss")}  ");
            sw.WriteLine($"**Condition/Dossier :** {OutputFolder}  \n");

            // En-tête du tableau Markdown
            sw.WriteLine("| Turn | Role | Length | ? | Knowledge | Posture | Profile | Cond. | Novelty | Complex | Coping | Goal Rel. | Avg Usr Len | Avg Agt Len | Last Usr W. | Last Agt W. | Est Usr(s) | Est Agt(s) | Max Agt(s) | Max Ratio | Bal. | Ratio | Time(s) | Emo. Int. |");
            sw.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");

            // Lignes de données
            foreach (var r in _records)
            {
                sw.WriteLine($"| {r.TurnIndex} | {r.Role} | {r.MessageLength} | " +
                             $"{r.ContainsQuestion} | {r.KnowledgeLevel} | {r.Posture} | " +
                             $"{r.MotivationalProfile} | {r.Condition} | " +
                             $"{r.Novelty:F2} | {r.Complexity:F2} | {r.CopingPotential:F2} | {r.GoalRelevance:F2} | " +
                             $"{r.AverageUserLength:F1} | {r.AverageAgentLength:F1} | " +
                             $"{r.LastUserWords} | {r.LastAgentWords} | " +
                             $"{r.EstimatedLastUserSpeechSec:F2} | {r.EstimatedLastAgentSpeechSec:F2} | " +
                             $"{r.MaxRecommendedAgentSpeechSec:F2} | {r.MaxAgentToUserSpeechRatio:F2} | " +
                             $"{r.DialogueBalance:F2} | {r.AgentToUserRatio:F2} | {r.TimestampSec:F2} | " +
                             $"{(r.EmotionalIntensity >= 0 ? r.EmotionalIntensity.ToString() : "-")} |");
            }
        }

        Debug.Log($"[InteractionLogger] Session exportée en Markdown → {path}");

        // 4. Nettoyage de la liste pour éviter une double écriture (OnDestroy + OnPlayModeChanged)
        _records.Clear();

        // 5. Dire à Unity de rafraîchir ses fichiers pour afficher le Markdown immédiatement
        #if UNITY_EDITOR
        AssetDatabase.Refresh();
        
        // Sélectionne automatiquement le nouveau fichier dans la fenêtre Project
        string assetPath = $"Assets/{OutputFolder}/{fileName}";
        UnityEngine.Object obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
        if (obj != null)
        {
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = obj;
        }
        #endif
    }

    #if UNITY_EDITOR
    private void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            SaveToMarkdown();
        }
    }
    #endif
}
