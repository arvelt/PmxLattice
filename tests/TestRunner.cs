using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using PEPlugin.Pmx;
using PEPlugin.SDX;

namespace PmxLattice {
    public static class TestRunner {
        static int assertions;
        static void Check(bool ok,string message) {if(!ok)throw new Exception(message);assertions++;}
        static bool Equal(byte[] a,byte[] b) {if(a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true;}
        [STAThread]
        public static int Main(string[] args) {
            try {
                Application.EnableVisualStyles();
                RendererTest();
                foreach(string path in args) {
                    PmxDocument doc=PmxDocument.Load(path);
                    bool[] all=new bool[doc.Points.Length];for(int i=0;i<all.Length;i++)all[i]=true;
                    Lattice cage=new Lattice(doc.Points,all,5);
                    Check(Equal(doc.Original,doc.Output(cage.Apply(doc.Points,all),all)),"恒等変形でバイトが変わりました。");
                    Point3 shift=new Point3(.125,-.25,.375);
                    for(int i=0;i<cage.Controls.Length;i++)cage.Controls[i]+=shift;
                    Point3[] moved=cage.Apply(doc.Points,all);
                    for(int i=0;i<moved.Length;i++) {
                        Point3 delta=moved[i]-doc.Points[i]-shift;
                        Check(Math.Abs(delta.X)+Math.Abs(delta.Y)+Math.Abs(delta.Z)<1e-8,"平行移動の再現に失敗しました。");
                    }
                    byte[] output=doc.Output(moved,all);
                    PmxDocument reloaded=PmxDocument.LoadBytes(output);
                    Check(reloaded.Points.Length==doc.Points.Length,"頂点数が変わりました。");
                    Check(reloaded.Materials.Count==doc.Materials.Count,"材質数が変わりました。");
                    bool rejected=false;byte[] bad=(byte[])output.Clone();bad[bad.Length-1]^=1;
                    try{doc.ValidatePreserved(bad,all);}catch(InvalidDataException){rejected=true;}
                    Check(rejected,"保護領域の変更を見逃しました。");
                    bool[] partial=doc.TargetVertices(new int[]{0});
                    Point3[] partMoved=cage.Apply(doc.Points,partial);
                    for(int i=0;i<partial.Length;i++)if(!partial[i])Check(partMoved[i].Equals(doc.Points[i]),"対象外の頂点が変わりました。");
                    doc.ValidatePreserved(doc.Output(partMoved,partial),partial);
                    Lattice rowCage=new Lattice(doc.Points,all,5);
                    rowCage.ScaleRow(2,.85,1.15);
                    Point3 center=new Point3((rowCage.Min.X+rowCage.Max.X)/2,(rowCage.Min.Y+rowCage.Max.Y)/2,(rowCage.Min.Z+rowCage.Max.Z)/2);
                    Point3 offset=new Point3((rowCage.Max.X-rowCage.Min.X)*.25,0,0);
                    Point3 a=rowCage.Deform(center+offset),b=rowCage.Deform(center-offset);
                    Check(Math.Abs(a.X+b.X-2*center.X)<1e-8,"段の拡縮で左右対称性が崩れました。");
                    Check(Math.Abs(a.Y-center.Y)<1e-8,"横幅の調整で高さが変わりました。");
                    Console.WriteLine(Path.GetFileName(path)+": "+doc.Points.Length+" vertices, "+doc.Materials.Count+" materials, SDEF "+doc.SdefCount+"; identity / translation / protected bytes / selection PASS");
                }
                // インストール済みSDKと同じ型で座標変更と保護検査を実行します。
                AppDomain.CurrentDomain.AssemblyResolve+=delegate(object sender,ResolveEventArgs e) {
                    string name=new AssemblyName(e.Name).Name;
                    if(name=="PEPlugin")return Assembly.LoadFrom(Path.Combine(Environment.GetEnvironmentVariable("PMXLATTICE_EDITOR_PATH"), "Lib", "PEPlugin", "PEPlugin.dll"));
                    if(name=="SlimDX")return Assembly.LoadFrom(Path.Combine(Environment.GetEnvironmentVariable("PMXLATTICE_EDITOR_PATH"), "Lib", "SlimDX", "x64", "SlimDX.dll"));
                    return null;
                };
                Assembly.LoadFrom(Path.Combine(Environment.GetEnvironmentVariable("PMXLATTICE_EDITOR_PATH"), "Lib", "SlimDX", "x64", "SlimDX.dll"));
                Assembly.LoadFrom(Path.Combine(Environment.GetEnvironmentVariable("PMXLATTICE_EDITOR_PATH"), "Lib", "PEPlugin", "PEPlugin.dll"));
                foreach(string path in args)NativeTest(path);
                PmxDocument example=PmxDocument.Load(args[0]);
                using(LatticeForm empty=new LatticeForm(example,null,null,null)){
                    Check(empty.WholeCage,"初期モードが格子全体移動ではありません。");
                    empty.ShowInTaskbar=false;empty.Opacity=0;empty.Show();Application.DoEvents();
                    foreach(Control c in Descendants(empty))if(c is ComboBox&&((ComboBox)c).Items.Count==0)Check(!c.Visible,"初期状態に空欄のドロップダウンがあります。");
                    empty.ClientSize=new Size(1020,720);Application.DoEvents();CheckPanels(empty);
                    empty.ClientSize=new Size(1200,800);Application.DoEvents();CheckPanels(empty);
                    foreach(int m in new int[]{0,1,2,0}){
                        empty.SetMoveMode(m);
                        foreach(Control c in Descendants(empty))if(c.Name=="DepthLayer"||c.Name=="SideLayer")Check(c.Enabled==(m==1),"個別操作専用の層選択が移動モードと連動していません。");
                    }
                    using(Bitmap image=new Bitmap(empty.Width,empty.Height)){empty.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save("dist/preview-initial.png");}
                    Console.WriteLine("Initial empty dropdown hidden / sidebar fixed width / no horizontal scroll PASS");
                }
                bool[] shirt=null;
                for(int i=0;i<example.Materials.Count;i++)if(example.Materials[i].Name.Contains("シャツ")&&!example.Materials[i].Name.Contains("ボタン")) {shirt=example.TargetVertices(new int[]{i});break;}
                if(shirt==null)shirt=example.TargetVertices(new int[]{0});
                using(LatticeForm form=new LatticeForm(example,shirt,null,null)) {
                    ComboBox rowList=null;foreach(Control c in Descendants(form))if(c.Name=="CageRows")rowList=(ComboBox)c;
                    Check(rowList!=null&&rowList.Items[0].ToString().Contains("（上）")&&rowList.Items[rowList.Items.Count-1].ToString().Contains("（下）"),"段一覧が上から下の順ではありません。");
                    rowList.SelectedIndex=0;Check(form.SelectedRow==form.Cage.NY-1,"最上段の選択が不正です。");
                    rowList.SelectedIndex=rowList.Items.Count-1;Check(form.SelectedRow==0,"最下段の選択が不正です。");
                    form.ChooseControl(form.Cage.Id(1,form.Cage.NY-1,1));Check(rowList.SelectedIndex==0,"制御点選択と段一覧が同期しません。");
                    form.Cage.ScaleRow(2,.85,1.15);form.Recalculate();
                    Point3[] shaped=(Point3[])form.Edited.Clone();
                    Point3[] shapedCage=(Point3[])form.Cage.Controls.Clone();
                    form.SetMoveMode(2);form.Remember();form.MoveControl(0,.75,-.25,false);form.MoveControl(0,.5,0,true);
                    Point3 shift=new Point3(.75,-.25,.5);
                    for(int i=0;i<shaped.Length;i++) {
                        Point3 expected=form.Target[i]?shaped[i]+shift:shaped[i];Point3 delta=form.Edited[i]-expected;
                        Check(Math.Abs(delta.X)+Math.Abs(delta.Y)+Math.Abs(delta.Z)<1e-8,"格子全体移動で形状または対象外が変わりました。");
                    }
                    Shortcut(form,Keys.Control|Keys.Z);
                    for(int i=0;i<shaped.Length;i++)Check(form.Edited[i].Equals(shaped[i]),"Ctrl+Zで全体移動を戻せません。");
                    Shortcut(form,Keys.Control|Keys.Y);
                    Check(form.Cage.Controls[0].Equals(shapedCage[0]+shift),"Ctrl+Yで全体移動を再現できません。");
                    Shortcut(form,Keys.Control|Keys.Z);Shortcut(form,Keys.Control|Keys.Shift|Keys.Z);
                    Check(form.Cage.Controls[0].Equals(shapedCage[0]+shift),"Ctrl+Shift+Zでやり直せません。");
                    form.Cage.ScaleRow(2,.85,1.15);form.Recalculate();
                    Point3 center=form.Cage.Controls[form.Cage.Id(1,2,1)]-shapedCage[form.Cage.Id(1,2,1)]-shift;
                    Check(Math.Abs(center.X)+Math.Abs(center.Y)+Math.Abs(center.Z)<1e-8,"段の拡縮で位置合わせが失われました。");
                    Point3[] beforeReset=(Point3[])form.Cage.Controls.Clone();
                    form.ResetShape();
                    for(int i=0;i<form.Cage.Controls.Length;i++)Check(form.Cage.Controls[i].Equals(form.Cage.Base[i]+shift),"形状リセットで位置合わせが失われました。");
                    for(int i=0;i<form.Edited.Length;i++){
                        Point3 expected=form.Target[i]?example.Points[i]+shift:example.Points[i],delta=form.Edited[i]-expected;
                        Check(Math.Abs(delta.X)+Math.Abs(delta.Y)+Math.Abs(delta.Z)<1e-8,"形状リセットが原形と移動量に一致しません。");
                    }
                    Shortcut(form,Keys.Control|Keys.Z);for(int i=0;i<beforeReset.Length;i++)Check(beforeReset[i].Equals(form.Cage.Controls[i]),"形状リセットのUndoに失敗しました。");
                    Shortcut(form,Keys.Control|Keys.Y);Point3 placement=form.Placement;Check(placement.Equals(shift),"形状リセットのRedoで位置合わせが失われました。");Shortcut(form,Keys.Control|Keys.Z);
                    Console.WriteLine("Shape reset preserves placement / original garment shape / undo / redo PASS");
                    Shortcut(form,Keys.Control|Keys.Z);form.Cage.Controls=(Point3[])form.Cage.Base.Clone();form.Recalculate();form.SetMoveMode(0);
                    Console.WriteLine("Whole cage translation preserves existing shape / target isolation / Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z / row scaling preserves position PASS");
                    Point3[] controls=(Point3[])form.Cage.Controls.Clone(),positions=(Point3[])form.Edited.Clone();
                    bool[] targets=(bool[])form.Target.Clone();
                    for(int i=0;i<example.Materials.Count;i++)form.SetMaterialVisible(i,false);
                    for(int i=0;i<controls.Length;i++)Check(controls[i].Equals(form.Cage.Controls[i]),"表示変更で格子が変わりました。");
                    for(int i=0;i<positions.Length;i++)Check(positions[i].Equals(form.Edited[i])&&targets[i]==form.Target[i],"表示変更で編集対象または座標が変わりました。");
                    form.MoveControl(0,.1,0,false);
                    bool changed=false;
                    for(int i=0;i<positions.Length;i++) {if(!targets[i])Check(positions[i].Equals(form.Edited[i]),"非表示中の操作で対象外が変わりました。");else if(!positions[i].Equals(form.Edited[i]))changed=true;}
                    Check(changed,"非表示にすると対象の変形ができません。");
                    form.Cage.Controls=controls;form.Recalculate();
                    for(int i=0;i<example.Materials.Count;i++)form.SetMaterialVisible(i,true);
                    form.ShowInTaskbar=false;form.Opacity=0;form.Show();Application.DoEvents();form.PerformLayout();
                    using(Bitmap image=new Bitmap(form.Width,form.Height)) {form.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save("dist/preview.png");}
                    TableLayoutPanel root=(TableLayoutPanel)form.Controls[0];TabControl tabs=(TabControl)root.GetControlFromPosition(0,0);tabs.SelectedIndex=1;Application.DoEvents();
                    using(Bitmap image=new Bitmap(form.Width,form.Height)) {form.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save("dist/preview-materials.png");}
                    Point3[] viewCage=(Point3[])form.Cage.Controls.Clone(),viewPositions=(Point3[])form.Edited.Clone();bool[] viewTargets=(bool[])form.Target.Clone();
                    foreach(PreviewView v in Enum.GetValues(typeof(PreviewView)))form.SetPreviewViews(v,v);
                    for(int i=0;i<viewCage.Length;i++)Check(viewCage[i].Equals(form.Cage.Controls[i]),"視点切替で格子が変わりました。");
                    for(int i=0;i<viewPositions.Length;i++)Check(viewPositions[i].Equals(form.Edited[i])&&viewTargets[i]==form.Target[i],"視点切替で対象または座標が変わりました。");
                    form.SetPreviewViews(PreviewView.Back,PreviewView.Top);Application.DoEvents();
                    using(Bitmap image=new Bitmap(form.Width,form.Height)) {form.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save("dist/preview-back-top.png");}
                    Console.WriteLine("Six preview views independent of cage / targets / coordinates PASS");
                    Console.WriteLine("Material visibility independent of targets / cage / coordinates; hidden target remains editable PASS; missing textures: "+form.Renderer.MissingTextures.Count);
                }
                SessionTest(args[0]);
                Console.WriteLine(assertions+" assertions PASS; interface rendered to dist/preview.png");return 0;
            } catch(Exception e){Console.Error.WriteLine(e);return 1;}
        }
        static System.Collections.Generic.IEnumerable<Control> Descendants(Control root){foreach(Control c in root.Controls){yield return c;foreach(Control child in Descendants(c))yield return child;}}
        static void SessionTest(string path) {
            PmxDocument doc=PmxDocument.Load(path);
            Point3[] original=(Point3[])doc.Points.Clone();
            int calls=0;Point3[] sent=null;bool[] mask=null;
            using(LatticeForm form=new LatticeForm(doc,null,delegate(Point3[] p,bool[] t){calls++;sent=(Point3[])p.Clone();mask=(bool[])t.Clone();},null)) {
                form.ShowInTaskbar=false;form.Opacity=0;form.Show();Application.DoEvents();
                form.ConfirmTargetChange=delegate{return DialogResult.Yes;};
                foreach(string name in new string[]{"制服シャツ","プリーツスカート","ブラ","ぱんつ"}) {
                    int id=doc.Materials.FindIndex(delegate(MaterialInfo m){return m.Name==name;});Check(id>=0,"試験対象の材質がありません。");
                    form.Build(doc.TargetVertices(new int[]{id}));form.SetMoveMode(2);form.MoveControl(0,new Point3(.25,.1,0));
                    Check(calls==0,"対象切替または編集中に本体へ反映しました。");
                }
                Point3[] pending=(Point3[])form.Edited.Clone();bool[] previous=(bool[])form.Target.Clone();
                bool[] another=doc.TargetVertices(new int[]{0});
                form.ConfirmTargetChange=delegate{return DialogResult.Cancel;};form.Build(another);
                Check(form.Edited[Array.FindIndex(previous,delegate(bool v){return v;})].Equals(pending[Array.FindIndex(previous,delegate(bool v){return v;})]),"切替キャンセルで修正が失われました。");
                for(int i=0;i<previous.Length;i++)Check(previous[i]==form.Target[i],"キャンセルで対象が変わりました。");
                form.ConfirmTargetChange=delegate{return DialogResult.Yes;};form.Build(another);
                Point3[] retained=(Point3[])form.Edited.Clone();
                form.SetMoveMode(2);form.MoveControl(0,new Point3(0,.75,0));
                form.ConfirmTargetChange=delegate{return DialogResult.No;};form.Build(previous);
                for(int i=0;i<retained.Length;i++)Check(form.Edited[i].Equals(retained[i]),"現在の修正を戻す際に保持済みの修正が変わりました。");
                Check(calls==0,"保持または戻す操作で本体を変更しました。");
                form.ConfirmTargetChange=delegate{return DialogResult.Cancel;};form.Close();
                Check(!form.IsDisposed&&calls==0,"終了キャンセルで終了または反映しました。");
                Check(form.ApplyAndClose(),"まとめて反映に失敗しました。");
                Check(form.IsDisposed&&calls==1,"反映後に閉じない、または複数回反映しました。");
                for(int i=0;i<original.Length;i++) {
                    Check(sent[i].Equals(retained[i]),"まとめて反映で保持済みの形状が失われました。");
                    Check(mask[i]==!retained[i].Equals(original[i]),"最後の材質以外の変更が反映対象から漏れました。");
                }
            }
            int discardedCalls=0;
            using(LatticeForm form=new LatticeForm(PmxDocument.Load(path),null,delegate(Point3[] p,bool[] t){discardedCalls++;},null)) {
                form.ShowInTaskbar=false;form.Opacity=0;form.Show();form.Build(doc.TargetVertices(new int[]{0}));form.SetMoveMode(2);form.MoveControl(0,new Point3(.2,0,0));
                form.ConfirmTargetChange=delegate{return DialogResult.No;};form.Close();
                Check(form.IsDisposed&&discardedCalls==0,"破棄して終了で本体を変更しました。");
            }
            Console.WriteLine("Multi-material session: keep / discard active edits / cancel / single final apply / close PASS");
        }
        static void CheckPanels(LatticeForm form){
            TableLayoutPanel root=(TableLayoutPanel)form.Controls[0];TabControl tabs=(TabControl)root.GetControlFromPosition(0,0);
            for(int t=0;t<tabs.TabPages.Count;t++){
                tabs.SelectedIndex=t;Application.DoEvents();VerticalPanel panel=(VerticalPanel)tabs.TabPages[t].Controls[0];
                panel.AutoScrollPosition=new Point(0,10000);Application.DoEvents();Check(!panel.HorizontalScroll.Visible,"サイドパネルに横スクロールがあります。");
                foreach(Control c in panel.Controls)if(c.Visible)Check(c.Right<=panel.ClientSize.Width-panel.Padding.Right,"サイドパネルの右端が切れています。");
                panel.AutoScrollPosition=Point.Empty;
            }
            tabs.SelectedIndex=0;
        }
        static void Shortcut(LatticeForm form,Keys keys) {
            MethodInfo method=typeof(LatticeForm).GetMethod("ProcessCmdKey",BindingFlags.Instance|BindingFlags.NonPublic);
            object[] args=new object[]{new Message(),keys};Check((bool)method.Invoke(form,args),"ショートカットが処理されません。");
        }
        static void RendererTest() {
            PmxDocument doc=new PmxDocument();
            doc.Points=new Point3[]{new Point3(-1,-1,0),new Point3(1,-1,0),new Point3(0,1,0),new Point3(-1,-1,-1),new Point3(1,-1,-1),new Point3(0,1,-1)};
            doc.Faces=new int[]{0,1,2,3,4,5};doc.Normals=new Point3[6];doc.UV=new PointF[6];
            doc.Materials.Add(new MaterialInfo{Name="back",Start=0,Count=3,R=1,G=0,B=0,TwoSided=true});
            doc.Materials.Add(new MaterialInfo{Name="front",Start=3,Count=3,R=0,G=1,B=0,TwoSided=true});
            MaterialRenderer renderer=new MaterialRenderer(doc);
            using(Bitmap b=renderer.Render(doc.Points,doc.Normals,new bool[]{true,true},false,60,60,20,0,0))Check(b.GetPixel(30,30).G>100&&b.GetPixel(30,30).R==0,"材質面の奥行き判定に失敗しました。");
            using(Bitmap b=renderer.Render(doc.Points,doc.Normals,new bool[]{true,false},false,60,60,20,0,0))Check(b.GetPixel(30,30).R>100&&b.GetPixel(30,30).G==0,"表示材質の選択に失敗しました。");
            doc.Materials[1].A=.5f;
            using(Bitmap b=renderer.Render(doc.Points,doc.Normals,new bool[]{true,true},false,60,60,20,0,0))Check(b.GetPixel(30,30).R>40&&b.GetPixel(30,30).G>40,"透明材質の合成に失敗しました。");
            doc.Materials[1].A=0;renderer.ShowHiddenMaterials=true;
            using(Bitmap b=renderer.Render(doc.Points,doc.Normals,new bool[]{true,true},false,60,60,20,0,0))Check(b.GetPixel(30,30).G>100&&b.GetPixel(30,30).R==0,"本体で非表示の材質を表示できません。");
            Check(doc.Materials[1].A==0,"ツール内表示が元の透明度を変更しました。");
            using(Bitmap b=renderer.Render(doc.Points,doc.Normals,new bool[]{true,false},false,60,60,20,0,0))Check(b.GetPixel(30,30).R>100&&b.GetPixel(30,30).G==0,"ツールの表示チェックで非表示にできません。");
            renderer.ShowHiddenMaterials=false;doc.Materials[1].A=.5f;
            string path=Path.GetFullPath("dist/test-texture.png");
            using(Bitmap texture=new Bitmap(2,2)){using(Graphics g=Graphics.FromImage(texture))g.Clear(Color.Blue);texture.Save(path);}
            doc.Materials[0].TexturePath=path;doc.Materials[0].R=doc.Materials[0].G=doc.Materials[0].B=1;
            renderer=new MaterialRenderer(doc);
            using(Bitmap b=renderer.Render(doc.Points,doc.Normals,new bool[]{true,false},false,60,60,20,0,0))Check(b.GetPixel(30,30).B>100&&b.GetPixel(30,30).R==0,"主テクスチャの表示に失敗しました。");
            File.Delete(path);Console.WriteLine("Renderer: depth / visibility / alpha / main texture PASS");
            doc.Materials[0].TexturePath=null;doc.Materials[0].R=1;doc.Materials[0].G=doc.Materials[0].B=0;doc.Materials[1].A=1;
            foreach(PreviewView v in Enum.GetValues(typeof(PreviewView))) {
                Point3 direction=new Point3(ViewAxes.Depth(new Point3(1,0,0),v),ViewAxes.Depth(new Point3(0,1,0),v),ViewAxes.Depth(new Point3(0,0,1),v));
                doc.Points=new Point3[]{ViewAxes.Drag(-1,-1,v),ViewAxes.Drag(1,-1,v),ViewAxes.Drag(0,1,v),ViewAxes.Drag(-1,-1,v)+direction,ViewAxes.Drag(1,-1,v)+direction,ViewAxes.Drag(0,1,v)+direction};
                renderer=new MaterialRenderer(doc);
                using(Bitmap b=renderer.Render(doc.Points,doc.Normals,new bool[]{true,true},v,60,60,20,0,0))Check(b.GetPixel(30,30).G>100&&b.GetPixel(30,30).R==0,"各視点の奥行き判定に失敗: "+v);
                Point3 drag=ViewAxes.Drag(.7,-.4,v);Check(Math.Abs(ViewAxes.Horizontal(drag,v)-.7)<1e-10&&Math.Abs(ViewAxes.Vertical(drag,v)+.4)<1e-10&&ViewAxes.Depth(drag,v)==0,"各視点のドラッグ方向が不正: "+v);
            }
            Console.WriteLine("Six views: projection / nearest surface / drag directions PASS");
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void NativeTest(string path) {
            Type type=typeof(IPXPmx).Assembly.GetType("PXCPlugin.Pmx.PXCPmx",true);
            IPXPmx native=(IPXPmx)Activator.CreateInstance(type,true);native.FromFile(path);
            byte[] before=HostState.Fingerprint(native,null);
            Check(Equal(before,HostState.Fingerprint(native,null)),"SDK状態の検査が安定しません。");
            IPXPmx clone=(IPXPmx)((ICloneable)native).Clone();
            if(!Equal(before,HostState.Fingerprint(clone,null))) {
                File.WriteAllBytes("dist/state_before.bin",HostState.SnapshotBytes(native,null));
                File.WriteAllBytes("dist/state_clone.bin",HostState.SnapshotBytes(clone,null));
            }
            Check(Equal(before,HostState.Fingerprint(clone,null)),"SDK複製の検査結果が一致しません。");
            PmxDocument doc=HostState.Document(native);
            bool[] target=doc.TargetVertices(new int[]{0});
            for(int i=0;i<target.Length;i++)if(target[i]) {
                V3 p=native.Vertex[i].Position;native.Vertex[i].Position=new V3(p.X+.2f,p.Y,p.Z);
            }
            Check(Equal(HostState.Fingerprint(clone,target),HostState.Fingerprint(native,target)),"座標以外のSDKデータが変わりました。");
            for(int i=0;i<target.Length;i++)if(target[i]) {Point3 p=doc.Points[i];native.Vertex[i].Position=new V3((float)p.X,(float)p.Y,(float)p.Z);}
            Check(Equal(before,HostState.Fingerprint(native,null)),"SDKでの座標復元に失敗しました。");
            Plugin.EnsurePositions(native,doc.Points,target);
            Point3[] requested=(Point3[])doc.Points.Clone();
            int first=Array.FindIndex(target,delegate(bool v){return v;});
            requested[first]=requested[first]+new Point3(.2,0,0);
            bool rejected=false;
            try {Plugin.EnsurePositions(native,requested,target);}catch(InvalidOperationException){rejected=true;}
            Check(rejected,"反映されていない座標を成功扱いしました。");
            native.Vertex[first].Position=new V3((float)requested[first].X,(float)requested[first].Y,(float)requested[first].Z);
            Plugin.EnsurePositions(native,requested,target);
            native.Vertex[first].Position=new V3((float)doc.Points[first].X,(float)doc.Points[first].Y,(float)doc.Points[first].Z);
            Console.WriteLine("PMXEditor 0.2.7.3 SDK ("+Path.GetFileName(path)+"): vertex-only change / full restore PASS");
        }
    }
}

