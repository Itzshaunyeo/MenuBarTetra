using UnityEngine;

/// <summary>Tracks one playable run's score, combo progression, and end-of-run statistics.</summary>
public sealed class RunStats
{
    public int Score { get; private set; }
    public int LinesCleared { get; private set; }
    public int CurrentCombo { get; private set; }
    public int HighestCombo { get; private set; }
    public int GravityChangesSurvived { get; private set; }
    public float PlayTime { get; private set; }

    public void Reset()
    {
        Score = 0; LinesCleared = 0; CurrentCombo = 0; HighestCombo = 0;
        GravityChangesSurvived = 0; PlayTime = 0;
    }

    public void AddPlayTime(float seconds) { PlayTime += seconds; }
    public void RecordGravityChange() { GravityChangesSurvived++; }

    public void RecordLineClear(int clearedLines)
    {
        if (clearedLines <= 0) { CurrentCombo = 0; return; }
        CurrentCombo++;
        HighestCombo = Mathf.Max(HighestCombo, CurrentCombo);
        LinesCleared += clearedLines;
        Score += ScorePerLine(CurrentCombo) * clearedLines;
    }

    public string FormatPlayTime()
    {
        int seconds = Mathf.FloorToInt(PlayTime);
        return (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
    }

    static int ScorePerLine(int combo)
    {
        if (combo == 1) return 100;
        if (combo == 2) return 250;
        if (combo == 3) return 450;
        return 800 + (combo - 4) * 250;
    }
}
