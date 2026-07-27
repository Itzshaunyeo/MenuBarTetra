using System.Collections.Generic;
using UnityEngine;

/// <summary>Self-contained 3D Tetris board. Add to an empty GameObject or let RuntimeBootstrap create it.</summary>
public sealed class TetraGame : MonoBehaviour
{
    const int Width = 10, Height = 20;
    readonly Transform[,] settled = new Transform[Width, Height];
    readonly List<Transform> falling = new List<Transform>(4);
    readonly Color[] colors = { new(0.15f, 0.9f, 1f), new(1f, 0.8f, 0.1f), new(0.75f, 0.25f, 1f), new(1f, 0.25f, 0.3f), new(0.2f, 0.9f, 0.45f), new(1f, 0.5f, 0.1f), new(0.2f, 0.35f, 1f) };
    Vector2Int[] cells;
    Vector2Int pivot;
    float dropTimer, dropInterval = .72f;
    int score, lines;
    bool gameOver;
    GUIStyle hud, message;

    void Awake()
    {
        Application.targetFrameRate = 60;
        CreateWorld();
        Restart();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.R)) { Restart(); return; }
        if (Input.GetKeyDown(KeyCode.Escape)) Application.Quit();
        if (gameOver) return;
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
        var cam = new GameObject("Playfield Camera").AddComponent<Camera>();
        cam.orthographic = true; cam.orthographicSize = 11.7f;
        cam.transform.position = new Vector3(4.5f, 9.5f, -25); cam.transform.rotation = Quaternion.Euler(0, 0, 0);
        cam.backgroundColor = new Color(.025f, .035f, .08f);
        var light = new GameObject("Soft Light").AddComponent<Light>();
        light.type = LightType.Directional; light.intensity = 1.2f; light.transform.rotation = Quaternion.Euler(35, -25, 0);
        for (int x = 0; x <= Width; x++) MakeLine(new Vector3(x - .5f, Height / 2f - .5f, .45f), new Vector3(.035f, Height, .035f));
        for (int y = 0; y <= Height; y++) MakeLine(new Vector3(Width / 2f - .5f, y - .5f, .45f), new Vector3(Width, .035f, .035f));
    }

    void MakeLine(Vector3 pos, Vector3 scale)
    {
        var line = GameObject.CreatePrimitive(PrimitiveType.Cube); line.name = "Grid"; line.transform.position = pos; line.transform.localScale = scale;
        line.GetComponent<Renderer>().material.color = new Color(.12f, .22f, .38f, .55f); Destroy(line.GetComponent<Collider>());
    }

    public void Restart()
    {
        foreach (var t in settled) if (t) Destroy(t.gameObject);
        foreach (var t in falling) if (t) Destroy(t.gameObject);
        System.Array.Clear(settled, 0, settled.Length); falling.Clear(); score = lines = 0; gameOver = false; dropInterval = .72f; Spawn();
    }

    void Spawn()
    {
        int type = Random.Range(0, 7);
        cells = Shape(type); pivot = new Vector2Int(4, 18);
        foreach (var ignored in cells) falling.Add(CreateBlock(colors[type]));
        if (!Valid(cells, pivot)) { gameOver = true; return; }
        RenderFalling();
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

    Transform CreateBlock(Color color)
    {
        var block = GameObject.CreatePrimitive(PrimitiveType.Cube); block.name = "Tetromino"; block.transform.localScale = Vector3.one * .91f;
        block.GetComponent<Renderer>().material.color = color; return block.transform;
    }
    bool Valid(Vector2Int[] test, Vector2Int at) { foreach (var c in test) { var p = at + c; if (p.x < 0 || p.x >= Width || p.y < 0 || p.y >= Height || settled[p.x,p.y]) return false; } return true; }
    void TryMove(Vector2Int delta) { if (Valid(cells, pivot + delta)) { pivot += delta; RenderFalling(); } }
    void TryRotate(int dir) { var rotated = new Vector2Int[cells.Length]; for (int i=0;i<cells.Length;i++) rotated[i] = dir > 0 ? new Vector2Int(-cells[i].y,cells[i].x) : new Vector2Int(cells[i].y,-cells[i].x); if (Valid(rotated,pivot)) { cells=rotated; RenderFalling(); } }
    bool StepDown() { if (Valid(cells, pivot + Vector2Int.down)) { pivot += Vector2Int.down; RenderFalling(); return true; } Lock(); return false; }
    void Lock()
    {
        for (int i=0;i<cells.Length;i++) settled[pivot.x+cells[i].x,pivot.y+cells[i].y] = falling[i]; falling.Clear(); ClearLines(); Spawn();
    }
    void ClearLines()
    {
        int removed=0;
        for (int y=0;y<Height;y++) { bool full=true; for(int x=0;x<Width;x++) if(!settled[x,y]) { full=false; break; } if(!full) continue;
            for(int x=0;x<Width;x++) Destroy(settled[x,y].gameObject);
            for(int yy=y;yy<Height-1;yy++) for(int x=0;x<Width;x++) { settled[x,yy]=settled[x,yy+1]; if(settled[x,yy]) settled[x,yy].position += Vector3.down; }
            for(int x=0;x<Width;x++) settled[x,Height-1]=null; y--; removed++;
        }
        if(removed>0) { lines+=removed; score += removed*removed*100; dropInterval=Mathf.Max(.12f,.72f-lines*.015f); }
    }
    void RenderFalling() { for(int i=0;i<falling.Count;i++) falling[i].position = new Vector3(pivot.x+cells[i].x,pivot.y+cells[i].y,-.35f); }
    void OnGUI()
    {
        hud ??= new GUIStyle(GUI.skin.label) { fontSize=18, fontStyle=FontStyle.Bold, alignment=TextAnchor.MiddleLeft, normal={textColor=Color.white} };
        message ??= new GUIStyle(hud) { fontSize=25, alignment=TextAnchor.MiddleCenter, normal={textColor=new Color(1,.85f,.25f)} };
        GUI.Label(new Rect(12, 8, 390, 28), "TETRA  //  SCORE " + score + "  //  LINES " + lines, hud);
        GUI.Label(new Rect(12, Screen.height-32, 405, 22), "← → move   ↓ soft drop   SPACE hard drop   Z/X rotate   R restart", new GUIStyle(hud){fontSize=11, alignment=TextAnchor.MiddleCenter});
        if(gameOver) GUI.Label(new Rect(20, Screen.height/2-40, Screen.width-40, 80), "GAME OVER\nPress R to restart", message);
    }
}
