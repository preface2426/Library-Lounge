using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System;
using System.Linq;
public static class LibraryLoungeDisplaySightlineAudit
{
    public static void Check()
    {
        var scene=EditorSceneManager.OpenScene(LibraryLoungeBuild.ScenePath,UnityEditor.SceneManagement.OpenSceneMode.Single);
        var cafe=scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<CafePrototype>(true)).Single();
        Physics.SyncTransforms();
        for(int i=0;i<6;i++)
        {
            Transform screen=cafe.screens[i].transform;
            Vector3 eye=i>=2?cafe.seats[i].position:screen.position-screen.forward*.60f;
            foreach(float u in new[]{-.48f,0f,.48f})
            foreach(float v in new[]{-.48f,0f,.48f})
            {
                Vector3 point=screen.TransformPoint(new Vector3(u,v,0));
                Vector3 toward=point-eye;float length=toward.magnitude;
                string hits=string.Join(",",Physics.RaycastAll(eye,toward/length,length-.035f,~(1<<28),QueryTriggerInteraction.Ignore)
                    .Where(h=>!h.collider.transform.IsChildOf(cafe.body.transform))
                    .Select(h=>h.collider.name+"@"+h.distance.ToString("F2")));
                if(!string.IsNullOrEmpty(hits))Debug.LogWarning("ROOMVIEW_LINE_BLOCK station="+i+" u="+u+" v="+v+" hits="+hits);
            }
            Debug.Log("ROOMVIEW_LINE_AUDIT station="+i+" done");
        }
    }
    public static void Optimize()
    {
        var scene=EditorSceneManager.OpenScene(LibraryLoungeBuild.ScenePath,OpenSceneMode.Single);
        var cafe=scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<CafePrototype>(true)).Single();
        Physics.SyncTransforms();
        for(int i=2;i<6;i++)
        {
            Transform screen=cafe.screens[i].transform;
            float best=999;Vector3 bestOffset=Vector3.zero;int bestHits=999;
            foreach(float up in new[]{0f,.12f,.22f,.32f,.42f,.52f,.62f})
            foreach(float forward in new[]{0f,.1f,.2f,.3f,.4f,.5f,.6f})
            {
                Vector3 offset=Vector3.up*up+Vector3.forward*forward;
                Vector3 eye=cafe.seats[i].position+offset;
                if(Physics.CheckSphere(eye,.12f,~(1<<28),QueryTriggerInteraction.Ignore))continue;
                int blockers=0;
                foreach(float u in new[]{-.48f,0f,.48f})
                foreach(float v in new[]{-.48f,0f,.48f})
                {
                    Vector3 point=screen.TransformPoint(new Vector3(u,v,0));
                    Vector3 direction=point-eye;float dist=direction.magnitude;
                    blockers+=Physics.RaycastAll(eye,direction/dist,dist-.03f,~(1<<28),QueryTriggerInteraction.Ignore)
                        .Count(h=>!h.collider.transform.IsChildOf(cafe.body.transform));
                }
                float score=blockers*100f+up*.9f+forward*.7f;
                if(score<best){best=score;bestHits=blockers;bestOffset=offset;}
                if(blockers==0)Debug.Log("ROOMVIEW_CLEAR_CANDIDATE station="+i+" up="+up+" forward="+forward+" score="+score.ToString("F3"));
            }
            Debug.Log("ROOMVIEW_BEST_CAMERA station="+i+" offset="+bestOffset+" blockers="+bestHits+" score="+best);
        }
    }
}