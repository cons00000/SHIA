using System;
using System.IO;
using Assets.Scripts;
using UnityEngine;

/// <summary>
/// Enregistre les métriques d'engagement (VD1) dans un fichier CSV horodaté.
/// Attachez ce script au même GameObject que LMStudioDialogManager.
/// Appelez LogTurn() à chaque tour, et SaveToCSV() à la fin de la session.
/// </summary>
public class InteractionLogger : MonoBehaviour
{
    [Header("Export")]
    [Tooltip("Identifiant participant (à renseigner avant chaque passation)")]
    public string ParticipantID = "P00";

    [Tooltip("Dossier de sortie relatif au projet Unity (Assets/Logs par défaut)")]
    public string OutputFolder = "Assets/Logs";

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
    }

    private System.Collections.Generic.List<TurnRecord> _records
        = new System.Collections.Generic.List<TurnRecord>();

    private int _turnIndex = 0;

    /// <summary>
    /// Enregistre un tour de parole.
    /// </summary>
    public void LogTurn(string role, string message,
                        ComputationalModel model)
    {
        _records.Add(new TurnRecord
        {
            TurnIndex          = _turnIndex++,
            Role               = role,
            MessageLength      = message?.Length ?? 0,
            ContainsQuestion   = message != null && message.Contains("?"),
            KnowledgeLevel     = model.UserKnowledge.ToString(),
            Posture            = model.CurrentPosture.ToString(),
            MotivationalProfile = model.ActiveProfile.ToString(),
            Novelty            = model.Novelty,
            Complexity         = model.Complexity,
            CopingPotential    = model.CopingPotential,
            GoalRelevance      = model.GoalRelevance,
            AverageUserLength  = model.AverageMessageLength,
            AverageAgentLength = model.AverageAgentMessageLength,
            LastUserWords      = model.LastUserWordCount,
            LastAgentWords     = model.LastAgentWordCount,
            EstimatedLastUserSpeechSec = model.EstimatedLastUserSpeechSec,
            EstimatedLastAgentSpeechSec = model.EstimatedLastAgentSpeechSec,
            MaxRecommendedAgentSpeechSec = model.MaxRecommendedAgentSpeechSec,
            MaxAgentToUserSpeechRatio = model.MaxAgentToUserSpeechRatio,
            DialogueBalance    = model.DialogueBalance,
            AgentToUserRatio   = model.AgentToUserLengthRatio,
            TimestampSec       = Time.time
        });
    }

    /// <summary>
    /// Exporte les données dans un CSV horodaté.
    /// Appelé automatiquement à la destruction du GameObject (fin de session).
    /// </summary>
    void OnDestroy() => SaveToCSV();

    public void SaveToCSV()
    {
        if (_records.Count == 0) return;

        if (!Directory.Exists(OutputFolder))
            Directory.CreateDirectory(OutputFolder);

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string path = Path.Combine(OutputFolder, $"interaction_{ParticipantID}_{timestamp}.csv");

        using (var sw = new StreamWriter(path, false, System.Text.Encoding.UTF8))
        {
            sw.WriteLine("TurnIndex,Role,MessageLength,ContainsQuestion," +
                         "KnowledgeLevel,Posture,MotivationalProfile,Condition," +
                         "Novelty,Complexity,CopingPotential,GoalRelevance," +
                         "AverageUserLength,AverageAgentLength,LastUserWords,LastAgentWords," +
                         "EstimatedLastUserSpeechSec,EstimatedLastAgentSpeechSec," +
                         "MaxRecommendedAgentSpeechSec,MaxAgentToUserSpeechRatio," +
                         "DialogueBalance,AgentToUserRatio,TimestampSec");

            foreach (var r in _records)
            {
                sw.WriteLine($"{r.TurnIndex},{r.Role},{r.MessageLength}," +
                             $"{r.ContainsQuestion},{r.KnowledgeLevel},{r.Posture}," +
                             $"{r.MotivationalProfile},{r.Condition}," +
                             $"{r.Novelty:F2},{r.Complexity:F2},{r.CopingPotential:F2},{r.GoalRelevance:F2}," +
                             $"{r.AverageUserLength:F1},{r.AverageAgentLength:F1}," +
                             $"{r.LastUserWords},{r.LastAgentWords}," +
                             $"{r.EstimatedLastUserSpeechSec:F2},{r.EstimatedLastAgentSpeechSec:F2}," +
                             $"{r.MaxRecommendedAgentSpeechSec:F2},{r.MaxAgentToUserSpeechRatio:F2}," +
                             $"{r.DialogueBalance:F2},{r.AgentToUserRatio:F2},{r.TimestampSec:F2}");
            }
        }

        Debug.Log($"[InteractionLogger] Session exportée → {path}");
    }
}
