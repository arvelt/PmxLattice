using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using PEPlugin;
using PEPlugin.Pmx;
using PEPlugin.SDX;

namespace PmxLattice {
    public sealed class Plugin : PEPluginClass {
        LatticeForm form;
        public Plugin() {m_option=new PEPluginOption(false,true,"PMX ラティス補助");}
        public override string Name {get{return "PMX ラティス補助";}}
        public override string Version {get{return "1.0.0";}}
        public override string Description {get{return "選択した衣装の頂点位置を格子で調整します。";}}
        public override void Run(IPERunArgs args) {
            if(form!=null && !form.IsDisposed){form.Activate();return;}
            try {
                IPEConnector connector=args.Host.Connector;
                IPXPmx initial=connector.Pmx.GetCurrentState();
                byte[] original=HostState.Fingerprint(initial,null);
                PmxDocument doc=HostState.Document(initial,connector.Pmx.CurrentPath);
                Func<bool[]> selection=delegate {
                    bool[] selected=new bool[doc.Points.Length];
                    foreach(int i in connector.View.PmxView.GetSelectedVertexIndices())if(i>=0&&i<selected.Length)selected[i]=true;
                    return selected;
                };
                Action<Point3[],bool[]> publish=delegate(Point3[] points,bool[] target) {
                    IPXPmx current=connector.Pmx.GetCurrentState();
                    EnsureExpected(original,HostState.Fingerprint(current,null));
                    byte[] protectedState=HostState.Fingerprint(current,target);
                    List<int> indices=new List<int>();
                    for(int i=0;i<target.Length;i++)if(target[i]) {
                        Point3 p=points[i];
                        if(double.IsNaN(p.X+p.Y+p.Z)||double.IsInfinity(p.X+p.Y+p.Z))throw new InvalidDataException("座標が不正です。");
                        current.Vertex[i].Position=new V3((float)p.X,(float)p.Y,(float)p.Z);
                        indices.Add(i);
                    }
                    EnsureExpected(protectedState,HostState.Fingerprint(current,target));
                    connector.Pmx.Update(current,PmxUpdateObject.Vertex,indices.ToArray());
                    connector.View.PmxView.UpdateModel_Vertex();connector.View.PmxView.UpdateView();
                    IPXPmx updated=connector.Pmx.GetCurrentState();
                    EnsurePositions(updated,points,target);
                };
                form=new LatticeForm(doc,selection(),publish,selection);form.Show();
            } catch(Exception e) {MessageBox.Show(e.Message,"ラティスを開始できませんでした",MessageBoxButtons.OK,MessageBoxIcon.Information);}
        }
        internal static void EnsurePositions(IPXPmx model,Point3[] positions,bool[] target) {
            if(model.Vertex.Count!=positions.Length)throw new InvalidOperationException("反映後の頂点数が一致しません。処理結果をPMXEditorで確認してください。");
            for(int i=0;i<target.Length;i++)if(target[i]) {
                V3 actual=model.Vertex[i].Position;Point3 p=positions[i];
                if(actual.X!=(float)p.X||actual.Y!=(float)p.Y||actual.Z!=(float)p.Z)
                    throw new InvalidOperationException("頂点位置の反映を確認できませんでした。処理結果をPMXEditorで確認してください。");
            }
        }
        static void EnsureExpected(byte[] expected,byte[] actual) {
            if(expected.Length!=actual.Length)throw new InvalidOperationException("PMXEditor側でデータが変更されています。このウィンドウを閉じて開き直してください。");
            for(int i=0;i<expected.Length;i++)if(expected[i]!=actual[i])throw new InvalidOperationException("PMXEditor側でデータが変更されています。上書きを避けるため処理を中止しました。このウィンドウを閉じて開き直してください。");
        }
        public override void Dispose() {if(form!=null&&!form.IsDisposed)form.Close();base.Dispose();}
    }
}

