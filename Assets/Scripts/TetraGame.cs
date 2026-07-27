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
    int currentType, heldType = -1;
    Vector2Int gravity = Vector2Int.down;
    float dropTimer, dropInterval = .72f;
    int score, lines;
    bool gameOver, paused, scoreRecorded, gameStarted, holdUsed;
    string playerName;
    float leaderboardRefreshTimer;
    Texture2D pixel, playerNameBackground;
    Shader gameplayShader;
    AudioSource audioSource;
    AudioClip moveSound, rotateSound, dropSound, holdSound, clearSound, gameOverSound;
    GUIStyle titleStyle, captionStyle, statStyle, valueStyle, controlStyle, messageStyle, rankStyle, menuTitleStyle, startStyle, playerNameStyle;
    Camera boardCamera;
    OnlineLeaderboardClient onlineLeaderboard;
    float uiScale;
    Vector2 uiOrigin;

    void Awake()
    {
        Application.targetFrameRate = 60;
        pixel = new Texture2D(1, 1); pixel.SetPixel(0, 0, Color.white); pixel.Apply();
        playerNameBackground = new Texture2D(1, 1); playerNameBackground.SetPixel(0, 0, new Color(.39f, .30f, .62f)); playerNameBackground.Apply();
        // A Resources shader is guaranteed to ship in a player build; Unity's default runtime cube material is not.
        gameplayShader = Resources.Load<Shader>("MenuBarTetraUnlit");
        if (!gameplayShader) { Debug.LogError("MenuBarTetraUnlit shader is missing."); enabled = false; return; }
        CreateWorld();
        CreateAudio();
        onlineLeaderboard = gameObject.AddComponent<OnlineLeaderboardClient>();
        playerName = PlayerPrefs.GetString("MenuBarTetra.PlayerName", "Player");
        onlineLeaderboard.Refresh();
        boardCamera.enabled = false;
    }

    void OnDestroy() { if (pixel) Destroy(pixel); if (playerNameBackground) Destroy(playerNameBackground); }

    void Update()
    {
        UpdateLayout();
        leaderboardRefreshTimer += Time.deltaTime;
        if (leaderboardRefreshTimer >= 15f) { leaderboardRefreshTimer = 0; onlineLeaderboard.Refresh(); }
        if (Input.GetKeyDown(KeyCode.L)) onlineLeaderboard.Refresh();
        if (!gameStarted)
        {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space)) StartGame();
            if (Input.GetKeyDown(KeyCode.Escape)) Application.Quit();
            return;
        }
        if (Input.GetKeyDown(KeyCode.R)) { Restart(); return; }
        if (Input.GetKeyDown(KeyCode.Escape)) Application.Quit();
        if (Input.GetKeyDown(KeyCode.P) && !gameOver) paused = !paused;
        if (gameOver || paused) return;
        if (Input.GetKeyDown(KeyCode.LeftArrow)) TryMove(Vector2Int.left);
        if (Input.GetKeyDown(KeyCode.RightArrow)) TryMove(Vector2Int.right);
        if (Input.GetKeyDown(KeyCode.DownArrow)) StepDown();
        if (Input.GetKeyDown(KeyCode.W)) SetGravity(Vector2Int.up);
        if (Input.GetKeyDown(KeyCode.A)) SetGravity(Vector2Int.left);
        if (Input.GetKeyDown(KeyCode.S)) SetGravity(Vector2Int.down);
        if (Input.GetKeyDown(KeyCode.D)) SetGravity(Vector2Int.right);
        if (Input.GetKeyDown(KeyCode.UpArrow)) TryRotate(1);
        if (Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift)) HoldPiece();
        if (Input.GetKeyDown(KeyCode.Space)) { while (StepGravity()) { } Play(dropSound); }
        dropTimer += Time.deltaTime;
        if (dropTimer >= dropInterval) { dropTimer = 0; StepGravity(); }
    }

    void CreateWorld()
    {
        var backdrop = new GameObject("Backdrop Camera").AddComponent<Camera>();
        // This camera only paints the full-window background. It must not render the board a second time.
        backdrop.depth = 50; backdrop.clearFlags = CameraClearFlags.SolidColor; backdrop.cullingMask = 0; backdrop.backgroundColor = new Color(.045f, .035f, .13f);
        var cam = new GameObject("Playfield Camera").AddComponent<Camera>();
        boardCamera = cam;
        cam.depth = 51; cam.clearFlags = CameraClearFlags.SolidColor; cam.orthographic = true; cam.orthographicSize = 11.8f;
        cam.transform.position = new Vector3(4.5f, 9.5f, -25); cam.backgroundColor = new Color(.035f, .055f, .14f);
        var light = new GameObject("Soft Light").AddComponent<Light>();
        light.type = LightType.Directional; light.intensity = 1.15f; light.transform.rotation = Quaternion.Euler(32, -25, 0);
        // Slightly thicker than one display pixel so no grid division disappears in Unity's scaled Game view.
        for (int x = 0; x <= Width; x++) MakeLine(new Vector3(x - .5f, Height / 2f - .5f, .45f), new Vector3(.055f, Height, .02f));
        for (int y = 0; y <= Height; y++) MakeLine(new Vector3(Width / 2f - .5f, y - .5f, .45f), new Vector3(Width, .055f, .02f));
        UpdateLayout();
    }

    void CreateAudio()
    {
        audioSource = gameObject.AddComponent<AudioSource>(); audioSource.playOnAwake = false; audioSource.volume = .28f;
        moveSound = MakeTone("Move", 330, .045f); rotateSound = MakeTone("Rotate", 520, .07f);
        dropSound = MakeTone("Drop", 125, .11f); holdSound = MakeTone("Hold", 700, .10f);
        clearSound = MakeTone("Clear", 880, .18f); gameOverSound = MakeTone("GameOver", 150, .35f);
    }

    AudioClip MakeTone(string clipName, float frequency, float seconds)
    {
        int sampleRate = 44100, samples = Mathf.CeilToInt(sampleRate * seconds);
        var data = new float[samples];
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)sampleRate;
            data[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * Mathf.Exp(-5f * t / seconds) * .45f;
        }
        var clip = AudioClip.Create(clipName, samples, 1, sampleRate, false); clip.SetData(data, 0); return clip;
    }
    void Play(AudioClip clip) { if (audioSource && clip) audioSource.PlayOneShot(clip); }

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
        var renderer = line.GetComponent<Renderer>();
        renderer.material = MakeMaterial(new Color(.25f, .33f, .66f));
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        Destroy(line.GetComponent<Collider>());
    }

    public void Restart()
    {
        foreach (var t in settled) if (t) Destroy(t.gameObject);
        ClearTransforms(falling); ClearTransforms(ghost);
        System.Array.Clear(settled, 0, settled.Length); nextPieces.Clear();
        for (int i = 0; i < 3; i++) nextPieces.Enqueue(Random.Range(0, 7));
        score = lines = 0; gravity = Vector2Int.down; heldType = -1; holdUsed = false; scoreRecorded = false; paused = gameOver = false; dropInterval = .72f; Spawn();
    }

    void StartGame()
    {
        playerName = string.IsNullOrWhiteSpace(playerName) ? "Player" : playerName.Trim().Substring(0, Mathf.Min(16, playerName.Trim().Length));
        PlayerPrefs.SetString("MenuBarTetra.PlayerName", playerName); PlayerPrefs.Save();
        gameStarted = true;
        boardCamera.enabled = true;
        Restart();
    }

    void ClearTransforms(List<Transform> pieces) { foreach (var t in pieces) if (t) Destroy(t.gameObject); pieces.Clear(); }

    void Spawn()
    {
        int type = nextPieces.Dequeue(); nextPieces.Enqueue(Random.Range(0, 7));
        SpawnType(type);
    }
    void SpawnType(int type)
    {
        currentType = type;
        cells = Shape(type); pivot = SpawnPivot();
        foreach (var ignored in cells) falling.Add(CreateBlock(colors[type], "Falling Tetromino", .91f));
        if (!Valid(cells, pivot)) { gameOver = true; RecordScore(); Play(gameOverSound); return; }
        RenderFalling(); UpdateGhost(type);
    }
    Vector2Int SpawnPivot()
    {
        if (gravity == Vector2Int.up) return new Vector2Int(4, 1);
        if (gravity == Vector2Int.left) return new Vector2Int(7, 9);
        if (gravity == Vector2Int.right) return new Vector2Int(2, 9);
        return new Vector2Int(4, 18);
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
        block.GetComponent<Renderer>().material = MakeMaterial(color); Destroy(block.GetComponent<Collider>()); return block.transform;
    }

    Material MakeMaterial(Color color)
    {
        var material = new Material(gameplayShader);
        material.color = color;
        return material;
    }

    bool Valid(Vector2Int[] test, Vector2Int at)
    {
        foreach (var c in test) { var p = at + c; if (p.x < 0 || p.x >= Width || p.y < 0 || p.y >= Height || settled[p.x, p.y]) return false; }
        return true;
    }
    void TryMove(Vector2Int delta) { if (Valid(cells, pivot + delta)) { pivot += delta; RenderFalling(); UpdateGhostColor(); Play(moveSound); } }
    void TryRotate(int direction)
    {
        var rotated = new Vector2Int[cells.Length];
        for (int i = 0; i < cells.Length; i++) rotated[i] = direction > 0 ? new Vector2Int(-cells[i].y, cells[i].x) : new Vector2Int(cells[i].y, -cells[i].x);
        if (Valid(rotated, pivot)) { cells = rotated; RenderFalling(); UpdateGhostColor(); Play(rotateSound); }
    }
    bool StepGravity()
    {
        if (Valid(cells, pivot + gravity)) { pivot += gravity; RenderFalling(); UpdateGhostColor(); return true; }
        Lock(); return false;
    }
    // Down Arrow keeps its original soft-drop behavior even after gravity is redirected.
    void StepDown()
    {
        if (Valid(cells, pivot + Vector2Int.down)) { pivot += Vector2Int.down; RenderFalling(); UpdateGhostColor(); return; }
        if (gravity == Vector2Int.down) Lock();
    }
    void Lock()
    {
        for (int i = 0; i < cells.Length; i++) settled[pivot.x + cells[i].x, pivot.y + cells[i].y] = falling[i];
        falling.Clear(); ClearTransforms(ghost); ClearLines(); holdUsed = false; Spawn();
    }
    void HoldPiece()
    {
        if (holdUsed) return;
        ClearTransforms(falling); ClearTransforms(ghost);
        if (heldType < 0) { heldType = currentType; Spawn(); }
        else { int swapType = heldType; heldType = currentType; SpawnType(swapType); }
        holdUsed = true; Play(holdSound);
    }
    void SetGravity(Vector2Int direction)
    {
        if (direction == gravity) return;
        gravity = direction;
        UpdateGhostColor(); Play(rotateSound);
    }
    void ClearLines()
    {
        int removed = gravity == Vector2Int.down ? ClearDownRows() : gravity == Vector2Int.up ? ClearUpRows() : gravity == Vector2Int.left ? ClearLeftColumns() : ClearRightColumns();
        if (removed > 0) { lines += removed; score += removed * removed * 100; dropInterval = Mathf.Max(.12f, .72f - lines * .015f); Play(clearSound); }
    }
    bool FullRow(int y) { for (int x = 0; x < Width; x++) if (!settled[x, y]) return false; return true; }
    bool FullColumn(int x) { for (int y = 0; y < Height; y++) if (!settled[x, y]) return false; return true; }
    void DestroyRow(int y) { for (int x = 0; x < Width; x++) Destroy(settled[x, y].gameObject); }
    void DestroyColumn(int x) { for (int y = 0; y < Height; y++) Destroy(settled[x, y].gameObject); }
    int ClearDownRows()
    {
        int removed = 0; for (int y = 0; y < Height; y++) { if (!FullRow(y)) continue; DestroyRow(y); for (int yy = y; yy < Height - 1; yy++) for (int x = 0; x < Width; x++) { settled[x, yy] = settled[x, yy + 1]; if (settled[x, yy]) settled[x, yy].position += Vector3.down; } for (int x = 0; x < Width; x++) settled[x, Height - 1] = null; y--; removed++; } return removed;
    }
    int ClearUpRows()
    {
        int removed = 0; for (int y = Height - 1; y >= 0; y--) { if (!FullRow(y)) continue; DestroyRow(y); for (int yy = y; yy > 0; yy--) for (int x = 0; x < Width; x++) { settled[x, yy] = settled[x, yy - 1]; if (settled[x, yy]) settled[x, yy].position += Vector3.up; } for (int x = 0; x < Width; x++) settled[x, 0] = null; y++; removed++; } return removed;
    }
    int ClearLeftColumns()
    {
        int removed = 0; for (int x = 0; x < Width; x++) { if (!FullColumn(x)) continue; DestroyColumn(x); for (int xx = x; xx < Width - 1; xx++) for (int y = 0; y < Height; y++) { settled[xx, y] = settled[xx + 1, y]; if (settled[xx, y]) settled[xx, y].position += Vector3.left; } for (int y = 0; y < Height; y++) settled[Width - 1, y] = null; x--; removed++; } return removed;
    }
    int ClearRightColumns()
    {
        int removed = 0; for (int x = Width - 1; x >= 0; x--) { if (!FullColumn(x)) continue; DestroyColumn(x); for (int xx = x; xx > 0; xx--) for (int y = 0; y < Height; y++) { settled[xx, y] = settled[xx - 1, y]; if (settled[xx, y]) settled[xx, y].position += Vector3.right; } for (int y = 0; y < Height; y++) settled[0, y] = null; x++; removed++; } return removed;
    }
    void RenderFalling() { for (int i = 0; i < falling.Count; i++) falling[i].position = new Vector3(pivot.x + cells[i].x, pivot.y + cells[i].y, -.35f); }
    Vector2Int LandingPivot() { var landing = pivot; while (Valid(cells, landing + gravity)) landing += gravity; return landing; }
    void UpdateGhost(int type) { ClearTransforms(ghost); for (int i = 0; i < cells.Length; i++) ghost.Add(CreateBlock(Dim(colors[type]), "Landing Ghost", .70f)); UpdateGhostColor(); }
    void UpdateGhostColor()
    {
        if (gameOver) return; var landing = LandingPivot();
        for (int i = 0; i < ghost.Count; i++) ghost[i].position = new Vector3(landing.x + cells[i].x, landing.y + cells[i].y, -.08f);
    }
    static Color Dim(Color color) { return Color.Lerp(new Color(.08f, .10f, .22f), color, .28f); }

    void RecordScore()
    {
        if (scoreRecorded || score <= 0) return;
        scoreRecorded = true;
        onlineLeaderboard.Submit(playerName, score);
    }

    void SetStyles()
    {
        if (titleStyle != null) return;
        titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 25, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
        captionStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, normal = { textColor = new Color(.61f, .65f, .90f) } };
        statStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, normal = { textColor = new Color(.75f, .78f, .96f) } };
        valueStyle = new GUIStyle(statStyle) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight, normal = { textColor = Color.white } };
        controlStyle = new GUIStyle(statStyle) { fontSize = 11, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(.65f, .69f, .9f) } };
        messageStyle = new GUIStyle(titleStyle) { fontSize = 23, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, .84f, .32f) } };
        rankStyle = new GUIStyle(statStyle) { fontSize = 11, alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(.8f, .82f, 1f) } };
        menuTitleStyle = new GUIStyle(titleStyle) { fontSize = 48, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
        startStyle = new GUIStyle(titleStyle) { fontSize = 20, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
        playerNameStyle = new GUIStyle(GUI.skin.textField)
        {
            fontSize = 15,
            alignment = TextAnchor.MiddleCenter,
            padding = new RectOffset(10, 10, 5, 5),
            normal = { background = playerNameBackground, textColor = Color.white },
            focused = { background = playerNameBackground, textColor = Color.white },
            hover = { background = playerNameBackground, textColor = Color.white }
        };
    }
    void DrawRect(Rect rect, Color color) { GUI.color = color; GUI.DrawTexture(rect, pixel); GUI.color = Color.white; }
    void DrawBorder(Rect rect, Color color, float thickness) { DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color); DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color); DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color); DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color); }

    void OnGUI()
    {
        UpdateLayout();
        SetStyles();
        GUI.matrix = Matrix4x4.TRS(uiOrigin, Quaternion.identity, new Vector3(uiScale, uiScale, 1));
        if (!gameStarted) { DrawMainMenu(); GUI.matrix = Matrix4x4.identity; return; }
        var board = BoardRect;
        var side = new Rect(286, 130, 128, 570);
        DrawRect(new Rect(16, 15, 398, 100), new Color(.12f, .10f, .32f));
        DrawBorder(new Rect(16, 15, 398, 790), new Color(.35f, .34f, .73f), 2);
        GUI.Label(new Rect(31, 28, 230, 35), "TETRA", titleStyle);
        GUI.Label(new Rect(33, 62, 250, 20), paused ? "PAUSED - Press P to continue" : "Ready in the menu bar", captionStyle);
        GUI.Label(new Rect(300, 28, 82, 35), score.ToString("000000"), valueStyle);
        DrawRect(board, new Color(.025f, .04f, .12f, .18f)); DrawBorder(board, new Color(.45f, .43f, .86f), 3);
        DrawRect(side, new Color(.12f, .10f, .32f)); DrawBorder(side, new Color(.34f, .33f, .69f), 2);
        // The queue is initialized before the first frame, but keep the HUD safe while Unity is reloading scripts.
        int[] queued = nextPieces.Count == 3 ? nextPieces.ToArray() : new[] { 0, 0, 0 };
        GUI.Label(new Rect(side.x + 12, side.y + 15, side.width - 20, 20), "NEXT", captionStyle);
        DrawPreview(side.x + 14, side.y + 42, queued[0], 13);
        GUI.Label(new Rect(side.x + 12, side.y + 92, side.width - 20, 18), holdUsed ? "HOLD LOCKED" : "HOLD [SHIFT]", captionStyle);
        if (heldType >= 0) DrawPreview(side.x + 14, side.y + 113, heldType, 11);
        else GUI.Label(new Rect(side.x + 12, side.y + 120, side.width - 24, 18), "EMPTY", rankStyle);
        GUI.Label(new Rect(side.x + 12, side.y + 148, side.width - 24, 18), "GRAVITY " + GravityName(), rankStyle);
        DrawRect(new Rect(side.x + 12, side.y + 170, side.width - 24, 1), new Color(.38f, .36f, .71f));
        GUI.Label(new Rect(side.x + 12, side.y + 185, side.width - 24, 20), "LINES", statStyle); GUI.Label(new Rect(side.x + 12, side.y + 200, side.width - 24, 28), lines.ToString(), valueStyle);
        GUI.Label(new Rect(side.x + 12, side.y + 245, side.width - 24, 20), "LEVEL", statStyle); GUI.Label(new Rect(side.x + 12, side.y + 260, side.width - 24, 28), (1 + lines / 10).ToString(), valueStyle);
        DrawRect(new Rect(side.x + 12, side.y + 308, side.width - 24, 1), new Color(.38f, .36f, .71f));
        GUI.Label(new Rect(side.x + 12, side.y + 324, side.width - 20, 20), "UP NEXT", captionStyle);
        DrawPreview(side.x + 14, side.y + 352, queued[1], 9);
        DrawPreview(side.x + 14, side.y + 414, queued[2], 9);
        DrawRect(new Rect(side.x + 12, side.y + 468, side.width - 24, 1), new Color(.38f, .36f, .71f));
        GUI.Label(new Rect(side.x + 12, side.y + 480, side.width - 20, 18), "LIVE SCORES", captionStyle);
        DrawOnlineScores(side.x + 12, side.y + 500, side.width - 24, 3);
        GUI.Label(new Rect(25, 716, 380, 20), gameOver ? "Game over. Press R to play again." : "Playing. Keyboard focus is captured.", captionStyle);
        GUI.Label(new Rect(22, 748, 386, 42), "ARROWS move/drop/rotate   SPACE hard drop   SHIFT hold\nW/A/S/D gravity   R restart   P pause   L refresh   ESC quit", controlStyle);
        if (gameOver) { DrawRect(new Rect(board.x + 12, 380, board.width - 24, 78), new Color(.05f, .04f, .17f, .92f)); GUI.Label(new Rect(board.x + 12, 388, board.width - 24, 60), "GAME OVER\nPress R to restart", messageStyle); }
        else if (paused) { DrawRect(new Rect(board.x + 12, 390, board.width - 24, 55), new Color(.05f, .04f, .17f, .92f)); GUI.Label(new Rect(board.x + 12, 395, board.width - 24, 42), "PAUSED", messageStyle); }
        GUI.matrix = Matrix4x4.identity;
    }

    void DrawMainMenu()
    {
        DrawRect(new Rect(16, 15, 398, 790), new Color(.13f, .095f, .30f));
        DrawBorder(new Rect(16, 15, 398, 790), new Color(.42f, .40f, .86f), 2);
        DrawRect(new Rect(32, 38, 366, 180), new Color(.12f, .10f, .34f));
        DrawBorder(new Rect(32, 38, 366, 180), new Color(.33f, .32f, .72f), 2);
        GUI.Label(new Rect(42, 63, 346, 62), "TETRA", menuTitleStyle);
        GUI.Label(new Rect(42, 124, 346, 22), "A keyboard-first menu bar puzzle", new GUIStyle(captionStyle) { alignment = TextAnchor.MiddleCenter });
        GUI.Label(new Rect(42, 161, 346, 22), "Build the stack. Clear the lines.", new GUIStyle(statStyle) { alignment = TextAnchor.MiddleCenter });

        var startButton = new Rect(68, 276, 294, 66);
        DrawRect(startButton, new Color(.35f, .27f, .86f)); DrawBorder(startButton, new Color(.72f, .67f, 1f), 2);
        if (GUI.Button(startButton, GUIContent.none, GUIStyle.none)) StartGame();
        GUI.Label(startButton, "START GAME", startStyle);
        GUI.Label(new Rect(68, 352, 294, 22), "Click to start or press ENTER / SPACE", new GUIStyle(captionStyle) { alignment = TextAnchor.MiddleCenter });

        DrawRect(new Rect(55, 424, 320, 1), new Color(.37f, .35f, .72f));
        GUI.Label(new Rect(55, 424, 320, 22), "PLAYER NAME", new GUIStyle(captionStyle) { alignment = TextAnchor.MiddleCenter });
        var nameField = new Rect(91, 446, 248, 38);
        DrawBorder(nameField, new Color(.73f, .65f, .98f), 2);
        playerName = GUI.TextField(new Rect(94, 449, 242, 32), playerName, 16, playerNameStyle);
        GUI.Label(new Rect(55, 496, 320, 22), "GLOBAL LEADERBOARD", new GUIStyle(captionStyle) { alignment = TextAnchor.MiddleCenter });
        DrawOnlineScores(55, 524, 320, 3, TextAnchor.MiddleCenter);
        var refreshButton = new Rect(125, 598, 180, 30);
        DrawRect(refreshButton, new Color(.28f, .23f, .67f)); DrawBorder(refreshButton, new Color(.65f, .61f, .98f), 1);
        if (GUI.Button(refreshButton, GUIContent.none, GUIStyle.none)) onlineLeaderboard.Refresh();
        GUI.Label(refreshButton, onlineLeaderboard.IsRefreshing ? "REFRESHING..." : "REFRESH LEADERBOARD", new GUIStyle(captionStyle) { alignment = TextAnchor.MiddleCenter });

        GUI.Label(new Rect(45, 684, 340, 24), "ARROWS move  |  Z / X rotate  |  SPACE drop", new GUIStyle(controlStyle) { fontSize = 12 });
        GUI.Label(new Rect(45, 714, 340, 24), "R restart  |  P pause  |  L refresh", controlStyle);
        GUI.Label(new Rect(45, 766, 340, 20), "TETRA", new GUIStyle(captionStyle) { alignment = TextAnchor.MiddleCenter });
    }
    void DrawPreview(float x, float y, int type, float size)
    {
        var shape = Shape(type); int minX = 99, minY = 99; foreach (var c in shape) { minX = Mathf.Min(minX, c.x); minY = Mathf.Min(minY, c.y); }
        foreach (var c in shape) { var r = new Rect(x + (c.x - minX) * size, y + (1 - (c.y - minY)) * size, size - 2, size - 2); DrawRect(r, colors[type]); DrawBorder(r, new Color(1, 1, 1, .35f), 1); }
    }

    void DrawOnlineScores(float x, float y, float width, int count, TextAnchor alignment = TextAnchor.MiddleLeft)
    {
        var entries = onlineLeaderboard != null ? onlineLeaderboard.Entries : System.Array.Empty<LeaderboardEntry>();
        if (entries.Length == 0)
        {
            GUI.Label(new Rect(x, y, width, 20), onlineLeaderboard != null ? onlineLeaderboard.Status : "Connecting", new GUIStyle(rankStyle) { alignment = alignment });
            return;
        }
        for (int i = 0; i < Mathf.Min(count, entries.Length); i++)
        {
            var entry = entries[i];
            GUI.Label(new Rect(x, y + i * 19, width, 18), (i + 1) + ".  " + entry.name + "  " + entry.score.ToString("000000"), new GUIStyle(rankStyle) { alignment = alignment });
        }
    }
    string GravityName()
    {
        if (gravity == Vector2Int.up) return "UP";
        if (gravity == Vector2Int.left) return "LEFT";
        if (gravity == Vector2Int.right) return "RIGHT";
        return "DOWN";
    }
}
