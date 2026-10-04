using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PmxLattice {
    public struct Point3 {
        public double X, Y, Z;
        public Point3(double x, double y, double z) { X=x; Y=y; Z=z; }
        public static Point3 operator +(Point3 a, Point3 b) { return new Point3(a.X+b.X,a.Y+b.Y,a.Z+b.Z); }
        public static Point3 operator -(Point3 a, Point3 b) { return new Point3(a.X-b.X,a.Y-b.Y,a.Z-b.Z); }
        public static Point3 operator *(Point3 a, double b) { return new Point3(a.X*b,a.Y*b,a.Z*b); }
    }
    public sealed class MaterialInfo {
        public string Name;
        public int Start, Count;
        public float R=1,G=1,B=1,A=1;
        public string TexturePath;
        public bool TwoSided;
        public override string ToString() { return Name; }
    }
    public sealed class PmxDocument {
        public byte[] Original;
        public Point3[] Points;
        public Point3[] Normals;
        public System.Drawing.PointF[] UV;
        public string SourceDirectory;
        public int[] PositionOffsets, Faces;
        public List<MaterialInfo> Materials = new List<MaterialInfo>();
        public int SdefCount;
        static int Index(BinaryReader r, int size, bool unsigned) {
            if(size==1) return unsigned ? (int)r.ReadByte() : (int)r.ReadSByte();
            if(size==2) return unsigned ? (int)r.ReadUInt16() : (int)r.ReadInt16();
            if(size==4) return r.ReadInt32();
            throw new InvalidDataException("インデックスサイズが不正です。");
        }
        static int Count(BinaryReader r, int maximum) {
            int n=r.ReadInt32();
            if(n<0 || n>maximum) throw new InvalidDataException("件数が不正、または対応範囲を超えています。");
            return n;
        }
        static string Text(BinaryReader r, Encoding e) {
            int n=Count(r, 64*1024*1024);
            byte[] b=r.ReadBytes(n);
            if(b.Length!=n) throw new EndOfStreamException();
            return e.GetString(b);
        }
        static void Skip(BinaryReader r, long n) {
            if(n<0 || r.BaseStream.Position+n>r.BaseStream.Length) throw new EndOfStreamException();
            r.BaseStream.Position+=n;
        }
        public static PmxDocument Load(string path) {
            PmxDocument doc=LoadBytes(File.ReadAllBytes(path));
            doc.SourceDirectory=Path.GetDirectoryName(Path.GetFullPath(path));return doc;
        }
        public static PmxDocument LoadBytes(byte[] bytes) {
            PmxDocument d=new PmxDocument();
            d.Original=(byte[])bytes.Clone();
            using(BinaryReader r=new BinaryReader(new MemoryStream(d.Original))) {
                if(Encoding.ASCII.GetString(r.ReadBytes(4))!="PMX ") throw new InvalidDataException("PMXファイルではありません。");
                float version=r.ReadSingle();
                if(Math.Abs(version-2f)>.001 && Math.Abs(version-2.1f)>.001) throw new InvalidDataException("PMX 2.0/2.1のみ対応しています。");
                int headerSize=r.ReadByte();
                byte[] h=r.ReadBytes(headerSize);
                if(h.Length<8 || h[0]>1 || h[1]>4) throw new InvalidDataException("PMXヘッダーが不正です。");
                Encoding encoding=h[0]==0 ? Encoding.Unicode : Encoding.UTF8;
                for(int i=0;i<4;i++) Text(r,encoding);
                int n=Count(r,5000000);
                d.Points=new Point3[n]; d.PositionOffsets=new int[n];
                d.Normals=new Point3[n];d.UV=new System.Drawing.PointF[n];
                for(int i=0;i<n;i++) {
                    d.PositionOffsets[i]=(int)r.BaseStream.Position;
                    d.Points[i]=new Point3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());
                    d.Normals[i]=new Point3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());
                    d.UV[i]=new System.Drawing.PointF(r.ReadSingle(),r.ReadSingle());
                    Skip(r,16*h[1]);
                    int type=r.ReadByte();
                    switch(type) {
                        case 0: Skip(r,h[5]); break;
                        case 1: Skip(r,2*h[5]+4); break;
                        case 2: case 4: Skip(r,4*h[5]+16); break;
                        case 3: Skip(r,2*h[5]+4+36); d.SdefCount++; break;
                        default: throw new InvalidDataException("未対応のウェイト形式です。");
                    }
                    Skip(r,4);
                }
                int faceCount=Count(r,30000000);
                if(faceCount%3!=0) throw new InvalidDataException("面インデックス数が不正です。");
                d.Faces=new int[faceCount];
                for(int i=0;i<faceCount;i++) {
                    int v=Index(r,h[2],true);
                    if(v<0 || v>=n) throw new InvalidDataException("面が存在しない頂点を参照しています。");
                    d.Faces[i]=v;
                }
                int textures=Count(r,1000000);
                string[] texturePaths=new string[textures];
                for(int i=0;i<textures;i++) texturePaths[i]=Text(r,encoding);
                int materials=Count(r,1000000), start=0;
                for(int i=0;i<materials;i++) {
                    string name=Text(r,encoding); Text(r,encoding);
                    MaterialInfo mat=new MaterialInfo{Name=name,Start=start};
                    mat.R=r.ReadSingle();mat.G=r.ReadSingle();mat.B=r.ReadSingle();mat.A=r.ReadSingle();
                    Skip(r,12+4+12);mat.TwoSided=(r.ReadByte()&1)!=0;
                    Skip(r,16+4);
                    int texture=Index(r,h[3],false);
                    if(texture>=0&&texture<texturePaths.Length)mat.TexturePath=texturePaths[texture];
                    Skip(r,h[3]+1);
                    int toon=r.ReadByte();
                    if(toon==0) Skip(r,h[3]);
                    else if(toon==1) Skip(r,1);
                    else throw new InvalidDataException("Toon設定が不正です。");
                    Text(r,encoding);
                    int count=Count(r,faceCount);
                    if(count%3!=0 || start+count>faceCount) throw new InvalidDataException("材質の面数が不正です。");
                    mat.Count=count;d.Materials.Add(mat); start+=count;
                }
                if(start!=faceCount) throw new InvalidDataException("材質の面数合計が一致しません。");
            }
            return d;
        }
        public bool[] TargetVertices(IEnumerable<int> materials) {
            bool[] result=new bool[Points.Length];
            foreach(int i in materials) {
                MaterialInfo m=Materials[i];
                for(int j=m.Start;j<m.Start+m.Count;j++) result[Faces[j]]=true;
            }
            return result;
        }
        public byte[] Output(Point3[] edited, bool[] target) {
            if(edited.Length!=Points.Length || target.Length!=Points.Length) throw new ArgumentException();
            byte[] output=(byte[])Original.Clone();
            for(int i=0;i<Points.Length;i++) {
                if(!target[i]) continue;
                Point3 p=edited[i];
                if(double.IsNaN(p.X+p.Y+p.Z) || double.IsInfinity(p.X+p.Y+p.Z)) throw new InvalidDataException("変形後の座標が不正です。");
                WriteFloat(output,PositionOffsets[i],p.X);
                WriteFloat(output,PositionOffsets[i]+4,p.Y);
                WriteFloat(output,PositionOffsets[i]+8,p.Z);
            }
            ValidatePreserved(output,target);
            return output;
        }
        static void WriteFloat(byte[] b,int offset,double value) {
            float f=(float)value;
            if(float.IsNaN(f)||float.IsInfinity(f)) throw new InvalidDataException("座標が保存可能な範囲を超えています。");
            Buffer.BlockCopy(BitConverter.GetBytes(f),0,b,offset,4);
        }
        public void ValidatePreserved(byte[] output,bool[] target) {
            if(output.Length!=Original.Length) throw new InvalidDataException("ファイル長が変わりました。");
            int vertex=0;
            for(int i=0;i<Original.Length;i++) {
                while(vertex<PositionOffsets.Length && PositionOffsets[vertex]+12<=i) vertex++;
                bool permitted=vertex<PositionOffsets.Length && target[vertex] && i>=PositionOffsets[vertex] && i<PositionOffsets[vertex]+12;
                if(!permitted && output[i]!=Original[i]) throw new InvalidDataException("頂点座標以外の変更を検出したため保存を中止しました。");
            }
        }
    }
    public sealed class Lattice {
        public int NX=3, NY=5, NZ=3;
        public Point3 Min, Max;
        public Point3[] Base, Controls;
        public Lattice(Point3[] points,bool[] target,int ny) {
            NY=ny;
            Min=new Point3(double.MaxValue,double.MaxValue,double.MaxValue);
            Max=new Point3(double.MinValue,double.MinValue,double.MinValue);
            int count=0;
            for(int i=0;i<points.Length;i++) if(target[i]) {
                Point3 p=points[i]; count++;
                Min=new Point3(Math.Min(Min.X,p.X),Math.Min(Min.Y,p.Y),Math.Min(Min.Z,p.Z));
                Max=new Point3(Math.Max(Max.X,p.X),Math.Max(Max.Y,p.Y),Math.Max(Max.Z,p.Z));
            }
            if(count==0) throw new InvalidOperationException("対象の材質を選んでください。");
            if(Max.X-Min.X<.000001 || Max.Y-Min.Y<.000001 || Max.Z-Min.Z<.000001) throw new InvalidOperationException("平面状の対象には格子を作成できません。");
            Base=new Point3[NX*NY*NZ];
            for(int z=0;z<NZ;z++) for(int y=0;y<NY;y++) for(int x=0;x<NX;x++)
                Base[Id(x,y,z)]=new Point3(Min.X+(Max.X-Min.X)*x/(NX-1),Min.Y+(Max.Y-Min.Y)*y/(NY-1),Min.Z+(Max.Z-Min.Z)*z/(NZ-1));
            Controls=(Point3[])Base.Clone();
        }
        public int Id(int x,int y,int z) { return (z*NY+y)*NX+x; }
        public int Row(int id) { return (id/NX)%NY; }
        static double[] Weights(double t) {
            double a=1-t;
            return new double[]{a*a*a/6, (3*t*t*t-6*t*t+4)/6, (-3*t*t*t+3*t*t+3*t+1)/6,t*t*t/6};
        }
        static int Clamp(int v,int n) { return Math.Max(0,Math.Min(n-1,v)); }
        public Point3 Deform(Point3 p) {
            double u=(p.X-Min.X)/(Max.X-Min.X)*(NX-1),v=(p.Y-Min.Y)/(Max.Y-Min.Y)*(NY-1),w=(p.Z-Min.Z)/(Max.Z-Min.Z)*(NZ-1);
            if(u<-.00001||u>NX-1+.00001||v<-.00001||v>NY-1+.00001||w<-.00001||w>NZ-1+.00001) return p;
            u=Math.Max(0,Math.Min(NX-1,u));v=Math.Max(0,Math.Min(NY-1,v));w=Math.Max(0,Math.Min(NZ-1,w));
            int ix=(int)Math.Floor(u),iy=(int)Math.Floor(v),iz=(int)Math.Floor(w);
            double[] ax=Weights(u-ix),ay=Weights(v-iy),az=Weights(w-iz);
            Point3 delta=new Point3();
            for(int z=0;z<4;z++) for(int y=0;y<4;y++) for(int x=0;x<4;x++) {
                int id=Id(Clamp(ix+x-1,NX),Clamp(iy+y-1,NY),Clamp(iz+z-1,NZ));
                delta+=(Controls[id]-Base[id])*(ax[x]*ay[y]*az[z]);
            }
            return p+delta;
        }
        public Point3[] Apply(Point3[] points,bool[] target) {
            Point3[] result=(Point3[])points.Clone();
            for(int i=0;i<points.Length;i++) if(target[i]) result[i]=Deform(points[i]);
            return result;
        }
        public void ScaleRow(int row,double x,double z) {
            double bx=(Min.X+Max.X)/2,bz=(Min.Z+Max.Z)/2,cx=0,cz=0;
            for(int k=0;k<NZ;k++)for(int i=0;i<NX;i++){Point3 p=Controls[Id(i,row,k)];cx+=p.X;cz+=p.Z;}
            cx/=NX*NZ;cz/=NX*NZ;
            for(int k=0;k<NZ;k++) for(int i=0;i<NX;i++) {
                int id=Id(i,row,k); Point3 b=Base[id],p=Controls[id];
                Controls[id]=new Point3(cx+(b.X-bx)*x,p.Y,cz+(b.Z-bz)*z);
            }
        }
    }
}

