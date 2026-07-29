using UnityEngine;

/// <summary>Renders the end-of-run summary without coupling it to board gameplay.</summary>
public sealed class GameOverSummaryUI
{
    public void Draw(Texture2D pixel, RunStats stats, int difficultyStage, GUIStyle messageStyle, GUIStyle captionStyle, GUIStyle statStyle, GUIStyle valueStyle)
    {
        var panel = new Rect(24, 205, 382, 430);
        DrawRect(pixel, panel, new Color(.055f, .04f, .16f, .97f));
        DrawBorder(pixel, panel, new Color(.78f, .64f, 1f), 3);
        GUI.Label(new Rect(panel.x + 20, panel.y + 20, panel.width - 40, 42), "GAME OVER", new GUIStyle(messageStyle) { fontSize = 29 });
        GUI.Label(new Rect(panel.x + 20, panel.y + 66, panel.width - 40, 20), "RUN SUMMARY", new GUIStyle(captionStyle) { alignment = TextAnchor.MiddleCenter });
        DrawRect(pixel, new Rect(panel.x + 24, panel.y + 94, panel.width - 48, 1), new Color(.42f, .38f, .74f));
        DrawRow(pixel, panel, 112, "FINAL SCORE", stats.Score.ToString("000000"), statStyle, valueStyle);
        DrawRow(pixel, panel, 158, "HIGHEST COMBO", "x" + stats.HighestCombo, statStyle, valueStyle);
        DrawRow(pixel, panel, 204, "LINES CLEARED", stats.LinesCleared.ToString(), statStyle, valueStyle);
        DrawRow(pixel, panel, 250, "GRAVITY CHANGES", stats.GravityChangesSurvived.ToString(), statStyle, valueStyle);
        DrawRow(pixel, panel, 296, "TIME PLAYED", stats.FormatPlayTime(), statStyle, valueStyle);
        DrawRow(pixel, panel, 342, "DIFFICULTY REACHED", "STAGE " + difficultyStage, statStyle, valueStyle);
        GUI.Label(new Rect(panel.x + 20, panel.y + 392, panel.width - 40, 24), "Press R to restart", new GUIStyle(captionStyle) { alignment = TextAnchor.MiddleCenter });
    }

    static void DrawRow(Texture2D pixel, Rect panel, float offsetY, string label, string value, GUIStyle statStyle, GUIStyle valueStyle)
    {
        GUI.Label(new Rect(panel.x + 28, panel.y + offsetY, 220, 28), label, statStyle);
        GUI.Label(new Rect(panel.x + 210, panel.y + offsetY - 3, panel.width - 238, 32), value, valueStyle);
        DrawRect(pixel, new Rect(panel.x + 24, panel.y + offsetY + 35, panel.width - 48, 1), new Color(.27f, .25f, .56f));
    }

    static void DrawRect(Texture2D pixel, Rect rect, Color color)
    {
        GUI.color = color; GUI.DrawTexture(rect, pixel); GUI.color = Color.white;
    }

    static void DrawBorder(Texture2D pixel, Rect rect, Color color, float thickness)
    {
        DrawRect(pixel, new Rect(rect.x, rect.y, rect.width, thickness), color);
        DrawRect(pixel, new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
        DrawRect(pixel, new Rect(rect.x, rect.y, thickness, rect.height), color);
        DrawRect(pixel, new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
    }
}
