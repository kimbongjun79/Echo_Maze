using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(CharacterController))]
public class EchoPlayer : MonoBehaviour
{
    public static EchoPlayer I;
    public Sprite footSprite;
    public float footSize = .3f;
    CharacterController cc;
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
        cc.enabled = false; transform.position = new(EchoMaze.C, transform.position.y, EchoMaze.C); cc.enabled = true;
        au = gameObject.AddComponent<AudioSource>();
        stepC = EchoMaze.Clip(EchoMaze.Band(150, 1, .005f, .12f, 2));
        doorC = EchoMaze.Clip(EchoMaze.Band(600, 6, .05f, .5f, 2), EchoMaze.Band(90, 1, .005f, .12f, 3));
        AudioClip B(float f1, float f2, float l) => EchoMaze.Clip(EchoMaze.Band(f1, .8f, .4f * l, .3f * l), EchoMaze.Band(f2, .8f, .2f * l, .6f * l, 1.3f));
        breath = new[] { B(1400, 900, 1), B(700, 500, .45f), B(2200, 1600, .25f) };
    }

    int Near() => EchoMaze.Cabs.FindIndex(c => Vector2.Distance(new(c.t.position.x, c.t.position.z), new(transform.position.x, transform.position.z)) < 1.5f);

    void Update()
    {
        Prints();
        if (EchoMaze.Over) { if (Input.GetMouseButtonDown(0)) SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex); return; }

        yaw += Input.GetAxis("Mouse X") * 2;
        pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * 2, -85, 85);
        transform.rotation = Quaternion.Euler(pitch, yaw, 0);

        if (Input.GetKeyDown(KeyCode.E) && !busy) { int c = hidden ? cab : Near(); if (c >= 0) StartCoroutine(Hide(c)); }
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

        if (Input.GetMouseButtonDown(0) && !hidden) { var hd = EchoMaze.Pulse(transform.position, 22); EchoMonster.Hear(transform.position, 22, hd); }

        // 숨소리: 공포 > 숨참 > 평상
        var mo = EchoMonster.I;
        if ((bt -= Time.deltaTime) <= 0) { int k = mo.mode > 0 && Vector3.Distance(mo.transform.position, transform.position) < 8 ? 2 : ex > .3f ? 1 : 0; bt = BI[k]; au.PlayOneShot(breath[k], BV[k]); }

        // 접촉: 몬스터(숨지 않았을 때) / 아이템
        var pos = transform.position;
        bool Hit(Transform t) => Vector2.Distance(new(t.position.x, t.position.z), new(pos.x, pos.z)) < .8f;
        if (!hidden && Hit(mo.transform)) { mo.Roar(); EchoMaze.End("붙잡혔습니다"); }
        for (int i = EchoMaze.Items.Count - 1; i >= 0; i--)
            if (Hit(EchoMaze.Items[i])) { Destroy(EchoMaze.Items[i].gameObject); EchoMaze.Items.RemoveAt(i); if (++got == EchoMaze.Total) EchoMaze.End("클리어"); }
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
        if (!busy && !EchoMaze.Over && (hidden || Near() >= 0)) GUI.Box(new Rect(Screen.width / 2 - 70, Screen.height * .78f, 200, 48), hidden ? "E  나오기" : "E  숨기", s);
        if (EchoMaze.Over) GUI.Label(new Rect(0, 0, Screen.width, Screen.height), EchoMaze.Msg + "\n클릭하여 재시작", s);
    }
}
