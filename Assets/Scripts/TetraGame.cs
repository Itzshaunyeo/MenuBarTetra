using System.Collections.Generic;
using UnityEngine;

/// <summary>A compact, keyboard-first Tetris game with a landing ghost and three-piece preview queue.</summary>
public sealed class TetraGame : MonoBehaviour
{
    const int Width = 10, Height = 20;
    const float DesignWidth = 430f, DesignHeight = 820f;
    static readonly Rect BoardRect = new Rect(26, 130, 245, 570);
    readonly Transform[,] settled = new Transform[Width, Height];
    readonly List<Transform> falling = new List<Transform>(4);
    readonly List<Transform> ghost = new List<Transform>(4);
    readonly Queue<int> nextPieces = new Queue<int>();
    readonly Color[] colors = {
        new Color(.18f, .83f, 1f), new Color(1f, .77f, .16f), new Color(.72f, .36f, 1f),
        new Color(1f, .32f, .46f), new Color(.25f, .88f, .52f), new Color(1f, .52f, .16f), new Color(.26f, .42f, 1f)
    };
    Vector2Int[] cells;
    Vector2Int pivot;
    float dropTimer, dropInterval = .72f;
    int score, lines;
    bool gameOver, paused;
    Texture2D pixel;
    GUIStyle titleStyle, captionStyle, statStyle, valueStyle, controlStyle, messageStyle;
    Camera boardCamera;
    float uiScale;
    Vector2 uiOrigin;

    void Awake()
    {
        Application.targetFrameRate = 60;
        pixel = new Texture2D(1, 1); pixel.SetPixel(0, 0, Color.white); pixel.Apply();
        CreateWorld();
        Restart();
    }

    void OnDestroy() { if (pixel) Destroy(pixel); }

    void Update()
    {
        UpdateLayout();
        if (Input.GetKeyDown(KeyCode.R)) { Restart(); return; }
        if (Input.GetKeyDown(KeyCode.Escape)) Application.Quit();
        if (Input.GetKeyDown(KeyCode.P) && !gameOver) paused = !paused;
        if (gameOver || paused) return;
        if (Input.GetKeyDown(KeyCode.LeftArrow)) TryMove(Vector2Int.left);
        if (Input.GetKeyDown(KeyCode.RightArrow)) TryMove(Vector2Int.right);
        if (Input.GetKeyDown(KeyCode.DownArrow)) StepDown();
        if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.X)) TryRotate(1);
        if (Input.GetKeyDown(KeyCode.Z)) TryRotate(-1);
        if (Input.GetKeyDown(KeyCode.Space)) while (StepDown()) { }
        dropTimer += Time.deltaTime;
        if (dropTimer >= dropInterval) { dropTimer = 0; StepDown(); }
    }

    void CreateWorld()
    {
        var backdrop = new GameObject("Backdrop Camera").AddComponent<Camera>();
        // This camera only paints the full-window background. It must not render the board a second time.
        backdrop.depth = -2; backdrop.clearFlags = CameraClearFlags.SolidColor; backdrop.cullingMask = 0; backdrop.backgroundColor = new Color(.045f, .035f, .13f);
        var cam = new GameObject("Playfield Camera").AddComponent<Camera>();
        boardCamera = cam;
        cam.depth = 0; cam.clearFlags = CameraClearFlags.SolidColor; cam.orthographic = true; cam.orthographicSize = 11.8f;
        cam.transform.position = new Vector3(4.5f, 9.5f, -25); cam.backgroundColor = new Color(.035f, .055f, .14f);
        var light = new GameObject("Soft Light").AddComponent<Light>();
        light.type = LightType.Directional; light.intensity = 1.15f; light.transform.rotation = Quaternion.Euler(32, -25, 0);
        for (int x = 0; x <= Width; x++) MakeLine(new Vector3(x - .5f, Height / 2f - .5f, .45f), new Vector3(.026f, Height, .02f));
        for (int y = 0; y <= Height; y++) MakeLine(new Vector3(Width / 2f - .5f, y - .5f, .45f), new Vector3(Width, .026f, .02f));
        UpdateLayout();
    }

    // Keep the visual layout identical in the 430x820 Windows player and in any Unity Game-view aspect ratio.
    void UpdateLayout()
    {
        uiScale = Mathf.Min(Screen.width / DesignWidth, Screen.height / DesignHeight);
        uiOrigin = new Vector2((Screen.width - DesignWidth * uiScale) * .5f, (Screen.height - DesignHeight * uiScale) * .5f);
        if (!boardCamera || Screen.width == 0 || Screen.height == 0) return;
        boardCamera.rect = new Rect(
            (uiOrigin.x + BoardRect.x * uiScale) / Screen.width,
            (Screen.height - (uiOrigin.y + (BoardRect.y + BoardRect.height) * uiScale)) / Screen.height,
            BoardRect.width * uiScale / Screen.width,
            BoardRect.height * uiScale / Screen.height);
    }

    void MakeLine(Vector3 position, Vector3 scale)
    {
        var line = GameObject.CreatePrimitive(PrimitiveType.Cube); line.name = "Grid";
        line.transform.position = position; line.transform.localScale = scale;
        line.GetComponent<Renderer>().material.color = new Color(.22f, .28f, .53f); Destroy(line.GetComponent<Collider>());
    }

    public void Restart()
    {
        foreach (var t in settled) if (t) Destroy(t.gameObject);
        ClearTransforms(falling); ClearTransforms(ghost);
        System.Array.Clear(settled, 0, settled.Length); nextPieces.Clear();
        for (int i = 0; i < 3; i++) nextPieces.Enqueue(Random.Range(0, 7));
        score = lines = 0; paused = gameOver = false; dropInterval = .72f; Spawn();
    }

    void ClearTransforms(List<Transform> pieces) { foreach (var t in pieces) if (t) Destroy(t.gameObject); pieces.Clear(); }

    void Spawn()
    {
        int type = nextPieces.Dequeue(); nextPieces.Enqueue(Random.Range(0, 7));
        cells = Shape(type); pivot = new Vector2Int(4, 18);
        foreach (var ignored in cells) falling.Add(CreateBlock(colors[type], "Falling Tetromino", .91f));
        if (!Valid(cells, pivot)) { gameOver = true; return; }
        RenderFalling(); UpdateGhost(type);
    }

    static Vector2Int[] Shape(int type) => type switch {
        0 => new[] { new Vector2Int(-1,0), new Vector2Int(0,0), new Vector2Int(1,0), new Vector2Int(2,0) },
        1 => new[] { new Vector2Int(0,0), new Vector2Int(1,0), new Vector2Int(0,1), new Vector2Int(1,1) },
        2 => new[] { new Vector2Int(-1,0), new Vector2Int(0,0), new Vector2Int(1,0), new Vector2Int(0,1) },
        3 => new[] { new Vector2Int(-1,0), new Vector2Int(0,0), new Vector2Int(0,1), new Vector2Int(1,1) },
        4 => new[] { new Vector2Int(-1,1), new Vector2Int(0,1), new Vector2Int(0,0), new Vector2Int(1,0) },
        5 => new[] { new Vector2Int(-1,0), new Vector2Int(0,0), new Vector2Int(1,0), new Vector2Int(1,1) },
        _ => new[] { new Vector2Int(-1,0), new Vector2Int(0,0), new Vector2Int(1,0), new Vector2Int(-1,1) }
    };

    Transform CreateBlock(Color color, string blockName, float scale)
    {
        var block = GameObject.CreatePrimitive(PrimitiveType.Cube); block.name = blockName; block.transform.localScale = Vector3.one * scale;
        block.GetComponent<Renderer>().material.color = color; Destroy(block.GetComponent<Collider>()); return block.transform;
    }

    bool Valid(Vector2Int[] test, Vector2Int at)
    {
        foreach (var c in test) { var p = at + c; if (p.x < 0 || p.x >= Width || p.y < 0 || p.y >= Height || settled[p.x, p.y]) return false; }
        return true;
    }
    void TryMove(Vector2Int delta) { if (Valid(cells, pivot + delta)) { pivot += delta; RenderFalling(); UpdateGhostColor(); } }
    void TryRotate(int direction)
    {
        var rotated = new Vector2Int[cells.Length];
        for (int i = 0; i < cells.Length; i++) rotated[i] = direction > 0 ? new Vector2Int(-cells[i].y, cells[i].x) : new Vector2Int(cells[i].y, -cells[i].x);
        if (Valid(rotated, pivot)) { cells = rotated; RenderFalling(); UpdateGhostColor(); }
    }
    bool StepDown()
    {
        if (Valid(cells, pivot + Vector2Int.down)) { pivot += Vector2Int.down; RenderFalling(); UpdateGhostColor(); return true; }
        Lock(); return false;
    }
    void Lock()
    {
        for (int i = 0; i < cells.Length; i++) settled[pivot.x + cells[i].x, pivot.y + cells[i].y] = falling[i];
        falling.Clear(); ClearTransforms(ghost); ClearLines(); Spawn();
    }
    void ClearLines()
    {
        int removed = 0;
        for (int y = 0; y < Height; y++)
        {
            bool full = true; for (int x = 0; x < Width; x++) if (!settled[x, y]) { full = false; break; }
            if (!full) continue;
            for (int x = 0; x < Width; x++) Destroy(settled[x, y].gameObject);
            for (int yy = y; yy < Height - 1; yy++) for (int x = 0; x < Width; x++) { settled[x, yy] = settled[x, yy + 1]; if (settled[x, yy]) settled[x, yy].position += Vector3.down; }
            for (int x = 0; x < Width; x++) settled[x, Height - 1] = null; y--; removed++;
        }
        if (removed > 0) { lines += removed; score += removed * removed * 100; dropInterval = Mathf.Max(.12f, .72f - lines * .015f); }
    }
    void RenderFalling() { for (int i = 0; i < falling.Count; i++) falling[i].position = new Vector3(pivot.x + cells[i].x, pivot.y + cells[i].y, -.35f); }
    Vector2Int LandingPivot() { var landing = pivot; while (Valid(cells, landing + Vector2Int.down)) landing += Vector2Int.down; return landing; }
    void UpdateGhost(int type) { ClearTransforms(ghost); for (int i = 0; i < cells.Length; i++) ghost.Add(CreateBlock(Dim(colors[type]), "Landing Ghost", .70f)); UpdateGhostColor(); }
    void UpdateGhostColor()
    {
        if (gameOver) return; var landing = LandingPivot();
        for (int i = 0; i < ghost.Count; i++) ghost[i].position = new Vector3(landing.x + cells[i].x, landing.y + cells[i].y, -.08f);
    }
    static Color Dim(Color color) { return Color.Lerp(new Color(.08f, .10f, .22f), color, .28f); }

    void SetStyles()
    {
        if (titleStyle != null) return;
        titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 25, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
        captionStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, normal = { textColor = new Color(.61f, .65f, .90f) } };
        statStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, normal = { textColor = new Color(.75f, .78f, .96f) } };
        valueStyle = new GUIStyle(statStyle) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight, normal = { textColor = Color.white } };
        controlStyle = new GUIStyle(statStyle) { fontSize = 11, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(.65f, .69f, .9f) } };
        messageStyle = new GUIStyle(titleStyle) { fontSize = 23, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, .84f, .32f) } };
    }
    void DrawRect(Rect rect, Color color) { GUI.color = color; GUI.DrawTexture(rect, pixel); GUI.color = Color.white; }
    void DrawBorder(Rect rect, Color color, float thickness) { DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color); DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color); DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color); DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color); }

    void OnGUI()
    {
        UpdateLayout();
        SetStyles();
        GUI.matrix = Matrix4x4.TRS(uiOrigin, Quaternion.identity, new Vector3(uiScale, uiScale, 1));
        var board = BoardRect;
        var side = new Rect(286, 130, 128, 570);
        DrawRect(new Rect(16, 15, 398, 100), new Color(.12f, .10f, .32f));
        DrawBorder(new Rect(16, 15, 398, 790), new Color(.35f, .34f, .73f), 2);
        GUI.Label(new Rect(31, 28, 230, 35), "MENU BAR TETRA", titleStyle);
        GUI.Label(new Rect(33, 62, 250, 20), paused ? "PAUSED - Press P to continue" : "Ready in the menu bar", captionStyle);
        GUI.Label(new Rect(300, 28, 82, 35), score.ToString("000000"), valueStyle);
        DrawRect(board, new Color(.025f, .04f, .12f, .18f)); DrawBorder(board, new Color(.45f, .43f, .86f), 3);
        DrawRect(side, new Color(.12f, .10f, .32f)); DrawBorder(side, new Color(.34f, .33f, .69f), 2);
        // The queue is initialized before the first frame, but keep the HUD safe while Unity is reloading scripts.
        int[] queued = nextPieces.Count == 3 ? nextPieces.ToArray() : new[] { 0, 0, 0 };
        GUI.Label(new Rect(side.x + 12, side.y + 15, side.width - 20, 20), "NEXT", captionStyle);
        DrawPreview(side.x + 14, side.y + 43, queued[0], 15);
        DrawRect(new Rect(side.x + 12, side.y + 125, side.width - 24, 1), new Color(.38f, .36f, .71f));
        GUI.Label(new Rect(side.x + 12, side.y + 143, side.width - 24, 20), "LINES", statStyle); GUI.Label(new Rect(side.x + 12, side.y + 158, side.width - 24, 28), lines.ToString(), valueStyle);
        GUI.Label(new Rect(side.x + 12, side.y + 205, side.width - 24, 20), "LEVEL", statStyle); GUI.Label(new Rect(side.x + 12, side.y + 220, side.width - 24, 28), (1 + lines / 10).ToString(), valueStyle);
        DrawRect(new Rect(side.x + 12, side.y + 270, side.width - 24, 1), new Color(.38f, .36f, .71f));
        GUI.Label(new Rect(side.x + 12, side.y + 286, side.width - 20, 20), "UP NEXT", captionStyle);
        DrawPreview(side.x + 14, side.y + 315, queued[1], 10);
        DrawPreview(side.x + 14, side.y + 385, queued[2], 10);
        GUI.Label(new Rect(25, 716, 380, 20), gameOver ? "Game over. Press R to play again." : "Playing. Keyboard focus is captured.", captionStyle);
        GUI.Label(new Rect(22, 748, 386, 42), "ARROWS move/drop   Z / X rotate   SPACE hard drop\nR restart   P pause   ESC quit", controlStyle);
        if (gameOver) { DrawRect(new Rect(board.x + 12, 380, board.width - 24, 78), new Color(.05f, .04f, .17f, .92f)); GUI.Label(new Rect(board.x + 12, 388, board.width - 24, 60), "GAME OVER\nPress R to restart", messageStyle); }
        else if (paused) { DrawRect(new Rect(board.x + 12, 390, board.width - 24, 55), new Color(.05f, .04f, .17f, .92f)); GUI.Label(new Rect(board.x + 12, 395, board.width - 24, 42), "PAUSED", messageStyle); }
        GUI.matrix = Matrix4x4.identity;
    }
    void DrawPreview(float x, float y, int type, float size)
    {
        var shape = Shape(type); int minX = 99, minY = 99; foreach (var c in shape) { minX = Mathf.Min(minX, c.x); minY = Mathf.Min(minY, c.y); }
        foreach (var c in shape) { var r = new Rect(x + (c.x - minX) * size, y + (1 - (c.y - minY)) * size, size - 2, size - 2); DrawRect(r, colors[type]); DrawBorder(r, new Color(1, 1, 1, .35f), 1); }
    }
}
