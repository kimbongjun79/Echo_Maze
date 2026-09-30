using System;
using UnityEditor;
using UnityEngine;

// 인스펙터 맵 편집기: 브러시 선택 후 칸 클릭. 위쪽 = +z. P 시작, M 몬스터(강제 일반칸)
[CustomEditor(typeof(EchoMaze))]
public class EchoMazeEditor : Editor
{
    static int brush, size = 21;
    static readonly char[] T = { '#', '.', 'C', 'I' };
    static readonly string[] TN = { "벽", "일반", "캐비넷", "아이템" };
    static readonly Color[] TC = { new(.25f, .25f, .25f), Color.white, new(.8f, .55f, .3f), Color.yellow };

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var m = (EchoMaze)target;
        EditorGUILayout.Space();
        brush = GUILayout.Toolbar(brush, TN);
        using (new EditorGUILayout.HorizontalScope())
        {
            size = Mathf.Max(5, EditorGUILayout.IntField("크기", size) | 1);
            if (GUILayout.Button("빈 맵")) Set(m, EchoMaze.Empty(size));
            if (GUILayout.Button("랜덤 생성 (Prim)")) Set(m, EchoMaze.Generate(size / 2));
        }
        if (m.map == null || m.map.Length == 0) return;
        int g = m.map.Length, n = 0;
        foreach (var r in m.map) n += r.Split('I').Length - 1;
        EditorGUILayout.LabelField($"아이템 {n}개 (전부 획득 시 클리어)");
        var bg = GUI.backgroundColor;
        for (int z = g - 1; z >= 0; z--)
            using (new EditorGUILayout.HorizontalScope())
                for (int x = 0; x < m.map[z].Length; x++)
                {
                    int t = Array.IndexOf(T, m.map[z][x]);
                    GUI.backgroundColor = TC[t < 0 ? 1 : t];
                    string lb = x == 1 && z == 1 ? "P" : x == g - 2 && z == g - 2 ? "M" : "";
                    if (GUILayout.Button(lb, EditorStyles.miniButton, GUILayout.Width(18), GUILayout.Height(18)))
                    {
                        var r = m.map[z].ToCharArray(); r[x] = T[brush];
                        Undo.RecordObject(m, "맵 편집"); m.map[z] = new string(r); EditorUtility.SetDirty(m);
                    }
                }
        GUI.backgroundColor = bg;
    }

    static void Set(EchoMaze m, string[] map) { Undo.RecordObject(m, "맵 생성"); m.map = map; m.randomEachRun = false; EditorUtility.SetDirty(m); }
}
