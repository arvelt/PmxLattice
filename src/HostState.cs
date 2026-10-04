using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using PEPlugin.Pmx;
using PEPlugin.SDX;

namespace PmxLattice {
    public static class HostState {
        // PMXの保存処理は検査に使いません。SDKの保存はボーン軸を正規化するため、
        // 繰り返し保存した際に浮動小数点の差が出ることを実データで確認しています。
        public static byte[] Fingerprint(IPXPmx pmx,bool[] allowedPositions) {
            using(SHA256 hash=SHA256.Create())return hash.ComputeHash(SnapshotBytes(pmx,allowedPositions));
        }
        public static byte[] SnapshotBytes(IPXPmx pmx,bool[] allowedPositions) {
            using(MemoryStream stream=new MemoryStream())using(BinaryWriter writer=new BinaryWriter(stream)) {
                StateWriter state=new StateWriter(writer,pmx,allowedPositions);state.Write(pmx);
                return stream.ToArray();
            }
        }
        public static PmxDocument Document(IPXPmx pmx,string sourcePath=null) {
            PmxDocument d=new PmxDocument();d.Points=new Point3[pmx.Vertex.Count];
            d.Normals=new Point3[pmx.Vertex.Count];d.UV=new System.Drawing.PointF[pmx.Vertex.Count];
            string path=String.IsNullOrEmpty(sourcePath)?pmx.FilePath:sourcePath;
            if(!String.IsNullOrEmpty(path))d.SourceDirectory=Path.GetDirectoryName(Path.GetFullPath(path));
            Dictionary<IPXVertex,int> index=new Dictionary<IPXVertex,int>();
            for(int i=0;i<pmx.Vertex.Count;i++) {IPXVertex v=pmx.Vertex[i];d.Points[i]=new Point3(v.Position.X,v.Position.Y,v.Position.Z);d.Normals[i]=new Point3(v.Normal.X,v.Normal.Y,v.Normal.Z);d.UV[i]=new System.Drawing.PointF(v.UV.X,v.UV.Y);index.Add(v,i);if(v.SDEF)d.SdefCount++;}
            List<int> faces=new List<int>();
            foreach(IPXMaterial mat in pmx.Material) {
                int start=faces.Count;
                foreach(IPXFace f in mat.Faces){faces.Add(index[f.Vertex1]);faces.Add(index[f.Vertex2]);faces.Add(index[f.Vertex3]);}
                d.Materials.Add(new MaterialInfo{Name=mat.Name,Start=start,Count=faces.Count-start,R=mat.Diffuse.X,G=mat.Diffuse.Y,B=mat.Diffuse.Z,A=mat.Diffuse.W,TexturePath=mat.Tex,TwoSided=mat.BothDraw});
            }
            d.Faces=faces.ToArray();return d;
        }
        sealed class RefComparer : IEqualityComparer<object> {
            public new bool Equals(object a,object b){return Object.ReferenceEquals(a,b);}
            public int GetHashCode(object a){return RuntimeHelpers.GetHashCode(a);}
        }
        sealed class StateWriter {
            static readonly Dictionary<Type,PropertyInfo[]> properties=new Dictionary<Type,PropertyInfo[]>();
            readonly BinaryWriter writer;
            readonly Dictionary<object,int> ids=new Dictionary<object,int>(new RefComparer());
            readonly HashSet<object> seen=new HashSet<object>(new RefComparer());
            readonly Dictionary<object,int> vertices=new Dictionary<object,int>(new RefComparer());
            readonly bool[] allowed;
            public StateWriter(BinaryWriter w,IPXPmx p,bool[] a) {
                writer=w;allowed=a;Register(p);
                Register(p.Header);Register(p.ModelInfo);
                RegisterList(p.Vertex);RegisterList(p.Material);RegisterList(p.Bone);RegisterList(p.Morph);
                RegisterList(p.Node);RegisterList(p.Body);RegisterList(p.Joint);RegisterList(p.SoftBody);
                for(int i=0;i<p.Vertex.Count;i++)vertices[p.Vertex[i]]=i;
            }
            void Register(object o){if(o!=null&&!ids.ContainsKey(o))ids.Add(o,ids.Count);}
            void RegisterList(IEnumerable list){foreach(object o in list)Register(o);}
            public void Write(object o) {
                if(o==null){writer.Write((byte)0);return;}
                Type t=o.GetType();
                if(t.IsEnum){writer.Write((byte)1);writer.Write(t.FullName);writer.Write(Convert.ToInt64(o));return;}
                if(o is string){writer.Write((byte)2);writer.Write((string)o);return;}
                if(o is float){writer.Write((byte)3);writer.Write((float)o);return;}
                if(o is double){writer.Write((byte)4);writer.Write((double)o);return;}
                if(o is bool){writer.Write((byte)5);writer.Write((bool)o);return;}
                if(t.IsPrimitive){writer.Write((byte)6);writer.Write(Convert.ToString(o,System.Globalization.CultureInfo.InvariantCulture));return;}
                V3 v3=o as V3;
                if(v3!=null){writer.Write((byte)7);writer.Write(v3.X);writer.Write(v3.Y);writer.Write(v3.Z);return;}
                V2 v2=o as V2;
                if(v2!=null){writer.Write((byte)8);writer.Write(v2.X);writer.Write(v2.Y);return;}
                V4 v4=o as V4;
                if(v4!=null){writer.Write((byte)9);writer.Write(v4.X);writer.Write(v4.Y);writer.Write(v4.Z);writer.Write(v4.W);return;}
                Q quaternion=o as Q;
                if(quaternion!=null){writer.Write((byte)12);writer.Write(quaternion.X);writer.Write(quaternion.Y);writer.Write(quaternion.Z);writer.Write(quaternion.W);return;}
                IEnumerable list=o as IEnumerable;
                if(list!=null){writer.Write((byte)10);foreach(object item in list){writer.Write(true);Write(item);}writer.Write(false);return;}
                Register(o);writer.Write((byte)11);writer.Write(ids[o]);
                if(!seen.Add(o)){writer.Write(false);return;}writer.Write(true);
                PropertyInfo[] props;
                if(!properties.TryGetValue(t,out props)) {
                    props=t.GetInterfaces().Where(x=>x.Namespace=="PEPlugin.Pmx").SelectMany(x=>x.GetProperties()).Where(x=>x.CanRead&&x.GetIndexParameters().Length==0).GroupBy(x=>x.Name).Select(x=>x.First()).OrderBy(x=>x.Name,StringComparer.Ordinal).ToArray();properties.Add(t,props);
                }
                if(props.Length==0)throw new NotSupportedException("検査で扱えないSDK型です: "+t.FullName);
                foreach(PropertyInfo p in props) {
                    // 保存パス・生成用Builder・計算用PrimitiveはPMXデータではありません。
                    if(p.Name=="FilePath"||p.Name=="Primitive"||p.Name=="RootNode"||p.Name=="ExpressionNode")continue;
                    int vi;
                    if(p.Name=="Position"&&allowed!=null&&vertices.TryGetValue(o,out vi)&&allowed[vi])continue;
                    writer.Write(p.Name);Write(p.GetValue(o,null));
                }
                writer.Write("");
            }
        }
    }
}

