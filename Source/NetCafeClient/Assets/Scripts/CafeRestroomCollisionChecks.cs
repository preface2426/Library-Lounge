using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Runs only through the explicitly requested collision smoke in a built client.
public static class CafeRestroomCollisionChecks
{
    static void Require(bool condition,string message)
    {
        if(!condition)throw new Exception(message);
        Debug.Log("RESTROOM_COLLISION_PASS "+message);
    }
    public static void Verify(CafePrototype cafe)
    {
        var body=cafe.body;var spawn=body.transform.position;
        float vertical=0;
        for(int i=0;i<180;i++)
        {
            vertical=body.isGrounded?-2:Mathf.Max(vertical-20f/60,-30);
            body.Move(Vector3.up*(vertical/60));
            if(body.transform.position.y<-.3f)throw new Exception("Player fell below restroom floor");
        }
        Require(body.isGrounded,"actual CharacterController stands on restroom floor");
        bool Furniture(Transform t)
        {
            for(;t!=null;t=t.parent)
                if(t.name=="LoungeFurniture_20261103"||t.name.StartsWith("ComputerSeatInteraction_")||t.name.StartsWith("SofaInteraction_"))return true;
            return false;
        }
        var furniture=UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Where(r=>Furniture(r.transform)).ToArray();
        Require(furniture.Length>30,"restroom furniture collision coverage includes "+furniture.Length+" visible meshes");
        foreach(var renderer in furniture)
        {
            var collider=renderer.GetComponent<MeshCollider>();var filter=renderer.GetComponent<MeshFilter>();
            Require(collider!=null&&collider.enabled&&!collider.isTrigger&&filter!=null&&collider.sharedMesh==filter.sharedMesh,"solid furniture mesh matches visible model: "+renderer.name);
        }
        var desk=furniture.Single(r=>r.name=="desk_ne_0_ImportedFBX");
        var deskBounds=desk.bounds;
        var chairColliders=cafe.seatedVisualRoots[0].GetComponentsInChildren<Collider>().Where(c=>c.enabled&&!c.isTrigger).ToArray();
        foreach(var c in chairColliders)c.enabled=false;
        try
        {
            body.enabled=false;body.transform.position=new Vector3(deskBounds.center.x,.06f,deskBounds.min.z-body.radius-.1f);Physics.SyncTransforms();
            float span=Mathf.Max(0,body.height*.5f-body.radius);var mid=body.transform.position+body.center;
            Require(!Physics.CheckCapsule(mid-Vector3.up*span,mid+Vector3.up*span,body.radius,~(1<<28),QueryTriggerInteraction.Ignore),"desk probe starts outside solid furniture");
            Require(Physics.CapsuleCast(mid-Vector3.up*span,mid+Vector3.up*span,body.radius,Vector3.forward,out var deskHit,2,~(1<<28),QueryTriggerInteraction.Ignore)&&deskHit.collider==desk.GetComponent<MeshCollider>(),"desk probe first hit belongs to actual desk mesh");
            body.enabled=true;
            for(int i=0;i<100;i++)body.Move(new Vector3(0,-.005f,.025f));
            Require(body.transform.position.z<deskBounds.center.z-.15f,"actual CharacterController cannot walk through desktop desk");
        }
        finally{foreach(var c in chairColliders)c.enabled=true;}
        body.enabled=false;body.transform.position=new Vector3(8.4f,.06f,4);body.enabled=true;Physics.SyncTransforms();
        for(int i=0;i<150;i++)body.Move(new Vector3(.04f,-.005f,0));
        Require(body.transform.position.x<9.55f&&body.transform.position.x>8.3f,"east wall physically blocks walking capsule");
        body.enabled=false;body.transform.position=spawn;Physics.SyncTransforms();
        var center=body.center;float radius=body.radius+.025f;
        float half=Mathf.Max(0,body.height*.5f-radius);
        Vector3 Low(Vector3 p)=>p+center-Vector3.up*half;
        Vector3 High(Vector3 p)=>p+center+Vector3.up*half;
        const float step=.4f;
        Vector3 Point(Vector2Int p)=>new Vector3(spawn.x+p.x*step,.06f,spawn.z+p.y*step);
        bool Clear(Vector3 p)=>!Physics.CheckCapsule(Low(p),High(p),radius,~(1<<28),QueryTriggerInteraction.Ignore);
        Require(Clear(Point(Vector2Int.zero)),"spawn capsule has no furniture overlap");
        var visited=new HashSet<Vector2Int>{Vector2Int.zero};var queue=new Queue<Vector2Int>();queue.Enqueue(Vector2Int.zero);
        var reachable=new List<Vector3>();
        var offsets=new[]{Vector2Int.up,Vector2Int.down,Vector2Int.left,Vector2Int.right};
        while(queue.Count>0)
        {
            var cell=queue.Dequeue();var from=Point(cell);reachable.Add(from);
            foreach(var offset in offsets)
            {
                var next=cell+offset;if(visited.Contains(next))continue;
                var to=Point(next);if(Mathf.Abs(to.x)>9.1f||Mathf.Abs(to.z)>7.5f)continue;
                if(!Clear(to)||Physics.CapsuleCast(Low(from),High(from),radius,(to-from).normalized,step,~(1<<28),QueryTriggerInteraction.Ignore))continue;
                visited.Add(next);queue.Enqueue(next);
            }
        }
        Require(reachable.Count>100,"connected capsule-clear walking region sampled from spawn: "+reachable.Count);
        var targets=UnityEngine.Object.FindObjectsByType<CafeInteractable>(FindObjectsSortMode.None);
        for(int i=0;i<6;i++)
        {
            var target=targets.Single(t=>t.name=="ComputerSeatInteraction_"+i);
            bool visible=reachable.Any(p=>
            {
                Vector3 head=p+Vector3.up*cafe.WalkingEyeHeight;Vector3 direction=target.transform.position-head;
                return direction.magnitude<3&&Physics.Raycast(head,direction.normalized,out var hit,3,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Collide)&&hit.collider.GetComponentInParent<CafeInteractable>()==target;
            });
            Require(visible,"computer "+i+" has an E gaze point connected to spawn by capsule-clear steps");
        }
        body.enabled=true;
        for(int i=0;i<cafe.seats.Length;i++)
        {
            cafe.Sit(i);cafe.Stand();
            Require(cafe.CurrentSeat==-1&&body.enabled,"seat "+i+" restores walking controller");
            body.enabled=false;Physics.SyncTransforms();
            Require(Clear(body.transform.position),"seat "+i+" exit capsule is collision-free");
            Require(cafe.walkableFloor.Raycast(new Ray(body.transform.position+Vector3.up,Vector3.down),out var hit,2)&&hit.normal.y>.9f,"seat "+i+" exit has supporting floor");
            body.enabled=true;
        }
        Debug.Log("COLLISION_COVERAGE_SMOKE_PASS: Restroom grounded controller, wall, spawn-connected six E gaze paths, eight safe exits");
    }
}
