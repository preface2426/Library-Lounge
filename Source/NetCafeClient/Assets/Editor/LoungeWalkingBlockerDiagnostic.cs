using System;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class LoungeWalkingBlockerDiagnostic
{
    public static void Run()
    {
        try
        {
            var scene = EditorSceneManager.OpenScene(LibraryLoungeBuild.ScenePath, OpenSceneMode.Single);
            Physics.SyncTransforms();
            var cafes = scene.GetRootGameObjects()
                .SelectMany(x => x.GetComponentsInChildren<CafePrototype>(true)).ToArray();
            if (cafes.Length != 1) throw new Exception("Expected exactly one CafePrototype; got " + cafes.Length);
            var cafe = cafes[0];
            var body = cafe.body;
            if (body == null) throw new Exception("CafePrototype.body is null");

            Vector3 start = new Vector3(0f, .05f, -3.9f);
            Vector3 end = start + Vector3.right * .4f;
            Vector3 center0 = start + body.center;
            Vector3 center1 = end + body.center;
            float radius = body.radius;
            float halfSegment = Mathf.Max(0f, body.height * .5f - radius);
            Vector3 p0 = center0 - Vector3.up * halfSegment;
            Vector3 p1 = center1 + Vector3.up * halfSegment;
            var overlaps = Physics.OverlapCapsule(p0, p1, radius, ~0, QueryTriggerInteraction.Ignore)
                .Where(c => c != body && c.enabled && c.gameObject.activeInHierarchy)
                .OrderBy(c => Vector3.Distance(c.bounds.ClosestPoint((start + end) * .5f), (start + end) * .5f))
                .ToArray();

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("\"scene\":\"" + LibraryLoungeBuild.ScenePath + "\",");
            sb.AppendLine("\"start\":\"" + start.ToString("F4") + "\",");
            sb.AppendLine("\"end\":\"" + end.ToString("F4") + "\",");
            sb.AppendLine("\"controller\":{\"radius\":" + radius.ToString("F4") +
                ",\"height\":" + body.height.ToString("F4") +
                ",\"center\":\"" + body.center.ToString("F4") + "\"},");
            sb.AppendLine("\"capsule\":{\"p0\":\"" + p0.ToString("F4") +
                "\",\"p1\":\"" + p1.ToString("F4") + "\"},");
            sb.AppendLine("\"overlapCount\":" + overlaps.Length + ",");
            sb.AppendLine("\"overlaps\":[");
            for (int i = 0; i < overlaps.Length; i++)
            {
                var c = overlaps[i];
                string path = Hierarchy(c.transform);
                var b = c.bounds;
                sb.Append("{\"path\":\"" + Esc(path) + "\",\"name\":\"" + Esc(c.name) +
                    "\",\"type\":\"" + c.GetType().Name + "\",\"trigger\":" + (c.isTrigger ? "true" : "false") +
                    ",\"layer\":\"" + LayerMask.LayerToName(c.gameObject.layer) +
                    "\",\"worldPosition\":\"" + c.transform.position.ToString("F4") +
                    "\",\"worldRotation\":\"" + c.transform.rotation.eulerAngles.ToString("F3") +
                    "\",\"boundsCenter\":\"" + b.center.ToString("F4") +
                    "\",\"boundsSize\":\"" + b.size.ToString("F4") +
                    "\",\"owner\":\"" + Esc(Owner(c.transform)) + "\"}");
                if (i + 1 < overlaps.Length) sb.Append(",");
                sb.AppendLine();
            }
            sb.AppendLine("],");

            var seats = cafe.seats ?? Array.Empty<Transform>();
            var exits = cafe.exits ?? Array.Empty<Transform>();
            var screens = cafe.screens ?? Array.Empty<Renderer>();
            sb.AppendLine("\"anchors\":{");
            sb.Append("\"seatCount\":" + seats.Length + ",\"seats\":[");
            for (int i=0;i<seats.Length;i++) {
                if (i>0) sb.Append(",");
                var t=seats[i]; sb.Append("{\"i\":"+i+",\"path\":\""+Esc(t?Hierarchy(t):"NULL")+"\",\"position\":\""+(t?t.position:Vector3.zero).ToString("F4")+"\"}");
            }
            sb.AppendLine("],");
            sb.Append("\"exitCount\":" + exits.Length + ",\"exits\":[");
            for (int i=0;i<exits.Length;i++) {
                if (i>0) sb.Append(",");
                var t=exits[i]; sb.Append("{\"i\":"+i+",\"path\":\""+Esc(t?Hierarchy(t):"NULL")+"\",\"position\":\""+(t?t.position:Vector3.zero).ToString("F4")+"\"}");
            }
            sb.AppendLine("],");
            sb.Append("\"screenCount\":" + screens.Length + ",\"screens\":[");
            for (int i=0;i<screens.Length;i++) {
                if (i>0) sb.Append(",");
                var t=screens[i]; sb.Append("{\"i\":"+i+",\"path\":\""+Esc(t?Hierarchy(t.transform):"NULL")+"\",\"position\":\""+(t?t.transform.position:Vector3.zero).ToString("F4")+"\"}");
            }
            sb.AppendLine("],");
            sb.Append("\"publicScreen\":\""+Esc(cafe.publicScreen?Hierarchy(cafe.publicScreen.transform):"NULL")+"\",");
            sb.Append("\"publicScreenPosition\":\""+(cafe.publicScreen?cafe.publicScreen.transform.position:Vector3.zero).ToString("F4")+"\"");
            sb.AppendLine("},");
            var interactables = scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<CafeInteractable>(true)).ToArray();
            sb.AppendLine("\"interactableCount\":"+interactables.Length+",\"interactables\":[");
            for(int i=0;i<interactables.Length;i++) {
                var x=interactables[i]; if(i>0)sb.Append(",");
                sb.Append("{\"path\":\""+Esc(Hierarchy(x.transform))+"\",\"action\":\""+x.action+
                    "\",\"position\":\""+x.transform.position.ToString("F4")+"\"}");
            }
            var furniture = scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<Transform>(true))
                .Where(t => new[]{"sofa","seat","chair","table","plant","rest"}.Any(k=>t.name.IndexOf(k,StringComparison.OrdinalIgnoreCase)>=0))
                .OrderBy(t=>Hierarchy(t)).ToArray();
            sb.AppendLine("],");
            sb.AppendLine("\"furnitureInventory\":[");
            for(int i=0;i<furniture.Length;i++) {
                var t=furniture[i]; if(i>0)sb.Append(",");
                var cols=t.GetComponents<Collider>();
                var rends=t.GetComponents<Renderer>();
                sb.Append("{\"path\":\""+Esc(Hierarchy(t))+"\",\"parent\":\""+Esc(t.parent?Hierarchy(t.parent):"NULL")+
                    "\",\"localPosition\":\""+t.localPosition.ToString("F4")+"\",\"worldPosition\":\""+t.position.ToString("F4")+
                    "\",\"worldRotation\":\""+t.rotation.eulerAngles.ToString("F3")+"\",\"renderers\":"+rends.Length+
                    ",\"colliders\":[");
                for(int j=0;j<cols.Length;j++) { if(j>0)sb.Append(",");
                    var c=cols[j];sb.Append("{\"type\":\""+c.GetType().Name+"\",\"trigger\":"+(c.isTrigger?"true":"false")+
                    ",\"center\":\""+c.bounds.center.ToString("F3")+"\",\"size\":\""+c.bounds.size.ToString("F3")+"\"}");
                }
                sb.Append("]}");
            }
            sb.AppendLine("],");
            var scanResults = new System.Collections.Generic.List<string>();
            for (float z=-5.4f; z<=5.401f; z+=.1f) {
                Vector3 a = new Vector3(0f,.05f,z);
                Vector3 b = new Vector3(20f,.05f,z);
                Vector3 ca = a + body.center, cb = b + body.center;
                Vector3 qa = ca - Vector3.up*halfSegment, qb = cb + Vector3.up*halfSegment;
                var hits = Physics.OverlapCapsule(qa,qb,radius,~0,QueryTriggerInteraction.Ignore)
                    .Where(c=>c!=body && c.enabled && c.gameObject.activeInHierarchy)
                    .Select(c=>new { C=c, MinX=c.bounds.min.x, MaxX=c.bounds.max.x })
                    .Where(x=>x.MinX<4.8f)
                    .OrderBy(x=>x.MinX).ToArray();
                string blockers = string.Join(";",hits.Select(x=>Hierarchy(x.C.transform)+"["+x.C.GetType().Name+
                    " x="+x.MinX.ToString("F2")+".."+x.MaxX.ToString("F2")+
                    " z="+x.C.bounds.min.z.ToString("F2")+".."+x.C.bounds.max.z.ToString("F2")+"]"));
                scanResults.Add("{\"z\":"+z.ToString("F2")+",\"preWallBlockerCount\":"+hits.Length+
                    ",\"blockers\":\""+Esc(blockers)+"\"}");
            }
            sb.AppendLine("\"routeScan\":[");
            sb.AppendLine(string.Join(",\n",scanResults));
            sb.AppendLine("]");
            sb.AppendLine("}");

            string output = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../Evidence/WalkingBlockerDiagnostic.json"));
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(output));
            System.IO.File.WriteAllText(output, sb.ToString());
            Debug.Log("WALKING_BLOCKER_DIAGNOSTIC_WRITTEN: " + output + "; overlapCount=" + overlaps.Length);
        }
        catch(Exception ex) { Debug.LogException(ex); throw; }
    }
    static string Hierarchy(Transform t) {
        var parts=new System.Collections.Generic.List<string>();
        while(t!=null){parts.Add(t.name);t=t.parent;}
        parts.Reverse();return string.Join("/",parts);
    }
    static string Owner(Transform t) {
        var cur=t;
        while(cur.parent!=null && cur.parent.parent!=null)cur=cur.parent;
        return cur.name;
    }
    static string Esc(string s) => (s??"").Replace("\\","\\\\").Replace("\"","\\\"").Replace("\r","\\r").Replace("\n","\\n");
}
