using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using SmartMediaPlatform.World.EditorTools;

public static class Probe
{
    struct R { public float x0, y0, x1, y1; }

    static R RectOf(RectTransform rt, R parent)
    {
        float ax0 = parent.x0 + rt.anchorMin.x * (parent.x1 - parent.x0);
        float ay0 = parent.y0 + rt.anchorMin.y * (parent.y1 - parent.y0);
        float ax1 = parent.x0 + rt.anchorMax.x * (parent.x1 - parent.x0);
        float ay1 = parent.y0 + rt.anchorMax.y * (parent.y1 - parent.y0);
        R r;
        if (rt.anchorMin.x == rt.anchorMax.x && rt.anchorMin.y == rt.anchorMax.y)
        {
            float px = ax0 + rt.anchoredPosition.x, py = ay0 + rt.anchoredPosition.y;
            r.x0 = px - rt.pivot.x * rt.sizeDelta.x; r.x1 = r.x0 + rt.sizeDelta.x;
            r.y0 = py - rt.pivot.y * rt.sizeDelta.y; r.y1 = r.y0 + rt.sizeDelta.y;
        }
        else
        {
            r.x0 = ax0 + rt.offsetMin.x; r.x1 = ax1 + rt.offsetMax.x;
            r.y0 = ay0 + rt.offsetMin.y; r.y1 = ay1 + rt.offsetMax.y;
            if (rt.anchorMin.x == rt.anchorMax.x) { float px = ax0 + rt.anchoredPosition.x; r.x0 = px - rt.pivot.x * rt.sizeDelta.x; r.x1 = r.x0 + rt.sizeDelta.x; }
            if (rt.anchorMin.y == rt.anchorMax.y) { float py = ay0 + rt.anchoredPosition.y; r.y0 = py - rt.pivot.y * rt.sizeDelta.y; r.y1 = r.y0 + rt.sizeDelta.y; }
        }
        return r;
    }

    static string F(float v) { return v.ToString("0.#", CultureInfo.InvariantCulture); }
    static string J(string s) { return s == null ? "" : s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n"); }

    static void Walk(Transform t, R parentRect, string path, bool parentActive, float z, StringBuilder sb, bool first)
    {
        var rt = t as RectTransform;
        R r = rt != null ? RectOf(rt, parentRect) : parentRect;
        bool active = parentActive && t.gameObject.activeSelf;
        float myZ = z + t.localPosition.z;
        string p = path + "/" + t.gameObject.name;

        var text = t.gameObject.GetComponent<Text>();
        var img = t.gameObject.GetComponent<Image>();
        var sel = t.gameObject.GetComponent<Selectable>();
        var col = t.gameObject.GetComponent<Collider>();
        var inp = t.gameObject.GetComponent<VRC.SDK3.Components.VRCUrlInputField>();
        var kinds = new List<string>();
        foreach (var c in t.gameObject.Components) kinds.Add(c.GetType().Name);

        sb.Append(sb.Length > 1 ? ",\n" : "\n");
        sb.Append("{\"path\":\"" + J(p) + "\",\"x0\":" + F(r.x0) + ",\"y0\":" + F(r.y0) + ",\"x1\":" + F(r.x1) + ",\"y1\":" + F(r.y1));
        sb.Append(",\"active\":" + (active ? "true" : "false") + ",\"z\":" + F(myZ));
        sb.Append(",\"comps\":\"" + J(string.Join(",", kinds.ToArray())) + "\"");
        if (text != null) sb.Append(",\"text\":\"" + J(text.text) + "\",\"fontSize\":" + text.fontSize + ",\"align\":\"" + text.alignment + "\",\"textRaycast\":" + (text.raycastTarget ? "true" : "false") + ",\"tr\":" + F(text.color.r) + ",\"tg\":" + F(text.color.g) + ",\"tb\":" + F(text.color.b) + ",\"ta\":" + F(text.color.a));
        if (img != null) sb.Append(",\"img\":true,\"raycast\":" + (img.raycastTarget ? "true" : "false") + ",\"r\":" + F(img.color.r) + ",\"g\":" + F(img.color.g) + ",\"b\":" + F(img.color.b) + ",\"a\":" + F(img.color.a) + ",\"fill\":" + F(img.type == Image.Type.Filled ? img.fillAmount : -1f));
        if (sel != null) sb.Append(",\"selectable\":\"" + sel.GetType().Name + "\"");
        if (col != null) sb.Append(",\"collider\":true");
        if (inp != null) sb.Append(",\"input\":true");
        sb.Append("}");

        foreach (Transform c in t.Children.ToArray()) Walk(c, r, p, active, myZ, sb, false);
    }

    static void Dump(GameObject root, string file)
    {
        var sb = new StringBuilder("[");
        R world; world.x0 = 0; world.y0 = 0; world.x1 = 0; world.y1 = 0;
        Walk(root.transform, world, "", true, 0f, sb, true);
        sb.Append("\n]\n");
        File.WriteAllText(file, sb.ToString());
    }

    public static void Main(string[] args)
    {
        string outDir = args.Length > 0 ? args[0] : ".";
        var wallHolder = new GameObject("Holder");
        var wall = UdonMediaPanelBuilder.BuildWallPanel(wallHolder, "Wall");
        Console.WriteLine("wall: " + (wall != null) + " needsCompile=" + UdonMediaPanelBuilder.NeedsCompile);
        Dump(wallHolder, Path.Combine(outDir, "wall.json"));

        var remoteHolder = new GameObject("Holder");
        var remote = UdonMediaPanelBuilder.BuildRemotePanel(remoteHolder, "Remote");
        Console.WriteLine("remote: " + (remote != null));
        Dump(remoteHolder, Path.Combine(outDir, "remote.json"));
    }
}
