using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PmxLattice {
    public sealed class LatticeForm : Form {
        public PmxDocument Document;
        public Lattice Cage;
        public Point3[] Edited;
        public bool[] Target;
        public int SelectedControl=-1, SelectedRow;
        public bool SinglePoint;
        public Point3 Placement;
        public int DepthLayer=1,SideLayer=1;
        public bool WholeCage {get{return mode.SelectedIndex==2;}}
        public void SetMoveMode(int index){mode.SelectedIndex=index;}
        public bool[] VisibleMaterials;
        public Point3[] PreviewNormals;
        public MaterialRenderer Renderer;
        public int RenderVersion;
        public Action<Point3[],bool[]> Publish;
        public Func<bool[]> ReadSelection;
        bool updating, applied;
        readonly Point3[] sessionOriginal;
        internal Func<DialogResult> ConfirmTargetChange {get;set;}
        CheckedListBox materials=new CheckedListBox();
        CheckedListBox displayMaterials=new CheckedListBox();
        NumericUpDown divisions=Number(5,3,9,1), width=Number(100,20,300,1), depth=Number(100,20,300,1);
        ComboBox rows=new ComboBox(), mode=new ComboBox();
        Label status=new Label();
        LatticeCanvas front,side;
        sealed class CageState {public Point3[] Controls;public Point3 Placement;}
        readonly Stack<CageState> undo=new Stack<CageState>();
        readonly Stack<CageState> redo=new Stack<CageState>();
        static NumericUpDown Number(decimal v,decimal min,decimal max,decimal increment) {
            return new NumericUpDown {Value=v,Minimum=min,Maximum=max,Increment=increment,Width=105,DecimalPlaces=1};
        }
        public LatticeForm(PmxDocument document,bool[] initial,Action<Point3[],bool[]> publish,Func<bool[]> readSelection) {
            Document=document; Edited=(Point3[])document.Points.Clone(); Target=new bool[Edited.Length];
            sessionOriginal=(Point3[])document.Points.Clone();
            PreviewNormals=document.Normals??MaterialRenderer.SmoothNormals(document,Edited);
            VisibleMaterials=new bool[document.Materials.Count];Renderer=new MaterialRenderer(document);Renderer.ShowHiddenMaterials=true;
            for(int i=0;i<VisibleMaterials.Length;i++)VisibleMaterials[i]=true;
            Publish=publish; ReadSelection=readSelection;
            Text="PMX ラティス補助 1.0.0"; ClientSize=new Size(1200,800); MinimumSize=new Size(1020,720);
            Font=new Font("Meiryo UI",9); StartPosition=FormStartPosition.CenterScreen;
            TableLayoutPanel root=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=2};
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,280));root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,60)); Controls.Add(root);
            TabControl tabs=new TabControl{Dock=DockStyle.Fill};
            TabPage operationPage=new TabPage("変形対象・操作"),displayPage=new TabPage("表示材質");
            tabs.TabPages.Add(operationPage);tabs.TabPages.Add(displayPage);root.Controls.Add(tabs,0,0);
            FlowLayoutPanel left=new VerticalPanel();
            operationPage.Controls.Add(left);
            FlowLayoutPanel displayPanel=new VerticalPanel();
            displayPage.Controls.Add(displayPanel);
            AddText(displayPanel,"プレビューに表示する材質",250,24);
            AddText(displayPanel,"表示の選択は変形対象と独立しています。\n身体を参照表示したまま衣装を調整できます。",250,46);
            displayMaterials.Width=250;displayMaterials.Height=450;displayMaterials.CheckOnClick=true;
            foreach(MaterialInfo m in document.Materials)displayMaterials.Items.Add(m.Name,true);
            displayPanel.Controls.Add(displayMaterials);
            displayMaterials.ItemCheck+=delegate(object sender,ItemCheckEventArgs e){VisibleMaterials[e.Index]=e.NewValue==CheckState.Checked;RenderVersion++;Redraw();};
            AddButton(displayPanel,"すべて表示",delegate{for(int i=0;i<displayMaterials.Items.Count;i++)displayMaterials.SetItemChecked(i,true);});
            AddButton(displayPanel,"すべて非表示",delegate{for(int i=0;i<displayMaterials.Items.Count;i++)displayMaterials.SetItemChecked(i,false);});
            AddText(displayPanel,"本体で非表示の材質も、この画面では表示します。\nスフィア・トゥーン・影は再現しません。",250,46);
            if(Renderer.MissingTextures.Count>0)AddText(displayPanel,"読めないテクスチャは材質色で表示します：\n"+String.Join("\n",Renderer.MissingTextures.ToArray()),250,Math.Min(240,45+Renderer.MissingTextures.Count*22));
            AddText(left,"1. 変形する衣装を選択",250,24);
            materials.Width=250; materials.Height=190;materials.CheckOnClick=true;
            foreach(MaterialInfo m in document.Materials) materials.Items.Add(m.Name);
            left.Controls.Add(materials);
            AddButton(left,"選んだ材質で格子を作成",delegate { Build(MaterialTargets()); });
            AddButton(left,"PMXViewの選択頂点で格子を作成",delegate {
                if(ReadSelection!=null) Build(ReadSelection()); else if(initial!=null) Build(initial);
            });
            FlowLayoutPanel dn=Line(left,"格子の段数",divisions);
            AddText(left,"2. 格子全体・段・制御点を調整",250,26);
            rows.Name="CageRows";rows.DropDownStyle=ComboBoxStyle.DropDownList;rows.Width=250;rows.Visible=false;left.Controls.Add(rows);
            rows.SelectedIndexChanged+=delegate { if(!updating && Cage!=null) { SelectedRow=Cage.NY-1-rows.SelectedIndex; UpdateScaleDisplay(); Redraw(); } };
            mode.DropDownStyle=ComboBoxStyle.DropDownList;mode.Width=250;
            mode.Items.AddRange(new object[]{"段をまとめて移動","制御点を個別に移動","格子全体を移動（位置合わせ）"});mode.SelectedIndex=2;
            mode.SelectedIndexChanged+=delegate {SinglePoint=mode.SelectedIndex==1;Redraw();};left.Controls.Add(mode);
            ComboBox depthLayer=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=105},sideLayer=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=105};
            depthLayer.Items.AddRange(new object[]{"前側","中央","後側"});sideLayer.Items.AddRange(new object[]{"X小側","中央","X大側"});depthLayer.SelectedIndex=sideLayer.SelectedIndex=1;
            AddText(left,"操作する点の層（個別移動のみ）",250,24);
            FlowLayoutPanel depthLine=Line(left,"正面・背面の点",depthLayer),sideLine=Line(left,"側面の点",sideLayer);
            depthLayer.Name="DepthLayer";sideLayer.Name="SideLayer";
            depthLine.Enabled=sideLine.Enabled=SinglePoint;
            mode.SelectedIndexChanged+=delegate{depthLine.Enabled=sideLine.Enabled=SinglePoint;};
            depthLayer.SelectedIndexChanged+=delegate{DepthLayer=depthLayer.SelectedIndex;Redraw();};sideLayer.SelectedIndexChanged+=delegate{SideLayer=sideLayer.SelectedIndex;Redraw();};
            AddText(left,"層を選んでから点をドラッグします。\n上・下からは選択中の段の点を使います。",250,42);
            Line(left,"横幅（%）",width);Line(left,"奥行き（%）",depth);
            width.ValueChanged+=delegate {ScaleRow();};depth.ValueChanged+=delegate {ScaleRow();};
            AddText(left,"段の拡縮は元の格子を基準にします。\n個別に動かしたX/Zは拡縮で置き換わります。",250,60);
            AddButton(left,"戻す（Ctrl+Z）",delegate { Step(undo,redo); });
            AddButton(left,"やり直す（Ctrl+Y）",delegate { Step(redo,undo); });
            AddButton(left,"形状調整をリセット（位置は保持）",delegate {ResetShape();});
            AddText(left,"3. 本体へ反映して終了",250,26);
            AddButton(left,"本体へ反映して閉じる",delegate { ApplyAndClose(); });
            AddText(left,"編集中はPMXEditor本体を変更しません。\nPMXの保存は反映後に本体で行ってください。",250,46);
            TableLayoutPanel views=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1};
            views.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));views.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            front=new LatticeCanvas(this,false);side=new LatticeCanvas(this,true);views.Controls.Add(ViewPanel(front,PreviewView.Front),0,0);views.Controls.Add(ViewPanel(side,PreviewView.Right),1,0);root.Controls.Add(views,1,0);
            status.Dock=DockStyle.Fill;status.Padding=new Padding(12,6,8,0);root.Controls.Add(status,0,1);root.SetColumnSpan(status,2);
            status.Text="「変形対象・操作」で衣装を選び、「表示材質」で参照表示を選べます。表示を変更しても格子と変形対象は変わりません。\nこのプラグインは頂点位置だけを調整します。物理・モーフ・SDEF基準点の追従は別途確認が必要です。";
            FormClosing+=OnClosing;
            Shown+=delegate {front.Fit();side.Fit();};
            bool any=false;if(initial!=null)foreach(bool b in initial)if(b){any=true;break;}
            if(any) Build(initial);
        }
        static void AddText(Control parent,string text,int width,int height) {parent.Controls.Add(new Label {Text=text,Width=width,Height=height,TextAlign=ContentAlignment.MiddleLeft});}
        Control ViewPanel(LatticeCanvas canvas,PreviewView initial) {
            TableLayoutPanel panel=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=2,ColumnCount=1};panel.RowStyles.Add(new RowStyle(SizeType.Absolute,36));panel.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            FlowLayoutPanel bar=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};
            ComboBox view=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=100};view.Items.AddRange(new object[]{"正面","背面","側面（X大側）","側面（X小側）","上から","下から"});view.SelectedIndex=(int)initial;
            canvas.ViewSelector=view;
            view.SelectedIndexChanged+=delegate{canvas.SetView((PreviewView)view.SelectedIndex);};
            CheckBox grid=new CheckBox{Text="格子表示",Checked=true,AutoSize=true};grid.CheckedChanged+=delegate{canvas.ShowGrid=grid.Checked;canvas.Invalidate();};
            Button fit=new Button{Text="全体を表示",Width=105,Height=27};fit.Click+=delegate{canvas.FitVisible();};
            bar.Controls.Add(view);bar.Controls.Add(grid);bar.Controls.Add(fit);panel.Controls.Add(bar,0,0);panel.Controls.Add(canvas,0,1);return panel;
        }
        public void SetPreviewViews(PreviewView first,PreviewView second){front.SetView(first);side.SetView(second);}
        static void AddButton(Control parent,string text,Action action) {
            Button b=new Button {Text=text,Width=250,Height=30};b.Click+=delegate {action();};parent.Controls.Add(b);
        }
        static FlowLayoutPanel Line(Control parent,string text,Control input) {
            FlowLayoutPanel p=new FlowLayoutPanel {Width=250,Height=30,WrapContents=false};
            p.Controls.Add(new Label {Text=text,Width=130,Height=25,TextAlign=ContentAlignment.MiddleLeft});p.Controls.Add(input);parent.Controls.Add(p);return p;
        }
        bool[] MaterialTargets() {
            List<int> list=new List<int>();foreach(int i in materials.CheckedIndices)list.Add(i);
            return Document.TargetVertices(list);
        }
        void Guard(Action action) { try {action();}catch(Exception e){MessageBox.Show(this,e.Message,"処理を中止しました",MessageBoxButtons.OK,MessageBoxIcon.Information);} }
        public void Build(bool[] target) { Guard(delegate {
            if(target==null || target.Length!=Document.Points.Length) throw new InvalidOperationException("選択頂点を取得できませんでした。");
            Lattice next=new Lattice(Document.Points,target,(int)divisions.Value);
            if(Cage!=null && HasPendingChanges()) {
                DialogResult choice=ConfirmTargetChange!=null?ConfirmTargetChange():AskTargetChange();
                if(choice==DialogResult.Cancel || choice==DialogResult.None)return;
                if(choice==DialogResult.Yes) {
                    Document.Points=(Point3[])Edited.Clone();
                    next=new Lattice(Document.Points,target,(int)divisions.Value);
                } else if(choice!=DialogResult.No)return;
            }
            Target=(bool[])target.Clone();Cage=next;Placement=new Point3();undo.Clear();redo.Clear();SelectedControl=-1;SelectedRow=Cage.NY-1;
            updating=true;rows.Items.Clear();for(int i=Cage.NY-1;i>=0;i--)rows.Items.Add("段 "+(i+1)+" / "+Cage.NY+(i==0?"（下）":i==Cage.NY-1?"（上）":""));rows.SelectedIndex=0;rows.Visible=true;updating=false;
            UpdateScaleDisplay();Recalculate();front.Fit();side.Fit();
        }); }
        bool HasPendingChanges() {
            for(int i=0;i<Edited.Length;i++)if(Target[i]&&!Edited[i].Equals(Document.Points[i]))return true;
            return false;
        }
        DialogResult AskTargetChange() {
            using(Form dialog=new Form()) {
                dialog.Text="対象を変更する前に";dialog.ClientSize=new Size(510,158);
                dialog.FormBorderStyle=FormBorderStyle.FixedDialog;dialog.MaximizeBox=false;dialog.MinimizeBox=false;
                dialog.StartPosition=FormStartPosition.CenterParent;dialog.Font=Font;
                dialog.Controls.Add(new Label {Left=16,Top=16,Width=478,Height=70,Text="現在の対象の修正を保持しますか？\n保持した修正はツール内に残り、他の材質を続けて調整できます。\n本体への反映は最後にまとめて行います。"});
                Button save=new Button{Left=16,Top=105,Width=155,Height=32,Text="保持して対象変更",DialogResult=DialogResult.Yes};
                Button revert=new Button{Left=179,Top=105,Width=155,Height=32,Text="戻して対象変更",DialogResult=DialogResult.No};
                Button cancel=new Button{Left=342,Top=105,Width=152,Height=32,Text="キャンセル",DialogResult=DialogResult.Cancel};
                dialog.Controls.Add(save);dialog.Controls.Add(revert);dialog.Controls.Add(cancel);dialog.CancelButton=cancel;
                return dialog.ShowDialog(this);
            }
        }
        public void ChooseControl(int id) {
            SelectedControl=id;SelectedRow=Cage.Row(id);rows.SelectedIndex=Cage.NY-1-SelectedRow;Redraw();
        }
        CageState Snapshot(){return new CageState{Controls=(Point3[])Cage.Controls.Clone(),Placement=Placement};}
        public void Remember() { if(Cage==null)return;undo.Push(Snapshot());redo.Clear(); }
        void Step(Stack<CageState> from,Stack<CageState> to) {
            if(Cage==null||from.Count==0)return;to.Push(Snapshot());CageState state=from.Pop();Cage.Controls=state.Controls;Placement=state.Placement;Recalculate();UpdateScaleDisplay();
        }
        public void ResetShape(){if(Cage==null)return;Remember();for(int i=0;i<Cage.Controls.Length;i++)Cage.Controls[i]=Cage.Base[i]+Placement;Recalculate();UpdateScaleDisplay();}
        protected override bool ProcessCmdKey(ref Message msg,Keys keyData) {
            if(keyData==(Keys.Control|Keys.Z)){Step(undo,redo);return true;}
            if(keyData==(Keys.Control|Keys.Y)||keyData==(Keys.Control|Keys.Shift|Keys.Z)){Step(redo,undo);return true;}
            return base.ProcessCmdKey(ref msg,keyData);
        }
        void ScaleRow() { if(updating||Cage==null)return;Remember();Cage.ScaleRow(SelectedRow,(double)width.Value/100,(double)depth.Value/100);Recalculate(); }
        void UpdateScaleDisplay() {
            if(Cage==null)return;
            updating=true;
            Point3 a=Cage.Controls[Cage.Id(0,SelectedRow,1)],b=Cage.Controls[Cage.Id(2,SelectedRow,1)];
            Point3 c=Cage.Controls[Cage.Id(1,SelectedRow,0)],d=Cage.Controls[Cage.Id(1,SelectedRow,2)];
            width.Value=Math.Max(width.Minimum,Math.Min(width.Maximum,(decimal)((b.X-a.X)/(Cage.Max.X-Cage.Min.X)*100)));
            depth.Value=Math.Max(depth.Minimum,Math.Min(depth.Maximum,(decimal)((d.Z-c.Z)/(Cage.Max.Z-Cage.Min.Z)*100)));
            updating=false;
        }
        public void MoveControl(int id,double a,double b,bool sideView) {
            MoveControl(id,ViewAxes.Drag(a,b,sideView?PreviewView.Right:PreviewView.Front));
        }
        public void MoveControl(int id,Point3 delta) {
            int row=Cage.Row(id);
            if(WholeCage)Placement+=delta;
            for(int i=0;i<Cage.Controls.Length;i++) if(WholeCage || (SinglePoint ? i==id : Cage.Row(i)==row)) {
                Cage.Controls[i]+=delta;
            }
            Recalculate();
        }
        public void Recalculate() {
            if(Cage==null)return;Edited=Cage.Apply(Document.Points,Target);int count=0;foreach(bool b in Target)if(b)count++;
            PreviewNormals=MaterialRenderer.SmoothNormals(Document,Edited);RenderVersion++;
            status.Text="対象 "+count.ToString("N0")+" 頂点　格子 3 × "+Cage.NY+" × 3　ドラッグ：移動／ホイール：拡大・縮小／右ドラッグ：表示移動\n編集中は本体を変更しません。「本体へ反映して閉じる」で終了します。骨・ウェイト・剛体・Jointは変更しません。";Redraw();
        }
        public void Redraw() {if(front!=null)front.Invalidate();if(side!=null)side.Invalidate();}
        public void SetMaterialVisible(int index,bool visible){displayMaterials.SetItemChecked(index,visible);}
        internal bool ApplyAndClose() {
            if(applied)return true;
            if(Cage==null)return false;bool ok=false;
            Guard(delegate {PublishAll();applied=true;ok=true;});
            if(ok)Close();return ok;
        }
        void OnClosing(object sender,FormClosingEventArgs e) {
            if(applied||Cage==null||!HasSessionChanges())return;
            DialogResult r=ConfirmTargetChange!=null?ConfirmTargetChange():MessageBox.Show(this,"修正を本体へ反映して終了しますか？\n「はい」：反映して終了\n「いいえ」：修正を破棄して終了\n「キャンセル」：編集を続ける","編集の終了",MessageBoxButtons.YesNoCancel,MessageBoxIcon.Question);
            if(r==DialogResult.Cancel||r==DialogResult.None){e.Cancel=true;return;}
            if(r==DialogResult.Yes) {
                bool ok=false;Guard(delegate{PublishAll();applied=true;ok=true;});e.Cancel=!ok;
            }
        }
        bool[] ChangedVertices() {
            bool[] changed=new bool[Edited.Length];
            for(int i=0;i<changed.Length;i++)changed[i]=!Edited[i].Equals(sessionOriginal[i]);
            return changed;
        }
        bool HasSessionChanges() {foreach(bool changed in ChangedVertices())if(changed)return true;return false;}
        void PublishAll() {if(Publish!=null&&HasSessionChanges())Publish(Edited,ChangedVertices());}
    }
    public sealed class VerticalPanel : FlowLayoutPanel {
        bool sizing;
        public VerticalPanel(){Dock=DockStyle.Fill;FlowDirection=FlowDirection.TopDown;WrapContents=false;AutoScroll=true;Padding=new Padding(5);}
        protected override void OnLayout(LayoutEventArgs e){
            if(sizing)return;sizing=true;
            try {
                int available=Math.Max(1,ClientSize.Width-Padding.Horizontal-SystemInformation.VerticalScrollBarWidth);
                foreach(Control child in Controls){
                    child.Width=Math.Max(1,available-child.Margin.Horizontal);
                    FlowLayoutPanel line=child as FlowLayoutPanel;
                    if(line!=null&&line.Controls.Count==2&&line.Controls[0] is Label){
                        Control label=line.Controls[0],input=line.Controls[1];
                        label.Width=Math.Max(1,line.ClientSize.Width-input.Width-label.Margin.Horizontal-input.Margin.Horizontal);
                    }
                }
                base.OnLayout(e);HorizontalScroll.Visible=false;
            } finally{sizing=false;}
        }
    }
    public sealed class LatticeCanvas : Control {
        readonly LatticeForm form;
        PreviewView view;
        public ComboBox ViewSelector;
        public bool ShowGrid=true;
        double zoom=1,cx,cy;
        int active=-1;Point last;bool pan;
        Bitmap materialImage;
        int imageVersion=-1,imageWidth,imageHeight;
        double imageZoom,imageX,imageY;
        public LatticeCanvas(LatticeForm f,bool s) {
            form=f;view=s?PreviewView.Right:PreviewView.Front;Dock=DockStyle.Fill;BackColor=Color.FromArgb(245,246,248);
            SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.UserPaint,true);
            MouseDown+=Down;MouseMove+=MovePointer;MouseUp+=delegate {active=-1;pan=false;Capture=false;};
            MouseWheel+=delegate(object sender,MouseEventArgs e) {zoom*=e.Delta>0?1.15:1/1.15;Invalidate();};
        }
        public void SetView(PreviewView value){if(ViewSelector!=null&&ViewSelector.SelectedIndex!=(int)value){ViewSelector.SelectedIndex=(int)value;return;}view=value;active=-1;pan=false;Capture=false;imageVersion=-1;FitVisible();}
        public void Fit() {
            if(form.Cage==null){FitVisible();return;}FitPoints(form.Cage.Controls);
        }
        void FitPoints(IEnumerable<Point3> points){
            double lo=double.MaxValue,hi=double.MinValue,yl=double.MaxValue,yh=double.MinValue;
            foreach(Point3 p in points){double x=ViewAxes.Horizontal(p,view),y=ViewAxes.Vertical(p,view);lo=Math.Min(lo,x);hi=Math.Max(hi,x);yl=Math.Min(yl,y);yh=Math.Max(yh,y);}
            if(lo>hi)return;cx=(lo+hi)/2;cy=(yl+yh)/2;
            zoom=Math.Min(Math.Max(100,Width-80)/Math.Max(.001,hi-lo),Math.Max(100,Height-80)/Math.Max(.001,yh-yl));Invalidate();
        }
        public void FitVisible() {
            double lo=double.MaxValue,hi=double.MinValue,yl=double.MaxValue,yh=double.MinValue;
            for(int m=0;m<form.Document.Materials.Count;m++)if(form.VisibleMaterials[m]) {
                MaterialInfo material=form.Document.Materials[m];if(material.A<=0&&!form.Renderer.ShowHiddenMaterials)continue;
                for(int f=material.Start;f<material.Start+material.Count;f++) {
                    Point3 p=form.Edited[form.Document.Faces[f]];double x=ViewAxes.Horizontal(p,view),y=ViewAxes.Vertical(p,view);
                    lo=Math.Min(lo,x);hi=Math.Max(hi,x);yl=Math.Min(yl,y);yh=Math.Max(yh,y);
                }
            }
            if(lo>hi)return;cx=(lo+hi)/2;cy=(yl+yh)/2;
            zoom=Math.Min(Math.Max(100,Width-80)/Math.Max(.001,hi-lo),Math.Max(100,Height-80)/Math.Max(.001,yh-yl));Invalidate();
        }
        PointF Project(Point3 p) {return new PointF((float)(Width/2.0+(ViewAxes.Horizontal(p,view)-cx)*zoom),(float)(Height/2.0-(ViewAxes.Vertical(p,view)-cy)*zoom));}
        bool VisibleControl(int id) {
            Lattice c=form.Cage;
            // 操作する面を限定し、奥で重なる制御点を誤って掴まないようにします。
            if(view==PreviewView.Top||view==PreviewView.Bottom)return c.Row(id)==form.SelectedRow;
            if(view==PreviewView.Right||view==PreviewView.Left)return id%c.NX==form.SideLayer;
            return id/(c.NX*c.NY)==form.DepthLayer;
        }
        void Down(object sender,MouseEventArgs e) {
            Focus();last=e.Location;if(e.Button==MouseButtons.Right){pan=true;Capture=true;return;}
            if(e.Button!=MouseButtons.Left||form.Cage==null||!ShowGrid)return;
            double best=144;int chosen=-1;
            for(int i=0;i<form.Cage.Controls.Length;i++)if(VisibleControl(i)) {
                PointF p=Project(form.Cage.Controls[i]);double d=(p.X-e.X)*(p.X-e.X)+(p.Y-e.Y)*(p.Y-e.Y);
                if(d<best){best=d;chosen=i;}
            }
            if(chosen<0)return;active=chosen;form.ChooseControl(active);form.Remember();Capture=true;
        }
        void MovePointer(object sender,MouseEventArgs e) {
            if(pan){cx-=(e.X-last.X)/zoom;cy+=(e.Y-last.Y)/zoom;Invalidate();}
            else if(active>=0)form.MoveControl(active,ViewAxes.Drag((e.X-last.X)/zoom,-(e.Y-last.Y)/zoom,view));
            last=e.Location;
        }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);Graphics g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
            if(materialImage==null||imageVersion!=form.RenderVersion||imageWidth!=Width||imageHeight!=Height||imageZoom!=zoom||imageX!=cx||imageY!=cy) {
                if(materialImage!=null)materialImage.Dispose();
                materialImage=form.Renderer.Render(form.Edited,form.PreviewNormals,form.VisibleMaterials,view,Width,Height,zoom,cx,cy);
                imageVersion=form.RenderVersion;imageWidth=Width;imageHeight=Height;imageZoom=zoom;imageX=cx;imageY=cy;
            }
            g.DrawImageUnscaled(materialImage,0,0);
            if(form.Cage==null||!ShowGrid)return;
            Lattice l=form.Cage;
            using(Pen grid=new Pen(Color.FromArgb(190,200,85,45),1.4f)) {
                for(int i=0;i<l.Controls.Length;i++)if(VisibleControl(i)) {
                    int x=i%l.NX,y=l.Row(i),z=i/(l.NX*l.NY);PointF p=Project(l.Controls[i]);
                    if(y+1<l.NY&&VisibleControl(l.Id(x,y+1,z)))g.DrawLine(grid,p,Project(l.Controls[l.Id(x,y+1,z)]));
                    if(x+1<l.NX&&VisibleControl(l.Id(x+1,y,z)))g.DrawLine(grid,p,Project(l.Controls[l.Id(x+1,y,z)]));
                    if(z+1<l.NZ&&VisibleControl(l.Id(x,y,z+1)))g.DrawLine(grid,p,Project(l.Controls[l.Id(x,y,z+1)]));
                    bool selected=form.WholeCage||(form.SinglePoint?i==form.SelectedControl:y==form.SelectedRow);
                    g.FillEllipse(selected?Brushes.OrangeRed:Brushes.White,p.X-4,p.Y-4,8,8);g.DrawEllipse(grid,p.X-4,p.Y-4,8,8);
                }
            }
        }
        protected override void Dispose(bool disposing){if(disposing&&materialImage!=null){materialImage.Dispose();materialImage=null;}base.Dispose(disposing);}
    }
}

