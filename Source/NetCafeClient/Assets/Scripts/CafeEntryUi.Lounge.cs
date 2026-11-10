using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Presentation shares the original session/device handlers; no simulated actions.
public sealed partial class CafeEntryUi
{
    RectTransform loungeHeader, loungeIdentity, loungeOrbit, loungeFooter;
    Button footerButton,lobbyQuitButton;
    TMP_Text identityName, identityMic, headerState;
    RawImage identityPortrait;
    Texture2D portraitAtlas;
    string portraitId;
    Button identityMicrophone;
    Page utilityReturn = Page.Lobby;
    bool roomEntered;
    TMP_Text[,] memberCells;
    RectTransform[] memberRowRoots;
    string UtilityBackLabel => utilityReturn == Page.Home ? "返回首页" : utilityReturn == Page.Members ? "返回成员" : "返回大厅";
    CafeRoomTransition roomTransition;

    bool IsLoungePage => page == Page.Home || page == Page.Lobby || page == Page.Members;

    void BuildLounge()
    {
        var sans = Resources.Load<TMP_FontAsset>("LoungeFonts/NotoSansSC-Dynamic");
        var serif = Resources.Load<TMP_FontAsset>("LoungeFonts/NotoSerifSC-Dynamic");
        if (sans != null) bodyFont = sans;
        if (serif != null) titleFont = serif;
        card.GetComponent<Outline>().enabled = false;
        loungeHeader = Node("LoungeHeader", safeRoot);
        loungeHeader.anchorMin = new Vector2(0, 1); loungeHeader.anchorMax = Vector2.one;
        loungeHeader.pivot = new Vector2(.5f, 1); loungeHeader.sizeDelta = new Vector2(-80, 76);
        var brand = AddText(loungeHeader, "LIBRARY LOUNGE   /   线上网吧", 17, CafeUiTheme.Ink, 30, true);
        Anchor(brand.rectTransform, new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(0, .5f), Vector2.zero, new Vector2(600, 36));
        brand.alignment = TextAlignmentOptions.Left; brand.enableAutoSizing = true; brand.fontSizeMin = 11; brand.fontSizeMax = 17;
        headerState = AddText(loungeHeader, "未入房", 14, CafeUiTheme.InkSoft, 30);
        Anchor(headerState.rectTransform, new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(1, .5f), Vector2.zero, new Vector2(200, 36));
        headerState.alignment = TextAlignmentOptions.Right;
        Rule(loungeHeader, "HeaderRule", new Vector2(0, 0), new Vector2(1, 0));

        loungeIdentity = Node("Identity", safeRoot);
        Anchor(loungeIdentity, new Vector2(1,.5f), new Vector2(1,.5f), new Vector2(1,.5f), new Vector2(-42, 10), new Vector2(230, 370));
        var stack = loungeIdentity.gameObject.AddComponent<VerticalLayoutGroup>();
        stack.spacing = 8; stack.childControlWidth = stack.childControlHeight = true;
        stack.childForceExpandWidth = true; stack.childForceExpandHeight = false;
        var identityRow=Node("PortraitAndName",loungeIdentity);Height(identityRow,64);
        var portraitButton=Node("SelectedAvatar",identityRow);
        Anchor(portraitButton,new Vector2(0,.5f),new Vector2(0,.5f),new Vector2(0,.5f),Vector2.zero,new Vector2(60,60));
        identityPortrait=portraitButton.gameObject.AddComponent<RawImage>();identityPortrait.color=Color.white;
        portraitButton.gameObject.AddComponent<Button>().onClick.AddListener(()=>OpenAvatarPage(page));
        portraitAtlas=Resources.Load<Texture2D>("LoungePortraits/characters-5x2");
        identityName = AddText(identityRow, nickname, 23, CafeUiTheme.Ink, 50, true);
        Anchor(identityName.rectTransform,new Vector2(0,.5f),new Vector2(1,.5f),new Vector2(.5f,.5f),new Vector2(36,0),new Vector2(-72,50));
        identityName.alignment = TextAlignmentOptions.Left; identityName.enableAutoSizing = true; identityName.fontSizeMin = 16; identityName.fontSizeMax = 23;
        identityMicrophone = AddButton("麦克风   关闭", ToggleRoomMicrophone, false, loungeIdentity);
        identityMic = identityMicrophone.GetComponentInChildren<TMP_Text>(); identityMic.fontSize = 13;identityMicrophone.GetComponent<LayoutElement>().minHeight=identityMicrophone.GetComponent<LayoutElement>().preferredHeight=32;
        identityMic.alignment = TextAlignmentOptions.Left;

        loungeOrbit = Node("BrassOrbit", safeRoot);
        Anchor(loungeOrbit, new Vector2(1,1), new Vector2(1,1), Vector2.one, new Vector2(-310,-96), new Vector2(190,190));
        loungeOrbit.gameObject.AddComponent<CafeLoungeOrbit>();
        loungeFooter=Node("LoungeFooter",safeRoot);
        Anchor(loungeFooter,new Vector2(0,0),new Vector2(1,0),new Vector2(.5f,0),Vector2.zero,new Vector2(-80,56));
        Rule(loungeFooter,"FooterRule",new Vector2(0,1),Vector2.one);
        footerButton=AddButton("返回首页",()=> {if(page==Page.Members)CloseModal();else Show(Page.Home);},false,loungeFooter);
        Anchor((RectTransform)footerButton.transform,new Vector2(0,0),new Vector2(0,0),Vector2.zero,Vector2.zero,new Vector2(190,42));
        footerButton.GetComponentInChildren<TMP_Text>().fontSize=13;
        lobbyQuitButton=AddButton("退出应用",RequestQuitFromUi,false,loungeFooter);
        Anchor((RectTransform)lobbyQuitButton.transform,Vector2.right,Vector2.right,Vector2.right,Vector2.zero,new Vector2(140,42));
        lobbyQuitButton.GetComponentInChildren<TMP_Text>().fontSize=13;
        roomTransition = gameObject.AddComponent<CafeRoomTransition>();
        roomTransition.Configure(cafe, bodyFont, titleFont);
        var quiet = gameObject.AddComponent<CafeLoungeHud>();
        quiet.Configure(cafe, hud, this, bodyFont, titleFont);
    }

    static void Anchor(RectTransform rt, Vector2 min, Vector2 max, Vector2 pivot, Vector2 position, Vector2 size)
    {
        rt.anchorMin = min; rt.anchorMax = max; rt.pivot = pivot; rt.anchoredPosition = position; rt.sizeDelta = size;
    }
    static void Rule(Transform parent, string name, Vector2 min, Vector2 max)
    {
        var rt = Node(name, parent);
        Anchor(rt, min, max, Vector2.zero, Vector2.zero, new Vector2(0,1));
        var image = rt.gameObject.AddComponent<Image>(); image.color = CafeUiTheme.Line; image.raycastTarget = false;
    }
    void LoungeResize(float w, float h)
    {
        if (loungeHeader == null) return;
        bool wide = w >= 1100 && Screen.safeArea.width / Mathf.Max(1, Screen.safeArea.height) >= 1.25f;
        loungeIdentity.gameObject.SetActive(IsLoungePage && page != Page.Home);
        var identityLayout=loungeIdentity.GetComponent<VerticalLayoutGroup>();identityLayout.enabled=wide;
        var portraitRow=(RectTransform)identityPortrait.transform.parent;
        if(wide)
        {
            Anchor(loungeIdentity,Vector2.one,Vector2.one,Vector2.one,new Vector2(-w*.10f-10,-112),new Vector2(210,150));
            identityMicrophone.GetComponent<LayoutElement>().ignoreLayout=false;
            portraitRow.GetComponent<LayoutElement>().ignoreLayout=false;
        }
        else
        {
            Anchor(loungeIdentity,Vector2.zero,Vector2.right,new Vector2(.5f,0),new Vector2(0,56),new Vector2(-48,64));
            portraitRow.GetComponent<LayoutElement>().ignoreLayout=true;
            identityMicrophone.GetComponent<LayoutElement>().ignoreLayout=true;
            Anchor(portraitRow,new Vector2(0,.5f),new Vector2(0,.5f),new Vector2(0,.5f),Vector2.zero,new Vector2(Mathf.Max(150,(w-48)*.55f),64));
            Anchor((RectTransform)identityMicrophone.transform,Vector2.right*.58f,Vector2.right,new Vector2(.5f,.5f),new Vector2(0,32),new Vector2(0,44));
        }
        loungeOrbit.gameObject.SetActive(IsLoungePage && w >= 850 && h >= 620);
        loungeHeader.sizeDelta = new Vector2(-Mathf.Min(80, w * .08f), 76);
        var brand = loungeHeader.GetChild(0).GetComponent<TMP_Text>();
        brand.rectTransform.sizeDelta = new Vector2(Mathf.Max(100, Mathf.Min(600, w - 300)), 36);
        bool identityBelow=!wide&&IsLoungePage&&page!=Page.Home;
        card.sizeDelta = new Vector2(Mathf.Max(1,IsLoungePage?Mathf.Min(1280,w*.84f):Mathf.Min(640,w-48)),Mathf.Max(1,h-(identityBelow?246:170)));
        card.anchoredPosition = new Vector2(0,identityBelow?26:-12);
        Anchor(loungeOrbit,Vector2.one,Vector2.one,Vector2.one,
            page==Page.Home?new Vector2(-w*.12f,-130):new Vector2(-w*.1f-270,-100),
            page==Page.Home?new Vector2(Mathf.Min(420,w*.34f),380):new Vector2(190,170));
        card.GetComponent<Image>().color = IsLoungePage?Color.clear:CafeUiTheme.Paper;
        var layout = content.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(28, 28, 26, 32); layout.spacing = IsLoungePage ? 18 : 10;
        loungeFooter.gameObject.SetActive(IsLoungePage);
        float footerSlot=Mathf.Max(1,(w-92)*.5f);
        ((RectTransform)footerButton.transform).sizeDelta=new Vector2(Mathf.Min(190,footerSlot),42);
        ((RectTransform)lobbyQuitButton.transform).sizeDelta=new Vector2(Mathf.Min(140,footerSlot),42);
        footerButton.gameObject.SetActive(page!=Page.Home);
        lobbyQuitButton.gameObject.SetActive(page==Page.Lobby);
        footerButton.GetComponentInChildren<TMP_Text>().text=page==Page.Home?"退出应用":page==Page.Members?"返回房间":"返回首页";
        UpdateLoungeIdentity();
    }
    void UpdateLoungeIdentity()
    {
        if (identityName == null) return;
        identityName.text = nickname;
        if(portraitAtlas!=null && portraitId!=CafeAvatarLibrary.SelectedId)
        {
            portraitId=CafeAvatarLibrary.SelectedId;
            int index=Array.IndexOf(CafeAvatarLibrary.Ids,portraitId);
            identityPortrait.texture=portraitAtlas;
            identityPortrait.uvRect=new Rect(index%5/5f,(1-index/5)/2f,.2f,.5f);
        }
#if NETCAFE_B1
        identityMic.text = "麦克风   " + (voice != null && voice.MicrophoneBusy ? "正在申请…" :
            InNetworkRoom ? voice != null && voice.MicrophoneActive ? "开启" : "关闭" : microphoneOnEntry ? "入房开启" : "关闭");
        identityMicrophone.interactable = voice != null && !voice.MicrophoneBusy;
        headerState.text = InNetworkRoom ? network.MemberCount + " / 6" : roomEntered ? "单人体验" : "未入房";
#endif
    }

    bool RenderLoungePage()
    {
        if (!IsLoungePage) return false;
        if (page == Page.Home)
        {
            var library=AddText(content,"Library",116,CafeUiTheme.Ink,142,true);
            library.fontStyle=FontStyles.Bold;library.enableAutoSizing=true;library.fontSizeMin=64;library.fontSizeMax=116;
            var lounge=AddText(content,"  Lounge",116,new Color(.49f,.40f,.26f),150,true);
            lounge.fontStyle=FontStyles.Italic; lounge.enableAutoSizing=true;lounge.fontSizeMin=64;lounge.fontSizeMax=116;
            var homeActions=Node("HomeActions",content);Height(homeActions,198);
            var actionsLayout=homeActions.gameObject.AddComponent<VerticalLayoutGroup>();actionsLayout.spacing=10;
            actionsLayout.childControlWidth=actionsLayout.childControlHeight=true;actionsLayout.childForceExpandWidth=false;actionsLayout.childForceExpandHeight=false;
            var enter=AddButton("进入大厅   ↗",()=>Show(Page.Lobby),true,homeActions);
            var enterSize=enter.GetComponent<LayoutElement>();enterSize.preferredWidth=350;enterSize.minWidth=0;
            AddButton("单人体验",EnterSingle,false,homeActions).GetComponent<LayoutElement>().preferredWidth=350;
            var quit=AddButton("退出应用",RequestQuitFromUi,false,homeActions);
            quit.GetComponent<LayoutElement>().preferredWidth=350;
            quit.GetComponentInChildren<TMP_Text>().fontSize=17;
            LoungeUtilities(Page.Home);
            return true;
        }
        AddText(content, page == Page.Lobby ? "大厅" : "私人房间", 68, CafeUiTheme.Ink, 148, true).alignment = TextAlignmentOptions.Left;
        AddRule(content);
        if (page == Page.Lobby)
        {
            bool wide = card.sizeDelta.x >= 680 && Screen.safeArea.width / Mathf.Max(1, Screen.safeArea.height) >= 1.15f;
            var row = Node("CreateJoinColumns", content);
            Height(row, wide ? 310 : 565);
            HorizontalOrVerticalLayoutGroup layout = wide ? row.gameObject.AddComponent<HorizontalLayoutGroup>() : row.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = wide ? 76 : 20;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandHeight = !wide; layout.childForceExpandWidth = true;
            var create = LoungeColumn(row, "CreateColumn");
            var join = LoungeColumn(row, "JoinColumn");
            AddText(create, "创建", 34, CafeUiTheme.Ink, 50, true).alignment = TextAlignmentOptions.Left;
            AddText(create, "最多  6  人", 17, CafeUiTheme.InkSoft, 94).alignment = TextAlignmentOptions.Left;
            AddButton("创建房间   ↗", () => Show(Page.Create), true, create);
            AddButton("单人体验", EnterSingle, false, create);
            AddText(join, "加入", 34, CafeUiTheme.Ink, 50, true).alignment = TextAlignmentOptions.Left;
            // The same native input builder is reused within the Join column.
            var previous = content; content = join;
            inviteField = AddInput("邀请码", inviteInput, "朋友发送的邀请码", 40, v => inviteInput = v);
            content = previous;
            AddButton("加入房间   ↗", () =>
            {
                if (string.IsNullOrWhiteSpace(inviteInput)) { message = "请输入邀请码。"; Render(true); inviteField?.ActivateInputField(); }
                else Show(Page.Join);
            }, true, join);
            LoungeUtilities(Page.Lobby);
        }
        else
        {
#if NETCAFE_B1
            memberCountText = AddText(content, InNetworkRoom?"成员  " + network.MemberCount + " / 6":"成员  1 / 1", 16, CafeUiTheme.InkSoft, 35);
            BuildMemberTable();
            var actions = Node("MemberActions", content); Height(actions, 54);
            actions.gameObject.SetActive(InNetworkRoom);
            var actionLayout = actions.gameObject.AddComponent<HorizontalLayoutGroup>();
            actionLayout.spacing = 18; actionLayout.childControlWidth = actionLayout.childControlHeight = true;
            actionLayout.childForceExpandWidth = true; actionLayout.childForceExpandHeight = false;
            AddText(actions,network.InviteCode,23,CafeUiTheme.Ink,54,true);
            AddButton("复制邀请码", CopyInvite, false, actions);
            AddButton("语音与屏幕", () => Show(Page.RoomControls, true), false, actions);
            AddButton("刷新成员", () => { network.RefreshMembers(); message = "正在刷新成员…"; Render(true); }, false, actions);
#endif
            var bottom=Node("RoomReturnActions",content);Height(bottom,54);
            var bottomLayout=bottom.gameObject.AddComponent<HorizontalLayoutGroup>();bottomLayout.spacing=30;
            bottomLayout.childControlWidth=bottomLayout.childControlHeight=true;bottomLayout.childForceExpandWidth=true;bottomLayout.childForceExpandHeight=false;
            AddButton("回到房间   ↗",CloseModal,false,bottom);
            AddButton("离开房间",()=>{if(InNetworkRoom)AskLeave();else ReturnHome();},true,bottom);
            LoungeUtilities(Page.Members);
        }
        return true;
    }
    void AddRule(Transform holder)
    {
        var rt=Node("SectionRule",holder);Height(rt,1);
        var image=rt.gameObject.AddComponent<Image>();image.color=CafeUiTheme.Line;image.raycastTarget=false;
    }
    void BuildMemberTable()
    {
#if NETCAFE_B1
        memberCells = new TMP_Text[6,4]; memberRowRoots = new RectTransform[6];
        AddMemberRow("MemberHeader", new[]{"成员", "状态", "电脑", "麦克风"}, null);
        for(int i=0;i<6;i++) {
            var cells = new TMP_Text[4];
            memberRowRoots[i] = AddMemberRow("MemberRow"+i, new[]{"", "", "", ""}, cells);
            for(int c=0;c<4;c++) memberCells[i,c]=cells[c];
        }
        UpdateLoungeMembers();
#endif
    }
    RectTransform AddMemberRow(string name,string[] values,TMP_Text[] cells)
    {
        var row=Node(name,content);Height(row,54);
        var layout=row.gameObject.AddComponent<HorizontalLayoutGroup>();layout.spacing=16;
        layout.childControlWidth=layout.childControlHeight=true;layout.childForceExpandWidth=true;layout.childForceExpandHeight=false;
        for(int c=0;c<4;c++) {
            var text=AddText(row,values[c],cells==null?15:19,cells==null?CafeUiTheme.InkSoft:CafeUiTheme.Ink,54);
            text.richText=false;text.enableAutoSizing=true;text.fontSizeMin=13;text.fontSizeMax=cells==null?15:19;
            var element=text.GetComponent<LayoutElement>();element.minWidth=0;element.flexibleWidth=c==0?2:1;
            if(cells!=null)cells[c]=text;
        }
        Rule(row,"RowRule",Vector2.zero,Vector2.right);
        // A rule is decorative, excluded from the row's column layout.
        row.GetChild(row.childCount-1).gameObject.AddComponent<LayoutElement>().ignoreLayout=true;
        return row;
    }
    void UpdateLoungeMembers()
    {
#if NETCAFE_B1
        if(memberRowRoots==null || memberCells==null || network==null)return;
        if(!InNetworkRoom){
            for(int i=0;i<6;i++)if(memberRowRoots[i]!=null)memberRowRoots[i].gameObject.SetActive(i==0);
            memberCells[0,0].text=nickname+" · 你";memberCells[0,1].text=cafe.CurrentSeat<0?"未入座":"已入座";
            memberCells[0,2].text=cafe.CurrentSeat>=0&&cafe.CurrentSeat<6?(cafe.CurrentSeat+1).ToString("00"):"—";memberCells[0,3].text="—";
            return;
        }
        for(int i=0;i<6;i++) {
            if(memberRowRoots[i]==null)continue;
            bool exists=i<network.Members.Count;memberRowRoots[i].gameObject.SetActive(exists);
            if(!exists)continue;
            var member=network.Members[i];
            int seat=member.IsLocal?cafe.CurrentSeat:GetComponent<CafeRoomSync>().RemoteSeatFor(member.Id);
            memberCells[i,0].text=member.Name+(member.IsLocal?" · 你":"")+(member.IsHost?" · 房主":"");
            memberCells[i,1].text=member.State=="reconnecting"?"重连中":member.State!="active"?"连接中":seat<0?"未入座":"已入座";
            memberCells[i,2].text=seat>=0&&seat<6?(seat+1).ToString("00"):"—";
            string mic="关闭";
            if(member.IsLocal)mic=voice!=null&&voice.MicrophoneBusy?"申请中":voice!=null&&voice.MicrophoneActive?"开启":"关闭";
            else if(network.TransportRoom!=null && network.TransportRoom.RemoteParticipants.TryGetValue(member.Id,out var participant))
                foreach(var track in participant.Tracks.Values)if(track.Source==LiveKit.Proto.TrackSource.SourceMicrophone&&!track.Muted)mic="开启";
            memberCells[i,3].text=mic;
        }
#endif
    }
    RectTransform LoungeColumn(Transform parent, string name)
    {
        var rt = Node(name, parent);
        var layout = rt.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 10; layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
        var element = rt.gameObject.AddComponent<LayoutElement>(); element.flexibleWidth = 1; element.minWidth = 0; element.preferredWidth = 0;
        return rt;
    }
    void LoungeUtilities(Page origin)
    {
        AddRule(content);
        var row = Node("Utilities", content);
        Height(row, 58);
        var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 14; layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
        AddButton("我的形象   ↗", () => OpenAvatarPage(origin), false, row);
        AddButton("设置   ↗", () => { utilityReturn = origin; Show(Page.Settings, origin == Page.Members); }, false, row);
        AddButton("使用说明   ↗", () => { utilityReturn = origin; Show(Page.Help, origin == Page.Members); }, false, row);
    }
    void BeginRoomEntry()
    {
        roomEntered = true;
        if (roomTransition == null) { Hide(); cafe.Continue(); return; }
        roomTransition.Play(false, () => { Hide(); cafe.Continue(); });
    }
    void BeginRoomLeave(Action action)
    {
        roomEntered = false;
        if (roomTransition == null) { action(); return; }
        roomTransition.Play(true, action);
    }
    public bool TransitionActive => roomTransition != null && roomTransition.Active;
    public void ToggleRoomMicrophone()
    {
#if NETCAFE_B1
        if(voice==null || voice.MicrophoneBusy)return;
        if(InNetworkRoom)voice.ToggleMicrophone();
        else {microphoneOnEntry=!microphoneOnEntry;UpdateLoungeIdentity();}
#endif
    }
    public string RoomIdentity => nickname;
    public string RoomCode
    {
        get
        {
#if NETCAFE_B1
            return network != null ? network.InviteCode : "";
#else
            return "";
#endif
        }
    }
}
