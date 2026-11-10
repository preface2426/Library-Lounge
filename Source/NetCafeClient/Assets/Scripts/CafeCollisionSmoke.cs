using System;
using System.Collections;
using System.Linq;
using UnityEngine;

// The smoke runs in actual built Windows clients, not just the editor.
// No desktop capture, native OS input injection, or LiveKit is needed.
public sealed class CafeCollisionSmoke : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-collisionSmoke") < 0) return;
        var cafe = FindFirstObjectByType<CafePrototype>();
        if (cafe == null) { Debug.LogError("COLLISION_SMOKE_FAIL: no CafePrototype"); Application.Quit(1); return; }
        cafe.gameObject.AddComponent<CafeCollisionSmoke>();
    }
    IEnumerator Start()
    {
        yield return null;
        bool pass=false;
        var cafe=GetComponent<CafePrototype>();
        if(cafe.GetComponent<CafeRestroomLayout>()!=null)
        {
            try{CafeRestroomCollisionChecks.Verify(cafe);pass=true;}
            catch(Exception ex){Debug.LogException(ex);Debug.LogError("COLLISION_SMOKE_FAIL: "+ex.Message);}
            Application.Quit(pass?0:1);yield break;
        }
        try
        {
            string[] walls={"North ivory wall","West ivory wall","East ivory wall",
                "South wall left","South wall right","Entrance arch lintel","Entrance door - closed"};
            string[] furniture={"Window reading desk 1","Window reading desk 2",
                "Close desk chair 1","Close desk chair 2","Warm framed monitor 1",
                "Warm framed monitor 2","Keyboard 1","Keyboard 2","Mouse 1","Mouse 2",
                "Amber task lamp 1","Amber task lamp 2","Reading recliner 1","Reading recliner 2",
                "Reading side table 1","Reading side table 2","Open books 1","Open books 2",
                "Social sofa - sage","Social laptop table","Corner reading chair",
                "Quiet corner round table","Guest rest sofa","Reading library shelves west",
                "Open bookcase north","Entry cabinet","Reading floor lamp",
                "Conversation floor lamp","Tall indoor greenery","Shelf plant","Corner plant"};
            var all=FindObjectsByType<Transform>(FindObjectsSortMode.None);
            foreach (var n in walls)
            {
                var t=all.SingleOrDefault(x=>x.name==n && x.gameObject.activeInHierarchy);
                if(t==null)throw new Exception("Missing active wall "+n);
                var c=t.GetComponent<Collider>();
                if(c==null||!c.enabled||c.isTrigger)throw new Exception("Wall is not solid "+n);
            }
            foreach(var n in furniture)
            {
                var model=all.SingleOrDefault(x=>x.name==n && x.gameObject.activeInHierarchy);
                if(model==null)throw new Exception("Missing visible furniture "+n);
                var proxy=all.SingleOrDefault(x=>x.name==n+" - walk blocker (no gaze ray)" &&
                    x.gameObject.activeInHierarchy);
                var c=(proxy!=null?proxy:model).GetComponent<Collider>();
                if(c==null||!c.enabled||c.isTrigger)throw new Exception("Furniture has no solid collider "+n);
            }
            var body=cafe.body;
            var original=body.transform.position;
            // Floor gravity under the actual character capsule, not a raycast-only test.
            body.enabled=false;
            body.transform.position=new Vector3(0,.06f,0);
            body.enabled=true;
            Physics.SyncTransforms();
            float vy=0;
            for(int i=0;i<180;i++)
            {
                vy=body.isGrounded?-2:Mathf.Max(vy-20/60f,-30);
                body.Move(Vector3.up*(vy/60f));
                if(body.transform.position.y<-.3f)throw new Exception("Fell through ground");
            }
            if(!body.isGrounded)throw new Exception("Player did not stand on floor");
            // A real controller must not cross the right wall in a clear test aisle.
            body.enabled=false;body.transform.position=new Vector3(4.3f,.05f,-2.1f);body.enabled=true;
            Physics.SyncTransforms();
            for(int i=0;i<120;i++)body.Move(new Vector3(.04f,-.002f,0));
            if(body.transform.position.x>5.72f)
                throw new Exception("Controller walked through east wall at "+body.transform.position);
            Debug.Log("COLLISION_WALL_WALK_PASS: east wall physically blocks CharacterController");
            // A visible sofa must prevent walking through its center. The seat
            // interaction trigger must *not* become an invisible player barrier.
            body.enabled=false;body.transform.position=new Vector3(1.4f,.05f,-2.35f);body.enabled=true;
            Physics.SyncTransforms();
            for(int i=0;i<120;i++)body.Move(new Vector3(0,-.002f,-.025f));
            if(body.transform.position.z< -4.25f)
                throw new Exception("Controller walked through rest sofa: "+body.transform.position);
            Debug.Log("COLLISION_FURNITURE_WALK_PASS: rest sofa physically blocks CharacterController");
            body.enabled=false;body.transform.position=original;body.enabled=true;
            for(int i=0;i<cafe.seats.Length;i++)
            {
                cafe.Sit(i);cafe.Stand();
                if(!body.enabled || cafe.CurrentSeat!=-1)
                    throw new Exception("Seat enter/leave broken "+i);
                if(body.transform.position.y<-.2f)
                    throw new Exception("Exited seat below floor "+i);
                if(i<2 && body.transform.position.z>3.55f)
                    throw new Exception("Desktop seat exited across the monitor desk "+i);

                if (Vector3.Distance(new Vector3(body.transform.position.x,0,body.transform.position.z),
                        new Vector3(cafe.seats[i].position.x,0,cafe.seats[i].position.z))>2.30f)
                    throw new Exception("Exited too far from seat "+i);
                var standing=body.transform.position;
                body.enabled=false;
                float r=body.radius+.035f;
                float span=Mathf.Max(0,body.height*.5f-r);
                Vector3 center=standing+body.center;
                bool overlapping=Physics.CheckCapsule(center-Vector3.up*span,center+Vector3.up*span,
                    r,~(1 << 28),QueryTriggerInteraction.Ignore);
                body.enabled=true;
                if(overlapping)throw new Exception("Seat exit overlaps a solid chair, sofa or table "+i);
                Debug.Log("SAFE_EXIT_CAPSULE_PASS seat="+i+" position="+standing);
            }
            Debug.Log("COLLISION_COVERAGE_SMOKE_PASS: "+walls.Length+" walls/door + "+
                furniture.Length+" furniture units + ground/controller + 8 seat exits");
            pass=true;
        }
        catch(Exception ex) {Debug.LogException(ex);Debug.LogError("COLLISION_SMOKE_FAIL: "+ex.Message);}
        Application.Quit(pass?0:1);
    }
}
