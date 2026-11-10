using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System;
using System.Linq;
public static class TaskbarGeometryAudit20261031
{
    public static void Run()
    {
        var scene=EditorSceneManager.OpenScene(LibraryLoungeBuild.ScenePath,OpenSceneMode.Single);
        var cafe=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<CafePrototype>(true)).Single();
        for(int i=0;i<6;i++)
        {
            var face=cafe.screens[i].transform;
            float yMin=face.TransformPoint(new Vector3(0,-.5f,0)).y;
            float yMax=face.TransformPoint(new Vector3(0,.5f,0)).y;
            var casing=GameObject.Find("Laptop casing station "+(i+1));
            var baseR=casing?casing.transform.Find("Keyboard base")?.GetComponent<Renderer>():null;
            var hingeR=casing?casing.transform.Find("Rear hinge")?.GetComponent<Renderer>():null;
            var bezel=face.Find(i<2?"Desktop monitor bezel":"Laptop lid bezel");
            var edge=face.Find(i<2?"Desktop lower edge":"Laptop lower edge");
            if(i>=2 && casing)
            {
                int blocked=0;
                Vector3 eye=cafe.seats[i].position+Vector3.up*.14f;
                foreach(float px in new[]{-.35f,0f,.35f})
                foreach(float py in new[]{-.48f,-.45f,-.40f})
                {
                    Vector3 point=face.TransformPoint(new Vector3(px,py,0));
                    Vector3 segment=point-eye;float full=segment.magnitude;
                    Ray ray=new Ray(eye,segment/full);
                    foreach(Renderer rr in casing.GetComponentsInChildren<Renderer>(true))
                    {
                        if(rr.bounds.IntersectRay(ray,out float dist) && dist<full-.002f)
                        {
                            Debug.LogWarning("TASKBAR_LINE_OCCLUDED seat="+i+" px="+px+" py="+py+
                                " by="+rr.name+" t="+dist+" full="+full);
                            blocked++;break;
                        }
                    }
                }
                Debug.Log("TASKBAR_LINE_AUDIT seat="+i+" occluded="+blocked+" of=9");
            }
            Debug.Log("TASKBAR_GEOMETRY seat="+i+" faceCenter="+face.position+" size="+face.lossyScale+
                " worldBottom="+yMin.ToString("F5")+" worldTop="+yMax.ToString("F5")+
                " screenForward="+face.forward+
                " baseTop="+(baseR?baseR.bounds.max.y.ToString("F5"):"NONE")+
                " hingeTop="+(hingeR?hingeR.bounds.max.y.ToString("F5"):"NONE")+
                " lowerEdgeBottom="+(edge?edge.GetComponent<Renderer>().bounds.min.y.ToString("F5"):"NONE")+
                " bezel="+(bezel?bezel.localScale.ToString():"NONE")+
                " seatEye="+cafe.seats[i].position);
        }
    }
}
