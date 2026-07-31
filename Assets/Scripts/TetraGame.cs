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
    readonly List<ClearParticle> clearParticles = new List<ClearParticle>();
    readonly Queue<int> nextPieces = new Queue<int>();
    readonly Color[] colors = {
        new Color(.18f, .83f, 1f), new Color(1f, .77f, .16f), new Color(.72f, .36f, 1f),
        new Color(1f, .32f, .46f), new Color(.25f, .88f, .52f), new Color(1f, .52f, .16f), new Color(.26f, .42f, 1f)
    };
    Vector2Int[] cells;
    Vector2Int pivot;
    int currentType, heldType = -1;
    Vector2Int gravity = Vector2Int.down;
    int stage = 1;
    float stageTimer;
    const float StageDuration = 180f;
    const float GravityWarningSeconds = 6.5f;
    const float GravityCountdownStepSeconds = 2f;
    bool gravityWarning;
    float gravityWarningTimer;
    int gravityWarningCue;
    const float KeyRepeatDelay = .18f, KeyRepeatInterval = .065f;
    float dropTimer, dropInterval = .72f;
    float horizontalKeyTimer, downKeyTimer, rotateKeyTimer;
    KeyCode activeHorizontalKey = KeyCode.None;
    readonly RunStats runStats = new RunStats();
    readonly GameOverSummaryUI gameOverSummary = new GameOverSummaryUI();
    bool gameOver, paused, scoreRecorded, gameStarted, holdUsed, quitPrompt, pauseBeforeQuitPrompt, quitToMenuSelected = true;
    string playerName;
    float leaderboardRefreshTimer;
    Texture2D pixel, playerNameBackground;
    Shader gameplayShader;
    AudioSource audioSource;
    AudioSource musicSource;
    AudioClip moveSound, rotateSound, dropSound, lockSound, holdSound, clearSound, gameOverSound, warningSound, shiftSound;
    AudioClip musicClip;
    GUIStyle titleStyle, captionStyle, statStyle, valueStyle, controlStyle, messageStyle, rankStyle, menuTitleStyle, startStyle, playerNameStyle;
    Camera boardCamera;
    Vector3 boardCameraBasePosition;
    float shakeTimer, shakeDuration, shakeStrength, clearFlashTimer;
    const float ClearFlashDuration = .42f;
    OnlineLeaderboardClient onlineLeaderboard;
    float uiScale;
    Vector2 uiOrigin;

    sealed class ClearParticle
    {
        public Transform transform;
        public Vector3 velocity;
        public float remaining;
        public readonly float lifetime;
        public ClearParticle(Transform transform, Vector3 velocity, float lifetime) { this.transform = transform; this.velocity = velocity; this.lifetime = lifetime; remaining = lifetime; }
    }

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

    void OnDestroy()
    {
        if (pixel) Destroy(pixel); if (playerNameBackground) Destroy(playerNameBackground);
        if (moveSound) Destroy(moveSound); if (rotateSound) Destroy(rotateSound); if (dropSound) Destroy(dropSound);
        if (lockSound) Destroy(lockSound); if (holdSound) Destroy(holdSound); if (clearSound) Destroy(clearSound); if (gameOverSound) Destroy(gameOverSound); if (warningSound) Destroy(warningSound); if (shiftSound) Destroy(shiftSound); if (musicClip) Destroy(musicClip);
    }

    void Update()
    {
        UpdateLayout();
        UpdateScreenShake();
        UpdateClearParticles();
        if (clearFlashTimer > 0) clearFlashTimer -= Time.unscaledDeltaTime;
        leaderboardRefreshTimer += Time.deltaTime;
        if (leaderboardRefreshTimer >= 15f) { leaderboardRefreshTimer = 0; onlineLeaderboard.Refresh(); }
        if (Input.GetKeyDown(KeyCode.L)) onlineLeaderboard.Refresh();
        if (!gameStarted)
        {
            if (quitPrompt) { HandleQuitPromptInput(); return; }
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space)) StartGame();
            if (Input.GetKeyDown(KeyCode.Escape)) OpenQuitPrompt();
            return;
        }
        if (quitPrompt) { HandleQuitPromptInput(); return; }
        if (Input.GetKeyDown(KeyCode.R)) { Restart(); return; }
        if (Input.GetKeyDown(KeyCode.Escape)) { OpenQuitPrompt(); return; }
        if (Input.GetKeyDown(KeyCode.P) && !gameOver) paused = !paused;
        if (gameOver || paused) return;
        runStats.AddPlayTime(Time.deltaTime);
        UpdateGravityShiftTimer();
        HandleHeldArrowKeys();
        if (Input.GetKeyDown(KeyCode.X)) TryRotate(1);
        if (Input.GetKeyDown(KeyCode.Z)) TryRotate(-1);
        if (Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift)) HoldPiece();
        if (Input.GetKeyDown(KeyCode.Space)) { while (StepGravity()) { } Play(dropSound); }
        dropTimer += Time.deltaTime;
        if (dropTimer >= dropInterval) { dropTimer = 0; StepGravity(); }
    }

    void OpenQuitPrompt()
    {
        pauseBeforeQuitPrompt = paused;
        if (gameStarted) paused = true;
        quitToMenuSelected = true;
        quitPrompt = true;
    }
    void CloseQuitPrompt()
    {
        quitPrompt = false;
        if (gameStarted) paused = pauseBeforeQuitPrompt;
    }
    void HandleQuitPromptInput()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) { CloseQuitPrompt(); return; }
        if (Input.GetKeyDown(KeyCode.LeftArrow)) quitToMenuSelected = true;
        if (Input.GetKeyDown(KeyCode.RightArrow)) quitToMenuSelected = false;
        if (Input.GetKeyDown(KeyCode.M)) { ReturnToMainMenu(); return; }
        if (Input.GetKeyDown(KeyCode.Q)) { Application.Quit(); return; }
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space))
        {
            if (quitToMenuSelected) ReturnToMainMenu(); else Application.Quit();
        }
    }
    void ReturnToMainMenu()
    {
        quitPrompt = false; paused = false; gameStarted = false;
        if (boardCamera) boardCamera.enabled = false;
        if (musicSource && musicClip && !musicSource.isPlaying) musicSource.Play();
    }

    // Arrow keys act immediately, then repeat after a brief delay like a desktop Tetris game.
    void UpdateGravityShiftTimer()
    {
        if (!gravityWarning)
        {
            stageTimer += Time.deltaTime;
            if (stageTimer >= StageDuration)
            {
                // The warning begins only once the visible bar has reached 100%.
                stageTimer = StageDuration;
                gravityWarning = true;
                gravityWarningTimer = GravityWarningSeconds;
                gravityWarningCue = 0;
                PlayGravityWarningCue();
            }
            return;
        }

        gravityWarningTimer -= Time.deltaTime;
        if (gravityWarningTimer > 0) { PlayGravityWarningCue(); return; }
        gravityWarning = false;
        stageTimer = 0;
        AdvanceStage(stage + 1);
    }

    void PlayGravityWarningCue()
    {
        int cue = gravityWarningTimer <= .5f ? 4 : 4 - Mathf.Clamp(Mathf.CeilToInt((gravityWarningTimer - .5f) / GravityCountdownStepSeconds), 1, 3);
        if (cue == gravityWarningCue) return;
        gravityWarningCue = cue;
        Play(cue == 4 ? shiftSound : warningSound);
    }

    void HandleHeldArrowKeys()
    {
        KeyCode horizontalKey = Input.GetKey(KeyCode.LeftArrow) ? KeyCode.LeftArrow : Input.GetKey(KeyCode.RightArrow) ? KeyCode.RightArrow : KeyCode.None;
        if (horizontalKey != activeHorizontalKey)
        {
            activeHorizontalKey = horizontalKey;
            horizontalKeyTimer = 0;
            if (horizontalKey == KeyCode.LeftArrow) TryMove(Vector2Int.left);
            else if (horizontalKey == KeyCode.RightArrow) TryMove(Vector2Int.right);
        }
        else if (horizontalKey != KeyCode.None)
        {
            horizontalKeyTimer += Time.deltaTime;
            if (horizontalKeyTimer >= KeyRepeatDelay)
            {
                RepeatHorizontalMove(horizontalKey);
            }
        }

        if (Input.GetKeyDown(KeyCode.DownArrow))
        {
            downKeyTimer = 0;
            StepGravity();
        }
        else if (Input.GetKey(KeyCode.DownArrow))
        {
            downKeyTimer += Time.deltaTime;
            if (downKeyTimer >= KeyRepeatDelay)
            {
                StepGravity();
                downKeyTimer -= KeyRepeatInterval;
                if (downKeyTimer < 0) downKeyTimer = 0;
            }
        }
        else downKeyTimer = 0;

        if (Input.GetKeyDown(KeyCode.UpArrow))
        {
            rotateKeyTimer = 0;
            TryRotate(1);
        }
        else if (Input.GetKey(KeyCode.UpArrow))
        {
            rotateKeyTimer += Time.deltaTime;
            if (rotateKeyTimer >= KeyRepeatDelay)
            {
                TryRotate(1);
                rotateKeyTimer -= KeyRepeatInterval;
                if (rotateKeyTimer < 0) rotateKeyTimer = 0;
            }
        }
        else rotateKeyTimer = 0;
    }

    void RepeatHorizontalMove(KeyCode key)
    {
        if (key == KeyCode.LeftArrow) TryMove(Vector2Int.left);
        else if (key == KeyCode.RightArrow) TryMove(Vector2Int.right);
        horizontalKeyTimer -= KeyRepeatInterval;
        if (horizontalKeyTimer < 0) horizontalKeyTimer = 0;
    }

    void CreateWorld()
    {
        var backdrop = new GameObject("Backdrop Camera").AddComponent<Camera>();
        // This camera only paints the full-window background. It must not render the board a second time.
        backdrop.depth = 50; backdrop.clearFlags = CameraClearFlags.SolidColor; backdrop.cullingMask = 0; backdrop.backgroundColor = new Color(.045f, .035f, .13f);
        var cam = new GameObject("Playfield Camera").AddComponent<Camera>();
        boardCamera = cam;
        cam.depth = 51; cam.clearFlags = CameraClearFlags.SolidColor; cam.orthographic = true; cam.orthographicSize = 11.8f;
        boardCameraBasePosition = new Vector3(4.5f, 9.5f, -25); cam.transform.position = boardCameraBasePosition; cam.backgroundColor = new Color(.035f, .055f, .14f);
        var light = new GameObject("Soft Light").AddComponent<Light>();
        light.type = LightType.Directional; light.intensity = 1.15f; light.transform.rotation = Quaternion.Euler(32, -25, 0);
        // Slightly thicker than one display pixel so no grid division disappears in Unity's scaled Game view.
        for (int x = 0; x <= Width; x++) MakeLine(new Vector3(x - .5f, Height / 2f - .5f, .45f), new Vector3(.055f, Height, .02f));
        for (int y = 0; y <= Height; y++) MakeLine(new Vector3(Width / 2f - .5f, y - .5f, .45f), new Vector3(Width, .055f, .02f));
        UpdateLayout();
    }

    void CreateAudio()
    {
        audioSource = gameObject.AddComponent<AudioSource>(); audioSource.playOnAwake = false; audioSource.volume = .72f;
        moveSound = MakeArcadeEffect("Move", 420, 350, .045f, .18f);
        rotateSound = MakeArcadeEffect("Rotate", 460, 780, .07f, .20f);
        dropSound = MakeArcadeEffect("HardDrop", 260, 115, .10f, .28f);
        lockSound = MakeArcadeEffect("Lock", 185, 105, .075f, .34f);
        holdSound = MakeArcadeEffect("Hold", 620, 880, .10f, .19f);
        clearSound = MakeArcadeEffect("LineClear", 700, 1240, .19f, .30f);
        gameOverSound = MakeArcadeEffect("GameOver", 260, 75, .38f, .26f);
        warningSound = MakeArcadeEffect("GravityWarning", 760, 980, .16f, .42f);
        shiftSound = MakeArcadeEffect("GravityShift", 420, 1320, .24f, .46f);
        musicSource = gameObject.AddComponent<AudioSource>(); musicSource.playOnAwake = false; musicSource.loop = true; musicSource.volume = .14f;
        musicClip = MakeRetroLoop(); musicSource.clip = musicClip; musicSource.Play();
    }

    void UpdateScreenShake()
    {
        if (!boardCamera) return;
        if (shakeTimer <= 0) { boardCamera.transform.position = boardCameraBasePosition; return; }
        shakeTimer -= Time.unscaledDeltaTime;
        float fade = Mathf.Clamp01(shakeTimer / shakeDuration);
        Vector2 offset = Random.insideUnitCircle * shakeStrength * fade;
        boardCamera.transform.position = boardCameraBasePosition + new Vector3(offset.x, offset.y, 0);
    }
    void UpdateClearParticles()
    {
        float delta = Time.unscaledDeltaTime;
        for (int i = clearParticles.Count - 1; i >= 0; i--)
        {
            var particle = clearParticles[i];
            if (!particle.transform) { clearParticles.RemoveAt(i); continue; }
            particle.remaining -= delta;
            if (particle.remaining <= 0) { Destroy(particle.transform.gameObject); clearParticles.RemoveAt(i); continue; }
            particle.velocity += Vector3.down * 5f * delta;
            particle.transform.position += particle.velocity * delta;
            particle.transform.localScale = Vector3.one * (.40f * particle.remaining / particle.lifetime);
        }
    }
    void TriggerClearEffects()
    {
        shakeDuration = .30f; shakeTimer = shakeDuration; shakeStrength = .32f; clearFlashTimer = ClearFlashDuration;
    }
    void EmitClearParticle(Transform source, int x, int y)
    {
        Color color = source && source.GetComponent<Renderer>() ? source.GetComponent<Renderer>().material.color : new Color(.78f, .68f, 1f);
        var particle = CreateBlock(color, "Line Clear Particle", .40f);
        particle.position = new Vector3(x, y, -.7f);
        clearParticles.Add(new ClearParticle(particle, new Vector3(Random.Range(-3.5f, 3.5f), Random.Range(.9f, 4.2f), 0), .70f));
    }

    // Original square-wave effects evoke an arcade cabinet without reusing any game's recorded audio.
    AudioClip MakeArcadeEffect(string clipName, float startFrequency, float endFrequency, float seconds, float level)
    {
        int sampleRate = 44100, samples = Mathf.CeilToInt(sampleRate * seconds);
        var data = new float[samples];
        float phase = 0;
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)sampleRate;
            float progress = t / seconds;
            float frequency = Mathf.Lerp(startFrequency, endFrequency, progress);
            phase += frequency / sampleRate;
            float square = Mathf.Repeat(phase, 1f) < .5f ? 1f : -1f;
            float sine = Mathf.Sin(phase * 2f * Mathf.PI);
            float envelope = Mathf.Min(1f, t / .004f) * Mathf.Clamp01((seconds - t) / .022f);
            data[i] = (square * .78f + sine * .22f) * envelope * level;
        }
        var clip = AudioClip.Create(clipName, samples, 1, sampleRate, false); clip.SetData(data, 0); return clip;
    }
    void Play(AudioClip clip) { if (audioSource && clip) audioSource.PlayOneShot(clip); }
    AudioClip MakeRetroLoop()
    {
        const int sampleRate = 44100, steps = 32;
        const float stepSeconds = .18f;
        float[] melody = { 523.25f, 659.25f, 783.99f, 659.25f, 587.33f, 698.46f, 880f, 698.46f, 493.88f, 587.33f, 739.99f, 587.33f, 440f, 523.25f, 659.25f, 523.25f };
        int samplesPerStep = Mathf.RoundToInt(sampleRate * stepSeconds), total = samplesPerStep * steps;
        var data = new float[total];
        for (int i = 0; i < total; i++)
        {
            int step = i / samplesPerStep, index = step % melody.Length, within = i % samplesPerStep;
            float t = within / (float)sampleRate, envelope = Mathf.Min(1f, within / 350f) * Mathf.Min(1f, (samplesPerStep - within) / 1200f);
            float lead = Mathf.Sin(2f * Mathf.PI * melody[index] * t) >= 0 ? 1f : -1f;
            float bassFrequency = melody[(step / 2) % melody.Length] * .25f;
            float bass = Mathf.Sin(2f * Mathf.PI * bassFrequency * t) >= 0 ? 1f : -1f;
            data[i] = (lead * .12f + bass * .055f) * envelope;
        }
        var clip = AudioClip.Create("RetroLoop", total, 1, sampleRate, false); clip.SetData(data, 0); return clip;
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
        var renderer = line.GetComponent<Renderer>();
        renderer.material = MakeMaterial(new Color(.25f, .33f, .66f));
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        Destroy(line.GetComponent<Collider>());
    }

    public void Restart()
    {
        foreach (var t in settled) if (t) Destroy(t.gameObject);
        ClearTransforms(falling);
        ClearClearParticles();
        shakeTimer = clearFlashTimer = 0;
        if (boardCamera) boardCamera.transform.position = boardCameraBasePosition;
        System.Array.Clear(settled, 0, settled.Length); nextPieces.Clear();
        for (int i = 0; i < 3; i++) nextPieces.Enqueue(Random.Range(0, 7));
        runStats.Reset(); stage = 1; stageTimer = 0; gravityWarning = false; gravityWarningTimer = 0; gravityWarningCue = 0; gravity = Vector2Int.down; boardCamera.transform.rotation = Quaternion.identity; heldType = -1; holdUsed = false; scoreRecorded = false; paused = gameOver = false; dropInterval = .72f; horizontalKeyTimer = downKeyTimer = rotateKeyTimer = 0; activeHorizontalKey = KeyCode.None; Spawn();
    }

    void StartGame()
    {
        playerName = string.IsNullOrWhiteSpace(playerName) ? "Player" : playerName.Trim().Substring(0, Mathf.Min(16, playerName.Trim().Length));
        PlayerPrefs.SetString("MenuBarTetra.PlayerName", playerName); PlayerPrefs.Save();
        gameStarted = true;
        boardCamera.enabled = true;
        if (musicSource && musicSource.isPlaying) musicSource.Stop();
        Restart();
    }

    // Destroy is end-of-frame; hide objects first so a removed falling piece cannot flash after Hold or Restart.
    void ClearTransforms(List<Transform> pieces) { foreach (var t in pieces) if (t) { t.gameObject.SetActive(false); Destroy(t.gameObject); } pieces.Clear(); }
    void ClearClearParticles() { foreach (var particle in clearParticles) if (particle.transform) Destroy(particle.transform.gameObject); clearParticles.Clear(); }

    void Spawn()
    {
        int type = nextPieces.Dequeue(); nextPieces.Enqueue(Random.Range(0, 7));
        SpawnType(type);
    }
    void SpawnType(int type)
    {
        currentType = type;
        cells = Shape(type); pivot = SpawnPivot();
        // Check before creating visual cubes. A blocked spawn is game over, not an untracked falling piece.
        if (!Valid(cells, pivot))
        {
            ClearTransforms(falling);
            gameOver = true; RecordScore(); Play(gameOverSound); return;
        }
        foreach (var ignored in cells) falling.Add(CreateBlock(colors[type], "Falling Tetromino", .91f));
        RenderFalling();
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

    Transform CreateBlock(Color color, string blockName, float scale, Shader shader = null)
    {
        var block = GameObject.CreatePrimitive(PrimitiveType.Cube); block.name = blockName; block.transform.localScale = Vector3.one * scale;
        block.GetComponent<Renderer>().material = MakeMaterial(color, shader); Destroy(block.GetComponent<Collider>()); return block.transform;
    }

    Material MakeMaterial(Color color, Shader shader = null)
    {
        var material = new Material(shader ?? gameplayShader);
        material.color = color;
        return material;
    }

    bool Valid(Vector2Int[] test, Vector2Int at)
    {
        foreach (var c in test) { var p = at + c; if (p.x < 0 || p.x >= Width || p.y < 0 || p.y >= Height || settled[p.x, p.y]) return false; }
        return true;
    }
    void TryMove(Vector2Int delta) { if (Valid(cells, pivot + delta)) { pivot += delta; RenderFalling(); Play(moveSound); } }
    void TryRotate(int direction)
    {
        var rotated = new Vector2Int[cells.Length];
        for (int i = 0; i < cells.Length; i++) rotated[i] = direction > 0 ? new Vector2Int(-cells[i].y, cells[i].x) : new Vector2Int(cells[i].y, -cells[i].x);
        if (Valid(rotated, pivot)) { cells = rotated; RenderFalling(); Play(rotateSound); }
    }
    bool StepGravity()
    {
        if (Valid(cells, pivot + gravity)) { pivot += gravity; RenderFalling(); return true; }
        Lock(); return false;
    }
    void Lock()
    {
        for (int i = 0; i < cells.Length; i++) settled[pivot.x + cells[i].x, pivot.y + cells[i].y] = falling[i];
        falling.Clear(); Play(lockSound); ClearLines(); holdUsed = false; Spawn();
    }
    void HoldPiece()
    {
        if (holdUsed) return;
        ClearTransforms(falling);
        if (heldType < 0) { heldType = currentType; Spawn(); }
        else { int swapType = heldType; heldType = currentType; SpawnType(swapType); }
        holdUsed = true; Play(holdSound);
    }
    void ClearLines()
    {
        int removed = gravity == Vector2Int.down ? ClearDownRows() : gravity == Vector2Int.up ? ClearUpRows() : gravity == Vector2Int.left ? ClearLeftColumns() : ClearRightColumns();
        SyncSettledVisuals();
        if (removed > 0)
        {
            runStats.RecordLineClear(removed); dropInterval = Mathf.Max(.12f, .72f - runStats.LinesCleared * .015f); Play(clearSound);
        }
        else runStats.RecordLineClear(0);
    }
    void AdvanceStage(int nextStage)
    {
        runStats.RecordGravityChange();
        stage = nextStage;
        bool inverted = stage % 2 == 0;
        gravity = inverted ? Vector2Int.up : Vector2Int.down;
        boardCamera.transform.rotation = Quaternion.identity;
        FlipSettledStack(inverted);
        FlipActivePiece();
        SettleStack();
        SyncSettledVisuals();
        Play(rotateSound);
    }
    // Rotate the established stack around the board's horizontal center so it changes stage with the playfield.
    void FlipSettledStack(bool inverted)
    {
        var flipped = new Transform[Width, Height];
        Quaternion rotation = Quaternion.Euler(0, 0, inverted ? 180 : 0);
        for (int x = 0; x < Width; x++) for (int y = 0; y < Height; y++)
        {
            var block = settled[x, y];
            if (!block) continue;
            int newY = Height - 1 - y;
            flipped[x, newY] = block;
            block.position = new Vector3(block.position.x, newY, block.position.z);
            block.rotation = rotation;
        }
        System.Array.Copy(flipped, settled, settled.Length);
    }
    // The active tetromino is part of the stage too; mapping it with the stack prevents overlap after a flip.
    void FlipActivePiece()
    {
        if (cells == null || falling.Count == 0) return;
        pivot = new Vector2Int(pivot.x, Height - 1 - pivot.y);
        for (int i = 0; i < cells.Length; i++) cells[i] = new Vector2Int(cells[i].x, -cells[i].y);
        RenderFalling();
    }
    bool OccupiedByFalling(int x, int y)
    {
        if (cells == null) return false;
        foreach (var cell in cells) if (pivot.x + cell.x == x && pivot.y + cell.y == y) return true;
        return false;
    }
    // The grid is authoritative. Reapply every settled block's cell position after grid mutations.
    void SyncSettledVisuals()
    {
        for (int x = 0; x < Width; x++) for (int y = 0; y < Height; y++)
            if (settled[x, y]) settled[x, y].position = new Vector3(x, y, 0);
    }
    void SettleStack()
    {
        bool moved; int safety = Width * Height;
        do
        {
            moved = false;
            if (gravity == Vector2Int.down)
            {
                for (int y = 1; y < Height; y++) for (int x = 0; x < Width; x++) if (settled[x, y] && !settled[x, y - 1] && !OccupiedByFalling(x, y - 1)) { settled[x, y - 1] = settled[x, y]; settled[x, y] = null; settled[x, y - 1].position += Vector3.down; moved = true; }
            }
            else
            {
                for (int y = Height - 2; y >= 0; y--) for (int x = 0; x < Width; x++) if (settled[x, y] && !settled[x, y + 1] && !OccupiedByFalling(x, y + 1)) { settled[x, y + 1] = settled[x, y]; settled[x, y] = null; settled[x, y + 1].position += Vector3.up; moved = true; }
            }
        } while (moved && --safety > 0);
    }
    bool FullRow(int y) { for (int x = 0; x < Width; x++) if (!settled[x, y]) return false; return true; }
    bool FullColumn(int x) { for (int y = 0; y < Height; y++) if (!settled[x, y]) return false; return true; }
    void DestroyRow(int y)
    {
        TriggerClearEffects();
        for (int x = 0; x < Width; x++) { EmitClearParticle(settled[x, y], x, y); Destroy(settled[x, y].gameObject); }
    }
    void DestroyColumn(int x)
    {
        TriggerClearEffects();
        for (int y = 0; y < Height; y++) { EmitClearParticle(settled[x, y], x, y); Destroy(settled[x, y].gameObject); }
    }
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
    static Color Dim(Color color) { return Color.Lerp(new Color(.08f, .10f, .22f), color, .28f); }

    void RecordScore()
    {
        if (scoreRecorded || runStats.Score <= 0) return;
        scoreRecorded = true;
        onlineLeaderboard.Submit(playerName, runStats.Score);
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
    Vector2 WorldToDesignPoint(Vector3 world)
    {
        Vector3 screen = boardCamera.WorldToScreenPoint(world);
        return new Vector2((screen.x - uiOrigin.x) / uiScale, (Screen.height - screen.y - uiOrigin.y) / uiScale);
    }
    // Camera-projected UI outline: exact board alignment without ghost scene objects that can look physical.
    void DrawGhostOverlay()
    {
        if (gameOver || cells == null || !boardCamera) return;
        var landing = LandingPivot();
        Color color = Dim(colors[currentType]);
        foreach (var cell in cells)
        {
            var p = landing + cell;
            Vector2 center = WorldToDesignPoint(new Vector3(p.x, p.y, 0));
            float width = Mathf.Abs(WorldToDesignPoint(new Vector3(p.x + 1, p.y, 0)).x - center.x);
            float height = Mathf.Abs(WorldToDesignPoint(new Vector3(p.x, p.y + 1, 0)).y - center.y);
            float size = Mathf.Min(width, height) * .65f;
            DrawBorder(new Rect(center.x - size / 2, center.y - size / 2, size, size), color, 2);
        }
    }
    void DrawGravityProgress(Rect side)
    {
        var bar = new Rect(side.x + 12, side.y + 158, side.width - 24, 11);
        float progress = Mathf.Clamp01(stageTimer / StageDuration);
        Color fill = gravityWarning ? new Color(1f, .62f, .22f) : new Color(.43f, .40f, .96f);
        DrawRect(bar, new Color(.045f, .04f, .14f));
        DrawRect(new Rect(bar.x + 2, bar.y + 2, (bar.width - 4) * progress, bar.height - 4), fill);
        DrawBorder(bar, new Color(.60f, .57f, .94f), 1);
    }
    string GravityWarningLabel()
    {
        if (gravityWarningTimer <= .5f) return "GRAVITY SHIFT INCOMING... " + ((stage + 1) % 2 == 0 ? "↑" : "↓");
        int number = Mathf.Clamp(Mathf.CeilToInt((gravityWarningTimer - .5f) / GravityCountdownStepSeconds), 1, 3);
        return "GRAVITY SHIFT INCOMING... " + number;
    }
    void DrawGravityWarning()
    {
        float flash = .55f + .45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 11f));
        Color accent = Color.Lerp(new Color(1f, .38f, .12f), new Color(1f, .90f, .30f), flash);
        var banner = new Rect(24, 84, 382, 27);
        DrawRect(banner, new Color(.25f, .07f, .18f, .96f));
        DrawBorder(banner, accent, 2);
        DrawBorder(new Rect(16, 15, 398, 790), accent, 3);
        GUI.Label(banner, GravityWarningLabel(), new GUIStyle(messageStyle) { fontSize = 14 });
    }
    void DrawLineClearFlash(Rect board)
    {
        float pulse = Mathf.Clamp01(clearFlashTimer / ClearFlashDuration);
        Color accent = Color.Lerp(new Color(1f, .45f, .16f), Color.white, pulse);
        DrawBorder(board, accent, 5);
        var banner = new Rect(board.x + 16, board.y + board.height * .42f, board.width - 32, 42);
        DrawRect(banner, new Color(.20f, .08f, .28f, .78f * pulse));
        DrawBorder(banner, accent, 2);
        GUI.Label(banner, "LINE CLEAR!", new GUIStyle(messageStyle) { fontSize = 19 });
    }
    void DrawQuitPrompt()
    {
        var panel = new Rect(48, 274, 334, 226);
        DrawRect(panel, new Color(.055f, .04f, .16f, .98f));
        DrawBorder(panel, new Color(.78f, .64f, 1f), 3);
        GUI.Label(new Rect(panel.x + 18, panel.y + 22, panel.width - 36, 34), "LEAVE THIS RUN?", new GUIStyle(messageStyle) { fontSize = 22 });
        GUI.Label(new Rect(panel.x + 20, panel.y + 64, panel.width - 40, 38), "Return to the start screen or\nclose Tetra?", new GUIStyle(statStyle) { alignment = TextAnchor.MiddleCenter });
        var menuButton = new Rect(panel.x + 20, panel.y + 122, 140, 48);
        var closeButton = new Rect(panel.x + 174, panel.y + 122, 140, 48);
        DrawRect(menuButton, quitToMenuSelected ? new Color(.40f, .30f, .90f) : new Color(.18f, .14f, .40f));
        DrawBorder(menuButton, quitToMenuSelected ? Color.white : new Color(.52f, .47f, .84f), 2);
        DrawRect(closeButton, !quitToMenuSelected ? new Color(.72f, .24f, .34f) : new Color(.30f, .11f, .24f));
        DrawBorder(closeButton, !quitToMenuSelected ? Color.white : new Color(.68f, .42f, .60f), 2);
        if (GUI.Button(menuButton, GUIContent.none, GUIStyle.none)) ReturnToMainMenu();
        if (GUI.Button(closeButton, GUIContent.none, GUIStyle.none)) Application.Quit();
        GUI.Label(menuButton, "MAIN MENU", new GUIStyle(captionStyle) { alignment = TextAnchor.MiddleCenter });
        GUI.Label(closeButton, "CLOSE APP", new GUIStyle(captionStyle) { alignment = TextAnchor.MiddleCenter });
        GUI.Label(new Rect(panel.x + 15, panel.y + 184, panel.width - 30, 22), "LEFT / RIGHT + ENTER  |  ESC cancel", new GUIStyle(controlStyle) { fontSize = 9 });
    }
    void OnGUI()
    {
        UpdateLayout();
        SetStyles();
        GUI.matrix = Matrix4x4.TRS(uiOrigin, Quaternion.identity, new Vector3(uiScale, uiScale, 1));
        if (!gameStarted) { DrawMainMenu(); if (quitPrompt) DrawQuitPrompt(); GUI.matrix = Matrix4x4.identity; return; }
        var board = BoardRect;
        var side = new Rect(286, 130, 128, 570);
        DrawRect(new Rect(16, 15, 398, 100), new Color(.12f, .10f, .32f));
        DrawBorder(new Rect(16, 15, 398, 790), new Color(.35f, .34f, .73f), 2);
        GUI.Label(new Rect(31, 28, 230, 35), "TETRA", titleStyle);
        GUI.Label(new Rect(33, 62, 250, 20), paused ? "PAUSED - Press P to continue" : "Ready in the menu bar", captionStyle);
        GUI.Label(new Rect(300, 28, 82, 35), runStats.Score.ToString("000000"), valueStyle);
        DrawRect(board, new Color(.025f, .04f, .12f, .18f)); DrawBorder(board, new Color(.45f, .43f, .86f), 3); DrawGhostOverlay();
        if (clearFlashTimer > 0) DrawLineClearFlash(board);
        if (gravityWarning) DrawGravityWarning();
        DrawRect(side, new Color(.12f, .10f, .32f)); DrawBorder(side, new Color(.34f, .33f, .69f), 2);
        // The queue is initialized before the first frame, but keep the HUD safe while Unity is reloading scripts.
        int[] queued = nextPieces.Count == 3 ? nextPieces.ToArray() : new[] { 0, 0, 0 };
        GUI.Label(new Rect(side.x + 12, side.y + 15, side.width - 20, 20), "NEXT", captionStyle);
        DrawPreview(side.x + 14, side.y + 42, queued[0], 13);
        GUI.Label(new Rect(side.x + 12, side.y + 92, side.width - 20, 18), holdUsed ? "HOLD LOCKED" : "HOLD [SHIFT]", captionStyle);
        if (heldType >= 0) DrawPreview(side.x + 14, side.y + 113, heldType, 11);
        else GUI.Label(new Rect(side.x + 12, side.y + 120, side.width - 24, 18), "EMPTY", rankStyle);
        GUI.Label(new Rect(side.x + 12, side.y + 140, side.width - 24, 16), runStats.CurrentCombo > 0 ? "COMBO  x" + runStats.CurrentCombo : "COMBO  --", rankStyle);
        DrawGravityProgress(side);
        DrawRect(new Rect(side.x + 12, side.y + 182, side.width - 24, 1), new Color(.38f, .36f, .71f));
        GUI.Label(new Rect(side.x + 12, side.y + 197, side.width - 24, 20), "LINES", statStyle); GUI.Label(new Rect(side.x + 12, side.y + 212, side.width - 24, 28), runStats.LinesCleared.ToString(), valueStyle);
        GUI.Label(new Rect(side.x + 12, side.y + 257, side.width - 24, 20), "STAGE", statStyle); GUI.Label(new Rect(side.x + 12, side.y + 272, side.width - 24, 28), stage.ToString(), valueStyle);
        DrawRect(new Rect(side.x + 12, side.y + 318, side.width - 24, 1), new Color(.38f, .36f, .71f));
        GUI.Label(new Rect(side.x + 12, side.y + 334, side.width - 20, 20), "UP NEXT", captionStyle);
        DrawPreview(side.x + 14, side.y + 362, queued[1], 9);
        DrawPreview(side.x + 14, side.y + 424, queued[2], 9);
        DrawRect(new Rect(side.x + 12, side.y + 476, side.width - 24, 1), new Color(.38f, .36f, .71f));
        GUI.Label(new Rect(side.x + 12, side.y + 488, side.width - 20, 18), "LIVE SCORES", captionStyle);
        DrawOnlineScores(side.x + 12, side.y + 508, side.width - 24, 3);
        GUI.Label(new Rect(25, 716, 380, 20), gameOver ? "Game over. Press R to play again." : "Playing. Keyboard focus is captured.", captionStyle);
        GUI.Label(new Rect(22, 748, 386, 42), "ARROWS Move | UP/X Rotate Right | Z Rotate Left | SPACE Hard Drop\nSHIFT Hold | R Restart | P Pause | L Refresh | ESC Quit", controlStyle);
        if (gameOver) gameOverSummary.Draw(pixel, runStats, stage, messageStyle, captionStyle, statStyle, valueStyle);
        else if (paused) { DrawRect(new Rect(board.x + 12, 390, board.width - 24, 55), new Color(.05f, .04f, .17f, .92f)); GUI.Label(new Rect(board.x + 12, 395, board.width - 24, 42), "PAUSED", messageStyle); }
        if (quitPrompt) DrawQuitPrompt();
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

        GUI.Label(new Rect(35, 684, 360, 24), "ARROWS Move | UP/X Rotate Right | Z Rotate Left", new GUIStyle(controlStyle) { fontSize = 11 });
        GUI.Label(new Rect(30, 714, 370, 24), "SPACE Hard Drop | SHIFT Hold | R Restart | P Pause | L Refresh", new GUIStyle(controlStyle) { fontSize = 10 });
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
}
