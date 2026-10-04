using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace PmxLattice {
    public enum PreviewView { Front,Back,Right,Left,Top,Bottom }
    public static class ViewAxes {
        public static double Horizontal(Point3 p,PreviewView v){switch(v){case PreviewView.Back:return -p.X;case PreviewView.Right:return p.Z;case PreviewView.Left:return -p.Z;default:return p.X;}}
        public static double Vertical(Point3 p,PreviewView v){return v==PreviewView.Top?p.Z:v==PreviewView.Bottom?-p.Z:p.Y;}
        public static double Depth(Point3 p,PreviewView v){switch(v){case PreviewView.Back:return p.Z;case PreviewView.Right:return p.X;case PreviewView.Left:return -p.X;case PreviewView.Top:return p.Y;case PreviewView.Bottom:return -p.Y;default:return -p.Z;}}
        public static Point3 Drag(double a,double b,PreviewView v){switch(v){case PreviewView.Back:return new Point3(-a,b,0);case PreviewView.Right:return new Point3(0,b,a);case PreviewView.Left:return new Point3(0,b,-a);case PreviewView.Top:return new Point3(a,0,b);case PreviewView.Bottom:return new Point3(a,0,-b);default:return new Point3(a,b,0);}}
    }
    public sealed class MaterialRenderer {
        sealed class Texture {
            public int Width,Height;
            public int[] Pixels;
            public bool Transparent;
            public int Sample(double u,double v) {
                if(double.IsNaN(u)||double.IsNaN(v)||double.IsInfinity(u)||double.IsInfinity(v))return -1;
                u-=Math.Floor(u);v-=Math.Floor(v);
                int x=Math.Min(Width-1,(int)(u*Width)),y=Math.Min(Height-1,(int)(v*Height));
                return Pixels[y*Width+x];
            }
        }
        struct ScreenVertex {public double X,Y,Z,U,V,Light;}
        struct Triangle {public int Face,Material;public double Depth;}
        readonly PmxDocument doc;
        readonly Texture[] textures;
        public readonly List<string> MissingTextures=new List<string>();
        public bool ShowHiddenMaterials;
        MaterialInfo DisplayMaterial(MaterialInfo m) {
            if(!ShowHiddenMaterials||m.A>0)return m;
            return new MaterialInfo{Name=m.Name,Start=m.Start,Count=m.Count,R=m.R,G=m.G,B=m.B,A=1,TexturePath=m.TexturePath,TwoSided=m.TwoSided};
        }
        public MaterialRenderer(PmxDocument document) {
            doc=document;textures=new Texture[doc.Materials.Count];
            Dictionary<string,Texture> cache=new Dictionary<string,Texture>(StringComparer.OrdinalIgnoreCase);
            for(int i=0;i<textures.Length;i++) {
                string name=doc.Materials[i].TexturePath;
                if(String.IsNullOrWhiteSpace(name))continue;
                try {
                    string path=Path.IsPathRooted(name)?name:Path.Combine(doc.SourceDirectory??"",name);
                    Texture texture;
                    if(!cache.TryGetValue(path,out texture)) {texture=ReadTexture(path);cache[path]=texture;}
                    textures[i]=texture;
                } catch {MissingTextures.Add(doc.Materials[i].Name+": "+name);}
            }
        }
        static Texture ReadTexture(string path) {
            using(FileStream stream=File.OpenRead(path))using(Image source=Image.FromStream(stream)) {
                double scale=Math.Min(1,1024.0/Math.Max(source.Width,source.Height));
                int width=Math.Max(1,(int)(source.Width*scale)),height=Math.Max(1,(int)(source.Height*scale));
                using(Bitmap image=new Bitmap(width,height,PixelFormat.Format32bppArgb)) {
                    using(Graphics g=Graphics.FromImage(image)) {g.CompositingMode=System.Drawing.Drawing2D.CompositingMode.SourceCopy;g.DrawImage(source,0,0,width,height);}
                    Texture t=new Texture{Width=width,Height=height,Pixels=new int[width*height]};
                    BitmapData data=image.LockBits(new Rectangle(0,0,width,height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
                    try {for(int y=0;y<height;y++)Marshal.Copy(IntPtr.Add(data.Scan0,y*data.Stride),t.Pixels,y*width,width);}finally{image.UnlockBits(data);}
                    foreach(int pixel in t.Pixels)if((uint)pixel>>24<250){t.Transparent=true;break;}
                    return t;
                }
            }
        }
        public static Point3[] SmoothNormals(PmxDocument doc,Point3[] points) {
            Point3[] normals=new Point3[points.Length];int[] faces=doc.Faces;
            for(int i=0;i<faces.Length;i+=3) {
                int a=faces[i],b=faces[i+1],c=faces[i+2];Point3 u=points[b]-points[a],v=points[c]-points[a];
                Point3 n=new Point3(u.Y*v.Z-u.Z*v.Y,u.Z*v.X-u.X*v.Z,u.X*v.Y-u.Y*v.X);
                normals[a]+=n;normals[b]+=n;normals[c]+=n;
            }
            for(int i=0;i<normals.Length;i++) {
                Point3 n=normals[i];double len=Math.Sqrt(n.X*n.X+n.Y*n.Y+n.Z*n.Z);
                if(len>1e-12)normals[i]=n*(1/len);else if(doc.Normals!=null)normals[i]=doc.Normals[i];
            }
            return normals;
        }
        public Bitmap Render(Point3[] points,Point3[] normals,bool[] visible,bool side,int width,int height,double zoom,double cx,double cy) {
            return Render(points,normals,visible,side?PreviewView.Right:PreviewView.Front,width,height,zoom,cx,cy);
        }
        public Bitmap Render(Point3[] points,Point3[] normals,bool[] visible,PreviewView view,int width,int height,double zoom,double cx,double cy) {
            width=Math.Max(1,width);height=Math.Max(1,height);
            int[] pixels=new int[width*height];double[] depth=new double[pixels.Length];
            for(int i=0;i<pixels.Length;i++){pixels[i]=unchecked((int)0xfff5f6f8);depth[i]=double.NegativeInfinity;}
            ScreenVertex[] vertices=new ScreenVertex[points.Length];
            double lightLength=Math.Sqrt(.3*.3+.4*.4+.85*.85);
            for(int i=0;i<vertices.Length;i++) {
                Point3 p=points[i],n=normals[i];PointF uv=doc.UV==null?PointF.Empty:doc.UV[i];
                vertices[i]=new ScreenVertex{X=width/2.0+(ViewAxes.Horizontal(p,view)-cx)*zoom,Y=height/2.0-(ViewAxes.Vertical(p,view)-cy)*zoom,Z=ViewAxes.Depth(p,view),U=uv.X,V=uv.Y,Light=.65+.35*Math.Max(0,(ViewAxes.Horizontal(n,view)*.3+ViewAxes.Vertical(n,view)*.4+ViewAxes.Depth(n,view)*.85)/lightLength)};
            }
            List<Triangle> transparent=new List<Triangle>();
            for(int m=0;m<doc.Materials.Count;m++)if(visible[m]) {
                MaterialInfo material=DisplayMaterial(doc.Materials[m]);if(material.A<=0)continue;
                for(int f=material.Start;f<material.Start+material.Count;f+=3) {
                    int a=doc.Faces[f],b=doc.Faces[f+1],c=doc.Faces[f+2];
                    Raster(vertices[a],vertices[b],vertices[c],material,textures[m],false,width,height,pixels,depth);
                    if(material.A<.99f||(textures[m]!=null&&textures[m].Transparent))transparent.Add(new Triangle{Face=f,Material=m,Depth=(vertices[a].Z+vertices[b].Z+vertices[c].Z)/3});
                }
            }
            transparent.Sort(delegate(Triangle a,Triangle b){return a.Depth.CompareTo(b.Depth);});
            foreach(Triangle t in transparent) {
                int f=t.Face;
                Raster(vertices[doc.Faces[f]],vertices[doc.Faces[f+1]],vertices[doc.Faces[f+2]],DisplayMaterial(doc.Materials[t.Material]),textures[t.Material],true,width,height,pixels,depth);
            }
            Bitmap output=new Bitmap(width,height,PixelFormat.Format32bppArgb);
            BitmapData bits=output.LockBits(new Rectangle(0,0,width,height),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
            try{for(int y=0;y<height;y++)Marshal.Copy(pixels,y*width,IntPtr.Add(bits.Scan0,y*bits.Stride),width);}finally{output.UnlockBits(bits);}
            return output;
        }
        static int Byte(double v){return (int)Math.Max(0,Math.Min(255,v));}
        static void Raster(ScreenVertex a,ScreenVertex b,ScreenVertex c,MaterialInfo material,Texture texture,bool transparent,int width,int height,int[] pixels,double[] depth) {
            double area=(b.Y-c.Y)*(a.X-c.X)+(c.X-b.X)*(a.Y-c.Y);
            if(double.IsNaN(area)||double.IsInfinity(area)||Math.Abs(area)<1e-9)return;
            if(!material.TwoSided&&area<=0)return;
            double minX=Math.Max(0,Math.Floor(Math.Min(a.X,Math.Min(b.X,c.X)))),maxX=Math.Min(width-1,Math.Ceiling(Math.Max(a.X,Math.Max(b.X,c.X))));
            double minY=Math.Max(0,Math.Floor(Math.Min(a.Y,Math.Min(b.Y,c.Y)))),maxY=Math.Min(height-1,Math.Ceiling(Math.Max(a.Y,Math.Max(b.Y,c.Y))));
            if(minX>maxX||minY>maxY)return;
            double dx1=(b.Y-c.Y)/area,dy1=(c.X-b.X)/area,dx2=(c.Y-a.Y)/area,dy2=(a.X-c.X)/area;
            double start1=((b.Y-c.Y)*(minX+.5-c.X)+(c.X-b.X)*(minY+.5-c.Y))/area;
            double start2=((c.Y-a.Y)*(minX+.5-c.X)+(a.X-c.X)*(minY+.5-c.Y))/area;
            for(int y=(int)minY;y<=(int)maxY;y++,start1+=dy1,start2+=dy2) {
                double w1=start1,w2=start2;
                for(int x=(int)minX;x<=(int)maxX;x++,w1+=dx1,w2+=dx2) {
                    double w3=1-w1-w2;if(w1<-.000001||w2<-.000001||w3<-.000001)continue;
                    double z=w1*a.Z+w2*b.Z+w3*c.Z;int index=y*width+x;
                    if(z<depth[index]-1e-7)continue;
                    int tex=texture==null?-1:texture.Sample(w1*a.U+w2*b.U+w3*c.U,w1*a.V+w2*b.V+w3*c.V);
                    int alpha=Byte(((uint)tex>>24)*material.A);
                    if(alpha==0||(transparent?alpha>=250:alpha<250))continue;
                    double lighting=w1*a.Light+w2*b.Light+w3*c.Light;
                    int r=Byte(((tex>>16)&255)*material.R*lighting),g=Byte(((tex>>8)&255)*material.G*lighting),bl=Byte((tex&255)*material.B*lighting);
                    if(transparent) {
                        int previous=pixels[index],inv=255-alpha;
                        r=(r*alpha+((previous>>16)&255)*inv)/255;g=(g*alpha+((previous>>8)&255)*inv)/255;bl=(bl*alpha+(previous&255)*inv)/255;
                    }else depth[index]=z;
                    pixels[index]=unchecked((int)0xff000000)|(r<<16)|(g<<8)|bl;
                }
            }
        }
    }
}

