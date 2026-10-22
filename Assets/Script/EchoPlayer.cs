using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 액자 중앙 조준 + E 연속 2.5초: 홀드 중 보석이 입체로 나오고, 취소하면 액자 안으로 복귀합니다.
// 중앙 링은 획득 진행(청록)을 우선하고, 박수 쿨타임(흰색)은 동시에 계속 감소합니다.
[RequireComponent(typeof(CharacterController))]
public class EchoPlayer : MonoBehaviour
{
    public static EchoPlayer I;
    public Sprite footSprite;
    public float footSize = .3f;
    [Tooltip("박수 재사용 대기 시간(초). 중앙 원형 UI로 남은 시간을 표시합니다.")]
    [Min(0)] public float clapCooldown = 3;
    const float ItemHoldDuration = 2.5f, ItemReach = 2.5f;
    static readonly GUIContent ItemPrompt = new("E  길게 눌러 획득");
    float clapRemaining, itemHeld;
    EchoItem heldItem, aimedItem;
    bool collecting;
    Image progressRing, progressTrack;
    Sprite ringSprite;
    Texture2D ringTexture;
    CharacterController cc;
    Camera viewCamera;
    AudioSource au;
    AudioClip stepC, doorC;
    AudioClip[] breath; // 평상, 숨참, 공포
    static readonly float[] BI = { 3.5f, 1, .6f }, BV = { .15f, 1, .5f }; // 주기, 볼륨
    float yaw, pitch, acc = 9, fAcc, ex, bt, sl;
    int got, cab = -1;
    bool hidden, busy, side;
    public bool Hidden => hidden;
    Material lm;
    readonly List<(SpriteRenderer f, LineRenderer o, float t, float r, float v)> fp = new();

    void Awake() => I = this;

    void Start()
    {
        cc = GetComponent<CharacterController>(); Cursor.lockState = CursorLockMode.Locked;
        viewCamera = GetComponentInChildren<Camera>();
        cc.enabled = false; transform.position = new(EchoMaze.C, transform.position.y, EchoMaze.C); cc.enabled = true;
        au = gameObject.AddComponent<AudioSource>();
        stepC = EchoMaze.Clip(EchoMaze.Band(150, 1, .005f, .12f, 2));
        doorC = EchoMaze.Clip(EchoMaze.Band(600, 6, .05f, .5f, 2), EchoMaze.Band(90, 1, .005f, .12f, 3));
        AudioClip B(float f1, float f2, float l) => EchoMaze.Clip(EchoMaze.Band(f1, .8f, .4f * l, .3f * l), EchoMaze.Band(f2, .8f, .2f * l, .6f * l, 1.3f));
        breath = new[] { B(1400, 900, 1), B(700, 500, .45f), B(2200, 1600, .25f) };
        CreateProgressUI();
    }

    int Near() => EchoMaze.Cabs.FindIndex(c => Vector2.Distance(new(c.t.position.x, c.t.position.z), new(transform.position.x, transform.position.z)) < 1.5f);

    void Update()
    {
        Prints();
        if (EchoMaze.Over) { CancelExtraction(); UpdateProgressUI(); if (Input.GetMouseButtonDown(0)) SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex); return; }

        yaw += Input.GetAxis("Mouse X") * 2;
        pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * 2, -85, 85);
        transform.rotation = Quaternion.Euler(pitch, yaw, 0);

        aimedItem = FindItem();
        if (Input.GetKeyDown(KeyCode.E) && !busy && !collecting && !aimedItem) { int c = hidden ? cab : Near(); if (c >= 0) StartCoroutine(Hide(c)); }
        sl = Mathf.MoveTowards(sl, hidden ? 1 : 0, Time.deltaTime * 2);

        bool sneak = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C);
        bool run = !sneak && Input.GetKey(KeyCode.LeftShift);
        float step = run ? 2.6f : 2.0f;
        var d = hidden || busy ? Vector3.zero : Quaternion.Euler(0, yaw, 0) * new Vector3(Input.GetAxisRaw("Horizontal"), 0, Input.GetAxisRaw("Vertical")).normalized;
        if (d != Vector3.zero)
        {
            var p0 = transform.position;
            cc.Move(d * (sneak ? 1.25f : run ? 5 : 2.5f) * Time.deltaTime);
            var p = transform.position; float m = Vector3.Distance(p0, p);
            if ((fAcc += m) >= .75f) { fAcc = 0; Foot(p, sneak ? 0 : run ? 2 : 1); }
            if ((acc += m) >= step)
            {
                acc = 0; float r = sneak ? 2.5f : run ? 14 : 7;
                var hd = EchoMaze.Pulse(new Vector3(p.x, .1f, p.z), r); au.PlayOneShot(stepC, sneak ? .1f : run ? 1 : .5f); EchoMonster.Hear(p, r, hd);
            }
        }
        else { acc = step; fAcc = .75f; }
        ex = Mathf.Clamp01(ex + (run && d != Vector3.zero ? .25f : -.08f) * Time.deltaTime);


        // 숨소리: 공포 > 숨참 > 평상
        var mo = EchoMonster.I;
        if ((bt -= Time.deltaTime) <= 0) { int k = mo.mode > 0 && Vector3.Distance(mo.transform.position, transform.position) < 8 ? 2 : ex > .3f ? 1 : 0; bt = BI[k]; au.PlayOneShot(breath[k], BV[k]); }

        // 접촉은 몬스터만 판정. 아이템은 액자 조준 + E 연속 홀드로 획득합니다.
        var pos = transform.position;
        bool Hit(Transform t) => Vector2.Distance(new(t.position.x, t.position.z), new(pos.x, pos.z)) < .8f;
        if (!hidden && Hit(mo.transform)) { mo.Attack(); EchoMaze.End("붙잡혔습니다"); }
        TickInteraction(Input.GetKey(KeyCode.E), Input.GetMouseButtonDown(0), Time.deltaTime);
    }

    EchoItem FindItem()
    {
        if (hidden || busy || collecting || EchoMaze.Over || !viewCamera) return null;
        var ray = viewCamera.ViewportPointToRay(new Vector3(.5f, .5f, 0));
        if (!Physics.Raycast(ray, out var hit, ItemReach, ~0, QueryTriggerInteraction.Collide)) return null;
        var item = hit.collider.GetComponentInParent<EchoItem>();
        return item && item.Available ? item : null;
    }

    // 획득 진행도 우선 표시. 박수 쿨타임은 뒤에서도 계속 흐르며 홀드 해제/시선 이탈은 초기화합니다.
    void TickInteraction(bool hold, bool clap, float deltaTime)
    {
        clapRemaining = Mathf.Max(0, clapRemaining - deltaTime);
        aimedItem = FindItem();
        if (EchoMaze.Over)
        {
            CancelExtraction();
            UpdateProgressUI();
            return;
        }
        if (clap && !hidden && !busy && clapRemaining <= 0)
        {
            var hd = EchoMaze.Pulse(transform.position, 22);
            EchoMonster.Hear(transform.position, 22, hd);
            clapRemaining = clapCooldown;
        }
        if (!hold || !aimedItem)
        {
            CancelExtraction();
        }
        else
        {
            if (heldItem != aimedItem) { CancelExtraction(); heldItem = aimedItem; }
            itemHeld = Mathf.Min(ItemHoldDuration, itemHeld + deltaTime);
            heldItem.SetExtractionProgress(itemHeld / ItemHoldDuration);
            if (itemHeld >= ItemHoldDuration)
            {
                collecting = true;
                heldItem.Collect(transform, () =>
                {
                    collecting = false;
                    if (!EchoMaze.Over && ++got == EchoMaze.Total) EchoMaze.End("클리어");
                });
                heldItem = aimedItem = null; itemHeld = 0;
            }
        }
        UpdateProgressUI();
    }

    void CancelExtraction()
    {
        if (heldItem) heldItem.SetExtractionProgress(0);
        heldItem = null;
        itemHeld = 0;
    }

    void OnDisable()
    {
        CancelExtraction();
        aimedItem = null;
        if (progressRing) progressRing.enabled = progressTrack.enabled = false;
    }

    void CreateProgressUI()
    {
        // 링 텍스처는 시작 시 한 번만 생성하고, 매 프레임 Image의 방사형 fillAmount만 갱신합니다.
        const int size = 128;
        ringTexture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Interaction Ring", wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(size / 2f, size / 2f));
                float alpha = Mathf.Clamp01(60 - distance) * Mathf.Clamp01(distance - 49);
                pixels[y * size + x] = new Color(1, 1, 1, alpha);
            }
        ringTexture.SetPixels32(pixels); ringTexture.Apply(false, true);
        ringSprite = Sprite.Create(ringTexture, new Rect(0, 0, size, size), new Vector2(.5f, .5f));
        var canvasObject = new GameObject("Interaction Progress", typeof(Canvas), typeof(CanvasScaler));
        canvasObject.transform.SetParent(transform, false);
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 10;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        progressTrack = MakeRing("Track", canvasObject.transform);
        progressTrack.color = new Color(0, 0, 0, .75f);
        progressRing = MakeRing("Progress", canvasObject.transform);
        progressRing.type = Image.Type.Filled;
        progressRing.fillMethod = Image.FillMethod.Radial360;
        progressRing.fillOrigin = (int)Image.Origin360.Top;
        progressRing.fillClockwise = true;
        UpdateProgressUI();
    }

    Image MakeRing(string label, Transform parent)
    {
        var image = new GameObject(label, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
        image.transform.SetParent(parent, false);
        image.rectTransform.anchorMin = image.rectTransform.anchorMax = new Vector2(.5f, .5f);
        image.rectTransform.anchoredPosition = Vector2.zero;
        image.rectTransform.sizeDelta = new Vector2(80, 80);
        image.sprite = ringSprite; image.raycastTarget = false;
        return image;
    }

    void UpdateProgressUI()
    {
        if (!progressRing) return;
        bool visible = !EchoMaze.Over && (aimedItem || collecting || clapRemaining > 0);
        progressTrack.enabled = progressRing.enabled = visible;
        bool pickup = collecting || itemHeld > 0;
        progressRing.fillAmount = collecting ? 1 : pickup ? itemHeld / ItemHoldDuration : clapCooldown > 0 ? clapRemaining / clapCooldown : 0;
        progressRing.color = pickup || (aimedItem && clapRemaining <= 0) ? new Color(.3f, 1, 1) : Color.white;
    }

    void OnDestroy()
    {
        if (ringSprite) Destroy(ringSprite);
        if (ringTexture) Destroy(ringTexture);
    }

    // 캐비넷 숨기/나오기: 위치·시선 보간 0.5초 + 문 소리, 숨으면 공격 모드 해제
    IEnumerator Hide(int c)
    {
        var (t, o, cy) = EchoMaze.Cabs[c];
        busy = true; cc.enabled = false; hidden = !hidden; cab = c; au.PlayOneShot(doorC, .6f);
        Vector3 a = transform.position, b = hidden ? new(t.position.x, a.y, t.position.z) : new(o.x, a.y, o.z);
        float y0 = yaw, y1 = y0 + Mathf.DeltaAngle(y0, cy);
        for (float s = 0; s < 1;)
        {
            s = Mathf.Min(1, s + Time.deltaTime * 2); float k = s * s * (3 - 2 * s);
            transform.position = Vector3.Lerp(a, b, k); yaw = Mathf.Lerp(y0, y1, k); yield return null;
        }
        if (!hidden) cc.enabled = true;
        busy = false;
    }

    // 발자국: 스프라이트(바닥에 눕힘, 왼발은 좌우 반전) + 이동 속도 비례 원형 파형 [반경, 세기] (살금/걷기/달리기)
    void Foot(Vector3 p, int k)
    {
        float r = new[] { .4f, .8f, 1.5f }[k], v = new[] { .3f, .6f, 1f }[k]; side = !side;
        var q = Quaternion.Euler(0, yaw, 0); var c = p + q * Vector3.right * (side ? .15f : -.15f); c.y = .02f; // 미로 바닥 y=0 (카메라 높이와 무관)
        var f = new GameObject("Footprint").AddComponent<SpriteRenderer>(); f.sprite = footSprite; f.flipX = !side;
        f.transform.SetPositionAndRotation(c, Quaternion.Euler(90, yaw, 0));
        if (footSprite) f.transform.localScale = Vector3.one * (footSize / footSprite.bounds.size.y);
        fp.Add((f, Ring(c, q, Vector3.one * .01f), 0, r, v));
    }
    LineRenderer Ring(Vector3 c, Quaternion q, Vector3 s)
    {
        var l = new GameObject("Footprint").AddComponent<LineRenderer>();
        l.transform.SetPositionAndRotation(c, q); l.transform.localScale = s;
        l.useWorldSpace = false; l.loop = true; l.positionCount = 24; l.widthMultiplier = .01f; l.material = lm ??= new Material(Shader.Find("Sprites/Default"));
        for (int i = 0; i < 24; i++) { float a = i * Mathf.PI / 12; l.SetPosition(i, new(Mathf.Cos(a), 0, Mathf.Sin(a))); }
        return l;
    }
    void Prints()
    {
        for (int i = fp.Count - 1; i >= 0; i--)
        {
            var (f, o, t, r, v) = fp[i]; t += Time.deltaTime; float k = Mathf.Min(1, t / .6f);
            f.color = new Color(1, 1, 1, v * (1 - t / 3));
            o.startColor = o.endColor = new Color(1, 1, 1, v * (1 - k)); o.transform.localScale = Vector3.one * (r * k + .01f);
            if (t < 3) fp[i] = (f, o, t, r, v); else { Destroy(f.gameObject); Destroy(o.gameObject); fp.RemoveAt(i); }
        }
    }

    void OnGUI()
    {
        var s = new GUIStyle(GUI.skin.label) { fontSize = 40, alignment = TextAnchor.MiddleCenter };
        GUI.color = new Color(0, 0, 0, sl); // 캐비넷 문틈
        for (float y = 0; y < Screen.height; y += Screen.height * .09f) GUI.DrawTexture(new Rect(0, y, Screen.width, Screen.height * .07f), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(16, 12, 300, 40), got + " / " + EchoMaze.Total, new GUIStyle { fontSize = 40, normal = { textColor = Color.white } });
        if (!EchoMaze.Over && aimedItem)
        {
            // 고정 폭 대신 실제 글자 크기를 사용하고, 좁은 화면에서는 줄바꿈합니다.
            s.fontSize = Mathf.Clamp(Screen.width / 20, 16, 40);
            s.wordWrap = true;
            float width = Mathf.Min(s.CalcSize(ItemPrompt).x + 16, Mathf.Max(1, Screen.width - 32));
            float height = s.CalcHeight(ItemPrompt, width) + 8;
            float y = Mathf.Max(0, Mathf.Min(Screen.height * .5f + 48, Screen.height - height - 8));
            GUI.Label(new Rect((Screen.width - width) * .5f, y, width, height), ItemPrompt, s);
        }
        else if (!busy && !collecting && !EchoMaze.Over && (hidden || Near() >= 0)) GUI.Box(new Rect(Screen.width / 2 - 70, Screen.height * .78f, 200, 48), hidden ? "E  나오기" : "E  숨기", s);
        if (EchoMaze.Over) GUI.Label(new Rect(0, 0, Screen.width, Screen.height), EchoMaze.Msg + "\n클릭하여 재시작", s);
    }
}
