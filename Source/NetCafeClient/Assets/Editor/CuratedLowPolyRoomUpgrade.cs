using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// Curated model-only furnishing. No player/network/screen transform is intentionally changed.
// Preview FIRST; production edit is a separate, guarded command.
public static class CuratedLowPolyRoomUpgrade
{
    const string Base = "Assets/CozyLoungeGreybox/CuratedLowPoly/";
    const string PreviewPath = "Assets/CozyLoungeGreybox/Scenes/CuratedFurniturePreview_20261101.unity";
    const string RoomPath = "Assets/CozyLoungeGreybox/Scenes/LibraryLounge_Furnished.unity";
    const string RoomRoot = "LIBRARY LOUNGE V2 - KENNEY CC0 + WARM ARCHITECTURE";
    const string OutputFolder = "docs/CURATED-FURNITURE-PREVIEW-20261101";
    const string ReplacementRoot = "Curated 2026-11-01 - low saturation visual props";
    static readonly Dictionary<string,Material> palette = new Dictionary<string,Material>();

    static Material MakeMaterial(string id, string hex)
    {
        if (palette.ContainsKey(id)) return palette[id];
        string folder = Base + "Materials";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(Base.TrimEnd('/'), "Materials");
        string path = folder + "/" + id + ".mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        Color color;
        if (!ColorUtility.TryParseHtmlString("#" + hex,out color)) throw new Exception("Invalid palette " + hex);
        if (!m)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (!shader) throw new Exception("No built-in lit shader");
            m = new Material(shader){name=id};
            AssetDatabase.CreateAsset(m,path);
        }
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor",color);
        if (m.HasProperty("_Color")) m.SetColor("_Color",color);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness",.13f);
        EditorUtility.SetDirty(m);
        palette[id]=m; return m;
    }
    static void LoadPalette()
    {
        MakeMaterial("Curated-Cream","F1E9D9");
        MakeMaterial("Curated-Linen","D9D0C0");
        MakeMaterial("Curated-Oak","CBB395");
        MakeMaterial("Curated-Walnut","907865");
        MakeMaterial("Curated-Sage","A9B7A3");
        MakeMaterial("Curated-BlueGrey","B5C4C6");
        MakeMaterial("Curated-Clay","C7A499");
        MakeMaterial("Curated-Graphite","5D615D");
    }
    static Material M(string key) => palette["Curated-"+key];
    static string ModelPath(string pack,string name) =>
        Base + "Models/" + pack + "/" + name + ".fbx";

    static Bounds VisualBounds(GameObject go)
    {
        var r=go.GetComponentsInChildren<Renderer>(true).Where(x=>x is MeshRenderer || x is SkinnedMeshRenderer).ToArray();
        if(r.Length==0) throw new InvalidOperationException("Imported model contains no renderers: " + go.name);
        Bounds b=r[0].bounds;
        for(int i=1;i<r.Length;i++) b.Encapsulate(r[i].bounds);
        if (b.size.x<.00001f||b.size.y<.00001f||b.size.z<.00001f)
            throw new InvalidOperationException("Zero bounds for " + go.name);
        return b;
    }
    static GameObject Put(string pack,string model,string visibleName,Transform parent,
        Vector3 centerXZandBaseY,Vector3 size,string primary,string secondary,float yaw=0)
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath(pack,model));
        if (!prefab) throw new FileNotFoundException("Model not imported: " + ModelPath(pack,model));
        var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,parent);
        go.name=visibleName;
        bool alineTable=pack=="AlineCC0" && (model.StartsWith("simple_desk_",StringComparison.Ordinal) || model.StartsWith("simple_rect_table_",StringComparison.Ordinal));
        Quaternion axisFix=alineTable?Quaternion.Euler(-90f,0f,0f):Quaternion.identity;
        go.transform.SetPositionAndRotation(Vector3.zero,axisFix);
        go.transform.localScale=Vector3.one;
        var renderers=go.GetComponentsInChildren<Renderer>(true).Where(x=>x is MeshRenderer || x is SkinnedMeshRenderer).ToArray();
        var original=VisualBounds(go);
        // A ninety-degree placement swaps world X/Z; normalize to the requested WORLD footprint.
        bool swapAxes=Mathf.Abs(Mathf.Cos(yaw*Mathf.Deg2Rad))<.001f;
        go.transform.localScale=new Vector3((swapAxes?size.z:size.x)/original.size.x,
            size.y/original.size.y,(swapAxes?size.x:size.z)/original.size.z);
        go.transform.rotation=Quaternion.Euler(0,yaw,0)*axisFix;
        // Use world-space final bounds after rotation; apply a deterministic anchoring translation.
        Bounds b=VisualBounds(go);
        go.transform.position+=new Vector3(centerXZandBaseY.x-b.center.x,
            centerXZandBaseY.y-b.min.y,centerXZandBaseY.z-b.center.z);
        int index=0;
        foreach (Renderer r in renderers)
        {
            var src=r.sharedMaterials;
            if (src.Length==0) src=new Material[]{M(primary)};
            var mats = new Material[src.Length];
            for(int i=0;i<mats.Length;i++) mats[i]=(index++%4==1)?M(secondary):M(primary);
            r.sharedMaterials=mats;
            r.gameObject.layer=2;
        }
        // Imported models are decoration; leave physics to existing inexpensive collider proxies.
        foreach(var c in go.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
        go.layer=2;
        return go;
    }
    static GameObject Block(string name,Transform parent,Vector3 at,Vector3 size,string mat)
    {
        var o=GameObject.CreatePrimitive(PrimitiveType.Cube);
        o.name=name;o.transform.SetParent(parent,false);o.transform.position=at;
        o.transform.localScale=size;
        o.GetComponent<Renderer>().sharedMaterial=M(mat);
        UnityEngine.Object.DestroyImmediate(o.GetComponent<Collider>());
        o.layer=2;
        return o;
    }
    static void Label(Transform root,string value,Vector3 where)
    {
        var obj=new GameObject("Label - "+value);obj.transform.SetParent(root,false);
        obj.transform.position=where;
        obj.transform.rotation=Quaternion.Euler(90,0,0);
        var t=obj.AddComponent<TextMesh>();
        t.text=value;t.fontSize=48;t.characterSize=.025f;
        t.anchor=TextAnchor.MiddleCenter;t.color=new Color(.29f,.30f,.28f);
    }
    static void Capture(string relativeDir,string file,Vector3 pos,Vector3 target,float fov)
    {
        string dir=Path.GetFullPath(Path.Combine(Application.dataPath,"../../",relativeDir));
        Directory.CreateDirectory(dir);
        var camObj=new GameObject("QA temporary camera - not saved");
        Camera cam=camObj.AddComponent<Camera>();cam.enabled=false;
        cam.clearFlags=CameraClearFlags.SolidColor;
        cam.backgroundColor=new Color(.95f,.92f,.86f);
        cam.nearClipPlane=.04f;cam.farClipPlane=90;cam.fieldOfView=fov;
        cam.transform.position=pos;
        cam.transform.rotation=Quaternion.LookRotation(target-pos,Vector3.up);
        RenderTexture rt=RenderTexture.GetTemporary(1280,720,24);
        Texture2D tex=new Texture2D(1280,720,TextureFormat.RGB24,false);
        RenderTexture old=RenderTexture.active;
        try
        {
            // Warm up URP/SRP before readback: the very first render can be underlit.
            RenderPipeline.SubmitRenderRequest(cam,new UniversalRenderPipeline.SingleCameraRequest {destination=rt});
            RenderPipeline.SubmitRenderRequest(cam,new UniversalRenderPipeline.SingleCameraRequest {destination=rt});
            RenderTexture.active=rt;
            tex.ReadPixels(new Rect(0,0,1280,720),0,0);tex.Apply();
            int variations=0;
            Color bg=tex.GetPixel(2,2);
            for(int y=25;y<715;y+=35)
                for(int x=25;x<1250;x+=35)
                    if (Vector3.Distance((Vector4)tex.GetPixel(x,y),(Vector4)bg)>.07f) variations++;
            string filename=Path.Combine(dir,file);
            File.WriteAllBytes(filename,tex.EncodeToPNG());
            if(variations<8)throw new InvalidOperationException("Blank visual capture: "+file+" variations="+variations);
            Debug.Log("CURATED_RENDER_SAVED "+filename+" variations="+variations);
        }
        finally
        {
            RenderTexture.active=old;
            RenderTexture.ReleaseTemporary(rt);
            UnityEngine.Object.DestroyImmediate(tex);
            UnityEngine.Object.DestroyImmediate(camObj);
        }
    }
    [MenuItem("NetCafe/Library Lounge/Curated low-poly - 1 Build furniture preview")]
    public static void BuildPreview()
    {
        LoadPalette();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        Transform stage=new GameObject("CURATED FURNITURE / LOW SATURATION COZY MATERIALS").transform;
        Block("Matte cream floor",stage,new Vector3(0,-.08f,0),new Vector3(20,.15f,17),"Linen");
        // 20 samples, with the two user-selected packs supplying the majority.
        var examples = new [] {
            new []{"FurnitureV2","corner_sofa","Corner sofa","Sage","Cream"},
            new []{"FurnitureV2","recliner","Reading recliner","Clay","Linen"},
            new []{"FurnitureV2","loveseat","Loveseat","Cream","Sage"},
            new []{"FurnitureV2","rocking_chair","Rocking chair","Sage","Oak"},
            new []{"FurnitureV2","arc_floor_lamp","Arc lamp","Oak","Cream"},
            new []{"FurnitureV2","monstera_pot","Monstera","Sage","Walnut"},
            new []{"FurnitureV2","snake_plant","Snake plant","Sage","Clay"},
            new []{"FurnitureV2","cube_shelf","Cube shelf","Oak","Linen"},
            new []{"FurnitureV2","console_table","Console","Oak","Walnut"},
            new []{"FurnitureV2","round_rug","Round rug","Linen","Cream"},
            new []{"CozyCafe","SM_Chair_Simple","Cafe chair","Sage","Oak"},
            new []{"CozyCafe","SM_Table_Simple","Cafe table","Oak","Cream"},
            new []{"CozyCafe","SM_Shelf_Book","Cafe bookcase","Walnut","Sage"},
            new []{"CozyCafe","SM_Shelf_Floor","Cafe open shelf","Oak","Linen"},
            new []{"CozyCafe","SM_Shelf_Wall_Mounted","Cafe wall shelf","Walnut","Cream"},
            new []{"CozyCafe","SM_Barstool","Cafe stool","BlueGrey","Oak"},
            new []{"AlineCC0","simple_desk_A","Pastel desk A","Oak","Linen"},
            new []{"AlineCC0","simple_library_B","Pastel library","Cream","Oak"},
            new []{"AlineCC0","armchair","Pastel armchair","BlueGrey","Linen"},
            new []{"AlineCC0","coffee_table","Pastel coffee table","Walnut","Oak"}
        };
        var sizes=new Vector3[]{
            new Vector3(2.45f,.90f,1.04f),new Vector3(.87f,1.05f,1.06f),new Vector3(1.7f,.92f,.98f),
            new Vector3(.9f,1.06f,.98f),new Vector3(.6f,1.67f,.67f),new Vector3(.75f,1.08f,.72f),
            new Vector3(.64f,.91f,.6f),new Vector3(.9f,1.30f,.39f),new Vector3(1.2f,.74f,.47f),
            new Vector3(1.32f,.04f,1.32f),new Vector3(.67f,.97f,.66f),new Vector3(.95f,.75f,.77f),
            new Vector3(1.2f,1.75f,.45f),new Vector3(.95f,1.73f,.38f),new Vector3(1.05f,.37f,.28f),
            new Vector3(.48f,.93f,.48f),new Vector3(1.37f,.75f,.74f),new Vector3(1.15f,1.8f,.42f),
            new Vector3(.91f,.96f,.87f),new Vector3(1.2f,.50f,.72f)};
        for(int i=0;i<examples.Length;i++)
        {
            int col=i%5,row=i/5;
            Vector3 at=new Vector3((col-2)*3.76f,.035f,(1.5f-row)*3.68f);
            Block("Palette display plinth "+i,stage,at+Vector3.down*.018f,
                new Vector3(3.37f,.032f,3.27f),i%2==0?"Cream":"Linen");
            Put(examples[i][0],examples[i][1],examples[i][2],stage,at,sizes[i],examples[i][3],examples[i][4],0);
            Label(stage,examples[i][2],at+new Vector3(0,.01f,-1.36f));
        }
        var light=new GameObject("Soft natural key light").AddComponent<Light>();
        light.type=LightType.Directional;light.intensity=1.15f;
        light.color=new Color(1f,.95f,.87f);light.transform.rotation=Quaternion.Euler(48,-32,0);
        var fill=new GameObject("Soft ambient fill").AddComponent<Light>();
        fill.type=LightType.Directional;fill.intensity=.35f;
        fill.color=new Color(.88f,.91f,1f);fill.transform.rotation=Quaternion.Euler(30,145,0);
        if(!EditorSceneManager.SaveScene(scene,PreviewPath))throw new IOException("Cannot save curated preview");
        Capture(OutputFolder,"01-furniture-catalog.png",new Vector3(0,11,-14),new Vector3(0,.2f,0),65f);
        Capture(OutputFolder,"02-sofas-and-reading.png",new Vector3(-5.8f,5.4f,-6.5f),new Vector3(-4.0f,.5f,4f),66f);
        Capture(OutputFolder,"03-cafe-and-library.png",new Vector3(4.5f,5.2f,-8.2f),new Vector3(3f,.5f,-1f),62f);
        Debug.Log("CURATED_PREVIEW_PASS modelCount="+examples.Length+" path="+PreviewPath+" palette="+palette.Count);
    }

    static Transform FindUnique(Transform root,string name)
    {
        var hit=root.GetComponentsInChildren<Transform>(true).Where(x=>x.name==name).ToArray();
        if(hit.Length!=1) throw new InvalidOperationException("Unique required: "+name+" count="+hit.Length);
        return hit[0];
    }
    static void Swap(Transform decor,string oldName,string pack,string asset,
        Vector3 xzBase,Vector3 size,string color,string accent,float yaw)
    {
        Transform old=FindUnique(decor,oldName);
        // These are dedicated decorative mesh instances; do not replace any scripted gameplay Transform.
        if (old.GetComponentsInChildren<MonoBehaviour>(true).Any())
            throw new InvalidOperationException("Game component on candidate furniture: "+oldName);
        Transform parent=old.parent;
        UnityEngine.Object.DestroyImmediate(old.gameObject);
        var replacement=Put(pack,asset,oldName,parent,xzBase,size,color,accent,yaw);
        Debug.Log("CURATED_FURNITURE_REPLACED name="+oldName+" source="+pack+"/"+asset+
            " placed="+VisualBounds(replacement).center.ToString("F2"));
    }

    [MenuItem("NetCafe/Library Lounge/Curated low-poly - 2 Apply furniture to furnished room")]
    public static void ApplyToRoom()
    {
        string screenshot=Path.GetFullPath(Path.Combine(Application.dataPath,"../../",OutputFolder,"01-furniture-catalog.png"));
        if(!File.Exists(PreviewPath)||!File.Exists(screenshot))
            throw new InvalidOperationException("PREVIEW-FIRST GATE: build and render furniture preview first.");
        if(!File.Exists(RoomPath)) throw new FileNotFoundException(RoomPath);
        LoadPalette();
        Scene scene=EditorSceneManager.OpenScene(RoomPath,OpenSceneMode.Single);
        var roots=scene.GetRootGameObjects();
        var cafe=roots.SelectMany(x=>x.GetComponentsInChildren<CafePrototype>(true)).Single();
        Transform decor=roots.Single(x=>x.name==RoomRoot).transform;
        if (decor.Find(ReplacementRoot)!=null)
            throw new InvalidOperationException("Curated changes already applied; refusing duplicate.");
        Vector3[] baselineScreens=cafe.screens.Take(6).Select(x=>x.transform.position).ToArray();
        Vector3[] baselineSeats=cafe.seats.Take(8).Select(x=>x.position).ToArray();
        Vector3[] baselineExits=cafe.exits.Take(8).Select(x=>x.position).ToArray();
        Vector3 baselinePublic=cafe.publicScreen.transform.position;
        // Existing physical screen models and the eight seat/hotspot objects are not swapped.
        Swap(decor,"Window reading desk 1","AlineCC0","simple_desk_A",new Vector3(1.22f,.0255f,3.10f),new Vector3(1.85f,.74f,.80f),"Oak","Linen",0);
        Swap(decor,"Window reading desk 2","AlineCC0","simple_desk_B",new Vector3(3.72f,.0255f,3.10f),new Vector3(1.85f,.74f,.80f),"Oak","Linen",0);
        Swap(decor,"Close desk chair 1","CozyCafe","SM_Chair_Simple",new Vector3(1.20f,.0255f,2.54f),new Vector3(.69f,.96f,.70f),"Sage","Oak",174f);
        Swap(decor,"Close desk chair 2","CozyCafe","SM_Chair_Simple",new Vector3(3.74f,.0255f,2.54f),new Vector3(.69f,.96f,.70f),"BlueGrey","Oak",186f);
        Swap(decor,"Reading recliner 1","FurnitureV2","recliner",new Vector3(-4.02f,.0255f,1.30f),new Vector3(1.03f,1.06f,1.17f),"Clay","Linen",171f);
        Swap(decor,"Reading recliner 2","FurnitureV2","recliner",new Vector3(-1.78f,.0255f,1.32f),new Vector3(1.03f,1.06f,1.17f),"Sage","Linen",188f);
        Swap(decor,"Reading side table 1","CozyCafe","SM_Table_Simple",new Vector3(-4.00f,.0255f,2.02f),new Vector3(.88f,.62f,.69f),"Oak","Cream",0);
        Swap(decor,"Reading side table 2","CozyCafe","SM_Table_Simple",new Vector3(-1.80f,.0255f,2.02f),new Vector3(.88f,.62f,.69f),"Oak","Cream",0);
        Swap(decor,"Social sofa - sage","FurnitureV2","corner_sofa",new Vector3(-2.40f,.0325f,-3.27f),new Vector3(3.25f,1.06f,1.00f),"Sage","Linen",178f);
        Swap(decor,"Social laptop table","AlineCC0","coffee_table",new Vector3(-2.40f,.0325f,-2.18f),new Vector3(2.86f,.52f,.83f),"Oak","Walnut",0);
        Swap(decor,"Guest rest sofa","FurnitureV2","loveseat",new Vector3(1.40f,0f,-3.62f),new Vector3(2.44f,1.04f,1.04f),"Cream","Sage",183f);
        Swap(decor,"Corner reading chair","FurnitureV2","rocking_chair",new Vector3(.63f,0f,-1.10f),new Vector3(.88f,1.04f,.84f),"Sage","Oak",287f);
        // Coffee table remains clear of rest-seat exits and is angled toward wall cinema.
        Swap(decor,"Quiet corner round table","CozyCafe","SM_Table_Simple",new Vector3(2.90f,0f,-1.48f),new Vector3(.90f,.70f,.90f),"Walnut","Oak",0);
        Swap(decor,"Reading library shelves west","CozyCafe","SM_Shelf_Book",new Vector3(-5.67f,0f,.55f),new Vector3(.43f,2.03f,1.42f),"Oak","Walnut",90f);
        Swap(decor,"Open bookcase north","AlineCC0","simple_library_B",new Vector3(-4.84f,0f,4.69f),new Vector3(1.05f,1.90f,.43f),"Oak","Sage",0);
        Swap(decor,"Entry cabinet","FurnitureV2","console_table",new Vector3(4.52f,0f,-4.635f),new Vector3(.96f,.73f,.51f),"Walnut","Linen",0);
        Swap(decor,"Reading floor lamp","FurnitureV2","arc_floor_lamp",new Vector3(-5.70f,0f,2.55f),new Vector3(.34f,1.70f,.34f),"Oak","Cream",0);
        Swap(decor,"Conversation floor lamp","FurnitureV2","arc_floor_lamp",new Vector3(-5.70f,0f,-3.75f),new Vector3(.34f,1.75f,.34f),"Oak","Cream",0);
        Swap(decor,"Tall indoor greenery","FurnitureV2","monstera_pot",new Vector3(5.28f,0f,.76f),new Vector3(.60f,1.28f,.60f),"Sage","Clay",0);
        Swap(decor,"Corner plant","FurnitureV2","snake_plant",new Vector3(-.03f,0f,3.84f),new Vector3(.36f,.66f,.36f),"Sage","Clay",0);

        var cinema=FindUnique(decor,"CinemaLounge20261031 - curved seating and wall cinema");
        foreach(string name in new[]{"Cream curved sectional","Olive rounded accent chair","Cinema low coffee table","Walnut book cabinet","Low poly foliage in vase"})
            FindUnique(cinema,name).gameObject.SetActive(false);
        // Avoid the old decorative cabinet/rug/sofa mesh being overlaid on replacements.
        var newGroup=new GameObject(ReplacementRoot).transform;
        newGroup.SetParent(decor,false);
        Put("FurnitureV2","cube_shelf","Wall-side little reading shelves",newGroup,
            new Vector3(-5.56f,.1f,-1.45f),new Vector3(.55f,1.22f,.38f),"Oak","Sage",90f);
        Put("FurnitureV2","hanging_planter","Warm hanging planter",newGroup,
            new Vector3(-.6f,1.95f,4.65f),new Vector3(.40f,.58f,.4f),"Sage","Clay");
        // Explicitly keep old floor, screens, monitor support, and all functional anchors unchanged.
        CozyLoungeCollisionCoverage.RepairScene(scene);
        for(int i=0;i<6;i++)
        {
            if(Vector3.Distance(cafe.screens[i].transform.position,baselineScreens[i])>.0001f)
                throw new InvalidOperationException("Screen binding moved: "+i);
            if(Vector3.Distance(cafe.seats[i].position,baselineSeats[i])>.0001f)
                throw new InvalidOperationException("Seat eye binding moved: "+i);
        }
        for(int i=0;i<8;i++)
            if(Vector3.Distance(cafe.exits[i].position,baselineExits[i])>.0001f)
                throw new InvalidOperationException("Exit changed: "+i);
        if(Vector3.Distance(cafe.publicScreen.transform.position,baselinePublic)>.0001f)
            throw new InvalidOperationException("Public display shifted");
        EditorSceneManager.MarkSceneDirty(scene);
        if(!EditorSceneManager.SaveScene(scene))throw new IOException("Curated furnishings scene save failed");
        Capture(OutputFolder,"04-room-window-computers.png",new Vector3(2.5f,2.2f,.05f),new Vector3(2.5f,.8f,3.05f),66f);
        Capture(OutputFolder,"05-room-reading-laptops.png",new Vector3(-2.9f,2.15f,-.55f),new Vector3(-2.95f,.9f,1.65f),67f);
        Capture(OutputFolder,"06-room-social.png",new Vector3(-.5f,2.45f,.15f),new Vector3(-2.0f,.78f,-2.85f),72f);
        Capture(OutputFolder,"07-room-cinema.png",new Vector3(-.9f,2.35f,.55f),new Vector3(3.1f,1.35f,-2.2f),77f);
        Debug.Log("CURATED_ROOM_APPLY_PASS changedModels=20 sixScreens/8Eyes/8Exits/publicScreen invariant, no new player or room logic");
    }
    [MenuItem("NetCafe/Library Lounge/Curated low-poly - 2b Polish placement and architecture colors")]
    public static void PolishLayout()
    {
        LoadPalette();
        Scene scene=EditorSceneManager.OpenScene(RoomPath,OpenSceneMode.Single);
        Transform decor=scene.GetRootGameObjects().Single(x=>x.name==RoomRoot).transform;
        Transform additions=FindUnique(decor,ReplacementRoot);
        if (additions.Find("Soft lounge ottoman")) throw new InvalidOperationException("Polish already applied");
        // The earlier oval table let laptop keyboards overhang near both ends.
        // A rectangular flat top supports BOTH original unmodified laptop screens.
        Swap(decor,"Social laptop table","AlineCC0","simple_rect_table_B",
            new Vector3(-2.40f,.0325f,-2.18f),new Vector3(2.86f,.52f,.90f),"Oak","Linen",0f);
        // Give the front of each reading recliner clearance from its side table.
        foreach(string seatName in new[]{"Reading recliner 1","Reading recliner 2"})
        {
            Transform seat=FindUnique(decor,seatName);
            seat.position+=new Vector3(0,0,-.09f);
            EditorUtility.SetDirty(seat);
        }
        // A soft footrest occupies a corner, not a computer or rest-seat exit.
        Put("FurnitureV2","ottoman","Soft lounge ottoman",additions,
            new Vector3(3.63f,0f,-3.50f),new Vector3(.59f,.35f,.58f),"Sage","Linen");
        var recolor = new Dictionary<string,string> {
            {"Warm plank floor","Oak"},
            {"North ivory wall","Cream"},{"West ivory wall","Cream"},{"East ivory wall","Cream"},
            {"South wall left","Cream"},{"South wall right","Cream"},
            {"Central conversation rug","Linen"},{"Reading rug","Sage"},
            {"Window work rug","Cream"},{"Cinema woven area rug","Linen"}
        };
        foreach(var kv in recolor)
        {
            Transform tr=FindUnique(decor,kv.Key);
            var ren=tr.GetComponent<Renderer>();
            if (!ren) throw new InvalidOperationException("Cannot recolor: "+kv.Key);
            ren.sharedMaterial=M(kv.Value);EditorUtility.SetDirty(ren);
        }
        foreach(Transform tr in decor.GetComponentsInChildren<Transform>(true))
        {
            if(tr.name.StartsWith("Floor inlay ",StringComparison.Ordinal))
                tr.GetComponent<Renderer>().sharedMaterial=M("Walnut");
            if(tr.name.StartsWith("Window diffused daylight ",StringComparison.Ordinal))
                tr.GetComponent<Renderer>().sharedMaterial=M("BlueGrey");
        }
        CozyLoungeCollisionCoverage.RepairScene(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        if(!EditorSceneManager.SaveScene(scene))throw new IOException("Could not save the polished furnished room");
        Capture(OutputFolder,"08-room-window-polished.png",new Vector3(2.5f,2.2f,.05f),new Vector3(2.5f,.8f,3.05f),66f);
        Capture(OutputFolder,"09-room-reading-polished.png",new Vector3(-2.9f,2.15f,-.55f),new Vector3(-2.95f,.9f,1.65f),67f);
        Capture(OutputFolder,"10-room-social-polished.png",new Vector3(-.5f,2.45f,.15f),new Vector3(-2.0f,.78f,-2.85f),72f);
        Capture(OutputFolder,"11-room-cinema-polished.png",new Vector3(-.9f,2.35f,.55f),new Vector3(3.1f,1.35f,-2.2f),77f);
        Debug.Log("CURATED_POLISH_PASS: rectangular computer table; 9cm reading chair clearance; 10 recolored architectural objects; soft seating props");
    }

    [MenuItem("NetCafe/Library Lounge/Curated low-poly - 2c Final table design")]
    public static void FinalTables()
    {
        LoadPalette();
        Scene scene=EditorSceneManager.OpenScene(RoomPath,OpenSceneMode.Single);
        Transform decor=scene.GetRootGameObjects().Single(x=>x.name==RoomRoot).transform;
        var changes=FindUnique(decor,ReplacementRoot);
        if (changes.Find("FINAL TABLE PASS"))throw new InvalidOperationException("Already finalized");
        Swap(decor,"Social laptop table","CozyCafe","SM_Table_Simple",
            new Vector3(-2.40f,.0325f,-2.18f),new Vector3(2.86f,.52f,.93f),"Oak","Linen",0f);
        Swap(decor,"Quiet corner round table","AlineCC0","coffee_table",
            new Vector3(2.90f,0f,-1.48f),new Vector3(1.12f,.50f,.84f),"Walnut","Oak",0f);
        new GameObject("FINAL TABLE PASS").transform.SetParent(changes,false);
        CozyLoungeCollisionCoverage.RepairScene(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        if(!EditorSceneManager.SaveScene(scene))throw new IOException("Could not save final table layout");
        Capture(OutputFolder,"12-room-social-final.png",new Vector3(-.5f,2.45f,.15f),new Vector3(-2.0f,.78f,-2.85f),72f);
        Capture(OutputFolder,"13-room-cinema-final.png",new Vector3(-.9f,2.35f,.55f),new Vector3(3.1f,1.35f,-2.2f),77f);
        Debug.Log("CURATED_FINAL_TABLES_PASS: open leg table supports laptops; oval table fits cinema");
    }

    [MenuItem("NetCafe/Library Lounge/Curated low-poly - Diagnose Aline model axes")]
    public static void DiagnoseAlineAxes()
    {
        LoadPalette();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var root=new GameObject("Aline rotation alternatives - left 0 / middle +90 / right -90").transform;
        Block("Background floor",root,new Vector3(0,-.08f,0),new Vector3(15,.12f,13),"Linen");
        string[] names={"simple_rect_table_B","simple_desk_A","simple_library_B"};
        float[] tilt={0f,90f,-90f};
        for(int i=0;i<names.Length;i++)for(int j=0;j<3;j++)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath("AlineCC0",names[i]));
            var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,root);
            go.name=names[i]+" TiltX="+tilt[j];
            go.transform.localScale=Vector3.one;go.transform.position=Vector3.zero;
            go.transform.rotation=Quaternion.Euler(tilt[j],0,0);
            Bounds b=VisualBounds(go);
            var size=i==2?new Vector3(1.2f,1.8f,.5f):new Vector3(2.2f,.74f,1.0f);
            go.transform.localScale=new Vector3(size.x/b.size.x,size.y/b.size.y,size.z/b.size.z);
            b=VisualBounds(go);
            go.transform.position+=new Vector3((j-1)*4.15f-b.center.x,
                .035f-b.min.y,(1-i)*3.70f-b.center.z);
            foreach(var r in go.GetComponentsInChildren<Renderer>(true))
                r.sharedMaterials=Enumerable.Repeat(M(i==2?"Oak":"Cream"),Math.Max(1,r.sharedMaterials.Length)).ToArray();
            Label(root,go.name,new Vector3((j-1)*4.15f,.035f,(1-i)*3.7f-1.4f));
        }
        var light=new GameObject("Directional warm").AddComponent<Light>();
        light.type=LightType.Directional;light.intensity=1.5f;light.transform.rotation=Quaternion.Euler(45,30,0);
        string path="Assets/CozyLoungeGreybox/Scenes/AlineAxisDiagnostic_20261101.unity";
        EditorSceneManager.SaveScene(scene,path);
        Capture(OutputFolder,"14-aline-axis-diagnostic.png",new Vector3(0,8,-11),new Vector3(0,.1f,0),68f);
        Debug.Log("ALINE_AXIS_DIAGNOSTIC_PASS: "+path);
    }

    [MenuItem("NetCafe/Library Lounge/Curated low-poly - 2d Correct Aline FBX table axes")]
    public static void CorrectAlineTables()
    {
        LoadPalette();
        Scene scene=EditorSceneManager.OpenScene(RoomPath,OpenSceneMode.Single);
        Transform decor=scene.GetRootGameObjects().Single(x=>x.name==RoomRoot).transform;
        Transform additions=FindUnique(decor,ReplacementRoot);
        if(additions.Find("ALINE AXES CORRECTED"))throw new InvalidOperationException("Aline axes already corrected");
        Swap(decor,"Window reading desk 1","AlineCC0","simple_desk_A",
            new Vector3(1.22f,.0255f,3.10f),new Vector3(1.85f,.74f,.80f),"Oak","Linen",0f);
        Swap(decor,"Window reading desk 2","AlineCC0","simple_desk_B",
            new Vector3(3.72f,.0255f,3.10f),new Vector3(1.85f,.74f,.80f),"Oak","Linen",0f);
        Swap(decor,"Social laptop table","AlineCC0","simple_rect_table_B",
            new Vector3(-2.40f,.0325f,-2.18f),new Vector3(2.86f,.52f,.93f),"Oak","Linen",0f);
        new GameObject("ALINE AXES CORRECTED").transform.SetParent(additions,false);
        CozyLoungeCollisionCoverage.RepairScene(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        if(!EditorSceneManager.SaveScene(scene))throw new IOException("Could not save corrected FBX tables");
        Capture(OutputFolder,"16-room-workstations-corrected.png",new Vector3(2.5f,2.2f,.05f),new Vector3(2.5f,.8f,3.05f),66f);
        Capture(OutputFolder,"17-room-social-corrected.png",new Vector3(-.5f,2.45f,.15f),new Vector3(-2.0f,.78f,-2.85f),72f);
        Debug.Log("CURATED_TABLE_AXES_PASS: reoriented FBX desk A/B and rectangle table; screen and seat transforms untouched");
    }

    [MenuItem("NetCafe/Library Lounge/Curated low-poly - 2e Final FBX axis leg correction")]
    public static void FixAlineTableLegs()
    {
        LoadPalette();
        Scene scene=EditorSceneManager.OpenScene(RoomPath,OpenSceneMode.Single);
        Transform decor=scene.GetRootGameObjects().Single(x=>x.name==RoomRoot).transform;
        Transform additions=FindUnique(decor,ReplacementRoot);
        if(additions.Find("ALINE MINUS 90 FINAL")) throw new InvalidOperationException("FBX leg fix already applied");
        Swap(decor,"Window reading desk 1","AlineCC0","simple_desk_A",
            new Vector3(1.22f,.0255f,3.10f),new Vector3(1.85f,.74f,.80f),"Oak","Linen",0f);
        Swap(decor,"Window reading desk 2","AlineCC0","simple_desk_B",
            new Vector3(3.72f,.0255f,3.10f),new Vector3(1.85f,.74f,.80f),"Oak","Linen",0f);
        Swap(decor,"Social laptop table","AlineCC0","simple_rect_table_B",
            new Vector3(-2.40f,.0325f,-2.18f),new Vector3(2.86f,.52f,.93f),"Oak","Linen",0f);
        new GameObject("ALINE MINUS 90 FINAL").transform.SetParent(additions,false);
        CozyLoungeCollisionCoverage.RepairScene(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        if(!EditorSceneManager.SaveScene(scene))throw new IOException("Could not save final FBX orientations");
        Capture(OutputFolder,"18-room-workstations-final.png",new Vector3(2.5f,2.2f,.05f),new Vector3(2.5f,.8f,3.05f),66f);
        Capture(OutputFolder,"19-room-social-final.png",new Vector3(-.5f,2.45f,.15f),new Vector3(-2.0f,.78f,-2.85f),72f);
        Debug.Log("CURATED_AXIS_LEGS_FINAL_PASS: Aline desks and table corrected -90deg; cameras and seats unchanged");
    }

    [MenuItem("NetCafe/Library Lounge/Curated low-poly - 4 Build isolated Windows visual QA")]
    public static void BuildIsolatedQA()
    {
        Verify();
        string output=Path.GetFullPath(Path.Combine(Application.dataPath,
            "../../Builds/LibraryLounge-CuratedFurniture-QA-20261101/LibraryLounge.exe"));
        if(Directory.Exists(Path.GetDirectoryName(output)))
            throw new IOException("Do not overwrite existing curated QA build: "+output);
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes=new[]{RoomPath},locationPathName=output,
            target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development
        });
        if(result.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)
            throw new InvalidOperationException("Curated QA build failed: "+result.summary.result);
        Debug.Log("CURATED_QA_BUILD_PASS path="+output+" size="+result.summary.totalSize);
    }

    [MenuItem("NetCafe/Library Lounge/Curated low-poly - 3 Verify gameplay and collisions")]
    public static void Verify()
    {
        var scene=EditorSceneManager.OpenScene(RoomPath,OpenSceneMode.Single);
        var decor=scene.GetRootGameObjects().Single(x=>x.name==RoomRoot).transform;
        FindUnique(decor,ReplacementRoot);
        CozyLoungeCollisionCoverage.Verify(scene);
        LibraryLoungeBuild.Validate();
        Debug.Log("CURATED_ROOM_VERIFY_PASS collision and gameplay components checked");
    }
}